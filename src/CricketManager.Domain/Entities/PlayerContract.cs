using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// Phase 9, Slice 9.0: a player's employment contract - the foundation the whole market rests on.
///
/// This is what replaces Phase 7's <see cref="Services.WageBillService"/> STUB (its own doc
/// comment: "Phase 9 owns real wages, negotiations, bonuses and buy-outs, and will replace this
/// outright"). Deliberately the same shape as <see cref="CoachingContract"/> / <see cref="StaffContract"/>
/// - start/end date, an annual figure, a <see cref="ContractStatus"/>, a compensation-style clause -
/// because a player deal and a coaching deal are the same kind of real-world thing.
///
/// A player can hold TWO contracts at once: one <see cref="ContractKind.Domestic"/> (his club,
/// which owns his registration and pays his year-round wage) and one <see cref="ContractKind.Franchise"/>
/// (a short deal for a single franchise-league season, which he plays in his club's off-window).
/// </summary>
public sealed class PlayerContract
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid PlayerId { get; init; }
    public required Guid TeamId { get; set; }

    public ContractKind Kind { get; set; } = ContractKind.Domestic;

    /// <summary>For a franchise contract, the franchise league it is for. Null for a domestic contract.</summary>
    public Guid? CompetitionId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>The year-round wage the club pays. The single largest line in a club's season finances - see SeasonFinanceService.</summary>
    public double AnnualWage { get; set; }

    /// <summary>A one-off lump paid to the player at signing - debited from the club's budget then.</summary>
    public double SigningBonus { get; set; }

    /// <summary>
    /// Slice 9.7: a fixed fee ANY club can pay to trigger a transfer, bypassing the selling club's
    /// "do we actually want to sell" decision. Null (the common case) = no release clause, so a
    /// transfer needs the selling club's agreement.
    /// </summary>
    public double? ReleaseClauseValue { get; set; }

    /// <summary>
    /// Slice 9.0: true when this club is where the player came through - an academy graduate, or a
    /// first senior professional deal. Counts toward homegrown-quota rules (Slice 9.5/9.8) and
    /// toward a testimonial (Slice 9.7). Stays true across a renewal at the same club; cleared on
    /// a transfer away.
    /// </summary>
    public bool IsHomegrown { get; set; }

    /// <summary>Slice 9.7: set once, when a one-club-servant's loyalty bonus has been paid, so it is never paid twice.</summary>
    public bool LoyaltyBonusPaid { get; set; }

    /// <summary>Slice 9.7: set once, when a one-club-servant's testimonial has been granted.</summary>
    public bool TestimonialGranted { get; set; }

    /// <summary>
    /// Phase 13 (§9.1): a sell-on clause - if this club later sells the player on, the club he was
    /// bought FROM is owed this percentage (0-40) of the fee. Set at signing when the buyer wanted
    /// the deal badly enough to concede it. <see cref="SellOnBeneficiaryTeamId"/> is who gets paid.
    /// </summary>
    public double SellOnPercentage { get; set; }
    public Guid? SellOnBeneficiaryTeamId { get; set; }

    /// <summary>
    /// Phase 13 (§9.1): a buy-back clause - the selling club's fixed price to re-sign the player
    /// within a set window (a big club letting a youngster go for game time, keeping first refusal).
    /// </summary>
    public double? BuyBackFee { get; set; }
    public DateOnly? BuyBackWindowEnd { get; set; }

    public ContractStatus Status { get; set; } = ContractStatus.Active;

    /// <summary>Whole calendar years between now and the end date - a rough "how long is he tied down" the valuation and renewal logic read.</summary>
    public int YearsRemaining(DateOnly asOf) => Math.Max(0, EndDate.DayNumber - asOf.DayNumber) / 365;

    public double MonthsRemaining(DateOnly asOf) => Math.Max(0, (EndDate.DayNumber - asOf.DayNumber) / 30.0);
}
