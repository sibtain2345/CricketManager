using System.Text.Json.Serialization;

namespace CricketManager.Domain.Entities;

/// <summary>Why two teams are rivals - shapes how an emergent rivalry is allowed to grow and fade.</summary>
public enum RivalrySource
{
    /// <summary>Same city or region - a derby. Seeded, permanent, does not decay.</summary>
    Geographic,
    /// <summary>A historic rivalry seeded into the world - two traditional powers, an old grudge. Seeded, slow to fade.</summary>
    Historical,
    /// <summary>Formed dynamically from repeated high-stakes meetings (finals, title deciders). Can strengthen and, without fresh meetings, slowly fade.</summary>
    Emergent
}

/// <summary>
/// Phase 6, Slice 6.1: a rivalry between two specific teams. A rivalry does two concrete things
/// to a fixture between its two sides: it lifts the match's MatchSetup.BaseImportance (a derby is
/// never a dead rubber), and it widens the post-match morale and reputation swing (beating your
/// rival matters more, losing to them stings more).
///
/// Deliberately an entity with its own id (so it can be persisted and referenced) rather than a
/// value object list on Team: a rivalry is symmetric and world-level, not owned by either side.
/// The pair is stored order-independently - <see cref="Involves"/> and <see cref="Match"/> do not
/// care which team is TeamAId.
/// </summary>
public sealed class Rivalry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid TeamAId { get; init; }
    public required Guid TeamBId { get; init; }

    public RivalrySource Source { get; set; } = RivalrySource.Emergent;

    /// <summary>0-100. How charged the fixture is. A local derby sits high (~75-90); a rivalry that
    /// formed from one dramatic final starts modest (~40) and grows if the meetings keep mattering.</summary>
    [JsonInclude] public double Intensity { get; private set; } = 50;

    /// <summary>The last high-stakes meeting that reinforced an emergent rivalry - used to fade it if the two stop meeting in matches that matter.</summary>
    public DateOnly? LastReinforced { get; set; }

    // ---- Phase 10: bilateral international rivalries carry a permanent trophy ----

    /// <summary>The name of the trophy this rivalry plays for (the Ashes, the Border-Gavaskar Trophy, ...). Null for a club rivalry or an international pairing with no named trophy.</summary>
    public string? TrophyName { get; set; }

    /// <summary>The team currently holding the trophy. A drawn series retains it with the previous holder.</summary>
    public Guid? TrophyHolderId { get; set; }

    /// <summary>The year the trophy was last contested - so a long gap without a series reads as "held since ...".</summary>
    public int LastContestedYear { get; set; }

    /// <summary>
    /// Settle a completed bilateral series for the trophy. <paramref name="winnerId"/> null (a drawn
    /// series) leaves the holder unchanged. A change of hands reinforces the rivalry.
    /// </summary>
    public void ContestTrophy(Guid? winnerId, int year, DateOnly date)
    {
        LastContestedYear = year;
        if (winnerId is null) return;
        bool changedHands = TrophyHolderId != winnerId;
        TrophyHolderId = winnerId;
        if (changedHands) Reinforce(date, 5);
        else LastReinforced = date;
    }

    public bool Involves(Guid teamId) => teamId == TeamAId || teamId == TeamBId;

    public bool Match(Guid teamId1, Guid teamId2) =>
        (teamId1 == TeamAId && teamId2 == TeamBId) || (teamId1 == TeamBId && teamId2 == TeamAId);

    /// <summary>A meeting that mattered (a final, a title decider, a genuine grudge match) reinforces the rivalry.</summary>
    public void Reinforce(DateOnly date, double amount = 6)
    {
        Intensity = Math.Clamp(Intensity + amount, 0, 100);
        LastReinforced = date;
    }

    /// <summary>An emergent rivalry with no recent meaningful meeting cools off. Geographic/historical ones do not.</summary>
    public void Fade(double amount = 2)
    {
        if (Source == RivalrySource.Emergent)
            Intensity = Math.Clamp(Intensity - amount, 0, 100);
    }

    /// <summary>Directly set the starting intensity - used by the seeder for geographic/historical rivalries.</summary>
    public void SetIntensity(double value) => Intensity = Math.Clamp(value, 0, 100);
}
