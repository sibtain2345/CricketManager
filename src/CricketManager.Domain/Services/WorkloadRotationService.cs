using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-6 (section F): who to rest for a match, and why.
///
/// The dead-rubber flag from Phase 6 is a real signal but must never be the ONLY one. Resting a
/// premium player weighs:
/// - his actual physical condition and injury load (a niggle, high InjuryProneness, age),
/// - his recent match load this season (WorldState.MatchesThisSeason),
/// - the real fixture calendar - how many more matches this side has in the next fortnight, and
///   whether there is a genuine gap after this one (a lot of cricket coming = more reason to
///   manage him; a long break after = less).
///
/// A dead rubber lowers the bar for resting someone; a big match raises it (you play your best).
/// </summary>
public sealed class WorkloadRotationService
{
    /// <summary>
    /// Post-Phase-16 completion pass (§17.7): is this side's calendar genuinely congested right
    /// now - enough matches packed in that a board would expect its coach to be rotating? Reads
    /// the fixture list only. Used by BoardRelationshipService to raise a workload-management
    /// complaint when a coach plays his key men into the ground.
    /// </summary>
    public bool IsCalendarCongested(WorldState world, Team team, DateOnly date)
    {
        int inThreeWeeks = world.Fixtures.Count(f => f.Status == FixtureStatus.Scheduled
            && (f.HomeTeamId == team.Id || f.AwayTeamId == team.Id)
            && f.ScheduledDate >= date.AddDays(-7) && f.ScheduledDate <= date.AddDays(14));
        return inThreeWeeks >= 5;
    }

    /// <summary>The ids to leave out of this match's XI - never more than a handful, and only when the squad can still field a legal side without them.</summary>
    public IReadOnlySet<Guid> PlayersToRest(WorldState world, Team team, MatchFormat format, DateOnly date, bool isDeadRubber, double matchImportance)
    {
        var squad = team.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null && !p!.IsRetired && !p.RetiredFormats.Contains(format))
            .Select(p => p!)
            .ToList();
        if (squad.Count < 15) return new HashSet<Guid>(); // no depth to rotate

        // Calendar pressure: matches for this team in the next 14 days, and whether there's a
        // 10+ day gap immediately after this one.
        var upcoming = world.Fixtures
            .Where(f => f.Status == FixtureStatus.Scheduled && (f.HomeTeamId == team.Id || f.AwayTeamId == team.Id)
                        && f.ScheduledDate > date && f.ScheduledDate <= date.AddDays(14))
            .OrderBy(f => f.ScheduledDate)
            .ToList();
        int matchesInFortnight = upcoming.Count;
        bool longBreakAfter = upcoming.Count == 0 || (upcoming[0].ScheduledDate.DayNumber - date.DayNumber) >= 10;

        // Test cricket has fewer, more spaced matches - a heavier per-match toll but far less
        // fixture congestion, so rotation is rarer and driven by condition, not the calendar.
        int congestionThreshold = format == MatchFormat.Test ? 1 : 2;

        // How willing this side is to rest anyone at all right now.
        double appetite =
            (isDeadRubber ? 0.55 : 0.0)
            + Math.Clamp((matchesInFortnight - congestionThreshold) * 0.18, 0, 0.5)
            - Math.Clamp((matchImportance - 55) / 45.0, 0, 1) * 0.4      // a big game: play your best
            - (longBreakAfter ? 0.20 : 0.0);                            // rest is coming anyway

        appetite += (team.ManagerPreferences.RotateInDeadRubbers ? 0.05 : -0.15);

        if (appetite <= 0.1) return new HashSet<Guid>();

        var ranked = new PlayerSelectionEvaluator().RankAvailable(squad, format, date, null)
            .Select(s => s.PlayerId).ToList();
        var topIds = ranked.Take(7).ToHashSet();

        int maxToRest = appetite >= 0.75 ? 4 : appetite >= 0.45 ? 3 : appetite >= 0.25 ? 2 : 1;

        var toRest = squad
            .Where(p => topIds.Contains(p.Id))              // only ever manage the players worth managing
            .Select(p => (Player: p, Load: RestNeed(p, world, date, format)))
            .Where(x => x.Load > 0)
            .OrderByDescending(x => x.Load)
            .Take(maxToRest)
            .Select(x => x.Player.Id)
            .ToHashSet();

        return toRest;
    }

    /// <summary>
    /// How much this specific player needs a rest right now, 0..~100. Condition and load, not the
    /// match. A fresh, fit 24-year-old scores ~0; a 34-year-old quick carrying a niggle with a
    /// heavy season behind him scores high.
    /// </summary>
    /// <param name="format">
    /// Follow-up pass (§5.4): when supplied, reads the player's own standing WorkloadPriority
    /// against THIS match's format - a genuinely stronger signal than the per-fixture congestion
    /// read alone, since it reflects a season-long plan rather than just the next fortnight.
    /// Prioritising the OTHER format raises his rest need here; prioritising THIS one protects him.
    /// Null (the default and every pre-§5.4 caller) is a no-op.
    /// </param>
    public double RestNeed(Player player, WorldState world, DateOnly date, MatchFormat? format = null)
    {
        double need = 0;

        if (format is { } f && player.WorkloadPriority != WorkloadPriority.Balanced)
        {
            bool thisIsRedBall = f == MatchFormat.Test;
            bool prioritisesThis = (player.WorkloadPriority == WorkloadPriority.PrioritiseRedBall) == thisIsRedBall;
            need += prioritisesThis ? -20 : 22;
        }

        // A niggle he could play through is exactly the case to manage.
        if (player.CurrentInjury is { } inj && inj.IsActiveOn(date) && inj.Severity <= InjurySeverity.Minor)
            need += 45;

        // Recent load this season.
        int played = world.MatchesThisSeason.TryGetValue(player.Id, out var n) ? n : 0;
        need += Math.Clamp((played - 8) * 3.5, 0, 35);

        // Age and injury-proneness - an older, brittle body needs more managing.
        int age = player.Age(date);
        need += Math.Clamp((age - 30) * 3.0, 0, 21);
        need += Math.Clamp((player.Physical.InjuryProneness - 10) * 1.4, 0, 14);

        // A genuine quick's body takes more of a battering.
        bool quick = player.BowlingRole != BowlingRoleType.NotABowler && player.Bowling.Pace >= 14;
        if (quick) need *= 1.25;

        // Low fitness compounds everything.
        double fitness = AbilityScale.AttributeToHundred(player.Physical.Fitness);
        need *= 1.0 + Math.Clamp((55 - fitness) / 55.0, 0, 1) * 0.4;

        return Math.Clamp(need, 0, 100);
    }
}
