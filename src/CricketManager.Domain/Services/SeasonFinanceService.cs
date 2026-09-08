using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.1: the season finance loop, finally running.
///
/// TeamFinanceService.ApplySeasonFinances, SponsorshipValuationService and MatchdayRevenueService
/// were all built, tested and CALLED BY NOTHING in the running world - sponsorship income, facility
/// upkeep and the wage bill never actually moved a budget over a season. This is the orchestrator
/// that wires them into the annual rollover:
///
/// - <b>Sponsorship income</b> via SponsorshipValuationService (team reputation x competition
///   prestige/reputation x recent form x home-ground commercial infrastructure).
/// - <b>Matchday income</b> - the gate money FixturePlayService already computes per fixture and
///   banks into WorldState.MatchdayIncomeThisSeason, summed and applied here.
/// - <b>Facility upkeep</b> via TeamFinanceService.CalculateAnnualUpkeep (derived from the
///   facilities the club actually owns plus stadium capacity).
/// - <b>The wage bill</b> - a deliberate stub (WageBillService), the single line Phase 9 replaces.
///
/// Competition prize money is applied separately, at season completion, by CompetitionSeasonRunner -
/// so it is passed here as 0 to avoid double-counting; it surfaces in its own SeasonCompleted event.
///
/// A negative closing budget is NOT clamped - a club spending itself into trouble is a legitimate
/// state the board and job-security systems already react to.
/// </summary>
public sealed class SeasonFinanceService
{
    private readonly TeamFinanceService _finance = new();
    private readonly SponsorshipValuationService _sponsorship = new();
    private readonly WageBillService _wages = new();

    /// <summary>
    /// Settles every club's finances for the year that has just finished and returns a
    /// SeasonFinancialResult per club (attached to a GameEvent by the caller). Reads and CLEARS
    /// WorldState.MatchdayIncomeThisSeason.
    /// </summary>
    public IReadOnlyList<(Team Team, SeasonFinancialResult Result)> SettleYear(WorldState world, int year)
    {
        var settled = new List<(Team, SeasonFinancialResult)>();

        // Deterministic order - the settlement itself consumes no RNG, but the caller iterates
        // the result and raises events, so a stable order keeps the event stream reproducible.
        foreach (var team in world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise).OrderBy(t => t.Name))
        {
            var squad = world.Players.Where(p => team.SquadPlayerIds.Contains(p.Id)).ToList();

            // --- sponsorship ---
            double? recentWinRate = RecentWinRate(world, team, year);
            var (competitionPrestige, competitionReputation) = BestCompetition(world, team, year);
            var homeGround = team.HomeGroundId is { } gid && world.Grounds.TryGetValue(gid, out var g) ? g : null;
            double sponsorship = _sponsorship.CalculateSponsorshipValue(
                team.Reputation.Domestic, competitionPrestige, recentWinRate, competitionReputation,
                homeGround?.Facilities);

            // A wealthy board tops up commercial income; a poor one cannot.
            sponsorship *= 0.75 + team.Board.Wealth / 100.0 * 0.5;

            // §10.2: membership / season-ticket income - a per-head fee on the membership base,
            // scaled by how happy the fans are (an angry base does not renew). Grows/shrinks the
            // base slowly toward what sentiment supports. 0 base = no effect.
            if (team.Board.MembershipBase > 0)
            {
                double sentimentFactor = 0.75 + team.Board.FanSentiment / 100.0 * 0.5;
                sponsorship += team.Board.MembershipBase * 55 * sentimentFactor;
                double baseTarget = team.Board.MembershipBase * (0.85 + team.Board.FanSentiment / 100.0 * 0.35);
                team.Board.MembershipBase += (baseTarget - team.Board.MembershipBase) * 0.15;
            }

            // §9.5 / S3: image rights / commercial value - now driven by the player's own
            // CommercialAppeal (reputation x marketability x form), which is a more precise read
            // than the MediaPersona bucket alone. The club takes a cut.
            double imageRights = squad.Sum(p => p.CommercialAppeal >= 30 ? 8_500 * (p.CommercialAppeal - 25) : 0);
            sponsorship += imageRights * (0.6 + team.Board.Wealth / 100.0 * 0.4);

            // §9.7: multi-year sponsorship deals. When a club has contracted deals, they replace
            // most of the per-year formula (a reduced base still counts smaller commercial income)
            // and add a performance bonus for a trophy / a top-3 finish.
            if (team.SponsorContracts.Count > 0)
            {
                bool wonTrophy = world.CompetitionSeasons.Any(s => s.Year == year && s.ChampionTeamId == team.Id);
                bool top3 = world.CompetitionSeasons
                    .Where(s => s.Year == year && s.ParticipatingTeamIds.Contains(team.Id))
                    .Any(s => s.Standings.OrderByDescending(x => x.Points).ThenByDescending(x => x.NetRunRate)
                        .Take(3).Any(x => x.TeamId == team.Id));
                double contracted = SponsorshipService.ContractedIncome(team, year, wonTrophy, top3);
                sponsorship = sponsorship * 0.55 + contracted;
            }

            // --- matchday ---
            double matchday = world.MatchdayIncomeThisSeason.TryGetValue(team.Id, out var md) ? md : 0;

            // --- upkeep ---
            double upkeep = _finance.CalculateAnnualUpkeep(
                team.Facilities, homeGround?.Facilities, homeGround?.Capacity ?? 0);

            // --- wages ---
            // Phase 9, Slice 9.0: real contract wages when the world has them; the Phase 7 stub
            // estimate only as a fallback for a world that never generated contracts. Staff AND
            // head-coach salaries are added on top either way (Post-Phase-9 wiring pass, review
            // §10.1 - the coaching payroll is a real line and nothing was summing it into the
            // club's books).
            double staffWages = world.StaffContracts
                .Where(c => c.Status == ContractStatus.Active && c.TeamId == team.Id)
                .Sum(c => c.AnnualSalary);
            double coachWages = world.CoachingContracts
                .Where(c => c.Status == ContractStatus.Active && c.TeamId == team.Id)
                .Sum(c => c.AnnualSalary);
            double wageBill = PlayerContractService.RealWageBill(world, team) is { } real
                ? real + staffWages + coachWages
                : _wages.EstimateAnnualWageBill(team, squad, world.StaffContracts) + coachWages;

            var result = _finance.ApplySeasonFinances(
                team.Finances,
                sponsorshipIncome: Math.Round(sponsorship, 0),
                matchdayIncome: Math.Round(matchday, 0),
                upkeepCost: upkeep,
                wageCost: wageBill,
                competitionIncome: 0); // applied at season completion by CompetitionSeasonRunner

            settled.Add((team, result));
        }

        world.MatchdayIncomeThisSeason.Clear();
        return settled;
    }

    /// <summary>The team's win rate across the season(s) that concluded this year, or null when it played nothing (neutral, not "poor form" - the same discipline SponsorshipValuationService's own doc comment describes).</summary>
    private static double? RecentWinRate(WorldState world, Team team, int year)
    {
        var standings = world.CompetitionSeasons
            .Where(s => s.Year == year)
            .SelectMany(s => s.Standings)
            .Where(s => s.TeamId == team.Id)
            .ToList();
        double played = standings.Sum(s => s.Played);
        return played <= 0 ? null : standings.Sum(s => s.Won) / played;
    }

    /// <summary>The most prestigious competition this team competed in this year (prestige, and reputation for the blend).</summary>
    private static (double Prestige, double? Reputation) BestCompetition(WorldState world, Team team, int year)
    {
        var comps = world.CompetitionSeasons
            .Where(s => s.Year == year && s.ParticipatingTeamIds.Contains(team.Id))
            .Select(s => world.Competitions.FirstOrDefault(c => c.Id == s.CompetitionId))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        if (comps.Count == 0) return (50, null);
        var best = comps.OrderByDescending(c => c.Prestige).First();
        return (best.Prestige, best.Reputation);
    }
}
