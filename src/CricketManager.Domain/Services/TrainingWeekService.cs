using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>What kind of week it is for a team, training-wise.</summary>
public enum TrainingWeekType
{
    /// <summary>A fixture is imminent - the week is match prep, travel and recovery, not skill development.</summary>
    MatchWeek,
    /// <summary>In or close to a competition window but no match this week - a genuine training week.</summary>
    PreparationWeek,
    /// <summary>No competition on the horizon - the off-season, when a player has the time to genuinely rebuild his game and his fitness.</summary>
    OffSeason
}

/// <summary>
/// Phase 8, Slice 8.5: a weekly training-calendar layer on top of Phase 5's monthly TrainingService.
/// Deliberately incremental, not a rewrite: the monthly tick still does the bulk of directed
/// development (one twelfth of a year, every month). This adds a small SEASONAL MODULATION on the
/// weekly tick - the real-cricket fact that a young player makes most of his gains in pre-season
/// and the off-season, when there is time to work on technique and fitness, and almost none in a
/// match week, when the week is travel, prep and recovery.
///
/// The full weekly calendar with match prep / recovery / individual focus / rest as an explicit
/// per-player schedule, and the three-tier training camps, stay deferred - camps to Phase 9
/// (they need franchise contracts for the eligibility rules). This is the honest, useful core
/// that the existing tick structure can actually drive.
/// </summary>
public sealed class TrainingWeekService
{
    private readonly CompetitionCalendarService _calendar = new();

    /// <summary>How close a fixture has to be for the week to count as a match week.</summary>
    private const int MatchWeekWindowDays = 3;

    /// <summary>How far ahead a competition window can be for the weeks before it to count as preparation rather than off-season.</summary>
    private const int PreSeasonLeadDays = 35;

    public TrainingWeekType ClassifyWeek(Team team, WorldState world, DateOnly date)
    {
        bool matchThisWeek = world.Fixtures.Any(f =>
            f.Status == FixtureStatus.Scheduled
            && (f.HomeTeamId == team.Id || f.AwayTeamId == team.Id)
            && Math.Abs(f.ScheduledDate.DayNumber - date.DayNumber) <= MatchWeekWindowDays);
        if (matchThisWeek) return TrainingWeekType.MatchWeek;

        foreach (var competition in world.Competitions)
        {
            var staging = _calendar.GetStaging(competition, date.Year) ?? _calendar.GetStaging(competition, date.Year + 1);
            if (staging is null) continue;

            bool teamInIt = world.CompetitionSeasons.Any(s =>
                s.CompetitionId == competition.Id && s.ParticipatingTeamIds.Contains(team.Id)
                && (s.Year == date.Year || s.Year == date.Year + 1));
            if (!teamInIt) continue;

            if (_calendar.IsInWindow(competition, date)) return TrainingWeekType.PreparationWeek;
            if (date < staging.StartDate && date >= staging.StartDate.AddDays(-PreSeasonLeadDays))
                return TrainingWeekType.PreparationWeek;
        }

        return TrainingWeekType.OffSeason;
    }

    /// <summary>
    /// One week of calendar-modulated development for a player. Small by construction - it is a
    /// modulation on top of the monthly programme, not a second full one. Only genuinely helps a
    /// young player with real headroom; a match week never adds development at all.
    /// Returns true if an attribute actually moved.
    /// </summary>
    public bool ApplyTrainingWeek(Player player, TrainingWeekType weekType, Random random, DateOnly date)
    {
        if (player.IsRetired || weekType == TrainingWeekType.MatchWeek) return false;
        if (player.CurrentInjury is { } inj && inj.Severity >= InjurySeverity.Moderate) return false;

        int age = player.Age(date);
        double youth = age <= 19 ? 1.0 : age <= 23 ? 0.6 : age <= 27 ? 0.25 : 0.05;
        double headroom = Math.Clamp((player.PotentialAbility - player.CurrentAbility) / 40.0, 0, 1);
        if (headroom <= 0) return false;

        double weekWeight = weekType == TrainingWeekType.OffSeason ? 0.022 : 0.012;
        double chance = weekWeight * youth * (0.3 + headroom);
        if (random.NextDouble() >= chance) return false;

        NudgeAny(player, random);
        player.CurrentAbility = Math.Clamp(player.CurrentAbility + 1, 1, player.PotentialAbility);
        player.RecalculateFormatSuitability();
        new RoleTraitDeriver().ApplyTo(player);
        return true;
    }

    private static void NudgeAny(Player p, Random random)
    {
        int C(int v) => Math.Clamp(v + 1, 1, 20);
        bool bats = p.PrimaryRole is not PlayerRole.Bowler;
        bool bowls = p.BowlingRole != BowlingRoleType.NotABowler;

        // Build the list of channels this player can actually work on, then pick one.
        var channels = new List<Action>();
        if (bats)
        {
            channels.Add(() => p.Batting.Technique = C(p.Batting.Technique));
            channels.Add(() => p.Batting.ShotSelection = C(p.Batting.ShotSelection));
        }
        if (bowls)
        {
            channels.Add(() => p.Bowling.Accuracy = C(p.Bowling.Accuracy));
            channels.Add(() => p.Bowling.Variation = C(p.Bowling.Variation));
        }
        channels.Add(() => { p.Physical.Fitness = C(p.Physical.Fitness); p.Physical.Stamina = C(p.Physical.Stamina); });
        channels.Add(() => p.Fielding.GroundFielding = C(p.Fielding.GroundFielding));

        channels[random.Next(channels.Count)]();
    }
}
