namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// All attributes use a 1-20 scale (Football Manager convention).
/// These are CURRENT ABILITY values - they change over a career via ageing (involuntary,
/// PlayerAgeingService) and training (voluntary, coach-directed, TrainingService - Phase 5).
/// </summary>
public sealed class BattingAttributes
{
    public int Technique { get; set; } = 10;
    public int Timing { get; set; } = 10;
    public int ShotSelection { get; set; } = 10;
    public int DefensiveAbility { get; set; } = 10;
    public int Aggression { get; set; } = 10;
    public int AgainstPace { get; set; } = 10;
    public int AgainstSpin { get; set; } = 10;
    public int ShortBallAbility { get; set; } = 10;
    public int SwingHandling { get; set; } = 10;
    public int SeamHandling { get; set; } = 10;
    public int SpinHandling { get; set; } = 10;
    public int DeathOverBatting { get; set; } = 10;
    public int PowerHitting { get; set; } = 10;
    public int StrikeRotation { get; set; } = 10;
    public int BoundaryHitting { get; set; } = 10;
    public int RiskManagement { get; set; } = 10;
}

public sealed class BowlingAttributes
{
    public int Pace { get; set; } = 10;
    public int Accuracy { get; set; } = 10;
    public int Swing { get; set; } = 10;
    public int Seam { get; set; } = 10;
    public int Spin { get; set; } = 10;
    public int Variation { get; set; } = 10;
    public int Yorker { get; set; } = 10;
    public int Bouncer { get; set; } = 10;
    public int SlowerBall { get; set; } = 10;
    public int DeathBowling { get; set; } = 10;
    public int NewBallBowling { get; set; } = 10;
    public int MiddleOverBowling { get; set; } = 10;
    public int Containment { get; set; } = 10;
    public int AttackingAbility { get; set; } = 10;
}

public sealed class FieldingAttributes
{
    public int Catching { get; set; } = 10;
    public int Reflexes { get; set; } = 10;
    public int Throwing { get; set; } = 10;
    public int GroundFielding { get; set; } = 10;
    public int Positioning { get; set; } = 10;
    public int BoundaryFielding { get; set; } = 10;
}

public sealed class MentalAttributes
{
    public int Composure { get; set; } = 10;
    public int Concentration { get; set; } = 10;
    public int Confidence { get; set; } = 10;
    public int Determination { get; set; } = 10;
    public int Leadership { get; set; } = 10;
    public int PressureHandling { get; set; } = 10;
    public int DecisionMaking { get; set; } = 10;
    public int Adaptability { get; set; } = 10;
    public int Professionalism { get; set; } = 10;
    public int Consistency { get; set; } = 10;
    public int GameAwareness { get; set; } = 10;

    /// <summary>
    /// Phase 11 (§2.10): judgement and communication between the wickets - the "yes / no / wait"
    /// call. A high value sets a floor under run-out risk however scrambled the pairing; a low one
    /// turns a tight single into a coin flip. Default 10 (neutral), so a player generated before
    /// this existed runs at average risk.
    /// </summary>
    public int RunningCalling { get; set; } = 10;

    /// <summary>
    /// Phase 15 (§1.7 - DRS): judgement of when to send a decision upstairs. A sharp reviewer
    /// (a good keeper, a canny captain, a technically aware batter) spends a review on the ones
    /// that are genuinely close and holds it back on the hopeful shout; a poor one burns both
    /// reviews on optimism inside ten overs. Default 10 (neutral). Only consulted when DRS is
    /// actually in force for the fixture, so a player generated before this existed is unaffected
    /// in every non-DRS match.
    /// </summary>
    public int ReviewJudgement { get; set; } = 10;
}

public sealed class PhysicalAttributes
{
    public int Fitness { get; set; } = 10;
    public int Stamina { get; set; } = 10;
    public int Strength { get; set; } = 10;
    public int Speed { get; set; } = 10;
    public int InjuryProneness { get; set; } = 10;
    public int Recovery { get; set; } = 10;
}

/// <summary>
/// Section 7: format-specific suitability. A player is NOT one universal rating -
/// this is computed from the base attributes above, weighted differently per format.
/// Stored as a cache (recomputed when attributes/form change) rather than hand-set,
/// so it can never drift out of sync with the underlying skills.
/// </summary>
public sealed class FormatSuitability
{
    public double TestSuitability { get; set; }
    public double OdiSuitability { get; set; }
    public double T20Suitability { get; set; }

    // Squad-selection follow-up (planning-brief allrounder-preference rule, Â§5.1): the
    // BATTING fields above answer "how good a batter is this player for this format" - they
    // say nothing about bowling, by original design (Player.RecalculateFormatSuitability only
    // ever read Batting.*/Mental.*). Comparing an allrounder against a specialist BOWLER needs
    // the same kind of format-aware, attribute-derived rating on the bowling side, computed the
    // same way and cached the same way, so a pure batter (whose Bowling.* attributes are simply
    // low) naturally rates low here rather than needing a separate "can this player bowl at
    // all" gate.
    public double TestBowlingSuitability { get; set; }
    public double OdiBowlingSuitability { get; set; }
    public double T20BowlingSuitability { get; set; }
}
