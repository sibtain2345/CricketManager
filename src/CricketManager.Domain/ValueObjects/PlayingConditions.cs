using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>Phase 15 (§16.5): how a tied limited-overs match is broken.</summary>
public enum LimitedOversTiebreak
{
    /// <summary>A one-over-per-side eliminator, repeated until decisive (then boundary count). The modern default for a knockout.</summary>
    SuperOver,
    /// <summary>The side that hit more boundaries in the match wins - the older rule, still used by some competitions.</summary>
    BoundaryCount,
    /// <summary>No decider - the result stands as a tie / shared, and a knockout falls to the higher-seeded side.</summary>
    SharedResult
}

/// <summary>
/// Phase 15 (§16.5): the per-competition playing conditions. Different leagues genuinely play to
/// different rules - the number of DRS reviews, how hard a slow over-rate is punished, how a tie
/// is broken, whether every no-ball brings a free hit. Held on <see cref="Entities.Competition"/>;
/// a competition that never sets one uses <see cref="For"/>'s sensible per-scope default, which
/// keeps every pre-Phase-15 competition byte-identical (no DRS, standard penalties).
/// </summary>
public sealed record PlayingConditions
{
    /// <summary>
    /// DRS reviews available to each side PER INNINGS. 0 (the default) = no DRS at all - the
    /// on-field call simply stands, exactly as before Phase 15. A limited-overs international runs
    /// 2, a Test 3, a top franchise league 2; ordinary domestic cricket runs 0.
    /// </summary>
    public int DrsReviewsPerInnings { get; init; }

    /// <summary>
    /// Multiplier on a slow over-rate fine / demerit risk. 1.0 = standard (internationals). A
    /// lenient domestic competition sits nearer 0.5; a strict flagship franchise league can go
    /// above 1.0.
    /// </summary>
    public double OverRatePenaltySeverity { get; init; } = 1.0;

    /// <summary>How a tied limited-overs match is resolved. Ignored in first-class cricket (a tie there is a legitimate, rare result on its own).</summary>
    public LimitedOversTiebreak LimitedOversTiebreak { get; init; } = LimitedOversTiebreak.SuperOver;

    /// <summary>White-ball: does every front-foot no-ball bring a free hit (the modern rule), or only the overstep? Presentation/consistency hook - the engine already gives a free hit after any no-ball in limited overs.</summary>
    public bool FreeHitForAllNoBalls { get; init; } = true;

    /// <summary>The neutral default - no DRS, standard penalties, super-over ties. Every pre-Phase-15 competition behaves exactly as this.</summary>
    public static PlayingConditions Standard { get; } = new();

    /// <summary>A sensible default set for a competition's scope + format, used when none is explicitly configured.</summary>
    public static PlayingConditions For(CompetitionScope scope, MatchFormat format) => scope switch
    {
        CompetitionScope.International => new PlayingConditions
        {
            DrsReviewsPerInnings = format == MatchFormat.Test ? 3 : 2,
            OverRatePenaltySeverity = 1.0,
            LimitedOversTiebreak = LimitedOversTiebreak.SuperOver
        },
        CompetitionScope.FranchiseLeague => new PlayingConditions
        {
            DrsReviewsPerInnings = 1,
            OverRatePenaltySeverity = 1.15,
            LimitedOversTiebreak = LimitedOversTiebreak.SuperOver
        },
        _ => Standard
    };
}
