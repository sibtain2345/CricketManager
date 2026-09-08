using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>How the ball that was actually bowled shifts the outcome distribution.</summary>
public sealed record DeliveryEffect(
    double WicketScale,
    double BoundaryScale,
    double DotScale,
    double ExtrasScale,
    /// <summary>Relative weights for each dismissal type - a full straight ball produces lbw and bowled, a short one produces catches.</summary>
    IReadOnlyDictionary<DismissalType, double> DismissalWeights,
    /// <summary>Which part of the field this delivery tends to be scored into, before the batter's own preferences.</summary>
    IReadOnlyDictionary<ShotZone, double> ZoneBias);

/// <summary>
/// Turns "fourth stump, good length, well executed" into consequences.
///
/// This is what makes a bowling plan a decision rather than a label. Three things come out of it:
///
/// - **Line decides where runs go, and therefore whether the field is right.** A ball outside off
///   is scored through cover and point; one into the body goes square on the leg side. A captain
///   who sets a leg-side field and then bowls outside off has beaten himself, and the model now
///   says so, because zone bias feeds the same field-effect system as everything else.
/// - **Length decides how a batter can get out.** Full and straight puts lbw and bowled in play;
///   short brings catches and takes lbw almost out of it; a yorker is nearly unhittable but
///   punishing when missed. This is why a plan can be right and still fail on execution.
/// - **Execution decides whether any of it matters.** A poorly landed ball loses most of the plan's
///   advantage and becomes a scoring opportunity, which is the whole reason accuracy is an
///   attribute rather than a formality.
///
/// It also reads the batter's specific weaknesses: a short ball is only dangerous to someone who
/// cannot play it, which is what makes an analyst's report worth having.
/// </summary>
public sealed class DeliveryEffectService
{
    private static double Scale(int attribute) => AbilityScale.AttributeToHundred(attribute);
    private static readonly FieldingAptitudeService FieldingAptitude = new();

    /// <param name="recentPattern">
    /// Sections B/C: the last few (line, length) pairs this bowler has actually bowled at this
    /// striker, oldest first - BallContext.RecentDeliveryPattern, sourced from
    /// SpellReadout.RecentDeliveries. Optional and defaults to null (no pattern to read), so
    /// every pre-existing call site is unaffected.
    /// </param>
    /// <param name="ballAge">
    /// Section Y/Z: legal balls bowled since this ball was new - BallContext.BallAge. Swing and
    /// seam are new-ball phenomena that fade as the shine and the seam wear away, so this gates
    /// how much genuine movement a swing/seam bowler is actually offering right now. Defaults to
    /// 0 (a brand-new ball), which is the correct assumption for a caller that has no better
    /// information rather than an arbitrary placeholder - swing is real from ball one.
    /// </param>
    /// <param name="keeper">
    /// External-probe follow-up: who is actually standing behind the stumps, when known - see
    /// BallOutcomeModel.ResolveKeeper. Null (every pre-existing call site) leaves the stumping
    /// weight at its previous, keeper-agnostic rate.
    /// </param>
    public DeliveryEffect Calculate(ExecutedDelivery delivery, Player striker, Player bowler, PitchConditions pitch,
        IReadOnlyList<(BowlingLine Line, BowlingLength Length)>? recentPattern = null, int ballAge = 0, Player? keeper = null)
    {
        // A ball that misses its mark loses most of what the plan was worth. Below this the plan's
        // effects fade towards neutral rather than switching off, because a half-decent ball still
        // does something.
        double quality = Math.Clamp(delivery.ExecutionQuality, 0, 1);
        double planWeight = 0.35 + quality * 0.65;

        var (wicket, boundary, dot, extras) = BaseEffects(delivery, striker, bowler, pitch, ballAge);
        (wicket, boundary, dot) = ApplyPatternReading(delivery, striker, bowler, recentPattern, wicket, boundary, dot);

        // Blend towards neutral by execution quality.
        double Blend(double value) => 1 + (value - 1) * planWeight;

        return new DeliveryEffect(
            Blend(wicket), Blend(boundary), Blend(dot), extras,
            DismissalWeightsFor(delivery, striker, bowler, pitch, keeper),
            ZoneBiasFor(delivery, bowler, striker));
    }

    /// <summary>
    /// External-probe follow-up: a spinner draws the keeper up to the stumps, and how often a
    /// genuine stumping chance is actually converted depends on his own hands and feet - an elite
    /// gloveman up to the stumps takes most of what comes his way, a clumsy stand-in fluffs far
    /// more. Null keeper (no field information available) stays at the model's original,
    /// keeper-agnostic rate.
    /// </summary>
    private static double KeeperStumpingFactor(Player? keeper)
    {
        if (keeper is null) return 1.0;
        double aptitude = FieldingAptitude.GetAptitude(keeper, FieldingPosition.WicketKeeper) / 100.0;
        return Math.Clamp(0.55 + aptitude * 0.75, 0.55, 1.30);
    }

    /// <summary>
    /// Section C: a switched-on batter reads what has just been bowled at him, not only what is
    /// being bowled right now. Two distinct, opposite cases, both scaled by the SAME attribute
    /// (Mental.GameAwareness) because both are the same underlying skill - noticing a pattern:
    /// - **Predictable continuation** (this ball repeats an already-established line or length):
    ///   a sharp batter has grooved in and cashes in - a modest scoring boost, never a huge one,
    ///   since "the bowler kept doing the same sensible thing" is not automatically punishable.
    /// - **A genuine change-up** (this ball breaks an established line or length - exactly what
    ///   BowlerExecutionService's trap-ball branch deliberately creates): the setup ball is still
    ///   dangerous to everyone, but a sharp batter reads the change and defends better than a
    ///   flat-footed one - the wicket boost a change-up would otherwise carry is partially, never
    ///   fully, read away. This is what makes a trap ball a real risk for the bowler too: bowl it
    ///   against a batter switched-on enough to read it, and it just becomes a ball.
    /// </summary>
    private static (double Wicket, double Boundary, double Dot) ApplyPatternReading(
        ExecutedDelivery delivery, Player striker, Player bowler,
        IReadOnlyList<(BowlingLine Line, BowlingLength Length)>? recentPattern,
        double wicket, double boundary, double dot)
    {
        if (recentPattern is not { Count: >= 3 }) return (wicket, boundary, dot);

        double awareness = Scale(striker.Mental.GameAwareness) / 100.0;

        // Mystery spinner: the entire point of a genuine mystery repertoire is that a batter
        // cannot read what's coming from the SHAPE of what has just been bowled - a googly and a
        // leg-break look the same out of the hand. His line/length pattern-reading is real
        // cricket, but reads far less into a mystery bowler specifically than into an honest one.
        if (BallOutcomeModel.IsMysterySpinner(bowler)) awareness *= 0.45;
        bool establishedLine = recentPattern.All(p => p.Line == recentPattern[0].Line);
        bool establishedLength = recentPattern.All(p => p.Length == recentPattern[0].Length);

        bool continuesLine = establishedLine && delivery.Line == recentPattern[0].Line;
        bool continuesLength = establishedLength && delivery.Length == recentPattern[0].Length;
        bool breaksLine = establishedLine && delivery.Line != recentPattern[0].Line;
        bool breaksLength = establishedLength && delivery.Length != recentPattern[0].Length;

        if (continuesLine || continuesLength)
        {
            double boost = 1 + awareness * 0.12;
            boundary *= boost;
            dot /= boost;
        }
        else if (breaksLine || breaksLength)
        {
            // Smaller than the continuation boost above, deliberately: a standing bowling plan
            // often repeats the SAME line/length for many consecutive balls (that is what a plan
            // IS), so "established pattern" triggers often, not rarely - a larger discount here
            // measurably lengthened match/innings duration in testing (a multi-day match's
            // overs-cannot-exceed-available-days invariant broke on some seeds), because fewer
            // wickets falling on average means more overs needed to bowl a side out. 0.15 keeps
            // the mechanic real - a sharp batter genuinely reads the change-up better than a
            // flat-footed one - without silently deflating the whole match's wicket rate. Reduced
            // again (0.15 -> 0.08) once the trap-ball mechanic itself started reaching for a
            // change-up specifically ON TOP of an established pattern (that is what a trap IS),
            // which meant this branch was firing on almost every trap ball too, compounding with
            // the trap's own intended wicket boost from DeliveryEffectService.BaseEffects instead
            // of only ever slightly blunting it.
            wicket *= 1 - awareness * 0.05;
        }

        return (wicket, boundary, dot);
    }

    /// <summary>
    /// Section Y/Z: a batter's response to genuine movement through the air (swing) and off the
    /// pitch (seam) as its own, separate examination from facing pace in general.
    ///
    /// Batting.SwingHandling and Batting.SeamHandling were seeded (WorldSeeder.RandomAttributeBand)
    /// but never read by anything else in the simulation - confirmed by a full-codebase grep before
    /// touching this - the exact "dead attribute" bug class this project's own history already
    /// names for RoleTraitDeriver staleness and the two abandoned parallel infrastructure systems.
    /// Bowling.Swing already fed the BOWLER's own effective skill (BallOutcomeModel.BallAgeFactor);
    /// this is the batter's side of the same contest, which that method never modelled at all - it
    /// applied the same new-ball bonus against every batter alike.
    ///
    /// Centred on a 0.65 handling reading (not 0.5) so an average, unremarkable batter (Scale ~50)
    /// is a genuine net NEGATIVE against real swing/seam threat, matching the "against pace in
    /// general is one thing, picking movement through the air is a specific skill most batters are
    /// only middling at" shape real cricket has - a flat 0.5 centre would have made the average
    /// batter neutral against a genuine swing bowler, which understates what new-ball swing does to
    /// an ordinary top order.
    /// </summary>
    private static (double Wicket, double Dot) ApplySwingAndSeamHandling(
        Player striker, Player bowler, int ballAge, double wicket, double dot)
    {
        bool spinner = bowler.Bowling.Spin > bowler.Bowling.Pace;
        if (spinner) return (wicket, dot);

        // Swing fades fast as the shine goes; seam movement holds up a little longer as the pitch
        // itself keeps offering it. Both are gone by the time the ball is genuinely old.
        double newBallFactor = Math.Clamp(1 - ballAge / 240.0, 0, 1);
        double wornBallFactor = Math.Clamp(1 - ballAge / 360.0, 0, 1);

        double swingThreat = Scale(bowler.Bowling.Swing) / 100.0 * newBallFactor;
        double seamThreat = Scale(bowler.Bowling.Seam) / 100.0 * wornBallFactor;

        // External-probe follow-up: a batter who reads the danger can change his guard - moving
        // across the crease to cover the line the ball is actually threatening. Real, and bounded:
        // it only ever partially compensates a genuine technical shortfall, and only for a batter
        // sharp enough to make the read (Mental.GameAwareness) AND facing a threat his own hands
        // (SwingHandling/SeamHandling) are genuinely below-average against - a technically capable
        // batter has nothing to adjust for, and an unaware one never makes the change at all.
        double awareness = Scale(striker.Mental.GameAwareness) / 100.0;
        double GuardAdjustment(double handling) =>
            awareness > 0.6 && handling < 0.5 ? (awareness - 0.6) * (0.5 - handling) * 0.4 : 0;

        if (swingThreat > 0.15)
        {
            double handling = Scale(striker.Batting.SwingHandling) / 100.0;
            double exposure = swingThreat * (0.65 - handling);
            double adjustment = GuardAdjustment(handling);
            // Only ever partially compensates a genuine positive exposure (poor handling) - never
            // touches, and never overshoots past zero into, an already-negative exposure (a batter
            // whose handling is naturally above the 0.65 threshold gets no adjustment at all, since
            // GuardAdjustment is itself gated on handling < 0.5).
            if (adjustment > 0) exposure = Math.Max(0, exposure - adjustment);
            wicket *= 1 + exposure * 0.35;
            dot *= 1 + Math.Max(0, exposure) * 0.25;
        }

        if (seamThreat > 0.15)
        {
            double handling = Scale(striker.Batting.SeamHandling) / 100.0;
            double exposure = seamThreat * (0.65 - handling);
            double adjustment = GuardAdjustment(handling);
            if (adjustment > 0) exposure = Math.Max(0, exposure - adjustment);
            wicket *= 1 + exposure * 0.30;
            dot *= 1 + Math.Max(0, exposure) * 0.20;
        }

        return (wicket, dot);
    }

    private (double Wicket, double Boundary, double Dot, double Extras) BaseEffects(
        ExecutedDelivery delivery, Player striker, Player bowler, PitchConditions pitch, int ballAge = 0)
    {
        double wicket = 1.0, boundary = 1.0, dot = 1.0, extras = 1.0;

        (wicket, dot) = ApplySwingAndSeamHandling(striker, bowler, ballAge, wicket, dot);

        // ---- length ----
        switch (delivery.Length)
        {
            case BowlingLength.Yorker:
                // Almost impossible to hit, brutal if you miss it - which is exactly why a coach
                // asks for it at the death and why only a specialist can deliver it.
                wicket *= 1.55; boundary *= 0.35; dot *= 1.45;
                break;
            case BowlingLength.Full:
                wicket *= 1.20; boundary *= 1.30; dot *= 0.80;
                break;
            case BowlingLength.Good:
                wicket *= 1.05; boundary *= 0.90; dot *= 1.10;
                break;
            case BowlingLength.BackOfLength:
                wicket *= 0.95; boundary *= 0.80; dot *= 1.20;
                break;
            case BowlingLength.Short:
                // Only dangerous to a batter who cannot play it. Against someone who can, it is
                // four runs - which is what makes an analyst's report on technique worth having.
                double shortBallWeakness = 1.6 - Scale(striker.Batting.ShortBallAbility) / 100.0 * 1.1;
                wicket *= 0.85 * shortBallWeakness * (0.7 + pitch.Bounce / 100.0 * 0.6);
                boundary *= 1.35 / shortBallWeakness;
                dot *= 0.95;
                break;
        }

        // ---- line ----
        switch (delivery.Line)
        {
            case BowlingLine.AtTheStumps:
                wicket *= 1.25; boundary *= 1.05; extras *= 0.75;
                break;
            case BowlingLine.FourthStump:
                // The corridor. Best wicket-taking line against a technically suspect batter.
                double technique = Scale(striker.Batting.Technique);
                wicket *= 1.15 + (60 - Math.Min(technique, 60)) / 100.0;
                dot *= 1.10;
                break;
            case BowlingLine.OutsideOff:
                wicket *= 1.05; boundary *= 1.15; dot *= 0.95;
                break;
            case BowlingLine.WideOutsideOff:
                // Denies the batter room to swing and chokes scoring, at the cost of wides.
                wicket *= 0.70; boundary *= 0.60; dot *= 1.30; extras *= 2.4;
                break;
            case BowlingLine.IntoTheBody:
                wicket *= 0.95; boundary *= 0.85; dot *= 1.15; extras *= 1.3;
                break;
            case BowlingLine.LegStump:
                wicket *= 0.65; boundary *= 0.75; dot *= 1.25; extras *= 1.6;
                break;
        }

        // ---- variation ----
        // A variation is worth something only if the bowler can actually bowl it well.
        double variationSkill = Scale(bowler.Bowling.Variation) / 100.0;
        switch (delivery.Variation)
        {
            case DeliveryVariation.Bouncer:
                wicket *= 1 + variationSkill * 0.20 * (1.4 - Scale(striker.Batting.ShortBallAbility) / 100.0);
                boundary *= 1 + (1 - variationSkill) * 0.15;
                break;
            case DeliveryVariation.SlowerBall:
            case DeliveryVariation.Cutter:
                wicket *= 1 + variationSkill * 0.25;
                boundary *= 1 - variationSkill * 0.15;
                break;
            case DeliveryVariation.Yorker:
            case DeliveryVariation.WideYorker:
                boundary *= 1 - variationSkill * 0.25;
                dot *= 1 + variationSkill * 0.15;
                break;
            case DeliveryVariation.Googly:
            case DeliveryVariation.TopSpinner:
                wicket *= 1 + variationSkill * 0.30 * (1.3 - Scale(striker.Batting.AgainstSpin) / 100.0);
                break;
            case DeliveryVariation.ArmBall:
                dot *= 1 + variationSkill * 0.10;
                break;
            case DeliveryVariation.CrossSeam:
                wicket *= 1 + variationSkill * 0.12 * (pitch.Bounce / 100.0);
                break;
        }

        // A ball sprayed down the leg side or well wide concedes extras regardless of the plan.
        if (delivery.Deviation is PlanDeviationReason.ExecutionError or PlanDeviationReason.Indiscipline)
            extras *= 1.5;

        return (wicket, boundary, dot, extras);
    }

    /// <summary>
    /// How a wicket is most likely to fall, given what was bowled. Length dominates - a full
    /// straight ball hits pads and stumps, a short one finds the outside edge or a top edge - and
    /// this is what makes the dismissal mix respond to tactics instead of being a fixed table.
    /// </summary>
    public static IReadOnlyDictionary<DismissalType, double> DismissalWeightsFor(
        ExecutedDelivery delivery, Player striker, Player bowler, PitchConditions pitch, Player? keeper = null)
    {
        bool spinner = bowler.Bowling.Spin > bowler.Bowling.Pace;

        // Caught/CaughtBehind bases nudged up from the original 0.34/0.15 (spinner 0.08) once
        // length/line multipliers were actually connected to the live model (see SimulateWicket) -
        // every one of those multipliers pulls SOME weight away from catches toward bowled/lbw,
        // and the aggregate, averaged over a realistic mix of deliveries, was landing under the
        // 35% real-cricket floor this project's own tests calibrate against. The base was always
        // dead code before this session (computed, never called), so it was never actually
        // exercised against that floor until now - this is a real, first-time calibration, not
        // an undoing of deliberate prior tuning.
        var weights = new Dictionary<DismissalType, double>
        {
            [DismissalType.Caught] = 0.40,
            [DismissalType.CaughtBehind] = spinner ? 0.10 : 0.17,
            [DismissalType.Bowled] = spinner ? 0.20 : 0.17,
            [DismissalType.LBW] = spinner ? 0.20 : 0.15,
            // §2.14: the keeper standing UP to a genuine medium-pacer on a slow, low pitch - low
            // bounce, a bowler without real pace - is a real, if uncommon, tactic and it costs the
            // batter a stumping he would never otherwise face. A quick with real pace is bowled from
            // too far back for this to be a thing at all.
            [DismissalType.Stumped] = spinner ? 0.06
                : (pitch.Bounce < 42 && Scale(bowler.Bowling.Pace) < 55 ? 0.012 : 0.0),
            [DismissalType.CaughtAndBowled] = 0.04,
            [DismissalType.HitWicket] = 0.008,
            [DismissalType.RunOut] = 0.055
        };

        switch (delivery.Length)
        {
            case BowlingLength.Yorker:
                // Softened twice from the original (untested, because unconnected until this
                // session) 0.25/0.4 - wiring this into the live model for the first time (see
                // SimulateWicket), and separately adding the trap-ball mechanic (Section B, which
                // deliberately bowls MORE yorkers once a bowler has built pressure) both pulled
                // the aggregate Caught share of all dismissals below the 35% real-cricket floor
                // this project's own tests calibrate against. Bowled/LBW still dominate a yorker
                // dismissal, just not to the point of nearly erasing catches (a leading edge or a
                // botched scoop are still real).
                weights[DismissalType.Bowled] *= 1.9;
                weights[DismissalType.LBW] *= 1.7;
                weights[DismissalType.Caught] *= 0.90;
                weights[DismissalType.CaughtBehind] *= 0.95;
                break;
            case BowlingLength.Full:
                weights[DismissalType.Bowled] *= 1.5;
                weights[DismissalType.LBW] *= 1.7;
                weights[DismissalType.Caught] *= 0.95;
                break;
            case BowlingLength.BackOfLength:
                weights[DismissalType.Caught] *= 1.25;
                weights[DismissalType.LBW] *= 0.55;
                weights[DismissalType.Bowled] *= 0.7;
                break;
            case BowlingLength.Short:
                // Nobody is lbw to a bouncer. Top edges and gloves down the leg side, though.
                weights[DismissalType.Caught] *= 1.7;
                weights[DismissalType.CaughtBehind] *= 1.4;
                weights[DismissalType.LBW] *= 0.10;
                weights[DismissalType.Bowled] *= 0.25;
                weights[DismissalType.HitWicket] *= 4.0;
                break;
        }

        switch (delivery.Line)
        {
            case BowlingLine.AtTheStumps:
                // CaughtBehind softened alongside the Yorker-length changes above, for the same
                // reason: this is the line the Section B trap-ball mechanic reaches for most
                // often (BuildTrapDelivery's yorker AND consistent-line-punish cases both use it),
                // so it needed to stop suppressing the aggregate Caught share as hard.
                weights[DismissalType.LBW] *= 2.0;
                weights[DismissalType.Bowled] *= 1.8;
                weights[DismissalType.CaughtBehind] *= 0.75;
                break;
            case BowlingLine.FourthStump:
            case BowlingLine.OutsideOff:
                weights[DismissalType.CaughtBehind] *= 1.8;
                weights[DismissalType.Caught] *= 1.15;
                weights[DismissalType.LBW] *= 0.35;
                weights[DismissalType.Bowled] *= 0.6;
                break;
            case BowlingLine.WideOutsideOff:
                weights[DismissalType.CaughtBehind] *= 1.5;
                weights[DismissalType.LBW] *= 0.05;
                weights[DismissalType.Bowled] *= 0.15;
                break;
            case BowlingLine.IntoTheBody:
            case BowlingLine.LegStump:
                // Down the leg side: gloves, top edges and stumpings, but not lbw or bowled.
                weights[DismissalType.CaughtBehind] *= 1.3;
                weights[DismissalType.Stumped] *= 1.6;
                weights[DismissalType.LBW] *= 0.30;
                weights[DismissalType.Bowled] *= 0.4;
                break;
        }

        // A pitch with bounce turns back-of-length balls into catches.
        if (pitch.Bounce > 60) weights[DismissalType.Caught] *= 1.10;

        ApplySpinDirection(weights, bowler, striker, spinner);
        ApplyBowlingAngle(weights, delivery, bowler, striker);
        weights[DismissalType.Stumped] *= KeeperStumpingFactor(keeper);

        return weights;
    }

    /// <summary>
    /// Section Y: off-spin and leg-spin do not bowl the same ball. Real geometry, not a label -
    /// off-spin and left-arm orthodox (finger spin, off the same side of the hand) turn from off
    /// to leg for a RIGHT-handed batter (into his body - a stumps/pad threat: bowled, lbw) and
    /// from leg to off for a LEFT-hander (away from him, toward the slips - an outside-edge
    /// threat: caught, caught-behind). Leg-spin and left-arm chinaman (wrist spin) are the exact
    /// mirror. This is why a leg-spinner is traditionally prized against right-handers (turning
    /// AWAY finds the edge) and why an off-spinner is often preferred against left-handers for the
    /// same reason - and it did not exist anywhere in this engine before: BowlingStyle already
    /// distinguished the four styles as real data (RoleTraitDeriver reads it), but the ball model
    /// only ever read a binary spinner/seamer split, so an off-spinner and a leg-spinner bowled
    /// identically to every batter regardless of which hand he batted with.
    ///
    /// Scaled down toward neutral by the batter's own Batting.SpinHandling - reading which way a
    /// delivery is turning is a specific skill, separate from AgainstSpin's broader "how good is
    /// this batter against spin bowling" rating, and (like SwingHandling/SeamHandling above) was
    /// seeded and never read by anything until this pass. A batter who reads the turn correctly is
    /// not exposed by it either way, whichever direction it goes.
    /// </summary>
    private static void ApplySpinDirection(
        Dictionary<DismissalType, double> weights, Player bowler, Player striker, bool spinner)
    {
        if (!spinner) return;

        bool offSpinFamily = bowler.BowlingStyle is BowlingStyle.RightArmOffSpin or BowlingStyle.LeftArmOrthodox;
        bool legSpinFamily = bowler.BowlingStyle is BowlingStyle.RightArmLegSpin or BowlingStyle.LeftArmChinaman;
        if (!offSpinFamily && !legSpinFamily) return; // BowlingStyle not set (e.g. a raw test fixture) - no claim to make

        bool turnsAway = offSpinFamily
            ? striker.BattingHand == BattingHand.Left
            : striker.BattingHand == BattingHand.Right;

        double reading = Scale(striker.Batting.SpinHandling) / 100.0;
        double exposure = 1 - reading; // 0 for a batter who reads it perfectly, up to 1 for one who cannot pick it at all
        double shift = 0.35 * exposure;
        if (shift <= 0) return;

        if (turnsAway)
        {
            weights[DismissalType.Caught] *= 1 + shift;
            weights[DismissalType.CaughtBehind] *= 1 + shift * 1.2;
            weights[DismissalType.Bowled] *= 1 - shift * 0.4;
            weights[DismissalType.LBW] *= 1 - shift * 0.4;
        }
        else
        {
            weights[DismissalType.Bowled] *= 1 + shift * 0.8;
            weights[DismissalType.LBW] *= 1 + shift;
            weights[DismissalType.Caught] *= 1 - shift * 0.3;
        }
    }

    /// <summary>
    /// Sections 8/9: bowling ANGLE - over or round the wicket - crossed with batting hand. Applies
    /// to both pace and spin (round-the-wicket-into-the-rough against a left-hander is one of the
    /// most famous tactics in Test cricket, every bit as real as the pace case), and STACKS with
    /// ApplySpinDirection for a spinner rather than replacing it - spin direction is about which
    /// way the ball deviates off the pitch, angle is about where the delivery is released from,
    /// and both are genuinely true at once.
    ///
    /// Real geometry, the one part of this that is genuinely uncontroversial: round the wicket to
    /// an OPPOSITE-handed batter (a right-arm bowler to a left-hander, or the mirror) creates the
    /// tight, close-to-the-body angle that attacks the stumps and pads directly - the single most
    /// common real reason a captain sends a bowler round the wicket. Over the wicket to an
    /// opposite-handed batter is the standard angle, and it is what naturally carries the ball
    /// ACROSS the batter toward the slip cordon - classic away-shape, edge-hunting geometry - so it
    /// gets a small, PERMANENT background tilt even when nobody has deliberately chosen an angle
    /// (deliberately smaller than the round-the-wicket case, since this is the default rather than
    /// a tactical decision).
    ///
    /// For a SAME-handed pairing (right-arm to a right-hander, left-arm to a left-hander), over the
    /// wicket is the default and round the wicket is a genuine but far less standardised change of
    /// angle - given only a small, honest variety nudge rather than a strong directional claim,
    /// because there is no single, uncontroversial real-world signature the way there is for the
    /// opposite-handed case.
    /// </summary>
    private static void ApplyBowlingAngle(
        Dictionary<DismissalType, double> weights, ExecutedDelivery delivery, Player bowler, Player striker)
    {
        if (bowler.BowlingStyle == BowlingStyle.None) return; // no arm on record - no claim to make

        bool oppositeHanded = BallOutcomeModel.IsLeftArm(bowler.BowlingStyle)
            ? striker.BattingHand == BattingHand.Right
            : striker.BattingHand == BattingHand.Left;
        bool roundTheWicket = delivery.Variation == DeliveryVariation.RoundTheWicket;

        if (oppositeHanded && roundTheWicket)
        {
            // The cramping, pad-attacking angle - the real reason this tactic exists.
            weights[DismissalType.Bowled] *= 1.45;
            weights[DismissalType.LBW] *= 1.5;
            weights[DismissalType.Caught] *= 0.85;
            weights[DismissalType.CaughtBehind] *= 0.8;
        }
        else if (oppositeHanded)
        {
            // Over the wicket, the default for this pairing - the standard away-shape angle across
            // the batter. A small, permanent tilt, not a deliberate escalation.
            weights[DismissalType.CaughtBehind] *= 1.15;
            weights[DismissalType.Caught] *= 1.08;
            weights[DismissalType.Bowled] *= 0.95;
        }
        else if (roundTheWicket)
        {
            // Same-handed, round the wicket: a genuine but far less standardised change of angle -
            // variety and a slightly different eyeline, nothing stronger claimed.
            weights[DismissalType.Bowled] *= 1.10;
            weights[DismissalType.LBW] *= 1.08;
        }
    }

    /// <summary>
    /// Where this delivery is naturally scored. Combined with the batter's own strong zones by the
    /// field-effect layer, so a captain's field has to match the plan his bowlers are bowling.
    /// </summary>
    private static IReadOnlyDictionary<ShotZone, double> ZoneBiasFor(ExecutedDelivery delivery, Player bowler, Player striker)
    {
        var bias = Enum.GetValues<ShotZone>().ToDictionary(z => z, _ => 1.0);

        switch (delivery.Line)
        {
            case BowlingLine.WideOutsideOff:
            case BowlingLine.OutsideOff:
                bias[ShotZone.Cover] = 1.8; bias[ShotZone.Point] = 1.7; bias[ShotZone.ThirdMan] = 1.4;
                bias[ShotZone.MidWicket] = 0.45; bias[ShotZone.SquareLeg] = 0.35; bias[ShotZone.FineLeg] = 0.5;
                break;
            case BowlingLine.FourthStump:
                bias[ShotZone.Cover] = 1.4; bias[ShotZone.Point] = 1.3; bias[ShotZone.MidOff] = 1.2;
                bias[ShotZone.SquareLeg] = 0.6; bias[ShotZone.MidWicket] = 0.7;
                break;
            case BowlingLine.AtTheStumps:
                bias[ShotZone.MidOff] = 1.4; bias[ShotZone.MidOn] = 1.4; bias[ShotZone.MidWicket] = 1.2;
                bias[ShotZone.ThirdMan] = 0.7;
                break;
            case BowlingLine.IntoTheBody:
            case BowlingLine.LegStump:
                bias[ShotZone.SquareLeg] = 1.9; bias[ShotZone.MidWicket] = 1.7; bias[ShotZone.FineLeg] = 1.6;
                bias[ShotZone.Cover] = 0.35; bias[ShotZone.Point] = 0.4; bias[ShotZone.ThirdMan] = 0.6;
                break;
        }

        // A short ball is pulled and cut; a full one is driven.
        if (delivery.Length == BowlingLength.Short)
        {
            bias[ShotZone.SquareLeg] *= 1.5; bias[ShotZone.MidWicket] *= 1.3;
            bias[ShotZone.ThirdMan] *= 1.4; bias[ShotZone.Point] *= 1.2;
            bias[ShotZone.MidOff] *= 0.5; bias[ShotZone.MidOn] *= 0.5;
        }
        else if (delivery.Length is BowlingLength.Full or BowlingLength.Yorker)
        {
            bias[ShotZone.MidOff] *= 1.5; bias[ShotZone.MidOn] *= 1.4; bias[ShotZone.Cover] *= 1.2;
            bias[ShotZone.ThirdMan] *= 0.5; bias[ShotZone.SquareLeg] *= 0.7;
        }

        // Sections 8/9: bowling angle compounds with line rather than overriding it - a captain who
        // sets a leg-side field and then bowls round the wicket to a left-hander has doubled down on
        // the same idea, not fought himself the way an off-side field with a leg-stump line would.
        if (bowler.BowlingStyle != BowlingStyle.None)
        {
            bool oppositeHanded = BallOutcomeModel.IsLeftArm(bowler.BowlingStyle)
                ? striker.BattingHand == BattingHand.Right
                : striker.BattingHand == BattingHand.Left;
            bool roundTheWicket = delivery.Variation == DeliveryVariation.RoundTheWicket;

            if (oppositeHanded && roundTheWicket)
            {
                // Cramped for room, into the pads - the ball is worked through the leg side and straight rather than driven or cut.
                bias[ShotZone.SquareLeg] *= 1.3; bias[ShotZone.MidWicket] *= 1.25; bias[ShotZone.MidOn] *= 1.15;
                bias[ShotZone.Cover] *= 0.75; bias[ShotZone.Point] *= 0.8;
            }
            else if (oppositeHanded)
            {
                // Over the wicket, the default for this pairing - the natural away-angle toward the off side and the cordon.
                bias[ShotZone.Cover] *= 1.15; bias[ShotZone.Point] *= 1.1; bias[ShotZone.ThirdMan] *= 1.1;
            }
            else if (roundTheWicket)
            {
                bias[ShotZone.MidWicket] *= 1.1; bias[ShotZone.SquareLeg] *= 1.08;
            }
        }

        return bias;
    }
}
