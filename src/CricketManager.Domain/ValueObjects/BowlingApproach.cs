using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// A bowling plan as a coach would actually give it: a line, a length, a go-to variation, and how
/// hard to attack. "Fourth stump, good length, bouncer as your surprise ball, keep him honest."
///
/// Deliberately NOT ball-by-ball. Cricket Coach makes you set every delivery, which is both
/// tedious and unlike how coaching works - a coach sets the plan at the top of a spell and the
/// bowler bowls it. The bowler's own intelligence then decides how faithfully, when to depart from
/// it, and whether he spots something the coach didn't. See BowlerExecutionService.
/// </summary>
public sealed record BowlingApproach(
    BowlingLine Line = BowlingLine.FourthStump,
    BowlingLength Length = BowlingLength.Good,
    DeliveryVariation Variation = DeliveryVariation.None,
    BowlingIntent Intent = BowlingIntent.Balanced)
{
    /// <summary>
    /// How strictly the coach wants this followed, 0-100.
    ///
    /// This is the dial the design brief asks for: the coach's input should count for MORE than in
    /// real life, but a bowler must not become a puppet who bowls outside off forever while being
    /// carted. At 0 the plan is a suggestion and the bowler plays it by feel; at 100 he sticks to
    /// it almost regardless, which is powerful when the plan is right and disastrous when it isn't.
    /// The default sits above the middle, because a coach who has bothered to set a plan expects it
    /// followed.
    /// </summary>
    public double Insistence { get; init; } = 65;

    /// <summary>The analyst's identified weakness this plan is built to exploit, if any. Set by the analytics layer; raises the bowler's confidence in the plan.</summary>
    public ShotZone? TargetWeakZone { get; init; }

    // Standard plans a coach reaches for, named the way he would name them.

    public static BowlingApproach NewBall => new(BowlingLine.FourthStump, BowlingLength.Good, DeliveryVariation.None, BowlingIntent.Attacking);
    public static BowlingApproach AttackTheStumps => new(BowlingLine.AtTheStumps, BowlingLength.Full, DeliveryVariation.None, BowlingIntent.Attacking);
    public static BowlingApproach CorridorOfUncertainty => new(BowlingLine.FourthStump, BowlingLength.Good, DeliveryVariation.CrossSeam, BowlingIntent.Attacking);
    public static BowlingApproach TestTheTechnique => new(BowlingLine.OutsideOff, BowlingLength.BackOfLength, DeliveryVariation.Bouncer, BowlingIntent.Attacking);
    public static BowlingApproach BounceHim => new(BowlingLine.IntoTheBody, BowlingLength.Short, DeliveryVariation.Bouncer, BowlingIntent.Attacking);
    public static BowlingApproach ChokeTheScoring => new(BowlingLine.WideOutsideOff, BowlingLength.Good, DeliveryVariation.Cutter, BowlingIntent.Defensive);
    public static BowlingApproach DeathYorkers => new(BowlingLine.AtTheStumps, BowlingLength.Yorker, DeliveryVariation.WideYorker, BowlingIntent.Attacking);
    /// <summary>A plain outside-off plan, used as a baseline in analysis and testing.</summary>
    public static BowlingApproach OutsideOffPlan() => new(BowlingLine.OutsideOff, BowlingLength.Good);

    public static BowlingApproach ContainAndWait => new(BowlingLine.LegStump, BowlingLength.Good, DeliveryVariation.None, BowlingIntent.Defensive);
    public static BowlingApproach SpinAttackingLine => new(BowlingLine.AtTheStumps, BowlingLength.Good, DeliveryVariation.Googly, BowlingIntent.Attacking);
    public static BowlingApproach SpinContainment => new(BowlingLine.OutsideOff, BowlingLength.Good, DeliveryVariation.ArmBall, BowlingIntent.Defensive);

    // Added with the post-Phase-4 planning brief's bowling-plan expansion - named plans that
    // weren't yet covered by the ten above, built from the SAME Line/Length/Variation/Intent
    // primitives (and therefore the same real DeliveryEffectService consequences) rather than
    // needing any new mechanic. A pure length/line preset, not a new "mode".

    /// <summary>A length just short of driving range, pitched to find the edge rather than the stumps or the ribs - distinct from CorridorOfUncertainty's cross-seam probing and TestTheTechnique's bouncer.</summary>
    public static BowlingApproach EdgeTrap => new(BowlingLine.OutsideOff, BowlingLength.Good, DeliveryVariation.None, BowlingIntent.Attacking);

    /// <summary>Cramp him for room with a full, straight line into the body - a leg-side squeeze that doesn't need the short ball BounceHim reaches for.</summary>
    public static BowlingApproach CrampForRoom => new(BowlingLine.IntoTheBody, BowlingLength.Good, DeliveryVariation.None, BowlingIntent.Defensive);

    /// <summary>The plan a captain turns to specifically to break an established stand - high insistence, straight at the stumps, forcing the issue rather than waiting for a mistake.</summary>
    public static BowlingApproach PartnershipBreaker => new(BowlingLine.AtTheStumps, BowlingLength.Good, DeliveryVariation.None, BowlingIntent.Attacking) { Insistence = 80 };

    /// <summary>Full and at the stumps for a batter who can't defend a yorker - the tail rarely survives this for long.</summary>
    public static BowlingApproach TailenderAttack => new(BowlingLine.AtTheStumps, BowlingLength.Full, DeliveryVariation.Yorker, BowlingIntent.Attacking) { Insistence = 85 };

    /// <summary>
    /// Phase 15-adjacent (§2.15): LEG THEORY - a packed leg-side field and a relentless short,
    /// into-the-body line, the Bodyline-lineage tactic aimed at intimidating rather than getting
    /// the batter out cleanly. Shares BounceHim's line/length but is a genuinely distinct PLAN a
    /// captain names for its own reason - high insistence, sustained rather than a one-over
    /// tactic. A named conduct-cost hook (a captain who leans on this all match draws real
    /// scrutiny) is a natural extension once the match-level plan-usage data DisciplineService
    /// would need to read it exists; not wired yet, stated rather than implied.
    /// </summary>
    public static BowlingApproach LegTheory => new(BowlingLine.IntoTheBody, BowlingLength.Short, DeliveryVariation.Bouncer, BowlingIntent.Attacking) { Insistence = 78 };

    /// <summary>
    /// Sections 8/9: change the angle of attack, not the ball itself - over/round the wicket
    /// crossed with the batter's hand (see DeliveryEffectService.ApplyBowlingAngle). At the stumps
    /// because the whole point of the angle change is to attack the stumps and pads directly, most
    /// often reached for against an opposite-handed batter.
    /// </summary>
    public static BowlingApproach AngleTheCrease => new(BowlingLine.AtTheStumps, BowlingLength.Good, DeliveryVariation.RoundTheWicket, BowlingIntent.Attacking);

    /// <summary>Whether this variation makes sense for this kind of bowler. A seamer cannot bowl a googly, and asking for one tells you the plan was set carelessly.</summary>
    public bool IsVariationValidFor(BowlerType bowlerType) => Variation switch
    {
        DeliveryVariation.None => true,
        DeliveryVariation.Googly or DeliveryVariation.ArmBall or DeliveryVariation.TopSpinner => bowlerType == BowlerType.Spin,
        DeliveryVariation.Bouncer or DeliveryVariation.Yorker or DeliveryVariation.WideYorker
            or DeliveryVariation.Cutter or DeliveryVariation.CrossSeam => bowlerType == BowlerType.Pace,
        _ => true
    };
}

/// <summary>What the bowler actually bowled, and whether it was what he was asked for.</summary>
public sealed record ExecutedDelivery(
    BowlingLine Line,
    BowlingLength Length,
    DeliveryVariation Variation,
    /// <summary>0-1. How well he landed it - a poorly executed good plan is still a bad ball.</summary>
    double ExecutionQuality,
    PlanDeviationReason Deviation,
    string? Note = null);
