using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

public sealed record CareerBucketRow(string BucketLabel, MatchFormat UnderlyingFormat, BattingStatsResult Batting, BowlingStatsResult Bowling);

/// <summary>
/// Builds the real cricket stats hierarchy:
///   Test        = Format=Test, Scope=International only (pure international record)
///   First-Class = Format=Test, ANY scope (Tests are a subset of First-Class - international
///                 performances roll UP into this broader domestic-level bucket)
///   ODI         = Format=ODI, Scope=International only
///   List A      = Format=ODI, ANY scope (ODIs roll up into List A the same way)
///   T20I        = Format=T20, Scope=International only
///   T20 Career  = Format=T20, ANY scope (T20Is + domestic T20 + every franchise league,
///                 all count toward this one broad total)
///   [League]    = LeagueName={league}, no format restriction - one extra row per distinct
///                 league the player has appeared in (IPL, PSL, or a domestic FC/List A trophy
///                 with a name), IN ADDITION to it already being included in the matching
///                 broad format bucket above (a T20 league also counts in T20 Career; a named
///                 domestic FC trophy also counts in First-Class).
///
/// The asymmetry is deliberate and matches the requirement: a domestic-only performance
/// (e.g. a First-Class century that isn't a Test) never appears in the pure "Test" row,
/// but every Test innings automatically appears in "First-Class" too - because narrower
/// filters (Scope=International) exclude domestic scopes, while broader queries (no
/// Scope filter) don't exclude anything.
/// </summary>
public sealed class CareerStatsAggregationService
{
    private readonly PlayerStatsQueryService _queryService = new();

    public IReadOnlyList<CareerBucketRow> BuildCareerSummary(
        Guid playerId,
        IReadOnlyList<BattingInningsRecord> battingRecords,
        IReadOnlyList<BowlingSpellRecord> bowlingRecords)
    {
        var rows = new List<CareerBucketRow>
        {
            BuildRow("Test", MatchFormat.Test, new StatsFilter(Format: MatchFormat.Test, Scope: CompetitionScope.International), playerId, battingRecords, bowlingRecords),
            BuildRow("First-Class", MatchFormat.Test, new StatsFilter(Format: MatchFormat.Test), playerId, battingRecords, bowlingRecords),
            BuildRow("ODI", MatchFormat.ODI, new StatsFilter(Format: MatchFormat.ODI, Scope: CompetitionScope.International), playerId, battingRecords, bowlingRecords),
            BuildRow("List A", MatchFormat.ODI, new StatsFilter(Format: MatchFormat.ODI), playerId, battingRecords, bowlingRecords),
            BuildRow("T20I", MatchFormat.T20, new StatsFilter(Format: MatchFormat.T20, Scope: CompetitionScope.International), playerId, battingRecords, bowlingRecords),
            BuildRow("T20 (Career)", MatchFormat.T20, new StatsFilter(Format: MatchFormat.T20), playerId, battingRecords, bowlingRecords),
        };

        // League names aren't assumed to be T20-only - a domestic league/cup can exist at any
        // format (e.g. a First-Class trophy, a List A cup, or a T20 franchise league). Filtering
        // only by LeagueName (no Format restriction) avoids silently producing an empty row for
        // a non-T20 league. UnderlyingFormat on the row is read back from the actual data.
        var leagueNames = battingRecords.Where(r => r.PlayerId == playerId && !string.IsNullOrEmpty(r.Context.LeagueName)).Select(r => r.Context.LeagueName)
            .Concat(bowlingRecords.Where(r => r.PlayerId == playerId && !string.IsNullOrEmpty(r.Context.LeagueName)).Select(r => r.Context.LeagueName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name);

        foreach (var league in leagueNames)
        {
            var leagueFormat = battingRecords.Where(r => r.PlayerId == playerId && string.Equals(r.Context.LeagueName, league, StringComparison.OrdinalIgnoreCase))
                .Select(r => (MatchFormat?)r.Context.Format)
                .FirstOrDefault()
                ?? bowlingRecords.Where(r => r.PlayerId == playerId && string.Equals(r.Context.LeagueName, league, StringComparison.OrdinalIgnoreCase))
                    .Select(r => (MatchFormat?)r.Context.Format)
                    .FirstOrDefault()
                ?? MatchFormat.T20; // fallback only if somehow neither list has it (shouldn't happen)

            rows.Add(BuildRow(league, leagueFormat, new StatsFilter(LeagueName: league), playerId, battingRecords, bowlingRecords));
        }

        return rows;
    }

    /// <summary>Returns only the rows that have at least one recorded innings/spell - skips empty buckets a player never played (e.g. no Test row for a T20-only player).</summary>
    public IReadOnlyList<CareerBucketRow> BuildNonEmptyCareerSummary(
        Guid playerId, IReadOnlyList<BattingInningsRecord> battingRecords, IReadOnlyList<BowlingSpellRecord> bowlingRecords)
        => BuildCareerSummary(playerId, battingRecords, bowlingRecords)
            .Where(r => r.Batting.Innings > 0 || r.Bowling.Innings > 0)
            .ToList();

    private CareerBucketRow BuildRow(string label, MatchFormat format, StatsFilter filter, Guid playerId,
        IReadOnlyList<BattingInningsRecord> battingRecords, IReadOnlyList<BowlingSpellRecord> bowlingRecords)
    {
        var batting = _queryService.QueryBatting(battingRecords, playerId, filter);
        var bowling = _queryService.QueryBowling(bowlingRecords, playerId, filter);
        return new CareerBucketRow(label, format, batting, bowling);
    }
}
