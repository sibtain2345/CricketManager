using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// A coach's active response to a player's decline - set by SquadManagementService.Apply
/// when a Roadmap or Rest is warranted, cleared by SquadManagementService.ReviewDecision
/// once ReviewDate passes. Deliberately only tracks Roadmap/Rest (both carry a real
/// unavailability period with a return date, so there is real state to hold open); a Drop
/// is a one-time event with nothing ongoing to track, so it never produces one of these -
/// see SquadManagementService.Apply.
/// </summary>
public sealed class SquadDecision
{
    public SquadDecisionType Type { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateOnly DecidedDate { get; init; }

    /// <summary>When this decision naturally concludes and the player becomes available again. Null only in states this class doesn't actually represent (Continue/Drop) - always set for Roadmap/Rest.</summary>
    public DateOnly? ReviewDate { get; init; }
}
