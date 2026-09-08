using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>A player's career totals in one format at a point in time - the fields milestone detection compares.</summary>
public readonly record struct CareerSnapshot(int Matches, int Runs, int Wickets, int Hundreds, int HighestScore);

/// <summary>
/// Phase 7, Slice 7.5: keeps the flat per-(player, format) career-stats cache current.
///
/// PlayerCareerStats has existed since Phase 2 as the "totals for this format" cache, and the
/// match engine never wrote to it - MatchRecorder produced the granular BattingInningsRecord /
/// BowlingSpellRecord rows, but nothing rolled them into the flat aggregate a career page and the
/// milestone/records systems want to read quickly. This closes that: after every match this
/// folds the match's records into WorldState.CareerStats.
/// </summary>
public sealed class CareerStatsService
{
    public static string Key(Guid playerId, MatchFormat format) => $"{playerId}|{format}";

    private static PlayerCareerStats GetOrCreate(WorldState world, Guid playerId, MatchFormat format)
    {
        var key = Key(playerId, format);
        if (!world.CareerStats.TryGetValue(key, out var stats))
            world.CareerStats[key] = stats = new PlayerCareerStats { PlayerId = playerId, Format = format };
        return stats;
    }

    public PlayerCareerStats? For(WorldState world, Guid playerId, MatchFormat format) =>
        world.CareerStats.TryGetValue(Key(playerId, format), out var s) ? s : null;

    /// <summary>A milestone-relevant snapshot of a set of players' current career totals in a format - taken BEFORE ApplyMatch so a crossing can be detected.</summary>
    public IReadOnlyDictionary<Guid, CareerSnapshot> Snapshot(WorldState world, IEnumerable<Guid> playerIds, MatchFormat format)
    {
        var result = new Dictionary<Guid, CareerSnapshot>();
        foreach (var id in playerIds.Distinct())
        {
            var s = For(world, id, format);
            result[id] = s is null
                ? new CareerSnapshot(0, 0, 0, 0, 0)
                : new CareerSnapshot(s.Matches, s.Runs, s.Wickets, s.Hundreds, s.HighestScore);
        }
        return result;
    }

    /// <summary>Folds one completed match's records into the career-stats cache. `appearances` is the set of player ids that took the field (so a match count is added even for a player who neither batted nor bowled).</summary>
    public void ApplyMatch(WorldState world, MatchRecords records, MatchFormat format, IReadOnlySet<Guid> appearances)
    {
        foreach (var id in appearances)
            GetOrCreate(world, id, format).Matches++;

        foreach (var bat in records.Batting)
        {
            var stats = GetOrCreate(world, bat.PlayerId, format);
            stats.RecordBattingInnings(bat.Runs, bat.BallsFaced, bat.NotOut, bat.Fours, bat.Sixes);
        }

        foreach (var bowl in records.Bowling)
        {
            var stats = GetOrCreate(world, bowl.PlayerId, format);
            stats.RecordBowlingInnings(bowl.OversBowled, bowl.RunsConceded, bowl.Wickets);
        }

        foreach (var field in records.Fielding)
        {
            var stats = GetOrCreate(world, field.PlayerId, format);
            stats.Catches += field.Catches;
            stats.RunOuts += field.RunOuts;
            stats.Stumpings += field.Stumpings;
        }
    }
}
