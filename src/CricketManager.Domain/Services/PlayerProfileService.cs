using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>One line of a player's venue honours - "on the honour board at Lord's (Test): 2 centuries, 1 five-for".</summary>
public sealed record PlayerGroundHonour(Guid GroundId, string GroundName, MatchFormat Format, int Centuries, int FiveWicketHauls, int TenWicketMatchHauls, int HighestScore, int BestInningsWickets);

public sealed record PlayerProfileResult(
    Guid PlayerId, string PlayerName,
    IReadOnlyList<CareerBucketRow> CareerSummary,              // default view: full Cricinfo-style hierarchy, no filter
    BattingStatsResult? FilteredBatting = null,                 // populated only when an extra filter is supplied
    BowlingStatsResult? FilteredBowling = null,
    StatsFilter? AppliedFilter = null);

/// <summary>
/// Section 14/62 UI intent: opening a player's profile should show the same shape as
/// a Cricinfo career page. Sourced from the granular per-innings/per-spell records
/// (not the flat PlayerCareerStats cache) via CareerStatsAggregationService, because
/// only the granular records carry enough context (Scope/LeagueName) to build the
/// full hierarchy: Test, First-Class, ODI, List A, T20I, T20 (Career), and one row
/// per franchise league the player has appeared in.
///
/// Only non-empty buckets are shown by default (a domestic-only player doesn't get an
/// empty "Test" row) - same principle as a real Cricinfo profile.
/// </summary>
public sealed class PlayerProfileService
{
    private readonly PlayerStatsQueryService _queryService = new();
    private readonly CareerStatsAggregationService _aggregationService = new();

    /// <summary>Default profile view: unfiltered, full format hierarchy, empty buckets omitted.</summary>
    public PlayerProfileResult GetOverallProfile(
        Player player,
        IReadOnlyList<BattingInningsRecord> battingRecords,
        IReadOnlyList<BowlingSpellRecord> bowlingRecords)
    {
        var rows = _aggregationService.BuildNonEmptyCareerSummary(player.Id, battingRecords, bowlingRecords);
        return new PlayerProfileResult(player.Id, player.FullName, rows);
    }

    /// <summary>
    /// Filtered view: applies an EXTRA StatsFilter (opponent/ground/season/specific league/etc.)
    /// on top of the granular records. The full career hierarchy is still included so a UI
    /// can show "overall" and "filtered" side by side, the way Cricinfo does - e.g. overall
    /// T20 career alongside "IPL only" or "T20 vs Australia" as a separate filtered slice.
    /// </summary>
    public PlayerProfileResult GetFilteredProfile(
        Player player,
        IReadOnlyList<BattingInningsRecord> battingRecords,
        IReadOnlyList<BowlingSpellRecord> bowlingRecords,
        StatsFilter filter)
    {
        var overall = GetOverallProfile(player, battingRecords, bowlingRecords);
        var filteredBatting = _queryService.QueryBatting(battingRecords, player.Id, filter);
        var filteredBowling = _queryService.QueryBowling(bowlingRecords, player.Id, filter);

        return overall with
        {
            FilteredBatting = filteredBatting,
            FilteredBowling = filteredBowling,
            AppliedFilter = filter
        };
    }

    /// <summary>
    /// Which grounds this player has got their name on the board at, per format. This is the
    /// player-profile side of GroundRecordsService's honour board - the same underlying rows,
    /// read from the player's perspective instead of the venue's, so the two can never
    /// disagree about whether someone scored a hundred at a given ground.
    ///
    /// Ground names are taken from the records' own context rather than from a Ground lookup,
    /// so a profile still renders correctly for a historical record whose venue no longer
    /// exists as an entity (imported real-world data will contain plenty of those).
    /// </summary>
    public IReadOnlyList<PlayerGroundHonour> GetGroundHonours(
        Guid playerId,
        IEnumerable<BattingInningsRecord> battingRecords,
        IEnumerable<BowlingSpellRecord> bowlingRecords)
    {
        var batting = battingRecords.Where(r => r.PlayerId == playerId && r.Context.GroundId != Guid.Empty).ToList();
        var bowling = bowlingRecords.Where(r => r.PlayerId == playerId && r.Context.GroundId != Guid.Empty).ToList();

        var keys = batting.Select(r => (r.Context.GroundId, r.Context.Format))
            .Concat(bowling.Select(r => (r.Context.GroundId, r.Context.Format)))
            .Distinct();

        return keys.Select(key =>
        {
            var groundBatting = batting.Where(r => r.Context.GroundId == key.GroundId && r.Context.Format == key.Format).ToList();
            var groundBowling = bowling.Where(r => r.Context.GroundId == key.GroundId && r.Context.Format == key.Format).ToList();

            int tenForMatches = groundBowling.GroupBy(r => r.Context.MatchId).Count(g => g.Sum(r => r.Wickets) >= 10);

            return new PlayerGroundHonour(
                key.GroundId,
                groundBatting.FirstOrDefault()?.Context.Ground ?? groundBowling.First().Context.Ground,
                key.Format,
                groundBatting.Count(r => r.Runs >= 100),
                groundBowling.Count(r => r.Wickets >= 5),
                tenForMatches,
                groundBatting.Count == 0 ? 0 : groundBatting.Max(r => r.Runs),
                groundBowling.Count == 0 ? 0 : groundBowling.Max(r => r.Wickets));
        })
        // Only grounds where something board-worthy actually happened - a profile shouldn't
        // list every venue a player has ever had a quiet game at.
        .Where(h => h.Centuries > 0 || h.FiveWicketHauls > 0)
        .OrderByDescending(h => h.Centuries + h.FiveWicketHauls)
        .ToList();
    }
}
