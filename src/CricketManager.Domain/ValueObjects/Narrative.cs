namespace CricketManager.Domain.ValueObjects;

/// <summary>Phase 12: the kind of season narrative a storyline tracks.</summary>
public enum StorylineKind
{
    /// <summary>A young player having a breakout run - milestones, awards, a rising reputation.</summary>
    BreakoutStar,
    /// <summary>A captain whose side is losing and whose board is losing faith - the "under pressure" arc.</summary>
    CaptainUnderFire,
    /// <summary>A club in genuine crisis - a fractured dressing room and a board on the edge.</summary>
    CrisisClub,
    /// <summary>A team on a real winning run - a dominant spell that the media builds up.</summary>
    DominantRun,
    /// <summary>A player or captain who was under fire and has come good - the payoff.</summary>
    Redemption,
    /// <summary>Phase 16 (§18.7): a player with a real, statistically-backed hoodoo at a specific ground, about to play there again.</summary>
    GroundHoodoo,
    /// <summary>Meeting-driven-selection ticket: a national selection panel repeatedly overruled on its own contested picks - a real, building story about a panel at odds with the coach/captain.</summary>
    SelectorsAtWar
}

/// <summary>
/// Phase 12: a connected media storyline that builds over weeks and pays off. Unlike a single
/// <c>GameEvent</c>, a storyline has a lifespan, an intensity that grows while it stays live, and
/// a resolution - which is what turns a stream of disconnected headlines into a season narrative
/// the player (and the sim's own PressureMoment / BoardConfidence machinery) can react to.
///
/// Tracked in <c>WorldState.Storylines</c>, reviewed monthly by <see cref="Services.NarrativeService"/>.
/// </summary>
public sealed class Storyline
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required StorylineKind Kind { get; init; }
    public required Guid SubjectId { get; init; }
    public Guid? TeamId { get; init; }
    public DateOnly Started { get; init; }
    public DateOnly LastUpdated { get; set; }
    public double Intensity { get; set; } = 40;
    public bool Resolved { get; set; }
    public string Summary { get; set; } = string.Empty;

    public void Reinforce(DateOnly date, double amount = 8)
    {
        Intensity = Math.Clamp(Intensity + amount, 0, 100);
        LastUpdated = date;
    }
}
