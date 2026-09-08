namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 9, Slice 9.6: the terms of a loan - what turns Phase 8's light "send a youngster out for
/// game time" into a real loan-market deal. Held on Player.CurrentLoan while a loan is active,
/// alongside the Phase 8 Player.ParentClubId / Player.LoanReturnDate.
/// </summary>
public sealed class LoanAgreement
{
    public Guid ParentClubId { get; set; }
    public Guid LoanClubId { get; set; }

    /// <summary>A one-off fee the loan club pays the parent club to take the player.</summary>
    public double LoanFee { get; set; }

    /// <summary>The fraction of the player's annual wage the loan club covers for the loan period (0-1). The parent pays the rest.</summary>
    public double WageContributionShare { get; set; } = 0.5;

    /// <summary>
    /// The fee at which the loan club can (option) or must (obligation) sign the player permanently
    /// when the loan ends. Null = a plain loan, he goes back.
    /// </summary>
    public double? BuyFee { get; set; }

    /// <summary>True = the loan club MUST complete the permanent transfer at BuyFee when the loan ends. False = it is an OPTION the club decides on.</summary>
    public bool IsObligationToBuy { get; set; }
}
