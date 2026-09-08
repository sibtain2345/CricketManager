using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 12 (§4.8): one edge of the coach-to-player relationship - distinct from the captain-only
/// <see cref="CoachCaptainRelationship"/>. Every coach has a working relationship with each of his
/// senior players, good or bad: a player who is picked, backed and developing well warms to the
/// coach; one who is dropped, played out of position or ignored cools on him.
///
/// A strong bond makes the player develop a shade faster, lifts his <see cref="Entities.Player.CoachTrust"/>,
/// and has him lobby (quietly) for a team-mate he rates. A poor one drags his CoachTrust down and,
/// past a threshold, has him agitate to leave. The net picture across a squad moves
/// DressingRoomHarmony.
///
/// CoachPlayerRelationshipService forms, moves and decays these; they are stored on WorldState.
/// </summary>
public sealed class CoachPlayerBond
{
    public required System.Guid CoachId { get; init; }
    public required System.Guid PlayerId { get; init; }

    /// <summary>0-100, 50 neutral. How well coach and player get on.</summary>
    [JsonInclude] public double Rapport { get; private set; } = 50;

    public System.DateOnly LastMoved { get; set; }

    public void Adjust(double delta) => Rapport = System.Math.Clamp(Rapport + delta, 0, 100);

    public void DecayTowardNeutral(double amount = 1.0)
    {
        if (Rapport > 50) Rapport = System.Math.Max(50, Rapport - amount);
        else if (Rapport < 50) Rapport = System.Math.Min(50, Rapport + amount);
    }
}
