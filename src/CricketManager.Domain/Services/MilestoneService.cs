using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.5: turns a career landmark into news. Reads the career-stats cache
/// CareerStatsService keeps current, comparing a BEFORE snapshot (taken by the caller ahead of
/// CareerStatsService.ApplyMatch) with the totals after this match, so a crossing is caught
/// exactly once, on the ball it happens.
///
/// What counts as a milestone is deliberately conservative - the real ones a broadcaster puts on
/// screen: a cap landmark, a run/wicket landmark, a maiden century, a genuine career-best.
/// </summary>
public sealed class MilestoneService
{
    private static readonly int[] CapLandmarks = { 25, 50, 100, 150, 200, 250, 300 };
    private static readonly int[] RunLandmarks = { 1_000, 2_500, 5_000, 7_500, 10_000, 12_500, 15_000, 20_000 };
    private static readonly int[] WicketLandmarks = { 50, 100, 200, 300, 400, 500, 700 };

    /// <summary>
    /// Detects milestones for the players in this match. `before` is the pre-match snapshot from
    /// CareerStatsService.Snapshot; `world.CareerStats` must already have this match folded in.
    /// </summary>
    public IReadOnlyList<GameEvent> Detect(
        WorldState world, MatchRecords records, IReadOnlyDictionary<Guid, Player> players, MatchFormat format,
        DateOnly date, IReadOnlyDictionary<Guid, CareerSnapshot> before)
    {
        var stats = new CareerStatsService();
        var events = new List<GameEvent>();
        string formatName = format.ToString();

        // Distinct players who did something this match.
        var involved = records.Batting.Select(b => b.PlayerId)
            .Concat(records.Bowling.Select(b => b.PlayerId))
            .Distinct();

        foreach (var id in involved)
        {
            if (!players.TryGetValue(id, out var player)) continue;
            var now = stats.For(world, id, format);
            if (now is null) continue;
            var was = before.TryGetValue(id, out var b) ? b : new CareerSnapshot(0, 0, 0, 0, 0);

            // --- caps ---
            int capsNow = player.Experience.MatchesIn(format);
            if (CapLandmarks.Contains(capsNow))
                events.Add(Milestone(date, id, player.Id,
                    $"{player.FullName} wins his {Ordinal(capsNow)} {formatName} cap for {TeamName(world, player)}."));

            // --- career runs ---
            foreach (var landmark in RunLandmarks)
                if (was.Runs < landmark && now.Runs >= landmark)
                    events.Add(Milestone(date, id, player.Id,
                        $"{player.FullName} goes past {landmark:N0} career {formatName} runs."));

            // --- career wickets ---
            foreach (var landmark in WicketLandmarks)
                if (was.Wickets < landmark && now.Wickets >= landmark)
                    events.Add(Milestone(date, id, player.Id,
                        $"{player.FullName} reaches {landmark} career {formatName} wickets."));

            // --- maiden century ---
            if (was.Hundreds == 0 && now.Hundreds >= 1
                && records.Batting.Any(bi => bi.PlayerId == id && bi.Runs >= 100))
                events.Add(Milestone(date, id, player.Id,
                    $"{player.FullName} brings up his maiden {formatName} century."));

            // --- career-best score ---
            var bestThisMatch = records.Batting.Where(bi => bi.PlayerId == id).Select(bi => bi.Runs).DefaultIfEmpty(0).Max();
            if (bestThisMatch > was.HighestScore && bestThisMatch >= 100 && was.HighestScore > 0)
                events.Add(Milestone(date, id, player.Id,
                    $"{player.FullName} posts a career-best {formatName} score of {bestThisMatch}."));

            // --- career-best bowling ---
            var bestSpell = records.Bowling.Where(bs => bs.PlayerId == id)
                .OrderByDescending(bs => bs.Wickets).ThenBy(bs => bs.RunsConceded).FirstOrDefault();
            if (bestSpell is { Wickets: >= 5 } && $"{bestSpell.Wickets}/{bestSpell.RunsConceded}" == now.BestBowlingFigures)
            {
                // Only report a five-for that is genuinely the career best (the figures string just landed on it).
                events.Add(Milestone(date, id, player.Id,
                    $"{player.FullName} takes {bestSpell.Wickets}/{bestSpell.RunsConceded} - a career-best {formatName} return."));
            }
        }

        return events;
    }

    private static GameEvent Milestone(DateOnly date, Guid subject, Guid player, string headline) =>
        new(date, GameEventType.PlayerMilestone, headline, subject);

    private static string TeamName(WorldState world, Player p) =>
        p.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var t) ? t.Name : "his side";

    private static string Ordinal(int n) => (n % 100 is >= 11 and <= 13) ? $"{n}th" : (n % 10) switch
    {
        1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th"
    };
}
