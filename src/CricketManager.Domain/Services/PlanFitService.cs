using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>A plan scored for a specific bowler against a specific batter, with the reasoning a coach or analyst would give.</summary>
public sealed record PlanRecommendation(
    BowlingApproach Approach,
    double Score,
    double ExploitsWeakness,
    double BowlerSuitability,
    string Reasoning);

/// <summary>
/// Matches a plan to the man bowling it as well as to the man facing it.
///
/// The point the design brief makes, and it is the right one: a batter being weak against the
/// short ball is only half the equation. If the bowler cannot bowl a good short ball, that plan is
/// worse than useless - it hands the batter a boundary and tells him where the ball is going. A
/// bowler is usually better off bowling to his OWN strength than to the opponent's weakness, and
/// the exception is when he happens to be good at exactly the thing the batter cannot handle.
/// That is the plan that wins a match, and it is rare.
///
/// So every plan is scored on two axes and they multiply:
/// - **ExploitsWeakness** - how much this plan attacks something this batter genuinely struggles with.
/// - **BowlerSuitability** - how good this bowler actually is at bowling it.
///
/// A high-weakness, low-suitability plan scores badly, which is the whole insight. And when nothing
/// exploits anything in particular, the recommendation falls back to the bowler's own best ball,
/// because that is what a sensible bowler does.
/// </summary>
public sealed class PlanFitService
{
    private static double Scale(int attribute) => AbilityScale.AttributeToHundred(attribute);

    /// <summary>Every plan worth considering, scored, best first. Exposed in full so an analyst screen can show the coach the options and the reasoning rather than just a verdict.</summary>
    public IReadOnlyList<PlanRecommendation> RankPlans(
        Player bowler, Player batter, PitchConditions pitch, MatchPhase phase, MatchFormat format)
    {
        var candidates = CandidatePlans(bowler, phase, format);

        return candidates
            .Select(plan => Score(bowler, batter, plan, pitch, phase))
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    public PlanRecommendation RecommendPlan(
        Player bowler, Player batter, PitchConditions pitch, MatchPhase phase, MatchFormat format) =>
        RankPlans(bowler, batter, pitch, phase, format).First();

    public PlanRecommendation Score(Player bowler, Player batter, BowlingApproach plan, PitchConditions pitch, MatchPhase phase)
    {
        double weakness = ExploitsWeakness(batter, bowler, plan, pitch);
        double suitability = BowlerSuitability(bowler, plan, pitch);

        // Multiplicative on purpose. A plan the bowler cannot execute scores badly however good the
        // idea is, and a plan he bowls beautifully scores badly if it troubles nobody.
        double score = weakness * suitability;

        return new PlanRecommendation(plan, Math.Round(score, 1), Math.Round(weakness, 1), Math.Round(suitability, 1),
            BuildReasoning(bowler, batter, plan, weakness, suitability));
    }

    /// <summary>
    /// How much this plan attacks something the batter actually struggles with, as a multiplier
    /// around 1.0. Reads the specific attribute the plan tests, not a general quality rating.
    /// </summary>
    public double ExploitsWeakness(Player batter, Player bowler, BowlingApproach plan, PitchConditions pitch)
    {
        double weakness = 1.0;

        // Sections 8/9: bowling angle only means something crossed with which hand the bowler is
        // attacking - see DeliveryEffectService.ApplyBowlingAngle for the same real geometry
        // (round the wicket to an opposite-handed batter is the genuine, well-known tactic; the
        // same-handed case is a minor variety option, not a real plan).
        if (plan.Variation == DeliveryVariation.RoundTheWicket && bowler.BowlingStyle != BowlingStyle.None)
        {
            bool oppositeHanded = BallOutcomeModel.IsLeftArm(bowler.BowlingStyle)
                ? batter.BattingHand == BattingHand.Right
                : batter.BattingHand == BattingHand.Left;
            weakness *= oppositeHanded ? 1.35 : 0.85;
        }

        // Length: what does this test?
        //
        // Note these are centred on 60 and NOT floored at 1.0. That is the whole point: a plan
        // aimed at something the batter is genuinely GOOD at must score below neutral, not merely
        // fail to score above it. Clamping at 1.0 meant bouncing an excellent puller was treated as
        // harmless rather than as the gift it is, and a quick bowler's best ball then beat every
        // plan regardless of who was facing.
        weakness *= plan.Length switch
        {
            BowlingLength.Short => 1 + (60 - Scale(batter.Batting.ShortBallAbility)) / 100.0 * 1.4,
            BowlingLength.Full or BowlingLength.Yorker
                => 1 + (60 - Scale(batter.Batting.Technique)) / 100.0 * 0.9,
            BowlingLength.BackOfLength => 1 + (55 - Scale(batter.Batting.ShortBallAbility)) / 100.0 * 0.5,
            _ => 1.0
        };

        // Line: the corridor punishes poor technique and poor shot selection; a leg-side line
        // troubles almost nobody, which is why it is a containment plan rather than a wicket plan.
        weakness *= plan.Line switch
        {
            BowlingLine.FourthStump or BowlingLine.OutsideOff
                => 1 + (60 - (Scale(batter.Batting.ShotSelection) * 0.5 + Scale(batter.Batting.Technique) * 0.5)) / 100.0 * 1.1,
            BowlingLine.AtTheStumps => 1 + (60 - Scale(batter.Batting.DefensiveAbility)) / 100.0 * 0.8,
            BowlingLine.IntoTheBody => 1 + (60 - Scale(batter.Batting.ShortBallAbility)) / 100.0 * 0.7,
            _ => 0.9
        };

        // Variation: spin variations test a batter's play against spin, pace variations his timing.
        weakness *= plan.Variation switch
        {
            DeliveryVariation.Googly or DeliveryVariation.TopSpinner or DeliveryVariation.ArmBall
                => 1 + (60 - Scale(batter.Batting.AgainstSpin)) / 100.0 * 1.0,
            DeliveryVariation.Bouncer
                => 1 + (60 - Scale(batter.Batting.ShortBallAbility)) / 100.0 * 0.9,
            DeliveryVariation.SlowerBall or DeliveryVariation.Cutter
                => 1 + (60 - Scale(batter.Batting.Timing)) / 100.0 * 0.6,
            _ => 1.0
        };

        return Math.Clamp(weakness, 0.4, 3.0);
    }

    /// <summary>
    /// How well this particular bowler bowls this particular plan, as a multiplier around 1.0.
    /// This is the half that stops a coach from simply picking the batter's weakness and winning.
    /// </summary>
    public double BowlerSuitability(Player bowler, BowlingApproach plan, PitchConditions pitch)
    {
        bool spinner = BallOutcomeModel.IsSpinner(bowler);
        double suitability = 1.0;

        // A variation he cannot bowl is worth nothing at all.
        if (!plan.IsVariationValidFor(spinner ? BowlerType.Spin : BowlerType.Pace))
            suitability *= 0.75;
        else
            suitability *= plan.Variation switch
            {
                DeliveryVariation.None => 1.0,
                _ => 0.85 + Scale(bowler.Bowling.Variation) / 100.0 * 0.4
            };

        // The pitch has to allow the plan, and this belongs on the BOWLER's side of the equation:
        // a bouncy surface helps a bowler bowl a short ball, it does not make a good hooker weak
        // against one. Putting it on the weakness side meant bouncing an excellent puller on a
        // bouncy pitch scored as a good plan, which is exactly backwards.
        if (plan.Length == BowlingLength.Short)
            suitability *= 0.6 + pitch.Bounce / 100.0 * 0.7;

        // Length: can he land it? A short-ball plan needs genuine pace behind it - a medium-pacer's
        // bouncer is not a threat to anybody, and the curve has to say so rather than merely
        // shading it. A yorker plan needs the skill to land yorkers, and accuracy underpins all of it.
        suitability *= plan.Length switch
        {
            BowlingLength.Short => spinner ? 0.4 : 0.35 + Scale(bowler.Bowling.Pace) / 100.0 * 1.0,
            BowlingLength.Yorker => 0.45 + Scale(bowler.Bowling.Accuracy) / 100.0 * 0.85
                                    + Scale(bowler.Bowling.Yorker) / 100.0 * 0.25,
            BowlingLength.Full => 0.75 + Scale(bowler.Bowling.Accuracy) / 100.0 * 0.45,
            _ => 0.80 + Scale(bowler.Bowling.Accuracy) / 100.0 * 0.35
        };

        // Line: the corridor is a movement bowler's plan. It is worth far more to someone who
        // swings or seams it than to someone bowling it straight with no movement on offer.
        if (plan.Line is BowlingLine.FourthStump or BowlingLine.OutsideOff && !spinner)
        {
            double movement = Scale(bowler.Bowling.Swing) * (pitch.Pace / 100.0) * 0.5
                              + Scale(bowler.Bowling.Seam) * 0.5;
            suitability *= 0.75 + movement / 100.0 * 0.55;
        }

        if (plan.Line == BowlingLine.WideOutsideOff)
            suitability *= 0.7 + Scale(bowler.Bowling.Accuracy) / 100.0 * 0.6; // wides are the risk

        if (spinner)
        {
            double turn = Scale(bowler.Bowling.Spin) * (0.4 + pitch.Spin / 100.0 * 0.6);
            suitability *= 0.75 + turn / 100.0 * 0.5;
        }

        return Math.Clamp(suitability, 0.3, 2.0);
    }

    /// <summary>
    /// The plans worth considering for this bowler in this phase. Deliberately built from his
    /// ATTRIBUTES rather than his role label - an opening bowler with good death skills should be
    /// offered death plans, because in real cricket he bowls them.
    /// </summary>
    public IReadOnlyList<BowlingApproach> CandidatePlans(Player bowler, MatchPhase phase, MatchFormat format)
    {
        bool spinner = BallOutcomeModel.IsSpinner(bowler);
        var plans = new List<BowlingApproach>();

        if (spinner)
        {
            // Spin deserves a proper set of plans of its own rather than one "bowl spin" setting.
            plans.Add(BowlingApproach.SpinAttackingLine);
            plans.Add(BowlingApproach.SpinContainment);
            plans.Add(new BowlingApproach(BowlingLine.OutsideOff, BowlingLength.Full, DeliveryVariation.None, BowlingIntent.Attacking));   // invite the drive, spin it past
            plans.Add(new BowlingApproach(BowlingLine.AtTheStumps, BowlingLength.Full, DeliveryVariation.TopSpinner, BowlingIntent.Attacking)); // lbw and bowled in play
            plans.Add(new BowlingApproach(BowlingLine.LegStump, BowlingLength.Good, DeliveryVariation.ArmBall, BowlingIntent.Defensive));   // into the rough, dry it up
            plans.Add(new BowlingApproach(BowlingLine.OutsideOff, BowlingLength.BackOfLength, DeliveryVariation.None, BowlingIntent.Defensive));
            if (Scale(bowler.Bowling.Variation) > 55)
                plans.Add(new BowlingApproach(BowlingLine.AtTheStumps, BowlingLength.Good, DeliveryVariation.Googly, BowlingIntent.Attacking));
            // Round the wicket, into the rough outside a left-hander's leg stump - one of the most
            // famous real tactics in Test spin bowling, and just as real here as the pace case.
            plans.Add(BowlingApproach.AngleTheCrease);
        }
        else
        {
            plans.Add(BowlingApproach.CorridorOfUncertainty);
            plans.Add(BowlingApproach.AttackTheStumps);
            plans.Add(BowlingApproach.TestTheTechnique);
            plans.Add(BowlingApproach.ChokeTheScoring);
            if (Scale(bowler.Bowling.Pace) > 50) plans.Add(BowlingApproach.BounceHim);
            if (Scale(bowler.Bowling.Pace) > 65 && Scale(bowler.Bowling.Bouncer) > 55) plans.Add(BowlingApproach.LegTheory);
            if (Scale(bowler.Bowling.Yorker) > 40 || phase == MatchPhase.DeathOvers) plans.Add(BowlingApproach.DeathYorkers);
            plans.Add(BowlingApproach.ContainAndWait);
            plans.Add(BowlingApproach.AngleTheCrease);
        }

        return plans;
    }

    /// <summary>
    /// How well this bowler suits a phase, from ATTRIBUTES not from his role label. This is what
    /// lets an opening bowler who happens to be an excellent death bowler actually be used at the
    /// death - which is completely normal in real cricket and impossible if a role enum gates it.
    /// </summary>
    public double PhaseCompetence(Player bowler, MatchPhase phase, bool newBall) => phase switch
    {
        MatchPhase.Powerplay when newBall => Scale(bowler.Bowling.NewBallBowling),
        MatchPhase.Powerplay => Scale(bowler.Bowling.NewBallBowling) * 0.6 + Scale(bowler.Bowling.MiddleOverBowling) * 0.4,
        MatchPhase.DeathOvers => Scale(bowler.Bowling.DeathBowling),
        _ => Scale(bowler.Bowling.MiddleOverBowling)
    };

    private static string BuildReasoning(Player bowler, Player batter, BowlingApproach plan, double weakness, double suitability)
    {
        if (weakness >= 1.3 && suitability >= 1.05)
            return $"{batter.FullName} struggles with this, and {bowler.FullName} bowls it well - the plan to go with.";

        if (weakness >= 1.3 && suitability < 1.0)
            return $"{batter.FullName} is vulnerable here, but {bowler.FullName} cannot bowl it well enough to exploit it - it would hand him runs.";

        if (weakness < 1.05 && suitability >= 1.15)
            return $"It troubles {batter.FullName} little, but it is {bowler.FullName}'s best ball - bowling to his own strength.";

        if (weakness < 1.0 && suitability < 0.9)
            return "Neither exploits a weakness nor suits the bowler - there are better options.";

        return "A reasonable option without being the obvious one.";
    }
}
