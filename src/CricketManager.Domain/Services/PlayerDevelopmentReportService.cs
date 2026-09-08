using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>How a player's game has moved between two development snapshots. Positive = improvement.</summary>
public sealed record DevelopmentProgress(
    DateOnly From, DateOnly To,
    double BattingDelta, double BattingVsPaceDelta, double BattingVsSpinDelta,
    double BowlingDelta, double FieldingDelta, double MentalDelta, double PhysicalDelta,
    int CurrentAbilityDelta)
{
    /// <summary>The single cluster that has moved the most (in absolute terms), with its signed delta - the headline for a report.</summary>
    public (string Cluster, double Delta) BiggestMover()
    {
        var moves = new (string, double)[]
        {
            ("batting", BattingDelta), ("play against pace", BattingVsPaceDelta), ("play against spin", BattingVsSpinDelta),
            ("bowling", BowlingDelta), ("fielding", FieldingDelta), ("mental game", MentalDelta), ("fitness", PhysicalDelta)
        };
        return moves.OrderByDescending(m => Math.Abs(m.Item2)).First();
    }
}

/// <summary>
/// Post-Phase-6 carry-forward: reads WorldState.DevelopmentHistory (the quarterly attribute-
/// cluster snapshots) into a period-over-period progress view. Pure - it only reads snapshots
/// the quarterly tick already took.
/// </summary>
public sealed class PlayerDevelopmentReportService
{
    /// <summary>
    /// Progress over roughly the last <paramref name="quarters"/> snapshots (4 = a year). Null
    /// when there isn't enough history yet.
    /// </summary>
    public DevelopmentProgress? Progress(IReadOnlyList<PlayerDevelopmentSnapshot>? history, int quarters = 4)
    {
        if (history is null || history.Count < 2) return null;
        var to = history[^1];
        var from = history[Math.Max(0, history.Count - 1 - Math.Max(1, quarters))];
        if (from.Date == to.Date) return null;

        return new DevelopmentProgress(
            from.Date, to.Date,
            to.Batting - from.Batting,
            to.BattingVsPace - from.BattingVsPace,
            to.BattingVsSpin - from.BattingVsSpin,
            to.Bowling - from.Bowling,
            to.Fielding - from.Fielding,
            to.Mental - from.Mental,
            to.Physical - from.Physical,
            to.CurrentAbility - from.CurrentAbility);
    }
}
