using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.7: the record-progression database - spec Section 15, deferred since Phase 3b
/// because it is its own feature.
///
/// GroundRecordsService can already derive "who holds this record now" on demand. What was
/// missing is the CHAIN: who held it before, and how long theirs stood - and a "record broken"
/// news item when one falls. This keeps a small book (WorldState.RecordBook) of the marquee
/// all-time records, updates it after every match, and emits an event with the story when a
/// record changes hands.
/// </summary>
public sealed class RecordProgressionService
{
    /// <summary>Reviews one completed match's records against the all-time book. Global scope only ("" scope) - a per-competition/per-ground book is a later extension the RecordEntry.Scope field already allows for.</summary>
    public IReadOnlyList<GameEvent> ReviewMatch(WorldState world, MatchRecords records, DateOnly date)
    {
        var events = new List<GameEvent>();

        // --- team totals ---
        foreach (var ti in records.TeamInnings)
        {
            events.AddIfNotNull(TryUpdate(world, RecordCategory.HighestTeamTotal, "", ti.Runs,
                ti.BattingTeamName, null, ti.BattingTeamId, date, higherIsBetter: true,
                minToQualify: 1, mustBeAllOut: false));

            // A "low total" record only counts a completed (all-out) innings.
            if (ti.Wickets >= 10 && !ti.Declared)
                events.AddIfNotNull(TryUpdate(world, RecordCategory.LowestTeamTotal, "", ti.Runs,
                    ti.BattingTeamName, null, ti.BattingTeamId, date, higherIsBetter: false,
                    minToQualify: 1, mustBeAllOut: true));
        }

        // --- highest individual score ---
        var topKnock = records.Batting.OrderByDescending(b => b.Runs).FirstOrDefault();
        if (topKnock is not null)
        {
            string name = world.Players.FirstOrDefault(p => p.Id == topKnock.PlayerId)?.FullName ?? "A batter";
            events.AddIfNotNull(TryUpdate(world, RecordCategory.HighestIndividualScore, "", topKnock.Runs,
                name, topKnock.PlayerId, null, date, higherIsBetter: true, minToQualify: 50, mustBeAllOut: false));
        }

        // --- Phase 16 (§15.2): most sixes in an innings ---
        var mostSixes = records.Batting.OrderByDescending(b => b.Sixes).FirstOrDefault();
        if (mostSixes is { Sixes: >= 4 })
        {
            string name = world.Players.FirstOrDefault(p => p.Id == mostSixes.PlayerId)?.FullName ?? "A batter";
            events.AddIfNotNull(TryUpdate(world, RecordCategory.MostSixesInInnings, "", mostSixes.Sixes,
                name, mostSixes.PlayerId, null, date, higherIsBetter: true, minToQualify: 4, mustBeAllOut: false, announce: false));
        }

        // --- Phase 16: fastest fifty / hundred (approximated from the innings' own rate at the
        // point it passed the mark - we do not track the exact ball, so an even run rate is
        // assumed, which is a fair approximation for a fast one) ---
        foreach (var knock in records.Batting.Where(b => b.BallsFaced > 0))
        {
            double sr = knock.Runs / (double)knock.BallsFaced;
            if (knock.Runs >= 50)
            {
                int ballsToFifty = Math.Max(15, (int)Math.Round(50 / Math.Max(0.5, sr)));
                string name = world.Players.FirstOrDefault(p => p.Id == knock.PlayerId)?.FullName ?? "A batter";
                events.AddIfNotNull(TryUpdate(world, RecordCategory.FastestFifty, "", 1000 - ballsToFifty,
                    name, knock.PlayerId, null, date, higherIsBetter: true, minToQualify: 0, mustBeAllOut: false, announce: false));
            }
            if (knock.Runs >= 100)
            {
                int ballsToHundred = Math.Max(30, (int)Math.Round(100 / Math.Max(0.5, sr)));
                string name = world.Players.FirstOrDefault(p => p.Id == knock.PlayerId)?.FullName ?? "A batter";
                events.AddIfNotNull(TryUpdate(world, RecordCategory.FastestHundred, "", 1000 - ballsToHundred,
                    name, knock.PlayerId, null, date, higherIsBetter: true, minToQualify: 0, mustBeAllOut: false, announce: false));
            }
        }

        // --- Phase 16: best economy in a spell of real length ---
        var tightestSpell = records.Bowling
            .Where(b => b.OversBowled >= 4)
            .OrderBy(b => b.RunsConceded / Math.Max(1.0, b.OversBowled)).FirstOrDefault();
        if (tightestSpell is not null)
        {
            double economy = tightestSpell.RunsConceded / Math.Max(1.0, tightestSpell.OversBowled);
            string name = world.Players.FirstOrDefault(p => p.Id == tightestSpell.PlayerId)?.FullName ?? "A bowler";
            events.AddIfNotNull(TryUpdate(world, RecordCategory.BestEconomyInInnings, "", 1000 - economy * 100,
                name, tightestSpell.PlayerId, null, date, higherIsBetter: true, minToQualify: 0, mustBeAllOut: false, announce: false));
        }

        // --- Phase 16 (§15.2): most catches by one fielder in a match ---
        var topCatcher = records.Fielding
            .GroupBy(f => f.PlayerId)
            .Select(g => (PlayerId: g.Key, Catches: g.Sum(f => f.Catches)))
            .OrderByDescending(x => x.Catches).FirstOrDefault();
        if (topCatcher.Catches >= 4)
        {
            string name = world.Players.FirstOrDefault(p => p.Id == topCatcher.PlayerId)?.FullName ?? "A fielder";
            events.AddIfNotNull(TryUpdate(world, RecordCategory.MostCatchesInMatch, "", topCatcher.Catches,
                name, topCatcher.PlayerId, null, date, higherIsBetter: true, minToQualify: 4, mustBeAllOut: false, announce: false));
        }

        // --- Phase 16 (§15.2): most ducks in one team's innings (the wooden-spoon record) ---
        int ducks = records.Batting.Count(b => b.Runs == 0 && !b.NotOut && b.Dismissal != DismissalType.NotOut);
        if (ducks >= 4)
            events.AddIfNotNull(TryUpdate(world, RecordCategory.MostDucksInInnings, "", ducks,
                "A batting card", null, null, date, higherIsBetter: true, minToQualify: 4, mustBeAllOut: false, announce: false));

        // --- best bowling in an innings ---
        var bestSpell = records.Bowling
            .OrderByDescending(b => b.Wickets).ThenBy(b => b.RunsConceded).FirstOrDefault();
        if (bestSpell is { Wickets: >= 4 })
        {
            string name = world.Players.FirstOrDefault(p => p.Id == bestSpell.PlayerId)?.FullName ?? "A bowler";
            // Encode figures as (wickets * 1000 - runs) so "more wickets, then fewer runs" is a
            // single higher-is-better number. DescribeValue decodes it for display.
            double figures = bestSpell.Wickets * 1000 - bestSpell.RunsConceded;
            events.AddIfNotNull(TryUpdate(world, RecordCategory.BestBowlingInInnings, "", figures,
                name, bestSpell.PlayerId, null, date,
                higherIsBetter: true, minToQualify: 0, mustBeAllOut: false));
        }

        // --- highest partnership ---
        var topStand = records.Partnerships.OrderByDescending(p => p.Runs).FirstOrDefault();
        if (topStand is { Runs: >= 100 })
            events.AddIfNotNull(TryUpdate(world, RecordCategory.HighestPartnership, "", topStand.Runs,
                $"{topStand.BatterAName} & {topStand.BatterBName}", null, topStand.BattingTeamId, date,
                higherIsBetter: true, minToQualify: 100, mustBeAllOut: false));

        return events;
    }

    /// <summary>Annual: career-aggregate records across all formats, scanned from the career-stats cache.</summary>
    public IReadOnlyList<GameEvent> ReviewCareerRecords(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();

        var byPlayer = world.CareerStats.Values
            .GroupBy(s => s.PlayerId)
            .ToDictionary(g => g.Key, g => (
                Runs: g.Sum(s => s.Runs),
                Wickets: g.Sum(s => s.Wickets),
                Catches: g.Sum(s => s.Catches)));

        void Check(RecordCategory category, Func<(int Runs, int Wickets, int Catches), int> pick, int min)
        {
            var leader = byPlayer.OrderByDescending(kv => pick(kv.Value)).FirstOrDefault();
            if (leader.Key == Guid.Empty && byPlayer.Count == 0) return;
            int value = byPlayer.Count == 0 ? 0 : pick(leader.Value);
            if (value < min) return;

            string name = world.Players.FirstOrDefault(p => p.Id == leader.Key)?.FullName ?? "A player";
            events.AddIfNotNull(TryUpdate(world, category, "", value, name, leader.Key, null, date,
                higherIsBetter: true, minToQualify: min, mustBeAllOut: false));
        }

        Check(RecordCategory.MostCareerRuns, v => v.Runs, 3_000);
        Check(RecordCategory.MostCareerWickets, v => v.Wickets, 100);
        Check(RecordCategory.MostCareerCatches, v => v.Catches, 50);

        return events;
    }

    private GameEvent? TryUpdate(
        WorldState world, RecordCategory category, string scope, double value,
        string holderName, Guid? holderPlayerId, Guid? holderTeamId, DateOnly date,
        bool higherIsBetter, double minToQualify, bool mustBeAllOut, bool announce = true)
    {
        if (higherIsBetter && value < minToQualify) return null;

        string key = $"{category}|{scope}";

        if (!world.RecordBook.TryGetValue(key, out var current))
        {
            world.RecordBook[key] = new RecordEntry(category, scope, value, holderName, holderPlayerId, holderTeamId, date, Array.Empty<PastRecordHolder>());
            return null; // establishing a record is not "breaking" one - no news
        }

        bool beaten = higherIsBetter ? value > current.Value : value < current.Value;
        if (!beaten) return null;

        // Phase 16: the fine-grained new categories (fastest fifty/hundred, best economy, most
        // sixes) change hands constantly early in a sim - track them in the book, but do NOT flood
        // the news archive with a "record broken" item every other match (the archive window drives
        // narrative / Hall-of-Fame / selection reads, and that much churn genuinely shifts a
        // long-run world). Only a genuinely dramatic break is news; the rest is a silent update.
        bool dramatic = announce || (category switch
        {
            RecordCategory.FastestFifty => value >= 980,           // <= 20 balls
            RecordCategory.FastestHundred => value >= 962,          // <= 38 balls
            RecordCategory.BestEconomyInInnings => value >= 780,    // <= 2.2 an over
            RecordCategory.MostSixesInInnings => value >= 9,
            RecordCategory.MostCatchesInMatch => value >= 5,
            RecordCategory.MostDucksInInnings => value >= 6,
            _ => true
        });

        // Same holder improving their own record is a smaller story - still logged, no "how long it stood".
        bool sameHolder = (holderPlayerId is not null && holderPlayerId == current.HolderPlayerId)
                          || (holderTeamId is not null && holderTeamId == current.HolderTeamId);

        var past = new List<PastRecordHolder>(current.PreviousHolders);
        if (!sameHolder)
            past.Insert(0, new PastRecordHolder(current.HolderName, current.Value, current.SetOn, date));

        world.RecordBook[key] = current with
        {
            Value = value,
            HolderName = holderName,
            HolderPlayerId = holderPlayerId,
            HolderTeamId = holderTeamId,
            SetOn = date,
            PreviousHolders = past.Take(10).ToList()
        };

        if (!dramatic) return null; // book updated, but not newsworthy this time

        string stood = sameHolder
            ? "extending his own mark"
            : $"the previous mark of {DescribeValue(category, current.Value)} by {current.HolderName} stood for {Years(current.SetOn, date)}";
        return new GameEvent(date, GameEventType.RecordBroken,
            $"New all-time record: {Describe(category)} - {holderName}, {DescribeValue(category, value)}. {stood}.",
            holderPlayerId ?? holderTeamId);
    }

    private static string Describe(RecordCategory c) => c switch
    {
        RecordCategory.HighestTeamTotal => "highest team total",
        RecordCategory.LowestTeamTotal => "lowest completed team total",
        RecordCategory.HighestIndividualScore => "highest individual score",
        RecordCategory.BestBowlingInInnings => "best bowling in an innings",
        RecordCategory.MostCareerRuns => "most career runs",
        RecordCategory.MostCareerWickets => "most career wickets",
        RecordCategory.MostCareerCatches => "most career catches",
        RecordCategory.HighestPartnership => "highest partnership",
        RecordCategory.MostSixesInInnings => "most sixes in an innings",
        RecordCategory.FastestFifty => "fastest fifty",
        RecordCategory.FastestHundred => "fastest hundred",
        RecordCategory.BestEconomyInInnings => "most economical spell",
        RecordCategory.MostCatchesInMatch => "most catches in a match",
        RecordCategory.MostDucksInInnings => "most ducks in an innings",
        _ => c.ToString()
    };

    private static string DescribeValue(RecordCategory c, double v) => c switch
    {
        RecordCategory.BestBowlingInInnings => $"{(int)(v / 1000)}/{(int)(v % 1000)}",
        RecordCategory.FastestFifty or RecordCategory.FastestHundred => $"{(int)Math.Round(1000 - v)} balls",
        RecordCategory.BestEconomyInInnings => $"{(1000 - v) / 100.0:F2} an over",
        _ => ((int)Math.Round(v)).ToString("N0")
    };

    private static string Years(DateOnly from, DateOnly to)
    {
        int days = Math.Max(0, to.DayNumber - from.DayNumber);
        return days >= 365 ? $"{days / 365} year{(days / 365 == 1 ? "" : "s")}" : $"{days} days";
    }
}

file static class RecordEventListExtensions
{
    public static void AddIfNotNull(this List<GameEvent> list, GameEvent? ev)
    {
        if (ev is not null) list.Add(ev);
    }
}
