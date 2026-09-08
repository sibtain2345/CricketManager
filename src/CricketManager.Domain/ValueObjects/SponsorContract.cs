namespace CricketManager.Domain.ValueObjects;

/// <summary>Phase 13 (§9.7): the kind of sponsorship deal.</summary>
public enum SponsorSlot
{
    /// <summary>The front-of-shirt / title sponsor - the big one.</summary>
    Title,
    /// <summary>The kit / apparel deal.</summary>
    Kit,
    /// <summary>Stadium naming rights.</summary>
    Stadium
}

/// <summary>
/// Phase 13 (§9.7): a MULTI-YEAR sponsorship deal, not a per-year formula. It has a fixed annual
/// base value for its term, and a performance clause that pays a bonus in a year the club wins
/// something or finishes near the top. Renegotiated when it expires against what the club commands
/// at that point - a club that has grown lands a bigger deal, one that has slipped lands less.
/// SponsorshipService signs and renews these; SeasonFinanceService pays them.
/// </summary>
public sealed class SponsorContract
{
    public required SponsorSlot Slot { get; init; }
    public required string Partner { get; init; }
    public required double AnnualValue { get; init; }

    /// <summary>A bonus paid in a year the club wins a trophy (full) or finishes top-3 (half). A fraction of AnnualValue.</summary>
    public double PerformanceBonusShare { get; init; } = 0.15;

    public int SignedYear { get; init; }
    public int ExpiresYear { get; init; }

    public bool IsExpired(int year) => year >= ExpiresYear;
}
