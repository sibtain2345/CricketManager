using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 9, Slice 9.8: the three-tier training camp - International, Club, Franchise - finally
/// buildable now that franchise CONTRACTS exist (the blocker named since Phase 5 Part 1).
///
/// The camp-eligibility rule that needed franchise contracts: when a national or club camp runs
/// during a window in which a player is also under an active franchise-league contract whose own
/// window overlaps, he is EXCUSED - the real "your franchise pays your wages that month, they get
/// first call" tension.
///
/// Each camp: a short, targeted development boost for the young players in the squad/pool, plus a
/// team-cohesion (DressingRoomHarmony) lift - a camp is as much about a group gelling as about
/// skills. A freshly auction-assembled franchise squad gets the biggest cohesion benefit because
/// it has the furthest to travel.
///
/// Runs on the quarterly tick, ahead of the windows it prepares for.
/// </summary>
public sealed class TrainingCampService
{
    private readonly CompetitionCalendarService _calendar = new();

    /// <summary>A camp runs in the ~6 weeks before its target window opens.</summary>
    private const int CampLeadDays = 42;

    public IEnumerable<GameEvent> RunCamps(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        foreach (var competition in world.Competitions)
        {
            var staging = _calendar.GetStaging(competition, date.Year) ?? _calendar.GetStaging(competition, date.Year + 1);
            if (staging is null) continue;
            bool campWindow = date >= staging.StartDate.AddDays(-CampLeadDays) && date < staging.StartDate;
            if (!campWindow) continue;

            var campType = competition.Scope switch
            {
                CompetitionScope.International => TrainingCampType.International,
                CompetitionScope.FranchiseLeague => TrainingCampType.Franchise,
                _ => TrainingCampType.Club
            };

            var season = world.CompetitionSeasons
                .Where(s => s.CompetitionId == competition.Id && (s.Year == date.Year || s.Year == date.Year + 1))
                .OrderByDescending(s => s.Year).FirstOrDefault();
            if (season is null) continue;

            foreach (var teamId in season.ParticipatingTeamIds)
            {
                if (!world.Teams.TryGetValue(teamId, out var team)) continue;

                var invited = team.SquadPlayerIds
                    .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                    .Where(p => p is not null && !p!.IsRetired && p.CurrentInjury is null)
                    .Select(p => p!)
                    .ToList();

                // The franchise-contract eligibility rule.
                int excused = 0;
                if (campType is TrainingCampType.International or TrainingCampType.Club)
                {
                    var franchiseCommitted = invited
                        .Where(p => HasOverlappingFranchiseCommitment(world, p, date))
                        .ToList();
                    excused = franchiseCommitted.Count;
                    invited = invited.Except(franchiseCommitted).ToList();
                }

                if (invited.Count == 0) continue;

                // Development boost - the young players in the camp.
                int developed = 0;
                foreach (var p in invited.Where(p => p.Age(date) <= 26))
                {
                    double headroom = Math.Clamp((p.PotentialAbility - p.CurrentAbility) / 40.0, 0, 1);
                    double chance = (campType == TrainingCampType.Franchise ? 0.10 : 0.16) * (0.3 + headroom);
                    if (random.NextDouble() < chance)
                    {
                        p.CurrentAbility = Math.Clamp(p.CurrentAbility + 1, 1, p.PotentialAbility);
                        p.RecalculateFormatSuitability();
                        developed++;
                        if (world.RecentDevelopmentByPlayer.TryGetValue(p.Id, out var acc))
                            world.RecentDevelopmentByPlayer[p.Id] = acc + 0.5;
                        else world.RecentDevelopmentByPlayer[p.Id] = 0.5;
                    }
                }

                // Cohesion - biggest for a freshly-assembled franchise squad.
                double cohesion = campType switch
                {
                    TrainingCampType.Franchise => 6,
                    TrainingCampType.International => 3,
                    _ => 2
                };
                team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony + cohesion, 0, 100);
                team.Morale.Adjust(cohesion * 0.4);

                string excusedNote = excused > 0 ? $", {excused} excused on franchise duty" : "";
                events.Add(new GameEvent(date, GameEventType.TrainingCampCallUp,
                    $"{team.Name} hold a {campType} training camp ({invited.Count} in{excusedNote}); {developed} players took a step forward.",
                    team.Id, competition.Id));
            }
        }

        return events;
    }

    private static bool HasOverlappingFranchiseCommitment(WorldState world, Player player, DateOnly campDay)
    {
        var franchiseContract = world.PlayerContracts.FirstOrDefault(c =>
            c.PlayerId == player.Id && c.Kind == ContractKind.Franchise && c.Status == ContractStatus.Active);
        if (franchiseContract?.CompetitionId is not { } compId) return false;

        var franchiseComp = world.Competitions.FirstOrDefault(c => c.Id == compId);
        if (franchiseComp is null) return false;

        // Is the camp running while the player is at (or has just come off) a franchise season? A
        // player is committed during the franchise window plus a short recovery/break tail after -
        // "he has just finished a grueling franchise season, he is not at your pre-season camp".
        var fStaging = _calendarStatic.GetStaging(franchiseComp, campDay.Year);
        if (fStaging is null) return false;
        return campDay >= fStaging.StartDate.AddDays(-10) && campDay <= fStaging.EndDate.AddDays(28);
    }

    private static readonly CompetitionCalendarService _calendarStatic = new();
}
