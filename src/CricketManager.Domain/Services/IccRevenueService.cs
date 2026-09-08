using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Seven-suggestions pass (S5): the ICC's annual revenue distribution to the member boards, and a
/// LIGHT national finance loop on top of it (before this, national boards had no running finances
/// at all - <see cref="SeasonFinanceService"/> and <see cref="FinancialFairPlayService"/> both
/// skip <c>IsNational</c> teams).
///
/// The real model (2024-27, verified): a pool of roughly US$600M/year; the BCCI takes ~38.5%
/// (~$230M); no other Full Member is in double digits (ECB 6.89%, CA 6.25%, PCB 5.75%); the 12
/// Full Members share ~89%, the 90+ Associates ~11% (~$700k each). The split criteria are cricket
/// history; performance in ICC events over the last 16 years; contribution to ICC commercial
/// revenue; and an equal weightage for Full-Member status. MEN'S events only - there is no
/// women's-cricket concept anywhere in this engine and none is added.
///
/// Two DELIBERATE deviations, both named tunable constants, both to make a long sim more
/// interesting than the real distribution:
/// <list type="bullet">
/// <item>India keeps the largest single share but it is CAPPED and the surplus redistributed among
///       the other Full Members - the majors are flattened toward each other.</item>
/// <item>The Associate pool starts more generous than reality and GROWS a little each year, so an
///       associate nation's trajectory over decades is real - the money is there to be earned.</item>
/// </list>
/// RNG-free - a deterministic distribution formula - so it can run anywhere in the annual rollover.
/// </summary>
public sealed class IccRevenueService
{
    /// <summary>The global annual pool at <see cref="WorldState.MarketIndex"/> = 1.0. Grows with the index over a long sim.</summary>
    public const double BasePoolAtIndexOne = 600_000_000;

    /// <summary>Deviation: India's single-nation share is capped here (real figure ~0.385) and the surplus spread among the other Full Members.</summary>
    public const double TopNationShareCap = 0.30;

    /// <summary>Deviation: the Associate pool's starting share (real ~0.11) - deliberately more generous.</summary>
    public const double AssociatePoolStartShare = 0.16;

    /// <summary>Deviation: the Associate pool's share grows by this much per sim-year (capped at 0.35) - a real, funded development trajectory.</summary>
    public const double AssociatePoolGrowthPerYear = 0.004;

    /// <summary>A flat annual board operating cost (staff, facilities upkeep, administration) drawn against the ICC money - so the distribution is not purely additive.</summary>
    private const double BoardOperatingCost = 6_000_000;

    private readonly record struct NationRevenue(CountryProfile Profile, Team Team, double Gross, double Costs, double Net);

    public IEnumerable<GameEvent> DistributeAnnually(WorldState world, GameCalendar calendar, DateOnly date)
    {
        var nationalByCountry = world.Teams.Values
            .Where(t => t.IsNational && !string.IsNullOrEmpty(t.Country))
            .GroupBy(t => t.Country, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);

        if (nationalByCountry.Count == 0) yield break;

        var profiles = world.CountryProfiles.Values
            .Where(p => nationalByCountry.ContainsKey(p.Nationality))
            .OrderBy(p => p.Nationality, StringComparer.Ordinal)
            .ToList();
        if (profiles.Count == 0) yield break;

        int yearsSinceStart = Math.Max(0, date.Year - calendar.StartDate.Year);
        double pool = BasePoolAtIndexOne * Math.Clamp(world.MarketIndex, 0.5, 6.0);
        double associateShare = Math.Clamp(AssociatePoolStartShare + yearsSinceStart * AssociatePoolGrowthPerYear, AssociatePoolStartShare, 0.35);

        var full = profiles.Where(p => p.Membership == MembershipStatus.FullMember).ToList();
        var assoc = profiles.Where(p => p.Membership == MembershipStatus.Associate).ToList();

        double associatePool = assoc.Count == 0 ? 0 : pool * associateShare;
        double fullPool = pool - associatePool;

        // ---- Full-member split: four weighted components, each normalised to a share of fullPool ----
        var rawScore = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var p in full)
        {
            var team = nationalByCountry[p.Nationality];
            double history = Math.Clamp(p.CricketHistoryWeight, 0, 100) / 100.0;
            double iccPerformance = Math.Clamp(team.Reputation.Worldwide * 0.7 + RecentMajorPoints(world, team, date) * 30, 0, 100) / 100.0;
            double commercial = Math.Clamp(p.MediaIntensity * 0.5 + team.Reputation.Worldwide * 0.5, 0, 100) / 100.0;
            // history 0.25 + ICC performance 0.25 + commercial 0.20 + an equal Full-Member-status slice 0.30
            // (a flat term - after normalisation it distributes 30% of the pool equally).
            rawScore[p.Nationality] = history * 0.25 + iccPerformance * 0.25 + commercial * 0.20 + 0.30;
        }
        // Normalise to shares.
        double totalRaw = rawScore.Values.Sum();
        var fullShare = full.ToDictionary(p => p.Nationality, p => totalRaw <= 0 ? 1.0 / full.Count : rawScore[p.Nationality] / totalRaw, StringComparer.Ordinal);

        // Deviation: cap the top nation and redistribute the surplus pro-rata among the others.
        if (fullShare.Count > 1)
        {
            var top = fullShare.OrderByDescending(kv => kv.Value).First();
            if (top.Value > TopNationShareCap)
            {
                double surplus = top.Value - TopNationShareCap;
                fullShare[top.Key] = TopNationShareCap;
                double othersTotal = fullShare.Where(kv => kv.Key != top.Key).Sum(kv => kv.Value);
                foreach (var key in fullShare.Keys.Where(k => k != top.Key).ToList())
                    fullShare[key] += surplus * (othersTotal <= 0 ? 1.0 / (fullShare.Count - 1) : fullShare[key] / othersTotal);
            }
        }

        // ---- Apply ----
        var results = new List<NationRevenue>();
        foreach (var p in profiles)
        {
            var team = nationalByCountry[p.Nationality];
            double gross = p.Membership == MembershipStatus.FullMember
                ? fullPool * fullShare.GetValueOrDefault(p.Nationality, 0)
                : assoc.Count == 0 ? 0 : associatePool / assoc.Count;
            gross = Math.Round(gross, 0);

            double coachCosts = world.CoachingContracts
                .Where(k => k.TeamId == team.Id && k.Status == ContractStatus.Active)
                .Sum(k => k.AnnualSalary);
            double costs = Math.Round(coachCosts + BoardOperatingCost, 0);
            double net = gross - costs;

            team.Finances.Budget += net;

            // A funded board invests more in the youth pathway - move BoardYouthInvestment toward a
            // target set by this year's gross, ~15% of the way.
            double target = Math.Clamp(40 + gross / 4_000_000, 40, p.Membership == MembershipStatus.FullMember ? 88 : 70);
            p.BoardYouthInvestment = Math.Clamp(p.BoardYouthInvestment + (target - p.BoardYouthInvestment) * 0.15, 20, 95);

            results.Add(new NationRevenue(p, team, gross, costs, net));
        }

        // ---- News: one summary line, plus a line for any board genuinely in the red ----
        var ordered = results.OrderByDescending(r => r.Gross).ToList();
        var headline = string.Join(", ", ordered.Take(3).Select(r => $"{r.Profile.Nationality} ({r.Gross / 1_000_000:N0}M)"));
        yield return new GameEvent(date, GameEventType.IccRevenueDistributed,
            $"The ICC distributes {pool / 1_000_000:N0}M to its members this year - {headline} the biggest shares; the {assoc.Count} associate boards take {associateShare:P0} of the pool between them.",
            null);

        foreach (var r in ordered.Where(r => r.Team.Finances.Budget < -8_000_000).OrderBy(r => r.Team.Finances.Budget))
            yield return new GameEvent(date, GameEventType.IccRevenueDistributed,
                $"{r.Profile.Nationality}'s board is running a deficit - its ICC share does not cover its coaching and operating costs.",
                r.Team.Id);
    }

    /// <summary>0..~1: how well this nation's team has done in ICC major events (IsMajor internationals) in the last ~4 years - a title is worth most, a final next.</summary>
    private static double RecentMajorPoints(WorldState world, Team team, DateOnly date)
    {
        double points = 0;
        foreach (var s in world.CompetitionSeasons.Where(s => s.IsCompleted && s.Year >= date.Year - 4))
        {
            var comp = world.Competitions.FirstOrDefault(c => c.Id == s.CompetitionId);
            if (comp is null || !comp.IsMajor) continue;
            if (s.ChampionTeamId == team.Id) points += 1.0;
            else if (s.RunnerUpTeamId == team.Id) points += 0.5;
            else if (s.PlayoffQualifiedTeamIds.Contains(team.Id)) points += 0.2;
        }
        return Math.Clamp(points / 3.0, 0, 1);
    }
}
