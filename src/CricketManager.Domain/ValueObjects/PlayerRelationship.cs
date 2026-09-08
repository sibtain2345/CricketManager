using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>Phase 14 (§18.1): the kind of bond between two players.</summary>
public enum RelationshipKind
{
    /// <summary>Close friends - lifts the dressing room, and one will follow the other to a new club.</summary>
    Friendship,
    /// <summary>A senior-to-junior mentoring bond - accelerates the junior's development and temperament.</summary>
    Mentorship,
    /// <summary>A competitive edge - pushes both to perform, no ill will.</summary>
    Rivalry,
    /// <summary>Genuine bad blood - drags the dressing room down and raises the run-out risk when they bat together.</summary>
    Feud
}

/// <summary>
/// Phase 14 (§18.1): one edge of the player-to-player relationship graph. Symmetric (the pair is
/// stored order-independently). Formed from shared time at a club, personality compatibility and
/// on-field events (a big partnership -> friendship; a run-out -> a feud), evolved and decayed by
/// <see cref="Services.PlayerRelationshipService"/>.
/// </summary>
public sealed class PlayerRelationship
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid PlayerAId { get; init; }
    public required Guid PlayerBId { get; init; }
    public required RelationshipKind Kind { get; set; }

    /// <summary>0-100. How strong the bond is - a fleeting rivalry sits ~35, a lifelong friendship 85+.</summary>
    [JsonInclude] public double Strength { get; private set; } = 40;

    public DateOnly Formed { get; init; }
    public DateOnly LastReinforced { get; set; }

    public bool Involves(Guid id) => id == PlayerAId || id == PlayerBId;
    public bool Match(Guid a, Guid b) => (a == PlayerAId && b == PlayerBId) || (a == PlayerBId && b == PlayerAId);
    public Guid Other(Guid id) => id == PlayerAId ? PlayerBId : PlayerAId;

    public void Adjust(double delta, DateOnly date)
    {
        Strength = Math.Clamp(Strength + delta, 0, 100);
        if (delta > 0) LastReinforced = date;
    }

    public void SetStrength(double value) => Strength = Math.Clamp(value, 0, 100);
}
