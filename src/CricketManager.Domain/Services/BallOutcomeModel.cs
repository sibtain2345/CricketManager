using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>The full probability distribution for one delivery. Exposed rather than kept internal so the model is inspectable and testable - you can assert on the probabilities themselves instead of only on sampled outcomes, which is the only way to test a stochastic system reliably.</summary>
public sealed record DeliveryProbabilities(
    double Dot, double Single, double Two, double Three, double Four, double Six,
    double Wicket, double Wide, double NoBall, double Byes)
{
    public double Total => Dot + Single + Two + Three + Four + Six + Wicket + Wide + NoBall + Byes;

    /// <summary>Expected runs from this ball - the cleanest single summary of whether the model thinks the batter is on top.</summary>
    public double ExpectedRuns => Single + Two * 2 + Three * 3 + Four * 4 + Six * 6 + Wide + NoBall + Byes * 1.5;
}

/// <summary>
/// One ball, simulated properly.
///
/// The design principle from Section 19: outcomes are probabilistic, but the PROBABILITIES are
/// intelligent. Nothing here rolls a die against a single "overall rating". Instead:
///
/// 1. The batter and the bowler each get an effective skill for THIS ball - not their general
///    ability, but what they bring to this specific delivery: the right attributes weighted for
///    the format and phase, shifted by form, by how set the batter is, by bowler fatigue, by how
///    well the pitch suits them, by their head-to-head history, and by whether they handle the
///    occasion.
/// 2. The gap between those two numbers becomes an ADVANTAGE, which shifts a realistic base
///    distribution rather than replacing it. This is what keeps upsets possible: a huge
///    advantage moves the odds a long way but never to certainty, so a great batter can still
///    nick off first ball and a tail-ender can still hit a six.
/// 3. Intent trades runs against risk on every single ball. Attacking scores faster AND gets out
///    more. There is no setting that is simply better, which is the fundamental cricket bargain.
///
/// Base rates are taken from real scoring patterns per format, so a simulated innings has the
/// right shape before any player attributes are applied at all.
/// </summary>
public sealed class BallOutcomeModel
{
    private readonly SituationalPerformanceModifier _situational = new();
    private readonly MatchupConfidenceService _matchups = new();
    private readonly FieldEffectService _field = new();
    private readonly DeliveryEffectService _delivery = new();
    private readonly PartnershipChemistryService _chemistry = new();
    private readonly BowlingPairSynergyService _pairSynergy = new();
    private readonly FieldingAptitudeService _fieldingAptitude = new();

    /// <summary>
    /// Real per-ball outcome frequencies by format. These are the skeleton: two average players
    /// with neutral everything should produce an innings that looks like real cricket in that
    /// format before a single attribute moves the needle.
    /// </summary>
    private static (double Dot, double One, double Two, double Three, double Four, double Six, double Wicket) BaseRates(MatchFormat format) => format switch
    {
        // Wicket rates are per BALL and chosen so a neutral innings falls out at realistic
        // totals: ~6 wickets in a T20, ~7 in a 50-over innings, ~10 across a long Test innings.
        MatchFormat.T20 => (0.365, 0.298, 0.062, 0.006, 0.120, 0.050, 0.0400),
        MatchFormat.ODI => (0.470, 0.320, 0.064, 0.007, 0.080, 0.016, 0.0182),
        // Test wicket rate calibrated to a wicket roughly every ten overs. At 0.0195 an innings
        // ended in about eighty overs, so all four fitted inside 300 of the 450 available and a
        // five-day match could never run out of time - which meant the draw, the most common result
        // in first-class cricket, simply never occurred.
        MatchFormat.Test => (0.672, 0.210, 0.040, 0.005, 0.060, 0.006, 0.0178),
        _ => (0.460, 0.300, 0.062, 0.006, 0.100, 0.030, 0.0300)
    };

    // ---------------- public API ----------------

    public DeliveryProbabilities CalculateProbabilities(BallContext ctx)
    {
        double batterSkill = GetBatterEffectiveSkill(ctx);
        double bowlerSkill = GetBowlerEffectiveSkill(ctx);

        // -1 (bowler dominant) .. +1 (batter dominant). Divided by 60 rather than 100 so a
        // realistic skill gap produces a meaningful - but not absolute - shift.
        double advantage = Math.Clamp((batterSkill - bowlerSkill) / 60.0, -1, 1);

        var b = BaseRates(ctx.Format);
        var phase = PhaseAdjustment(ctx.Phase, ctx.Format);
        var intent = IntentAdjustment(ctx.BattingIntent);
        var bowling = BowlingIntentAdjustment(ctx.BowlingIntent);

        // Extras first: they come out of the total before anything else, and they depend almost
        // entirely on the bowler's accuracy and how hard he is trying.
        double accuracy = AbilityScale.AttributeToHundred(ctx.Bowler.Bowling.Accuracy);
        double fatigue = FatigueFactor(ctx);
        // Wides are far rarer in red-ball cricket - the white-ball wide law is much stricter, and
        // a Test innings that leaked twenty wides would look absurd on a scorecard.
        double formatWideRate = ctx.Format switch { MatchFormat.Test => 0.011, MatchFormat.ODI => 0.026, _ => 0.028 };
        double wideRate = Math.Clamp(formatWideRate * (1.6 - accuracy / 100.0) * (2 - fatigue) * bowling.ExtrasMultiplier, 0.001, 0.09);
        double noBallRate = Math.Clamp(0.006 * (1.6 - accuracy / 100.0) * (2 - fatigue), 0.0005, 0.03);
        double byeRate = Math.Clamp(0.010 * (1 + ctx.PitchBounce / 200.0), 0.002, 0.03);

        // Wicket probability. Advantage cuts both ways; intent raises it sharply; a set batter is
        // markedly harder to remove, which is why "getting in" is the whole game.
        double wicket = b.Wicket
            * phase.WicketMultiplier
            * intent.WicketMultiplier
            * bowling.WicketMultiplier
            * (1 - advantage * 0.45)
            * SetBatterFactor(ctx)
            * (0.85 + (100 - ctx.PitchBattingFriendliness) / 100.0 * 0.4);

        var keeper = ResolveKeeper(ctx);

        if (ctx.Delivery is { } bowled)
            wicket *= _delivery.Calculate(bowled, ctx.Striker, ctx.Bowler,
                new PitchConditions(ctx.PitchPace, ctx.PitchSpin, ctx.PitchBounce, ctx.PitchBattingFriendliness),
                ctx.RecentDeliveryPattern, ctx.BallAge, keeper).WicketScale;

        // Rescale for how well the field actually converts chances - see FieldEffect.ChanceToWicketScale.
        wicket *= _field.Calculate(ctx.Field, ctx.StrikerZones, ctx.Format).ChanceToWicketScale;

        wicket = Math.Clamp(wicket, 0.002, 0.25);

        double remaining = 1.0 - wicket - wideRate - noBallRate - byeRate;
        if (remaining <= 0.05) remaining = 0.05;

        // Scoring shape. Advantage and intent move mass between dots and boundaries; the middle
        // (ones and twos) is far more stable, because rotating the strike is much less dependent
        // on being on top than clearing the rope is.
        double dotWeight = b.Dot * phase.DotMultiplier * intent.DotMultiplier * bowling.DotMultiplier * (1 - advantage * 0.35);
        double oneWeight = b.One * intent.SingleMultiplier * (1 + advantage * 0.10)
                           * (0.85 + AbilityScale.AttributeToHundred(ctx.Striker.Batting.StrikeRotation) / 100.0 * 0.3);
        double twoWeight = b.Two * (1 + advantage * 0.15) * intent.SingleMultiplier;
        // The harder run - Section Q/W: two batters who trust each other's calls take the second
        // run more often than two who don't. Deliberately only on the two (and the run-out share
        // below), not the single - a comfortable single is mostly a shot-selection question, not
        // a running-partnership one.
        if (ctx.NonStriker is { } chemistryPartner)
            twoWeight *= _chemistry.GetStrikeRotationMultiplier(ctx.Striker, chemistryPartner.Id);
        double threeWeight = b.Three * (1 + advantage * 0.15);
        // Ground dimensions finally do something. Phase 3 added boundary sizes, outfield speed and
        // altitude to Ground and deliberately left them unread for this phase: a short square
        // boundary at altitude with a fast outfield is a six-hitting ground, a big square boundary
        // with a slow outfield rewards running instead.
        double fourGround = GroundBoundaryFactor(ctx, isSix: false);
        double sixGround = GroundBoundaryFactor(ctx, isSix: true);

        double fourWeight = b.Four * phase.BoundaryMultiplier * intent.BoundaryMultiplier * bowling.BoundaryMultiplier * (1 + advantage * 0.55)
                            * (0.8 + AbilityScale.AttributeToHundred(ctx.Striker.Batting.BoundaryHitting) / 100.0 * 0.4)
                            * fourGround;
        double sixWeight = b.Six * phase.BoundaryMultiplier * intent.SixMultiplier * bowling.BoundaryMultiplier * (1 + advantage * 0.75)
                           * (0.65 + AbilityScale.AttributeToHundred(ctx.Striker.Batting.PowerHitting) / 100.0 * 0.7)
                           * sixGround;

        // The field placement. Boundary riders cut fours, a packed ring cuts singles and forces
        // dots, and neither is free - protecting the rope empties the ring and vice versa.
        // The ball that was actually bowled. Line and length shift the whole distribution, scaled by
        // how well the bowler landed it - a brilliant plan poorly executed is still a bad ball.
        if (ctx.Delivery is { } executed)
        {
            var deliveryEffect = _delivery.Calculate(executed, ctx.Striker, ctx.Bowler,
                new PitchConditions(ctx.PitchPace, ctx.PitchSpin, ctx.PitchBounce, ctx.PitchBattingFriendliness),
                ctx.RecentDeliveryPattern, ctx.BallAge, keeper);

            fourWeight *= deliveryEffect.BoundaryScale;
            sixWeight *= deliveryEffect.BoundaryScale;
            dotWeight *= deliveryEffect.DotScale;
        }

        // A free hit is a free swing. Whatever his intent was a ball earlier, a batter takes on a
        // delivery he cannot be out to - and that is worth boundaries, not merely safety.
        if (ctx.IsFreeHit)
        {
            fourWeight *= 1.55;
            sixWeight *= 1.85;
            dotWeight *= 0.65;
        }

        var fieldEffect = _field.Calculate(ctx.Field, ctx.StrikerZones, ctx.Format);
        fourWeight *= fieldEffect.BoundaryScale;
        sixWeight *= fieldEffect.SixScale;
        oneWeight *= fieldEffect.SingleScale;
        twoWeight *= fieldEffect.SingleScale;
        dotWeight *= fieldEffect.DotScale;

        double weightTotal = dotWeight + oneWeight + twoWeight + threeWeight + fourWeight + sixWeight;
        double scale = remaining / weightTotal;

        return new DeliveryProbabilities(
            dotWeight * scale, oneWeight * scale, twoWeight * scale, threeWeight * scale,
            fourWeight * scale, sixWeight * scale,
            wicket, wideRate, noBallRate, byeRate);
    }

    /// <summary>Samples one delivery from the model. The Random is passed in so a whole match can be replayed exactly from a seed - essential for debugging a simulation and for testing it at all.</summary>
    public DeliveryOutcome SimulateBall(BallContext ctx, Random random)
    {
        var p = CalculateProbabilities(ctx);
        double roll = random.NextDouble() * p.Total;
        double cumulative = 0;

        if ((cumulative += p.Wide) > roll) return DeliveryOutcome.Wide(random.NextDouble() < 0.06 ? 4 : 0);
        if ((cumulative += p.NoBall) > roll) return SimulateNoBall(ctx, random);
        if ((cumulative += p.Wicket) > roll)
        {
            // On a free hit the batter simply cannot be dismissed by this delivery - only a run-out
            // is possible, and that is a running error rather than a bowling one. What would have
            // been a wicket becomes a shot played at, which is exactly how a free hit looks.
            if (ctx.IsFreeHit)
            {
                double freeHitRunOutChance = 0.06;
                if (ctx.NonStriker is { } freeHitPartner)
                    freeHitRunOutChance *= _chemistry.GetRunOutRiskMultiplier(ctx.Striker, freeHitPartner.Id);

                return random.NextDouble() < freeHitRunOutChance
                    ? DeliveryOutcome.Wicket(DismissalType.RunOut, PickFielder(ctx, random))
                    : DeliveryOutcome.Runs(random.NextDouble() < 0.35 ? 1 : 0);
            }

            return SimulateWicket(ctx, random);
        }
        if ((cumulative += p.Byes) > roll) return DeliveryOutcome.Byes(random.NextDouble() < 0.85 ? 1 : 4, legByes: random.NextDouble() < 0.7);
        if ((cumulative += p.Dot) > roll) return DeliveryOutcome.Dot();
        if ((cumulative += p.Single) > roll) return ResolveRunningShot(ctx, 1, random);
        if ((cumulative += p.Two) > roll) return ResolveRunningShot(ctx, 2, random);
        if ((cumulative += p.Three) > roll) return ResolveRunningShot(ctx, 3, random);
        if ((cumulative += p.Four) > roll) return ResolveBoundaryShot(ctx, random);
        // Cleared the rope - nobody stops a six. Deliberately NOT rolling a zone here: doing so
        // would consume an extra random draw on every six and reshuffle the entire rest of the
        // match's random stream from that point on (the exact, well-documented hazard this
        // project's own history calls out repeatedly for probability-affecting changes) for a
        // signal Section R's trap-reading does not need sixes to work - fours alone (which already
        // rolled a zone before this change) are a common enough attacking signal on their own.
        return DeliveryOutcome.Runs(6);
    }

    /// <summary>
    /// A shot to the rope, with a fielder possibly out there. An elite outfielder turns four into
    /// two or three often enough to matter over an innings, and a plodder in the same position
    /// turns it into nothing at all - which is exactly the difference the design asks for.
    /// </summary>
    private DeliveryOutcome ResolveBoundaryShot(BallContext ctx, Random random)
    {
        var zone = _field.RollZone(ctx.StrikerZones, random);
        if (ctx.Field is null || ctx.FieldingSide is null) return DeliveryOutcome.Runs(4) with { Zone = zone };

        var players = ctx.FieldingSide.ToDictionary(p => p.Id);

        // How hard it was struck. A flat, powerful shot cannot be chased down; a placed one can.
        double shotPower = Math.Clamp(
            Common.AbilityScale.AttributeToHundred(ctx.Striker.Batting.PowerHitting) / 100.0 * 0.6
            + random.NextDouble() * 0.4, 0, 1);

        // Phase 15 (§19.6): a strong wind makes the DOWNWIND boundary play shorter (a ball a fielder
        // would have cut off carries) and the upwind one longer. Folded into shotPower rather than a
        // new random draw, so it is RNG-neutral - net ~zero across the ground. Only above ~20 kph.
        if (ctx.WindSpeedKph >= 20)
            shotPower = Math.Clamp(shotPower + WindAlignment(zone, ctx.WindBearing) * 0.07
                * Math.Clamp((ctx.WindSpeedKph - 20) / 20.0, 0, 1), 0, 1);

        var outcome = _field.ResolveBoundary(ctx.Field, zone, players, shotPower, random);
        return (outcome is null ? DeliveryOutcome.Runs(4) : DeliveryOutcome.Runs(Math.Clamp(outcome.RunsConceded, 1, 4))) with { Zone = zone };
    }

    /// <summary>
    /// A ball worked into the field for a run or two. Mostly uneventful - but a fumble gives away
    /// an extra, and a side that fumbles regularly bleeds runs nobody ever remembers.
    /// </summary>
    private DeliveryOutcome ResolveRunningShot(BallContext ctx, int runs, Random random)
    {
        if (ctx.Field is null || ctx.FieldingSide is null) return DeliveryOutcome.Runs(runs);

        var zone = _field.RollZone(ctx.StrikerZones, random);
        var players = ctx.FieldingSide.ToDictionary(p => p.Id);

        var outcome = _field.ResolveGroundFielding(ctx.Field, zone, players, runs, random);
        return outcome is null ? DeliveryOutcome.Runs(runs) : DeliveryOutcome.Runs(Math.Clamp(outcome.RunsConceded, runs, runs + 1));
    }

    /// <summary>Whether this bowler is a spinner or a seamer, derived from what he can actually do.</summary>
    public static BowlerType GetBowlerType(Player bowler) =>
        IsSpinner(bowler) ? BowlerType.Spin : BowlerType.Pace;

    // ---------------- effective skill ----------------

    /// <summary>
    /// What this batter actually brings to THIS ball. Attribute weights change by format and
    /// phase, which is the whole reason a Test opener and a T20 finisher are different players
    /// rather than the same player with a different number.
    /// </summary>
    public double GetBatterEffectiveSkill(BallContext ctx)
    {
        var bat = ctx.Striker.Batting;

        double technical = (bat.Technique * 2 + bat.Timing * 2 + bat.ShotSelection * 1.5 + bat.DefensiveAbility) / 6.5;
        double scoring = (bat.PowerHitting * 2 + bat.BoundaryHitting * 2 + bat.StrikeRotation * 1.5 + bat.Aggression) / 6.5;

        // Format decides how much technique matters versus scoring power. In a Test, method is
        // nearly everything; in T20 it is roughly a third of the story.
        double technicalWeight = ctx.Format switch
        {
            MatchFormat.Test => 0.75,
            MatchFormat.ODI => 0.55,
            _ => 0.35
        };

        // Phase shifts it again - the same T20 batter needs method in the powerplay against the
        // new ball and power at the death.
        technicalWeight += ctx.Phase switch
        {
            MatchPhase.Powerplay => 0.08,
            MatchPhase.DeathOvers => -0.12,
            _ => 0
        };
        technicalWeight = Math.Clamp(technicalWeight, 0.2, 0.85);

        double core = AbilityScale.AttributeToHundred((int)Math.Round(technical * technicalWeight + scoring * (1 - technicalWeight)));

        // Bowler-type matchup: facing genuine pace is a different skill from facing spin, and the
        // game has separate attributes for it precisely so this can be modelled rather than averaged.
        bool facingSpin = IsSpinner(ctx.Bowler);
        double typeSkill = AbilityScale.AttributeToHundred(facingSpin ? bat.AgainstSpin : bat.AgainstPace);
        core = core * 0.7 + typeSkill * 0.3;

        // Death-over specialists really are different players in the last few overs.
        if (ctx.Phase == MatchPhase.DeathOvers)
            core = core * 0.75 + AbilityScale.AttributeToHundred(bat.DeathOverBatting) * 0.25;

        // Form, head-to-head history and the occasion.
        core *= 1 + ctx.Striker.Form.CurrentForm / 100.0 * 0.12;
        // BUG FIX: this was MatchupKey.ForOpponent(bowler.Id) - the OPPONENT-TEAM key built from a
        // BOWLER's id. It could never match anything PerformanceRecordingService writes (which
        // records opponent keys from team ids and bowler keys from bowler ids), so head-to-head
        // history silently did nothing, and a bowler id could in principle collide with a team
        // key. Section 22's batsman-vs-bowler matchups depend on this key being right.
        // Wave 3 (point 7): the head-to-head edge is amplified under pressure - a batter's genuine
        // problem against a bowler bites harder in a knockout than a dead rubber - and reads the
        // recent-weighted / slower-career blend, not the raw career average.
        core *= _matchups.GetMatchupMultiplier(ctx.Striker, MatchupKey.ForBowler(ctx.Bowler.Id), ctx.PressureLevel);

        // Issue 11: "the batsman's eye gets set on that bowler" - a WITHIN-THIS-MATCH read,
        // deliberately separate from the career-level matchup line just above. Outcome-gated in
        // BatterMatchState.RecordBowlerFamiliarity - a bowler who keeps beating the bat does not
        // somehow look easier for having bowled a long spell.
        if (ctx.StrikerState is { } strikerMatchState)
            core *= strikerMatchState.GetBowlerFamiliarityMultiplier(ctx.Bowler.Id, mysterious: IsMysterySpinner(ctx.Bowler));

        core *= 1 + _situational.PressureAdjustment(ctx.Striker, ctx.PressureLevel);

        // How he's FEELING, not how he's playing (Form, above) - a smaller effect than form
        // deliberately, since morale is a secondary psychological factor and Form already
        // carries the primary "is he actually in nick" signal. See PlayerMorale/PlayerMoraleService.
        core *= 1 + (ctx.Striker.Morale.Level - 50) / 50.0 * 0.06;

        // §5.10: match sharpness - a ring-rusty batter who has been out of the side has lost a
        // touch of timing. Small (a few per cent at the low end), and it comes back fast once he
        // plays. 100 (a regular) is a no-op.
        core *= 0.94 + Math.Clamp(ctx.Striker.MatchSharpness, 40, 100) / 100.0 * 0.06;

        // Wave 6: in-match momentum. Gated - only a genuine swing does anything - and small,
        // secondary to set-ness/form/morale, per the brief's own "never makes a side unbeatable".
        if (ctx.Momentum is { } m) core *= ValueObjects.MatchMomentum.BattingMultiplierFor(m);

        // §6.3 (in-match, the frequency half): an unfixed technical flaw is a genuine weakness, not
        // just a dismissal-mix flavour - a bowler landing exactly the delivery that tests it makes
        // the batter measurably more fallible THIS ball. Small and only when the delivery actually
        // matches the flaw's shape (a good-length ball at the stumps for a trigger-movement fault, a
        // fourth-stump line for weak outside off, full and straight for a front-foot lbw prone
        // batter, a tight line with no room for a strike-rotation gap).
        if (ctx.Striker.TechnicalFlaw is { } flaw && ctx.Delivery is { } flawDelivery)
        {
            bool exploited = flaw switch
            {
                Enums.TechnicalFlaw.TriggerMovementFault => flawDelivery.Length is BowlingLength.Good && flawDelivery.Line is BowlingLine.AtTheStumps,
                Enums.TechnicalFlaw.WeakOutsideOff => flawDelivery.Line is BowlingLine.FourthStump or BowlingLine.OutsideOff or BowlingLine.WideOutsideOff,
                Enums.TechnicalFlaw.FrontFootLbwProne => flawDelivery.Length is BowlingLength.Full && flawDelivery.Line is BowlingLine.AtTheStumps,
                Enums.TechnicalFlaw.StrikeRotationGap => flawDelivery.Line is BowlingLine.AtTheStumps && flawDelivery.Length is BowlingLength.Good,
                _ => false
            };
            if (exploited) core *= 0.93;
        }

        // §2.9: partnership STYLE FIT. An anchor/partnership-builder paired with a genuine
        // finisher/power-hitter is the classic complementary pairing - one rotates and holds an
        // end, the other cashes in - and it is worth a small amount to both. Two batters both
        // playing the SAME high-risk game (both dominant sloggers/power-hitters) crowd each other
        // and take more risk with nobody settling; a small penalty. Bounded, secondary.
        if (ctx.NonStriker is { } partner)
        {
            int strikerAggro = Math.Max(ctx.Striker.BattingTraits.PowerHitter, ctx.Striker.BattingTraits.Slogger);
            int strikerCalm = Math.Max(ctx.Striker.BattingTraits.Anchor, ctx.Striker.BattingTraits.PartnershipBuilder);
            int partnerAggro = Math.Max(partner.BattingTraits.PowerHitter, partner.BattingTraits.Slogger);
            int partnerCalm = Math.Max(partner.BattingTraits.Anchor, partner.BattingTraits.PartnershipBuilder);

            bool complementary = (strikerCalm >= 60 && partnerAggro >= 60) || (strikerAggro >= 60 && partnerCalm >= 60);
            bool bothAggressive = strikerAggro >= 65 && partnerAggro >= 65;
            if (complementary) core *= 1.02;
            else if (bothAggressive) core *= 0.98;
        }

        // A batter who is in gets better. This is one of the strongest real effects in cricket
        // and it is why a new batter is a wicket-taking opportunity in itself.
        // How he is going right now: set-ness, confidence and fatigue, or the simple balls-faced
        // approximation when no match state is being tracked.
        core *= ctx.StrikerState?.GetEffectivenessMultiplier()
                ?? (0.82 + Math.Min(ctx.StrikerBallsFaced, 30) / 30.0 * 0.18);

        // Playing under a captain the side believes in.
        core *= ctx.CaptainLift;

        // Slice 6.1: playing at home - the crowd, familiar conditions, a settled dressing room.
        core *= ctx.BattingHomeEdge;

        // A friendly surface helps the batter directly.
        core *= 0.9 + ctx.PitchBattingFriendliness / 100.0 * 0.2;

        return Math.Clamp(core, 1, 130);
    }

    /// <summary>What this bowler brings to this ball - his own attributes, weighted for the phase, degraded by fatigue, and helped or hindered by the surface and the state of the ball.</summary>
    public double GetBowlerEffectiveSkill(BallContext ctx)
    {
        // Rhythm, confidence and fatigue. A bowler who has found his spot is a different
        // proposition from one just thrown the ball, and a tiring one is another again.
        double stateMultiplier = ctx.BowlerState?.GetEffectivenessMultiplier() ?? 1.0;

        var bowl = ctx.Bowler.Bowling;
        bool spinner = IsSpinner(ctx.Bowler);

        double control = (bowl.Accuracy * 2 + bowl.Containment) / 3.0;
        double threat = spinner
            ? (bowl.Spin * 2 + bowl.Variation * 1.5 + bowl.AttackingAbility) / 4.5
            : (bowl.Pace * 1.5 + bowl.Swing + bowl.Seam + bowl.Variation + bowl.AttackingAbility) / 5.5;

        // Attacking phases reward threat; containment phases reward control.
        double threatWeight = ctx.Phase switch
        {
            MatchPhase.Powerplay => 0.6,
            MatchPhase.DeathOvers => 0.45,
            _ => 0.5
        };

        double core = AbilityScale.AttributeToHundred((int)Math.Round(threat * threatWeight + control * (1 - threatWeight)));

        // Phase specialists.
        core = ctx.Phase switch
        {
            MatchPhase.Powerplay => core * 0.8 + AbilityScale.AttributeToHundred(bowl.NewBallBowling) * 0.2,
            MatchPhase.DeathOvers => core * 0.75 + AbilityScale.AttributeToHundred(bowl.DeathBowling) * 0.25,
            _ => core * 0.85 + AbilityScale.AttributeToHundred(bowl.MiddleOverBowling) * 0.15
        };

        // The surface. A spinner on a turning pitch and a quick on a green seamer are different
        // propositions from the same bowlers on a flat one - this is what makes conditions matter.
        double pitchHelp = spinner ? ctx.PitchSpin : ctx.PitchPace;
        core *= 0.85 + pitchHelp / 100.0 * 0.3;

        // Ball condition: seam and swing are new-ball weapons, spin and reverse come later.
        core *= BallAgeFactor(ctx, spinner);

        core *= 1 + ctx.Bowler.Form.CurrentForm / 100.0 * 0.10;
        core *= 1 + (ctx.Bowler.Morale.Level - 50) / 50.0 * 0.06;
        core *= 0.94 + Math.Clamp(ctx.Bowler.MatchSharpness, 40, 100) / 100.0 * 0.06; // §5.10: a ring-rusty bowler is off his rhythm
        core *= FatigueFactor(ctx);

        // Wave 6: in-match momentum - the mirror of the batter's read above.
        if (ctx.Momentum is { } m) core *= ValueObjects.MatchMomentum.BowlingMultiplierFor(m);

        // Slice 6.1: the home attack knows its own conditions - what to bowl, which end, how the
        // pitch behaves under lights. Same small, bounded edge the batting side gets at home.
        core *= ctx.BowlingHomeEdge;

        // Section Q/W follow-up: the new-ball partnership. A small, bounded lift (or drag) from
        // how well this specific pairing has contained an opening spell together before - real,
        // but secondary to every skill term above it.
        if (ctx.BowlerState?.NewBallPartnerId is { } partnerId)
            core *= _pairSynergy.GetSynergyMultiplier(ctx.Bowler, partnerId);

        return Math.Clamp(core * stateMultiplier, 1, 130);
    }

    // ---------------- modifiers ----------------

    /// <summary>Tiring bowlers lose accuracy and zip. Stamina decides how fast, which finally gives that attribute something to do.</summary>
    private static double FatigueFactor(BallContext ctx)
    {
        double stamina = AbilityScale.AttributeToHundred(ctx.Bowler.Physical.Stamina);

        // A spell of 4 overs is nothing; 10 straight is a lot. Scaled by stamina so a fit bowler
        // holds his level much longer.
        double spellFatigue = Math.Clamp(ctx.BowlerBallsInSpell / (24.0 + stamina / 100.0 * 36.0), 0, 1) * 0.12;
        double matchFatigue = Math.Clamp(ctx.BowlerBallsInMatch / (90.0 + stamina / 100.0 * 120.0), 0, 1) * 0.08;

        return Math.Clamp(1 - spellFatigue - matchFatigue, 0.75, 1.0);
    }

    /// <summary>
    /// The new ball swings and seams; an old ball turns and, for skilled quicks, reverses. This is
    /// why an opening spell and a 40th-over spell are different disciplines.
    /// </summary>
    private static double BallAgeFactor(BallContext ctx, bool spinner)
    {
        // §1.5: an ODI is played with TWO new balls, one from each end - so a ball is never more
        // than ~25 overs (150 balls) old and the old-ball phase (soft ball, reverse swing) barely
        // exists. Model it by capping the effective age at half.
        int effAge = ctx.Format == MatchFormat.ODI ? Math.Min(ctx.BallAge, 150) : ctx.BallAge;

        if (spinner)
            return 0.9 + Math.Clamp(effAge / 180.0, 0, 1) * 0.2; // grip improves as the ball scuffs

        if (effAge <= 36) return 1.12;                            // brand new - hard, shiny, dangerous
        if (effAge <= 120) return 1.0;
        // §1.2 / reverse swing is a skill, not a gift: only bowlers with real swing ability get it
        // back, and only in the long formats (first-class / a rare old-ball spell). It shifts the
        // dismissal mix toward bowled + LBW (handled in DeliveryEffectService) - here it is the
        // effective-skill bump for the bowler who can genuinely do it.
        double reverseSkill = AbilityScale.AttributeToHundred(ctx.Bowler.Bowling.Swing) / 100.0;
        double reverseSeamAssist = ctx.Format == MatchFormat.Test ? AbilityScale.AttributeToHundred(ctx.Bowler.Bowling.Seam) / 100.0 * 0.06 : 0;
        return 0.92 + reverseSkill * 0.18 + reverseSeamAssist;
    }

    /// <summary>
    /// Boundary scaling from the venue's physical dimensions. 68m is treated as a neutral
    /// boundary. Square size matters most for sixes, outfield speed for fours (a well-timed shot
    /// beating the sweeper), and thin air at altitude carries the ball further.
    /// Returns 1.0 when no ground is supplied, so a neutral-venue simulation is unaffected.
    /// </summary>
    /// <summary>
    /// Phase 15 (§19.6): how aligned a shot zone is with the wind. +1 = straight downwind (the
    /// boundary the wind carries the ball toward), -1 = straight into the wind, ~0 = square to it.
    /// The 8 zones are 45 degrees apart starting behind square on the off side.
    /// </summary>
    private static double WindAlignment(ShotZone zone, double windBearing)
    {
        // Zone bearings (degrees), with 0 = straight behind the bowler's arm.
        double zoneBearing = (int)zone switch
        {
            0 => 315, 1 => 270, 2 => 225, 3 => 180, 4 => 0, 5 => 45, 6 => 90, 7 => 135,
            _ => 0
        };
        double diff = System.Math.Abs(((zoneBearing - windBearing + 540) % 360) - 180); // 0 = same direction, 180 = opposite
        return System.Math.Cos(diff * System.Math.PI / 180.0); // +1 aligned, -1 opposed
    }

    private static double GroundBoundaryFactor(BallContext ctx, bool isSix)
    {
        if (ctx.Ground is not { } ground) return 1.0;

        double boundary = isSix
            ? ground.SquareBoundaryMetres * 0.6 + ground.StraightBoundaryMetres * 0.4
            : ground.SquareBoundaryMetres * 0.5 + ground.StraightBoundaryMetres * 0.5;

        double sizeFactor = 1 + (68 - Math.Clamp(boundary, 50, 90)) / 68.0 * (isSix ? 0.85 : 0.40);
        double outfieldFactor = isSix ? 1.0 : 0.90 + Math.Clamp(ground.OutfieldSpeed, 0, 100) / 100.0 * 0.20;
        double altitudeFactor = isSix ? 1 + Math.Clamp(ground.AltitudeMetres, 0, 2000) / 2000.0 * 0.15 : 1.0;

        return Math.Clamp(sizeFactor * outfieldFactor * altitudeFactor, 0.6, 1.6);
    }

    /// <summary>
    /// A set batter is much harder to dismiss - but never safe.
    ///
    /// Uses the full BatterMatchState when one is supplied, which folds in confidence, fatigue and
    /// the difficulty of the surface as well as balls faced. That last part matters: on a pitch
    /// where the ball is doing something, a batter can make a hundred and never once feel in, which
    /// the old balls-faced-only version could not express.
    ///
    /// The floor stays well above zero on purpose. However set a batter is, some deliveries have
    /// his name on them, and he can be bowled by a part-timer.
    /// </summary>
    private static double SetBatterFactor(BallContext ctx)
    {
        if (ctx.StrikerState is { } state) return state.GetDismissalMultiplier();

        double concentration = AbilityScale.AttributeToHundred(ctx.Striker.Mental.Concentration);
        int ballsToSettle = (int)(24 - concentration / 100.0 * 12); // 12-24 balls

        double settledness = Math.Clamp((double)ctx.StrikerBallsFaced / ballsToSettle, 0, 1);
        return 1.42 - settledness * 0.52;
    }

    private static (double DotMultiplier, double BoundaryMultiplier, double WicketMultiplier) PhaseAdjustment(MatchPhase phase, MatchFormat format)
    {
        if (format == MatchFormat.Test) return (1.0, 1.0, 1.0); // Tests have no fielding-restriction phases

        return phase switch
        {
            // Fielding restrictions mean more boundaries, but the new ball and attacking fields
            // also take more wickets.
            // NOTE: these must not double-count with BattingIntent. The phase multiplier models
            // the OPPORTUNITY the phase creates (fielding restrictions, the new ball, an old ball
            // in the death); intent models the batting side's CHOICE. Driving intent straight off
            // the phase as well would multiply the same effect twice - the identical mistake that
            // once let form be counted twice in selection.
            MatchPhase.Powerplay => (0.94, 1.28, 1.10),
            MatchPhase.MiddleOvers => (1.06, 0.84, 0.92),
            MatchPhase.DeathOvers => (0.84, 1.38, 1.30),
            _ => (1.0, 1.0, 1.0)
        };
    }

    private static (double DotMultiplier, double SingleMultiplier, double BoundaryMultiplier, double SixMultiplier, double WicketMultiplier) IntentAdjustment(BattingIntent intent) => intent switch
    {
        // Every level buys runs with risk. Blocking is very safe and scores almost nothing;
        // AllOut nearly triples the wicket rate. Neither is "better" - that is the point.
        BattingIntent.Blocking => (1.55, 0.55, 0.28, 0.12, 0.50),
        BattingIntent.Anchoring => (1.16, 1.15, 0.70, 0.45, 0.80),
        BattingIntent.Normal => (1.00, 1.00, 1.00, 1.00, 1.00),
        BattingIntent.Attacking => (0.84, 0.96, 1.30, 1.48, 1.26),
        _ => (0.68, 0.74, 1.62, 2.05, 1.80)
    };

    private static (double DotMultiplier, double BoundaryMultiplier, double WicketMultiplier, double ExtrasMultiplier) BowlingIntentAdjustment(BowlingIntent intent) => intent switch
    {
        // Attacking bowling buys wickets by conceding more when it misses - and by leaking extras.
        BowlingIntent.Defensive => (1.18, 0.75, 0.80, 0.85),
        BowlingIntent.Attacking => (0.88, 1.30, 1.30, 1.25),
        _ => (1.0, 1.0, 1.0, 1.0)
    };

    // ---------------- dismissal and special deliveries ----------------

    /// <summary>
    /// Which dismissal, chosen from a realistic distribution that shifts with the bowler's type
    /// and the surface. Stumpings only happen to spin; a bouncy pitch produces more catches and
    /// fewer lbws; a low pitch does the reverse. The specific mode matters for dismissal-pattern
    /// analytics and for crediting the right fielder.
    /// </summary>
    /// <summary>
    /// Turns a catching chance into an actual outcome, against the field that is actually set.
    ///
    /// Three things can happen, and all three are everyday cricket:
    /// - the ball carries to a fielder who holds it - a wicket;
    /// - it carries to a fielder who puts it down - the batter survives, and depending on where
    ///   the chance went it may still run away for runs;
    /// - nobody is there at all - an edge through a vacant slip cordon, which is four.
    ///
    /// This is the mechanic behind the classic trade-off: a fourth slip closes the gap that
    /// edges fly through, but it is a man taken from somewhere else.
    /// </summary>
    private DeliveryOutcome ResolveCaught(BallContext ctx, Random random, bool behindWicket)
    {
        var zone = behindWicket
            ? (random.NextDouble() < 0.75 ? ShotZone.ThirdMan : ShotZone.Point) // edges fly behind square on the off side
            : _field.RollZone(ctx.StrikerZones, random);

        // With no field supplied the model falls back to its old behaviour so neutral-venue
        // testing and any caller that hasn't set a field still works.
        if (ctx.Field is null)
            return DeliveryOutcome.Wicket(behindWicket ? DismissalType.CaughtBehind : DismissalType.Caught,
                behindWicket ? PickKeeper(ctx) : PickFielder(ctx, random));

        if (behindWicket)
        {
            double carry = _field.EdgeCarry(ctx.Field, ctx.Format);
            if (random.NextDouble() >= carry)
                // Beat the cordon entirely. Through the gap, and away.
                return DeliveryOutcome.Runs(random.NextDouble() < 0.65 ? 4 : 1);
        }

        var players = ctx.FieldingSide?.ToDictionary(p => p.Id) ?? new Dictionary<Guid, Player>();

        // How hard the chance is, then whether this specific fielder reaches it and holds it.
        // Three genuinely different outcomes come out of here - held, reached-and-dropped, and
        // never got near it - and they are not interchangeable: the first is a wicket, the second
        // is a let-off the batter cashes in, the third is usually runs.
        double difficulty = _field.RollChanceDifficulty(random);
        var outcome = _field.ResolveCatchChance(ctx.Field, zone, players, difficulty, random);

        if (outcome.Held && outcome.FielderId is not null)
        {
            bool isKeeper = outcome.Position == FieldingPosition.WicketKeeper;
            return DeliveryOutcome.Wicket(
                isKeeper || behindWicket ? DismissalType.CaughtBehind : DismissalType.Caught, outcome.FielderId);
        }

        // Not held. Whatever it cost - a dropped catch in the deep often still runs away, a chance
        // nobody reached usually goes for runs - comes back as runs off the bat.
        return DeliveryOutcome.Runs(Math.Clamp(outcome.RunsConceded, 0, 6));
    }

    private DeliveryOutcome SimulateWicket(BallContext ctx, Random random)
    {
        bool spinner = IsSpinner(ctx.Bowler);
        double bounce = ctx.PitchBounce / 100.0;

        double caught, caughtBehind, bowled, lbw, stumped, caughtAndBowled, hitWicket;

        if (ctx.Delivery is { } executed)
        {
            // DeliveryEffectService.DismissalWeightsFor already computes exactly this - line and
            // length shift HOW a wicket falls (a yorker collapses caught in favour of bowled/lbw;
            // a short ball all but rules lbw out) - but nothing ever called it here, so every
            // dismissal this engine has ever produced was apportioned by spinner/bounce alone,
            // regardless of what was actually bowled. Found while wiring the trap-ball mechanic
            // (Section B), which needs a yorker-built trap to actually shift toward bowled/lbw to
            // mean anything, and fixed rather than left the same way once found.
            var weights = DeliveryEffectService.DismissalWeightsFor(executed, ctx.Striker, ctx.Bowler,
                new PitchConditions(ctx.PitchPace, ctx.PitchSpin, ctx.PitchBounce, ctx.PitchBattingFriendliness),
                ResolveKeeper(ctx));
            caught = weights[DismissalType.Caught];
            caughtBehind = weights[DismissalType.CaughtBehind];
            bowled = weights[DismissalType.Bowled];
            lbw = weights[DismissalType.LBW];
            stumped = weights[DismissalType.Stumped];
            caughtAndBowled = weights[DismissalType.CaughtAndBowled];
            hitWicket = weights[DismissalType.HitWicket];
        }
        else
        {
            caught = 0.34 + bounce * 0.10;
            caughtBehind = spinner ? 0.07 : 0.14 + bounce * 0.04;
            bowled = spinner ? 0.20 : 0.17;
            lbw = (spinner ? 0.20 : 0.15) * (1.3 - bounce * 0.5);
            stumped = (spinner ? 0.06 : 0.0) * KeeperStumpingFactor(ResolveKeeper(ctx));
            caughtAndBowled = 0.04;
            hitWicket = 0.008;
        }

        // Phase 7, Slice 7.9: a weaker / more rattled umpiring panel gives more of the marginal
        // LBWs and caught-behinds. Deterministic multiplier, no new draw - 1.0 (a null panel) is
        // byte-identical to before.
        if (ctx.UmpireOutBias is var bias and not 1.0)
        {
            lbw *= bias;
            caughtBehind *= bias;

            // Phase 15 (§1.7): with DRS, the fielding side reviews some of the marginal ones a
            // cautious panel turned down and gets a fraction of them given. A small, deterministic
            // partial reclaim of the bias gap - no new random draw.
            if (ctx.FieldingHasDrs && bias > 1.0)
            {
                double clawback = 1.0 + (bias - 1.0) * 0.35;
                lbw *= clawback;
                caughtBehind *= clawback;
            }
        }

        // Phase 14 (§6.3, in-match): an unfixed technical flaw shapes HOW this batter tends to get
        // out - it does NOT change how often (that stays the wicket-probability path, deliberately
        // untouched here to keep the scoring/dismissal calibration intact) - so a flawed player's
        // scorecard reads true to his weakness. Mix-only, no new random draw.
        if (ctx.Striker.TechnicalFlaw is { } flaw)
        {
            switch (flaw)
            {
                case Enums.TechnicalFlaw.TriggerMovementFault: bowled *= 1.35; lbw *= 1.30; break;
                case Enums.TechnicalFlaw.WeakOutsideOff: caughtBehind *= 1.45; caught *= 1.10; break;
                case Enums.TechnicalFlaw.FrontFootLbwProne: lbw *= 1.55; break;
                case Enums.TechnicalFlaw.StrikeRotationGap: bowled *= 1.20; lbw *= 1.15; break;
            }
        }

        double runOut = 0.055;
        // Section Q/W: a pair with a history of mix-ups (or none, yet - stays neutral) runs a
        // real, earned risk; a pair who read each other well runs a genuinely lower one.
        if (ctx.NonStriker is { } runningPartner)
        {
            runOut *= _chemistry.GetRunOutRiskMultiplier(ctx.Striker, runningPartner.Id);
            // Phase 11 (§2.10): a good caller between the wickets sets a FLOOR under how much a bad
            // call can cost. Two sharp runners rarely have a mix-up whatever their pairing history;
            // two poor ones compound it. Bounded and secondary to the chemistry read above.
            double calling = (ctx.Striker.Mental.RunningCalling + runningPartner.Mental.RunningCalling) / 2.0;
            runOut *= Math.Clamp(1.25 - calling / 20.0 * 0.5, 0.75, 1.25);
            // Phase 14 (§18.1): a feud between the two at the crease adds a further, bounded risk.
            runOut *= Math.Clamp(ctx.PairRunOutExtra, 1.0, 1.3);
        }

        // Phase 15 (§16.6): with a TV / third umpire (first-class + most domestic), a few of the
        // tightest run-outs and stumpings that the on-field umpire would have given are shown to be
        // not out on the replay - so those dismissals are marginally rarer. Deterministic, no draw.
        if (ctx.HasThirdUmpire)
        {
            runOut *= 0.93;
            stumped *= 0.95;
        }

        double total = caught + caughtBehind + bowled + lbw + stumped + caughtAndBowled + hitWicket + runOut;
        double roll = random.NextDouble() * total;
        double c = 0;

        if ((c += caught) > roll) return ResolveCaught(ctx, random, behindWicket: false);
        if ((c += caughtBehind) > roll) return ResolveCaught(ctx, random, behindWicket: true);
        if ((c += bowled) > roll) return DeliveryOutcome.Wicket(DismissalType.Bowled);
        if ((c += lbw) > roll) return DeliveryOutcome.Wicket(DismissalType.LBW);
        if ((c += stumped) > roll) return DeliveryOutcome.Wicket(DismissalType.Stumped, PickKeeper(ctx));
        if ((c += caughtAndBowled) > roll) return DeliveryOutcome.Wicket(DismissalType.CaughtAndBowled, ctx.Bowler.Id);
        if ((c += hitWicket) > roll) return DeliveryOutcome.Wicket(DismissalType.HitWicket);

        // Run-outs can remove either batter and are not credited to the bowler.
        return DeliveryOutcome.Wicket(DismissalType.RunOut, PickFielder(ctx, random), nonStriker: random.NextDouble() < 0.45);
    }

    /// <summary>A no-ball is a free hit in effect - the batter can still score off it, and often does, because he knows he cannot be bowled or caught out.</summary>
    private DeliveryOutcome SimulateNoBall(BallContext ctx, Random random)
    {
        double roll = random.NextDouble();
        int runs = roll switch
        {
            < 0.45 => 0,
            < 0.70 => 1,
            < 0.78 => 2,
            < 0.92 => 4,
            _ => 6
        };
        return DeliveryOutcome.NoBall(runs);
    }

    /// <summary>Better fielders take more catches, so the catch goes to a weighted pick rather than a uniform one - which is what makes fielding attributes worth having.</summary>
    private static Guid? PickFielder(BallContext ctx, Random random)
    {
        if (ctx.FieldingSide is null || ctx.FieldingSide.Count == 0) return null;

        var candidates = ctx.FieldingSide.Where(p => p.Id != ctx.Bowler.Id).ToList();
        if (candidates.Count == 0) return null;

        double total = candidates.Sum(p => (double)p.Fielding.Catching + p.Fielding.Reflexes);
        double roll = random.NextDouble() * total;
        double c = 0;
        foreach (var fielder in candidates)
        {
            c += fielder.Fielding.Catching + fielder.Fielding.Reflexes;
            if (c > roll) return fielder.Id;
        }
        return candidates[^1].Id;
    }

    private static Guid? PickKeeper(BallContext ctx) =>
        ctx.FieldingSide?.FirstOrDefault(p => p.PrimaryRole == PlayerRole.WicketKeeper)?.Id;

    /// <summary>
    /// External-probe follow-up (Issue-adjacent, keeper standing up/back): the Player actually
    /// standing behind the stumps right now, read from the field that was actually SET rather than
    /// simply whoever's PrimaryRole says keeper - this is what lets an occasional keeper (Section
    /// A's AutoFieldSetter fallback, when no specialist is on the field) genuinely affect stumping
    /// chances rather than the model silently assuming a specialist is always there. Falls back to
    /// PickKeeper's PrimaryRole read when no field was supplied at all.
    /// </summary>
    private static Player? ResolveKeeper(BallContext ctx)
    {
        if (ctx.Field is not null && ctx.FieldingSide is not null)
        {
            var placement = ctx.Field.Placements.FirstOrDefault(p => p.Position == FieldingPosition.WicketKeeper);
            if (placement is not null)
                return ctx.FieldingSide.FirstOrDefault(p => p.Id == placement.PlayerId);
        }
        return ctx.FieldingSide?.FirstOrDefault(p => p.PrimaryRole == PlayerRole.WicketKeeper);
    }

    /// <summary>
    /// A spinner draws the keeper up to the stumps - the real "standing up" decision, and the
    /// reason a genuine stumping chance exists at all against slow bowling. How often it is
    /// actually TAKEN depends on the keeper's own hands and feet: an elite keeper up to the stumps
    /// converts most of what comes his way, a clumsy occasional keeper standing in fluffs far more
    /// of them - which is why an average side does not simply inherit the same stumping rate a
    /// specialist keeper would produce. No field/keeper supplied stays at the model's original,
    /// keeper-agnostic rate (1.0) - the previous behaviour, unaffected.
    /// </summary>
    private double KeeperStumpingFactor(Player? keeper)
    {
        if (keeper is null) return 1.0;
        double aptitude = _fieldingAptitude.GetAptitude(keeper, FieldingPosition.WicketKeeper) / 100.0;
        return Math.Clamp(0.55 + aptitude * 0.75, 0.55, 1.30); // a poor stand-in converts barely half as often as an elite gloveman
    }

    public static bool IsSpinner(Player bowler) =>
        bowler.BowlingRole == BowlingRoleType.SpecialistSpinner
        || (bowler.Bowling.Spin > bowler.Bowling.Pace && bowler.Bowling.Spin >= 10);

    /// <summary>Which side of the crease a bowler naturally runs in from - the arm, not the spin style. Used wherever bowling ANGLE (over/round the wicket) needs to be crossed with batting hand.</summary>
    public static bool IsLeftArm(BowlingStyle style) => style is
        BowlingStyle.LeftArmFast or BowlingStyle.LeftArmFastMedium or BowlingStyle.LeftArmMediumFast
        or BowlingStyle.LeftArmMedium or BowlingStyle.LeftArmOrthodox or BowlingStyle.LeftArmChinaman;

    /// <summary>
    /// A genuine mystery spinner - the planning-brief archetype (confirmed NOT to already exist
    /// anywhere in this codebase before this pass, despite the brief's own claim that it did).
    /// Deliberately NOT a new BowlingStyle value or a new attribute: "mystery" is not an arm/action
    /// category the way off-spin vs leg-spin is, and real mystery bowlers span both finger spin
    /// (Ashwin's carrom ball) and wrist spin (Kuldeep, Ajantha Mendis) - it is a DECEPTION skill
    /// layered on top of an existing spin style, which Bowling.Variation (how wide and well-disguised
    /// a repertoire he actually has) already measures. A genuine mystery threat is a spinner with a
    /// wide enough repertoire that his stock ball and his variation are not reliably tellable apart -
    /// which is exactly what a high Variation reading on a spinner represents, so this reuses that
    /// attribute rather than inventing a parallel one for the same underlying idea.
    ///
    /// This is a CLASSIFICATION, not an effectiveness bonus by itself - "+X% effectiveness" is
    /// explicitly the wrong shape per the brief. What actually happens to a batter facing one comes
    /// from BatterMatchState.RecordBowlerFamiliarity/GetBowlerFamiliarityMultiplier (harder and
    /// slower to read him specifically, the more so the less familiar the batter still is - which
    /// naturally favours a longer format, where there is more time to study him, over a short one,
    /// without any format-specific code at all) and DeliveryEffectService.ApplyPatternReading
    /// (his own line/length pattern tells a batter far less than an honest bowler's would).
    /// </summary>
    public static bool IsMysterySpinner(Player bowler) =>
        IsSpinner(bowler) && AbilityScale.AttributeToHundred(bowler.Bowling.Variation) >= 65;
}
