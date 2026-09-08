namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-16 completion pass (§10.3 / §14.6): a competition's broadcast-rights deal - a
/// multi-year contract with a broadcaster, at a fixed annual value, that has to be RENEGOTIATED
/// when it runs out. A competition whose standing has risen since the last deal was signed lands a
/// bigger one; one that has slipped gets less. This is what makes a competition's growth actually
/// worth money in a lumpy, real way rather than a smooth per-season formula.
/// </summary>
public sealed class BroadcastDeal
{
    public required string Partner { get; init; }

    /// <summary>The fixed pool paid to the competition each year of the deal.</summary>
    public double AnnualValue { get; set; }

    /// <summary>The year the current deal runs out - it is renegotiated in that year's rollover.</summary>
    public int ExpiresYear { get; set; }

    public bool IsExpired(int year) => year >= ExpiresYear;
}
