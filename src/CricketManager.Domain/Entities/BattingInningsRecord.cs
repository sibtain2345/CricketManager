using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

/// <summary>
/// One batting innings, with full context. PlayerCareerStats (format-level aggregate)
/// remains for quick "overall" totals; this is the granular layer underneath it that
/// PlayerStatsQueryService filters/aggregates on demand for contextual queries.
/// </summary>
public sealed class BattingInningsRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlayerId { get; init; }
    public MatchContext Context { get; init; } = new();

    // Dismissal information. Logged as tech debt through Phases 1-3 and deliberately not added
    // until something could populate it - adding a field with nothing to fill it is how this
    // codebase's recurring unpopulated-default bug keeps happening. The match engine fills it now.
    // Unblocks: dismissal-pattern analytics (Section 34), bowler-vs-batter records, and fielder
    // credit on the batting side of the database.
    public DismissalType Dismissal { get; init; } = DismissalType.NotOut;
    public Guid? DismissedByBowlerId { get; init; }
    public Guid? FielderId { get; init; }

    public BattingRole PositionAssigned { get; init; } // where the player actually batted this innings
    public int Runs { get; init; }
    public int BallsFaced { get; init; }
    public bool NotOut { get; init; }
    public int Fours { get; init; }
    public int Sixes { get; init; }

    // Phase breakdown - lets "death overs performance" etc. be queried without a full ball-by-ball log.
    public int PowerplayRuns { get; init; }
    public int PowerplayBalls { get; init; }
    public int MiddleOversRuns { get; init; }
    public int MiddleOversBalls { get; init; }
    public int DeathOversRuns { get; init; }
    public int DeathOversBalls { get; init; }

    public double StrikeRate => BallsFaced == 0 ? 0 : (double)Runs / BallsFaced * 100;
}
