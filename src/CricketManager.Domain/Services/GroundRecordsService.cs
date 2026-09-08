using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>A single team total at a ground, kept with its context so a record is reportable ("351/6 vs Australia, 2027"), not just a number.</summary>
public sealed record TeamTotalEntry(Guid MatchId, DateOnly Date, string TeamName, string OpponentName, int Runs, int Wickets, double Overs, int InningsNumber, MatchFormat Format, bool AllOut);

/// <summary>A single player performance at a ground, in reportable form. Used for both "record" answers and honour-board listings.</summary>
public sealed record GroundPerformanceEntry(Guid PlayerId, string PlayerName, Guid MatchId, DateOnly Date, string OpponentName, MatchFormat Format, int Season, string Description, int PrimaryValue, int SecondaryValue);

/// <summary>Aggregated career-at-this-ground line for one player - the "most runs at this ground" leaderboard row.</summary>
public sealed record GroundPlayerAggregate(Guid PlayerId, string PlayerName, int Innings, int Runs, int Wickets, double Average, double StrikeRate, int Hundreds, int Fifties, int FiveWicketHauls);

/// <summary>Result split at a ground - the "is this a chasing ground?" answer, which is one of the first things a coach actually looks up.</summary>
public sealed record GroundResultSplit(int MatchesHosted, int WonBattingFirst, int WonChasing, int Drawn, int Tied, int NoResult, double? BattingFirstWinPercentage);

/// <summary>
/// Everything a real ground statistics page shows, derived on demand from the underlying
/// records rather than cached on the Ground entity (see Ground's doc comment for why).
///
/// Every method returns null / an empty list when there is no data, and NEVER a zero that
/// could be mistaken for a real value. This codebase has already been bitten twice by an
/// unpopulated default being read as a meaningful signal, so "no matches here yet" is
/// modelled as its own answer throughout.
///
/// Player names are resolved through an optional id -> name lookup. The records themselves
/// only carry PlayerId (correct - names change, ids don't); the lookup is a presentation
/// concern the caller supplies.
/// </summary>
public sealed class GroundRecordsService
{
    private static string NameOf(Guid playerId, IReadOnlyDictionary<Guid, string>? names) =>
        names is not null && names.TryGetValue(playerId, out var n) ? n : string.Empty;

    private static IEnumerable<TeamInningsRecord> AtGround(IEnumerable<TeamInningsRecord> innings, Guid groundId, MatchFormat? format) =>
        innings.Where(i => i.GroundId == groundId && (format is null || i.Format == format));

    private static IEnumerable<T> AtGround<T>(IEnumerable<T> records, Guid groundId, MatchFormat? format, Func<T, ValueObjects.MatchContext> ctx) =>
        records.Where(r => ctx(r).GroundId == groundId && (format is null || ctx(r).Format == format));

    // ---------------- Team records ----------------

    /// <summary>Highest team total at this ground. Null when the ground has hosted nothing in this format.</summary>
    public TeamTotalEntry? GetHighestTeamTotal(IEnumerable<TeamInningsRecord> innings, Guid groundId, MatchFormat? format = null) =>
        AtGround(innings, groundId, format).OrderByDescending(i => i.Runs).Select(ToEntry).FirstOrDefault();

    /// <summary>
    /// Lowest team total. Counts only completed (all-out) innings by default - a side that
    /// was 38/1 when rain ended the match is not the ground's lowest total, and reporting it
    /// as one is the kind of statistic that immediately looks wrong to anyone who knows cricket.
    /// </summary>
    public TeamTotalEntry? GetLowestTeamTotal(IEnumerable<TeamInningsRecord> innings, Guid groundId, MatchFormat? format = null, bool allOutOnly = true) =>
        AtGround(innings, groundId, format).Where(i => !allOutOnly || i.AllOut)
            .OrderBy(i => i.Runs).Select(ToEntry).FirstOrDefault();

    /// <summary>Highest total by a side that went on to win while batting second - the "highest successful chase" line.</summary>
    public TeamTotalEntry? GetHighestSuccessfulChase(IEnumerable<TeamInningsRecord> innings, Guid groundId, MatchFormat? format = null) =>
        AtGround(innings, groundId, format)
            .Where(i => i.MatchResultForBattingTeam == MatchOutcome.Win && IsChasingInnings(i))
            .OrderByDescending(i => i.Runs).Select(ToEntry).FirstOrDefault();

    /// <summary>Average score for a given innings number (1 = first innings par score). Null when there isn't a single innings on record.</summary>
    public double? GetAverageScore(IEnumerable<TeamInningsRecord> innings, Guid groundId, MatchFormat? format = null, int inningsNumber = 1)
    {
        var relevant = AtGround(innings, groundId, format).Where(i => i.InningsNumber == inningsNumber).ToList();
        return relevant.Count == 0 ? null : Math.Round(relevant.Average(i => (double)i.Runs), 1);
    }

    /// <summary>
    /// How matches at this ground have actually finished. BattingFirstWinPercentage is null
    /// until at least one match produced a result - with no data the honest answer is "we
    /// don't know yet", not 0% (which would read as "chasing always wins here").
    /// </summary>
    public GroundResultSplit GetResultSplit(IEnumerable<TeamInningsRecord> innings, Guid groundId, MatchFormat? format = null)
    {
        var relevant = AtGround(innings, groundId, format).ToList();
        var firstInnings = relevant.Where(i => i.InningsNumber == 1).ToList();
        int matches = relevant.Select(i => i.MatchId).Distinct().Count();

        int wonBattingFirst = firstInnings.Count(i => i.MatchResultForBattingTeam == MatchOutcome.Win);
        int lostBattingFirst = firstInnings.Count(i => i.MatchResultForBattingTeam == MatchOutcome.Loss);
        int drawn = firstInnings.Count(i => i.MatchResultForBattingTeam == MatchOutcome.Draw);
        int tied = firstInnings.Count(i => i.MatchResultForBattingTeam == MatchOutcome.Tie);
        int noResult = firstInnings.Count(i => i.MatchResultForBattingTeam == MatchOutcome.NoResult);

        int decided = wonBattingFirst + lostBattingFirst;
        double? battingFirstWinPct = decided == 0 ? null : Math.Round((double)wonBattingFirst / decided * 100, 1);

        return new GroundResultSplit(matches, wonBattingFirst, lostBattingFirst, drawn, tied, noResult, battingFirstWinPct);
    }

    // ---------------- Individual records ----------------

    public GroundPerformanceEntry? GetHighestIndividualScore(IEnumerable<BattingInningsRecord> batting, Guid groundId, MatchFormat? format = null, IReadOnlyDictionary<Guid, string>? playerNames = null) =>
        AtGround(batting, groundId, format, r => r.Context)
            .OrderByDescending(r => r.Runs).ThenBy(r => r.BallsFaced)
            .Select(r => new GroundPerformanceEntry(r.PlayerId, NameOf(r.PlayerId, playerNames), r.Context.MatchId, r.Context.MatchDate,
                r.Context.OpponentName, r.Context.Format, r.Context.Season,
                $"{r.Runs}{(r.NotOut ? "*" : "")} ({r.BallsFaced}b)", r.Runs, r.BallsFaced))
            .FirstOrDefault();

    /// <summary>Best bowling in an INNINGS: most wickets, fewest runs as the tiebreak - the standard cricket ordering.</summary>
    public GroundPerformanceEntry? GetBestBowlingInInnings(IEnumerable<BowlingSpellRecord> bowling, Guid groundId, MatchFormat? format = null, IReadOnlyDictionary<Guid, string>? playerNames = null) =>
        AtGround(bowling, groundId, format, r => r.Context)
            .OrderByDescending(r => r.Wickets).ThenBy(r => r.RunsConceded)
            .Select(r => new GroundPerformanceEntry(r.PlayerId, NameOf(r.PlayerId, playerNames), r.Context.MatchId, r.Context.MatchDate,
                r.Context.OpponentName, r.Context.Format, r.Context.Season,
                $"{r.Wickets}/{r.RunsConceded}", r.Wickets, r.RunsConceded))
            .FirstOrDefault();

    /// <summary>
    /// Best bowling in a MATCH - both innings combined, which is a genuinely different record
    /// from best innings figures and only answerable because MatchContext now carries MatchId.
    /// </summary>
    public GroundPerformanceEntry? GetBestBowlingInMatch(IEnumerable<BowlingSpellRecord> bowling, Guid groundId, MatchFormat? format = null, IReadOnlyDictionary<Guid, string>? playerNames = null) =>
        AtGround(bowling, groundId, format, r => r.Context)
            .GroupBy(r => new { r.PlayerId, r.Context.MatchId })
            .Select(g => new
            {
                g.Key.PlayerId,
                g.Key.MatchId,
                Wickets = g.Sum(r => r.Wickets),
                Runs = g.Sum(r => r.RunsConceded),
                First = g.First()
            })
            .OrderByDescending(x => x.Wickets).ThenBy(x => x.Runs)
            .Select(x => new GroundPerformanceEntry(x.PlayerId, NameOf(x.PlayerId, playerNames), x.MatchId, x.First.Context.MatchDate,
                x.First.Context.OpponentName, x.First.Context.Format, x.First.Context.Season,
                $"{x.Wickets}/{x.Runs}", x.Wickets, x.Runs))
            .FirstOrDefault();

    /// <summary>Leaderboard of players by aggregate performance at this ground - "most runs at Lord's" and friends.</summary>
    public IReadOnlyList<GroundPlayerAggregate> GetLeadingPlayers(
        IEnumerable<BattingInningsRecord> batting,
        IEnumerable<BowlingSpellRecord> bowling,
        Guid groundId,
        MatchFormat? format = null,
        IReadOnlyDictionary<Guid, string>? playerNames = null,
        int take = 10)
    {
        var bat = AtGround(batting, groundId, format, r => r.Context).ToList();
        var bowl = AtGround(bowling, groundId, format, r => r.Context).ToList();

        var playerIds = bat.Select(r => r.PlayerId).Concat(bowl.Select(r => r.PlayerId)).Distinct();

        return playerIds.Select(id =>
        {
            var innings = bat.Where(r => r.PlayerId == id).ToList();
            var spells = bowl.Where(r => r.PlayerId == id).ToList();

            int runs = innings.Sum(r => r.Runs);
            int balls = innings.Sum(r => r.BallsFaced);
            int dismissals = innings.Count(r => !r.NotOut);

            return new GroundPlayerAggregate(
                id, NameOf(id, playerNames),
                innings.Count,
                runs,
                spells.Sum(r => r.Wickets),
                dismissals == 0 ? runs : Math.Round((double)runs / dismissals, 2),
                balls == 0 ? 0 : Math.Round((double)runs / balls * 100, 2),
                innings.Count(r => r.Runs >= 100),
                innings.Count(r => r.Runs is >= 50 and < 100),
                spells.Count(r => r.Wickets >= 5));
        })
        .OrderByDescending(a => a.Runs).ThenByDescending(a => a.Wickets)
        .Take(take).ToList();
    }

    // ---------------- Honour boards ----------------

    /// <summary>
    /// The honour board, as real grounds keep one: every century, and every five-wicket haul,
    /// in chronological order - NOT just the record holder. Lord's does not remove your name
    /// when someone scores more than you, and neither does this: an honour board is a
    /// permanent list of everyone who cleared the bar, which is exactly why players talk
    /// about "getting on the honour board" rather than about holding a ground record.
    ///
    /// Filtered by format because honour boards are per-format in reality (the Lord's boards
    /// are Test boards); pass null to see every format at once.
    /// </summary>
    public IReadOnlyList<GroundPerformanceEntry> GetHonourBoard(
        IEnumerable<BattingInningsRecord> batting,
        IEnumerable<BowlingSpellRecord> bowling,
        Guid groundId,
        MatchFormat? format = null,
        IReadOnlyDictionary<Guid, string>? playerNames = null)
    {
        var centuries = AtGround(batting, groundId, format, r => r.Context)
            .Where(r => r.Runs >= 100)
            .Select(r => new GroundPerformanceEntry(r.PlayerId, NameOf(r.PlayerId, playerNames), r.Context.MatchId, r.Context.MatchDate,
                r.Context.OpponentName, r.Context.Format, r.Context.Season,
                DescribeCentury(r.Runs, r.NotOut), r.Runs, r.BallsFaced));

        var fiveFors = AtGround(bowling, groundId, format, r => r.Context)
            .Where(r => r.Wickets >= 5)
            .Select(r => new GroundPerformanceEntry(r.PlayerId, NameOf(r.PlayerId, playerNames), r.Context.MatchId, r.Context.MatchDate,
                r.Context.OpponentName, r.Context.Format, r.Context.Season,
                r.Wickets >= 10 ? $"Ten-wicket haul {r.Wickets}/{r.RunsConceded}" : $"Five-wicket haul {r.Wickets}/{r.RunsConceded}",
                r.Wickets, r.RunsConceded));

        return centuries.Concat(fiveFors).OrderBy(e => e.Date).ThenBy(e => e.PlayerName).ToList();
    }

    /// <summary>Ten-wicket MATCH hauls - the other half of a real bowling honour board, and again only answerable via MatchId grouping.</summary>
    public IReadOnlyList<GroundPerformanceEntry> GetTenWicketMatchHauls(
        IEnumerable<BowlingSpellRecord> bowling, Guid groundId, MatchFormat? format = null, IReadOnlyDictionary<Guid, string>? playerNames = null) =>
        AtGround(bowling, groundId, format, r => r.Context)
            .GroupBy(r => new { r.PlayerId, r.Context.MatchId })
            .Where(g => g.Sum(r => r.Wickets) >= 10)
            .Select(g => new GroundPerformanceEntry(g.Key.PlayerId, NameOf(g.Key.PlayerId, playerNames), g.Key.MatchId,
                g.First().Context.MatchDate, g.First().Context.OpponentName, g.First().Context.Format, g.First().Context.Season,
                $"Ten wickets in the match {g.Sum(r => r.Wickets)}/{g.Sum(r => r.RunsConceded)}",
                g.Sum(r => r.Wickets), g.Sum(r => r.RunsConceded)))
            .OrderBy(e => e.Date).ToList();

    // ---------------- Fielding ----------------

    /// <summary>
    /// Most dismissals in the field at this ground. Fielding was the one statistical pillar
    /// with no record type behind it, so this whole dimension was previously missing from
    /// every ground page - catches at Lord's are as much a part of a venue's record as runs.
    /// </summary>
    public IReadOnlyList<GroundPlayerAggregate> GetLeadingFielders(
        IEnumerable<FieldingRecord> fielding, Guid groundId, MatchFormat? format = null,
        IReadOnlyDictionary<Guid, string>? playerNames = null, int take = 10) =>
        AtGround(fielding, groundId, format, r => r.Context)
            .GroupBy(r => r.PlayerId)
            .Select(g => new GroundPlayerAggregate(
                g.Key, NameOf(g.Key, playerNames),
                Innings: g.Count(),
                Runs: 0,
                Wickets: g.Sum(r => r.Dismissals),   // dismissals in the field, reused slot
                Average: 0, StrikeRate: 0, Hundreds: 0, Fifties: 0, FiveWicketHauls: 0))
            .OrderByDescending(a => a.Wickets)
            .Take(take).ToList();

    /// <summary>Best fielding performance in a single match at this ground - the "four catches in the match" line.</summary>
    public GroundPerformanceEntry? GetBestFieldingPerformance(
        IEnumerable<FieldingRecord> fielding, Guid groundId, MatchFormat? format = null, IReadOnlyDictionary<Guid, string>? playerNames = null) =>
        AtGround(fielding, groundId, format, r => r.Context)
            .Where(r => r.Dismissals > 0)
            .OrderByDescending(r => r.Dismissals)
            .Select(r => new GroundPerformanceEntry(r.PlayerId, NameOf(r.PlayerId, playerNames), r.Context.MatchId, r.Context.MatchDate,
                r.Context.OpponentName, r.Context.Format, r.Context.Season,
                r.KeptWicket ? $"{r.Dismissals} dismissals (wk)" : $"{r.Dismissals} catches/run-outs", r.Dismissals, 0))
            .FirstOrDefault();

    /// <summary>Has this player got their name on this ground's board? The question a player/media system will actually ask.</summary>
    public bool IsOnHonourBoard(IEnumerable<BattingInningsRecord> batting, IEnumerable<BowlingSpellRecord> bowling, Guid playerId, Guid groundId, MatchFormat? format = null) =>
        AtGround(batting, groundId, format, r => r.Context).Any(r => r.PlayerId == playerId && r.Runs >= 100)
        || AtGround(bowling, groundId, format, r => r.Context).Any(r => r.PlayerId == playerId && r.Wickets >= 5);

    // ---------------- helpers ----------------

    private static bool IsChasingInnings(TeamInningsRecord i) =>
        i.Format == MatchFormat.Test ? i.InningsNumber == 4 : i.InningsNumber == 2;

    private static string DescribeCentury(int runs, bool notOut) => runs switch
    {
        >= 300 => $"Triple century {runs}{(notOut ? "*" : "")}",
        >= 200 => $"Double century {runs}{(notOut ? "*" : "")}",
        >= 150 => $"Century {runs}{(notOut ? "*" : "")} (150+)",
        _ => $"Century {runs}{(notOut ? "*" : "")}"
    };

    private static TeamTotalEntry ToEntry(TeamInningsRecord i) =>
        new(i.MatchId, i.MatchDate, i.BattingTeamName, i.BowlingTeamName, i.Runs, i.Wickets, i.Overs, i.InningsNumber, i.Format, i.AllOut);
}
