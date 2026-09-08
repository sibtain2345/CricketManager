using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 9, Slice 9.2: the free-agent market. A player whose contract expired with no renewal
/// (Slice 9.1's Bosman path), and a prospect Phase 8's academy released, both land at
/// CurrentTeamId == null. This picks them up.
///
/// Mirrors JobMarketService's shape exactly: a club with a genuine role gap and budget headroom
/// looks at the available free agents, the best fit whose wage it can carry is offered a deal, and
/// the player decides (a free agent with no club is generally keen, but weighs the wage, the
/// game-time prospect and how far he would have to move). No fee - that is the point of a free
/// transfer. Runs on the monthly tick, after JobMarketService.
/// </summary>
public sealed class FreeAgentMarketService
{
    private readonly PlayerContractService _contracts = new();
    private readonly PlayerValuationService _valuation = new();
    private readonly RoleFitService _fit = new();

    /// <summary>One free-agent signing per club per month, so the market moves gradually rather than in a flurry.</summary>
    public IEnumerable<GameEvent> RunMonthly(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        var freeAgents = world.Players
            .Where(p => p.CurrentTeamId is null && !p.IsRetired && p.AcademyTeamId is null && p.LoanReturnDate is null)
            .OrderByDescending(SquadNeeds.OverallScore)
            .ToList();
        if (freeAgents.Count == 0) return events;

        foreach (var team in world.Teams.Values.Where(IsSigningClub).OrderBy(t => t.Name))
        {
            var format = PrimaryFormat(world, team);
            var squad = team.SquadPlayerIds
                .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Where(p => p is not null).Select(p => p!).ToList();

            // The domestic overseas rules (the user's "every country benefits from every other"):
            // <=4 overseas in the squad, >=1 of them an associate. If a club is short an associate
            // and has room, it goes and gets one BEFORE anything else.
            var prof = world.ProfileFor(team.Country);
            bool IsOverseas(Player p) => !string.Equals(p.Nationality, team.Country, StringComparison.OrdinalIgnoreCase);
            bool IsAssociate(Player p) => world.ProfileFor(p.Nationality).Membership == MembershipStatus.Associate;
            int overseasNow = squad.Count(IsOverseas);
            int associatesNow = squad.Count(IsAssociate);
            bool needsAssociate = prof.AllowsForeignDomesticPlayers
                                  && associatesNow < prof.DomesticMinAssociatesInSquad
                                  && overseasNow < prof.DomesticSquadOverseasLimit;

            if (needsAssociate)
            {
                var assoc = freeAgents.FirstOrDefault(fa => IsAssociate(fa)
                    && !world.Teams.Values.Any(t => !t.IsNational && t.SquadPlayerIds.Contains(fa.Id))
                    && SquadNeeds.ScoreFor(fa, format) >= 26);
                if (assoc is not null)
                {
                    double aw = _valuation.MarketWage(assoc, world.MarketIndex) * 0.85;
                    if (SquadNeeds.CanCarryWage(team, aw) && SquadNeeds.WithinSalaryCap(world, team, aw) && PlayerAcceptsFreeTransfer(assoc, team, aw, world.MarketIndex, random))
                    {
                        _contracts.Sign(world, assoc, team, date, aw, assoc.Age(date) >= 32 ? 1 : 2, isHomegrown: false);
                        assoc.SquadStatus = SquadStatus.Backup;
                        assoc.Morale.Adjust(12);
                        events.Add(new GameEvent(date, GameEventType.PlayerSigned,
                            $"{team.Name} sign {assoc.FullName} ({assoc.Nationality}) - an associate-nation player to meet the domestic squad rules.",
                            assoc.Id, team.Id));
                        continue; // one signing per club per month
                    }
                }
            }

            // Need a genuine gap, or genuinely thin overall.
            var need = SquadNeeds.WeakestGroup(squad, format);
            bool thinSquad = squad.Count < 13;
            if (need is null && !thinSquad) continue;

            var matching = freeAgents
                .Where(fa => !world.Teams.Values.Any(t => !t.IsNational && t.SquadPlayerIds.Contains(fa.Id))) // not already snapped up this pass
                .Where(fa => need is null || need.Value.Group.Matches(fa))
                .Take(6)
                .ToList();

            foreach (var target in matching)
            {
                double score = SquadNeeds.ScoreFor(target, format);
                // Respect the domestic squad overseas cap - a foreign signing is only allowed if it
                // does not push the squad past the limit (an associate is still fine while associates
                // are short).
                if (IsOverseas(target) && overseasNow >= prof.DomesticSquadOverseasLimit
                    && !(IsAssociate(target) && associatesNow < prof.DomesticMinAssociatesInSquad))
                    continue;
                if (IsOverseas(target) && !IsAssociate(target)
                    && squad.Count(p => IsOverseas(p) && !IsAssociate(p)) >= prof.ForeignDomesticFullMemberMax)
                    continue;
                // Must be at least a squad-filler, and an upgrade if the club has a specific gap.
                if (need is { } n && score < n.WeakestScore - 4 && !thinSquad) continue;
                if (score < 30 && !thinSquad) continue;

                double wage = _valuation.MarketWage(target, world.MarketIndex)
                              * (score >= 55 ? 1.0 : 0.8); // a fringe free agent takes what he can get
                if (!SquadNeeds.CanCarryWage(team, wage) || !SquadNeeds.WithinSalaryCap(world, team, wage)) continue;

                if (!PlayerAcceptsFreeTransfer(target, team, wage, world.MarketIndex, random)) continue;

                int years = target.Age(date) >= 32 ? 1 : random.Next(2, 4);
                _contracts.Sign(world, target, team, date, wage, years, isHomegrown: false);
                target.SquadStatus = score >= 60 ? SquadStatus.SecondChoice : SquadStatus.Backup;
                target.Morale.Adjust(10);
                target.Form.RecordPerformance(4);

                events.Add(new GameEvent(date, GameEventType.PlayerSigned,
                    $"{team.Name} sign the free agent {target.FullName} on a {years}-year deal worth {wage:N0} a year.",
                    target.Id, team.Id));
                break; // one per club per month
            }
        }

        return events;
    }

    private static bool IsSigningClub(Team t) => !t.IsNational && !t.IsFranchise;

    private static MatchFormat PrimaryFormat(WorldState world, Team team)
    {
        var comp = world.CompetitionSeasons
            .Where(s => s.ParticipatingTeamIds.Contains(team.Id))
            .Select(s => world.Competitions.FirstOrDefault(c => c.Id == s.CompetitionId))
            .FirstOrDefault(c => c is not null);
        return comp?.Format ?? MatchFormat.T20;
    }

    private bool PlayerAcceptsFreeTransfer(Player player, Team team, double wageOffered, double marketIndex, Random random)
    {
        double marketWage = _valuation.MarketWage(player, marketIndex);
        double wageSatisfaction = Math.Clamp(wageOffered / Math.Max(1, marketWage), 0.5, 1.5);

        double accept = 0.55 + (wageSatisfaction - 0.9) * 0.7; // a free agent needs a club - the bar is low
        if (player.Personality.HasFlag(PersonalityTrait.MoneyFocused) && wageSatisfaction < 1.0) accept -= 0.2;
        if (player.Personality.HasFlag(PersonalityTrait.Ambitious))
            accept += (team.Reputation.Domestic - 45) / 100.0 * 0.3;

        bool differentCountry = !string.Equals(player.Nationality, team.Country, StringComparison.OrdinalIgnoreCase);
        accept *= 0.7 + _fit.RelocationComfort(player.Mental.Adaptability, differentCountry) * 0.3;

        return random.NextDouble() < Math.Clamp(accept, 0.05, 0.95);
    }
}
