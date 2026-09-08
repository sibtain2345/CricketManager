namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Batting traits, each scored 0-100. A player can hold several at meaningful
/// strength simultaneously (e.g. Anchor:70, ChaseSpecialist:60) - this is what
/// makes hybrid players possible instead of one exclusive label.
/// Values are normally auto-derived from attributes (see RoleTraitDeriver) but
/// can be manually overridden afterward for a hand-crafted/real player.
/// </summary>
public sealed class BattingTraits
{
    public int Anchor { get; set; }
    public int PowerHitter { get; set; }
    public int Slogger { get; set; }
    public int ClassicalBatter { get; set; }
    public int StrokeMaker { get; set; }
    public int Finisher { get; set; }
    public int PartnershipBuilder { get; set; }
    public int ChaseSpecialist { get; set; }
    public int PressurePlayer { get; set; }
}

public sealed class BowlingTraits
{
    public int SwingBowler { get; set; }
    public int SeamBowler { get; set; }
    public int WicketTaker { get; set; }
    public int PartnershipBreaker { get; set; }
    public int GoldenArm { get; set; }
    public int DeathSpecialist { get; set; }
    public int MiddleOversSpecialist { get; set; }
    public int ContainmentBowler { get; set; }
}
