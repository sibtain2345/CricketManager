namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-7/8/9 rectification (Section F): a franchise's PRE-AUCTION plan - the target list
/// with priority tiers and a rough budget allocation per role - produced by a planning pass the
/// auction service consults, rather than a max-bid number computed fresh every time a player comes
/// up. The plan adapts during the auction (a lost target falls back to a plan-B; a filled role
/// stops drawing spend; a plan that is working frees up spend for an unplanned exceptional buy).
/// </summary>
public sealed class FranchiseAuctionPlan
{
    public required Guid FranchiseId { get; init; }

    /// <summary>The remaining purse this franchise has to spend at the auction.</summary>
    public double RemainingPurse { get; set; }

    /// <summary>Squad slots still to fill.</summary>
    public int SlotsToFill { get; set; }

    /// <summary>Overseas slots still available on the roster.</summary>
    public int OverseasSlotsLeft { get; set; }

    /// <summary>How much of the purse is earmarked for each broad role group. Depleted as buys are made in that group.</summary>
    public Dictionary<string, double> BudgetByRole { get; init; } = new();

    /// <summary>How many players the franchise still wants in each broad role group.</summary>
    public Dictionary<string, int> TargetsByRole { get; init; } = new();

    /// <summary>Prioritised targets - the players the franchise most wants, with a priority tier (0 = must-have, higher = fallback) and a private valuation ceiling.</summary>
    public List<PlanTarget> Targets { get; init; } = new();

    /// <summary>RTM cards available to reclaim a released former player at (or above) the winning bid.</summary>
    public int RtmCards { get; set; }

    /// <summary>Ids of the franchise's former players it can use an RTM on (released this cycle).</summary>
    public HashSet<Guid> RtmEligiblePlayerIds { get; init; } = new();

    /// <summary>True while the plan is "on track" - early targets secured - which frees the franchise to spend up on an unplanned standout.</summary>
    public bool OnTrack { get; set; }

    /// <summary>
    /// Corrections pass (correction 1): role groups where this franchise's genuine must-have
    /// (PriorityTier 0) target has already gone to a rival. The plan-B for that role is then
    /// PROMOTED - the "hold a fallback back while a higher-priority one of the same role is still
    /// to come" dampener is dropped, and the franchise fights for its next-best option as if it
    /// were the must-have. This is the real "plan A failed, pivot to plan B" behaviour.
    /// </summary>
    public HashSet<string> RolesWithLostMustHave { get; init; } = new();

    public sealed record PlanTarget(Guid PlayerId, string RoleGroup, int PriorityTier, double Ceiling, bool Overseas);
}
