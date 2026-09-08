using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Seven-suggestions pass (S7): an Associate nation earning ICC Full Membership and Test status.
///
/// Real process (verified): Article 2.1 sets six criteria areas (governance, performance,
/// participation & domestic structures, infrastructure, development programmes, general); Ireland
/// and Afghanistan were the last two promoted (unanimous ICC Council vote, 22 June 2017). And
/// **Article 2.7: Full Member status is IRREVOCABLE** - there is no demotion mechanism, so a
/// promoted nation stays promoted whatever its later results. This service keeps that: the roster
/// of Test nations only ever grows.
///
/// The grant is a rare, telegraphed long-sim milestone. An associate accumulates
/// <see cref="CountryProfile.FullMembershipCredit"/> from a real, sustained record - qualifier
/// results, its national-team reputation, the board's youth investment (funded by
/// <see cref="IccRevenueService"/>) and financial stability. Crossing a first threshold makes it a
/// public CANDIDATE; only after a couple of years as a candidate, with the credit above a higher
/// bar, is Full Membership granted. On the grant the nation joins the World Test Championship and
/// the ODI Championship and moves onto the Full-Member ICC-revenue tier automatically.
///
/// RNG-free. Runs once a year.
/// </summary>
public sealed class FullMembershipService
{
    public const double CandidacyThreshold = 40;
    public const double GrantThreshold = 100;
    public const int MinYearsAsCandidate = 2;

    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date)
    {
        var associateComp = world.Competitions.FirstOrDefault(c => c.Name.Contains("Associate Qualifier"));
        var recentAssocSeasons = associateComp is null
            ? new List<CompetitionSeason>()
            : world.CompetitionSeasons.Where(s => s.CompetitionId == associateComp.Id && s.IsCompleted && s.Year >= date.Year - 3).ToList();

        foreach (var profile in world.CountryProfiles.Values
                     .Where(p => p.Membership == MembershipStatus.Associate)
                     .OrderBy(p => p.Nationality, StringComparer.Ordinal))
        {
            var team = world.Teams.Values.FirstOrDefault(t => t.IsNational && string.Equals(t.Country, profile.Nationality, StringComparison.OrdinalIgnoreCase));
            if (team is null) continue;

            // --- this year's progress ---
            double qualifierPoints =
                recentAssocSeasons.Count(s => s.ChampionTeamId == team.Id) * 12
                + recentAssocSeasons.Count(s => s.RunnerUpTeamId == team.Id) * 6
                + recentAssocSeasons.Count(s => s.PlayoffQualifiedTeamIds.Contains(team.Id)) * 2;

            double standing = Math.Clamp(team.Reputation.Continental * 0.6 + team.Reputation.Worldwide * 0.4, 0, 100);
            double infrastructure = Math.Clamp(profile.BoardYouthInvestment, 0, 100);
            bool financiallyStable = team.Finances.Budget > -5_000_000;
            bool governanceStable = profile.PoliticalStability >= 40;

            double progress =
                qualifierPoints
                + standing * 0.14
                + infrastructure * 0.10
                + (financiallyStable ? 4 : -3)
                + (governanceStable ? 2 : -2);

            // Below a floor of genuine progress the credit erodes - a stalled programme slips back.
            if (progress < 6) progress -= 5;

            profile.FullMembershipCredit = Math.Max(0, profile.FullMembershipCredit + progress);

            // --- candidacy ---
            if (profile.FullMembershipCredit >= CandidacyThreshold && profile.FullMembershipCandidateSince is null)
            {
                profile.FullMembershipCandidateSince = date;
                yield return new GameEvent(date, GameEventType.IccFullMembershipGranted,
                    $"{profile.Nationality} is now a serious candidate for ICC Full Membership - the qualifier results, the infrastructure and the finances all point the right way. A grant is still some years off.",
                    team.Id);
                continue;
            }

            // --- the grant ---
            bool longEnoughACandidate = profile.FullMembershipCandidateSince is { } since && since <= date.AddYears(-MinYearsAsCandidate);
            if (profile.FullMembershipCredit >= GrantThreshold && longEnoughACandidate && financiallyStable && governanceStable)
            {
                profile.Membership = MembershipStatus.FullMember;
                profile.CricketHistoryWeight = Math.Max(profile.CricketHistoryWeight, 30);
                profile.EconomicScale = Math.Max(profile.EconomicScale, 0.55);
                team.Reputation.Adjust(domesticDelta: 6, continentalDelta: 10, worldwideDelta: 8);

                AdmitToChampionships(world, team, date);

                yield return new GameEvent(date, GameEventType.IccFullMembershipGranted,
                    $"HISTORIC: {profile.Nationality} is granted ICC Full Membership and Test status - the first new Test nation in years. They join the World Test Championship and the ODI Championship, and move onto the Full-Member revenue tier. The status is permanent.",
                    team.Id);
            }
        }
    }

    /// <summary>Add the newly-promoted nation to next year's World Test Championship and ODI Championship rosters (via PlannedRosters, which CreateSeasonsForYear consults first).</summary>
    private static void AdmitToChampionships(WorldState world, Team team, DateOnly date)
    {
        foreach (var name in new[] { "World Test Championship", "ODI Championship" })
        {
            var comp = world.Competitions.FirstOrDefault(c => c.Name == name);
            if (comp is null) continue;

            int nextYear = date.Year + 1;
            var basis = world.PlannedRosters.TryGetValue((comp.Id, nextYear), out var planned) && planned.Count >= 2
                ? planned
                : world.CompetitionSeasons.Where(s => s.CompetitionId == comp.Id && s.ParticipatingTeamIds.Count >= 2)
                      .OrderByDescending(s => s.Year).FirstOrDefault()?.ParticipatingTeamIds.ToList()
                  ?? new List<Guid>();

            if (!basis.Contains(team.Id)) basis.Add(team.Id);
            world.PlannedRosters[(comp.Id, nextYear)] = basis;
        }
    }
}
