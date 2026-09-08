using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

public sealed class BowlingSpellRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlayerId { get; init; }
    public MatchContext Context { get; init; } = new();

    public BowlingRoleType RoleUsedAs { get; init; }
    public double OversBowled { get; init; }
    public int RunsConceded { get; init; }
    public int Wickets { get; init; }

    public int PowerplayOvers { get; init; }
    public int PowerplayRuns { get; init; }
    public int MiddleOversOvers { get; init; }
    public int MiddleOversRuns { get; init; }
    public int DeathOversOvers { get; init; }
    public int DeathOversRuns { get; init; }

    public double Economy => OversBowled == 0 ? 0 : RunsConceded / OversBowled;
}
