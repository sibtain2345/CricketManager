using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Every field is optional (null = "don't filter on this"). Fields combine with AND,
/// so "Format=T20 AND OpponentName=Australia AND Ground=Brisbane" narrows correctly.
/// This is intentionally a flat filter object rather than a query-builder/expression
/// tree - simpler to serialize for a future UI screen and easy to extend with one
/// more nullable field when a new filter dimension is needed.
/// </summary>
public sealed record StatsFilter(
    string? OpponentName = null,
    string? Ground = null,
    Guid? GroundId = null,
    string? Country = null,
    HomeAwayNeutral? HomeAwayNeutral = null,
    MatchFormat? Format = null,
    string? Tournament = null,
    int? Season = null,
    BattingRole? BattingPosition = null,
    InningsRole? InningsRole = null,
    CompetitionScope? Scope = null,
    string? LeagueName = null);

public sealed record BattingStatsResult(
    int Innings, int Runs, int NotOuts, int HighestScore, int Fifties, int Hundreds,
    double Average, double StrikeRate, int Fours, int Sixes,
    int PowerplayRuns, int PowerplayBalls, int DeathOversRuns, int DeathOversBalls);

public sealed record BowlingStatsResult(
    int Innings, double Overs, int RunsConceded, int Wickets, double Average, double Economy, double StrikeRate);

/// <summary>
/// Aggregates raw per-innings/per-spell records on demand for whatever filter
/// combination is requested. Doesn't touch or replace PlayerCareerStats (which
/// still serves fast "overall per format" totals) - this is the granular layer
/// underneath it for everything more specific than "career, this format".
/// </summary>
public sealed record FieldingStatsResult(
    int Matches, int Catches, int RunOuts, int Stumpings, int Dismissals,
    int DroppedCatches, double? CatchSuccessRate, int MatchesAsKeeper);

public sealed class PlayerStatsQueryService
{
    /// <summary>
    /// Fielding was previously unqueryable - there was no record type to query. CatchSuccessRate
    /// is null when no chance ever came the player's way, rather than 0%, which would brand a
    /// fielder who has never been offered a catch as someone who drops everything.
    /// </summary>
    public FieldingStatsResult QueryFielding(IEnumerable<FieldingRecord> records, Guid playerId, StatsFilter filter)
    {
        var matches = records.Where(r => r.PlayerId == playerId && Matches(r.Context, null, filter)).ToList();

        int catches = matches.Sum(r => r.Catches);
        int dropped = matches.Sum(r => r.DroppedCatches);
        int runOuts = matches.Sum(r => r.RunOuts);
        int stumpings = matches.Sum(r => r.Stumpings);

        double? successRate = catches + dropped == 0 ? null : Math.Round((double)catches / (catches + dropped) * 100, 1);

        return new FieldingStatsResult(matches.Count, catches, runOuts, stumpings,
            catches + runOuts + stumpings, dropped, successRate, matches.Count(r => r.KeptWicket));
    }

    public BattingStatsResult QueryBatting(IEnumerable<BattingInningsRecord> records, Guid playerId, StatsFilter filter)
    {
        var matches = records.Where(r => r.PlayerId == playerId && Matches(r.Context, r.PositionAssigned, filter)).ToList();

        int innings = matches.Count;
        int runs = matches.Sum(r => r.Runs);
        int notOuts = matches.Count(r => r.NotOut);
        int highest = matches.Count == 0 ? 0 : matches.Max(r => r.Runs);
        int fifties = matches.Count(r => r.Runs is >= 50 and < 100);
        int hundreds = matches.Count(r => r.Runs >= 100);
        int balls = matches.Sum(r => r.BallsFaced);

        double average = (innings - notOuts) <= 0 ? runs : (double)runs / (innings - notOuts);
        double strikeRate = balls == 0 ? 0 : (double)runs / balls * 100;

        return new BattingStatsResult(
            innings, runs, notOuts, highest, fifties, hundreds,
            Math.Round(average, 2), Math.Round(strikeRate, 2),
            matches.Sum(r => r.Fours), matches.Sum(r => r.Sixes),
            matches.Sum(r => r.PowerplayRuns), matches.Sum(r => r.PowerplayBalls),
            matches.Sum(r => r.DeathOversRuns), matches.Sum(r => r.DeathOversBalls));
    }

    public BowlingStatsResult QueryBowling(IEnumerable<BowlingSpellRecord> records, Guid playerId, StatsFilter filter)
    {
        var matches = records.Where(r => r.PlayerId == playerId && Matches(r.Context, null, filter)).ToList();

        int innings = matches.Count;
        double overs = matches.Sum(r => r.OversBowled);
        int runs = matches.Sum(r => r.RunsConceded);
        int wickets = matches.Sum(r => r.Wickets);

        double average = wickets == 0 ? 0 : (double)runs / wickets;
        double economy = overs == 0 ? 0 : runs / overs;
        double strikeRate = wickets == 0 ? 0 : (overs * 6) / wickets;

        return new BowlingStatsResult(innings, Math.Round(overs, 1), runs, wickets,
            Math.Round(average, 2), Math.Round(economy, 2), Math.Round(strikeRate, 2));
    }

    private static bool Matches(ValueObjects.MatchContext ctx, BattingRole? positionAssigned, StatsFilter f)
    {
        if (f.OpponentName is not null && !string.Equals(ctx.OpponentName, f.OpponentName, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.GroundId is not null && ctx.GroundId != f.GroundId) return false;
        if (f.Ground is not null && !string.Equals(ctx.Ground, f.Ground, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.Country is not null && !string.Equals(ctx.Country, f.Country, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.HomeAwayNeutral is not null && ctx.HomeAwayNeutral != f.HomeAwayNeutral) return false;
        if (f.Format is not null && ctx.Format != f.Format) return false;
        if (f.Tournament is not null && !string.Equals(ctx.Tournament, f.Tournament, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.Season is not null && ctx.Season != f.Season) return false;
        if (f.InningsRole is not null && ctx.InningsRole != f.InningsRole) return false;
        if (f.BattingPosition is not null && positionAssigned is not null && positionAssigned != f.BattingPosition) return false;
        if (f.Scope is not null && ctx.Scope != f.Scope) return false;
        if (f.LeagueName is not null && !string.Equals(ctx.LeagueName, f.LeagueName, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
