using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What this bowler has learned about the batter he is bowling to during this spell.</summary>
/// <param name="ConsecutiveDots">
/// The TRAILING streak of dots/non-boundary balls this bowler has just strung together against
/// THIS batter specifically - not BatterMatchState.ConsecutiveDots, which is a whole-innings
/// streak agnostic of who is bowling. Sections B/C: this is the genuine "pressure built" signal a
/// bowler's own judgement escalates from (see BowlerExecutionService's trap-ball branch) - a
/// different, proactive trigger from "being milked" (RunRate), which is the opposite state.
/// </param>
/// <param name="RecentDeliveries">
/// The last few (line, length) pairs actually bowled at this batter by this bowler, oldest
/// first - the raw material for both the bowler's own trap-building (BuildTrapDelivery) and the
/// batter's pattern-reading (DeliveryEffectService.Calculate's recentPattern parameter). Null/
/// empty means there is nothing yet to read.
/// </param>
/// <param name="WithinOverPattern">
/// Follow-up Pass 4 (§2.6): the (line, length) pairs bowled at THIS batter so far in the CURRENT
/// over only - a genuinely different window from <see cref="RecentDeliveries"/>, which can span
/// several of this bowler's spells across the innings. This is what lets a bowler shape an over
/// as its own unit (several balls on one theme, then something different to finish it),
/// regardless of whether those balls went for dots - the reason this is a separate signal from
/// <see cref="ConsecutiveDots"/> rather than folded into it.
/// </param>
/// <param name="IsLastBallOfOver">Follow-up Pass 4 (§2.6): true when this delivery is the sixth legal ball of the over - the only point an "over-ending" finisher makes sense.</param>
public sealed record SpellReadout(
    int BallsAtThisBatter, int RunsConceded, int Boundaries, int PlayAndMisses,
    int ConsecutiveDots = 0,
    IReadOnlyList<(BowlingLine Line, BowlingLength Length)>? RecentDeliveries = null,
    IReadOnlyList<(BowlingLine Line, BowlingLength Length)>? WithinOverPattern = null,
    bool IsLastBallOfOver = false)
{
    public double RunRate => BallsAtThisBatter == 0 ? 0 : RunsConceded * 6.0 / BallsAtThisBatter;
    public static SpellReadout Empty => new(0, 0, 0, 0);
}

/// <summary>
/// The bowler's own head. This is the piece the design brief insists on: a bowler told to bowl
/// outside off must not simply bowl outside off forever while being carted.
///
/// Three things happen between the coach's instruction and the ball that is actually delivered:
///
/// 1. **Execution.** Landing a plan is a skill. Accuracy, and whether he has bowled this length
///    before, decide how close the ball is to the instruction. A brilliant plan poorly executed is
///    still a bad ball, and that is why a coach cannot simply out-think a weak attack.
///
/// 2. **Judgement.** A smart bowler notices things - that the batter is picking him, that the plan
///    is being milked, that there is an obvious weakness the coach didn't mention - and adjusts.
///    Game awareness and decision making drive this, and experience sharpens it. A dim bowler
///    keeps bowling the same thing into the same gap.
///
/// 3. **Discipline.** A bowler with poor professionalism departs from a plan that IS working,
///    usually to try something that feels more exciting. That is a real and infuriating thing that
///    happens in cricket, and it is why a coach's relationship with his bowlers matters.
///
/// The coach's Insistence dial sits over all of it. High insistence buys obedience - which is
/// exactly what you want when your plan is right and exactly what loses matches when it isn't.
/// </summary>
public sealed class BowlerExecutionService
{
    private static double Scale(int attribute) => AbilityScale.AttributeToHundred(attribute);

    /// <summary>How many trailing dots against THIS batter a bowler needs before his own judgement considers going for the kill - see SpellReadout.ConsecutiveDots.</summary>
    private const int TrapPressureThreshold = 3;

    /// <summary>
    /// How reliably this bowler can land the instructed line and length, 0-100. A yorker is far
    /// harder to land than a good length, and that difficulty gap is why death bowling is a
    /// specialism rather than a decision.
    /// </summary>
    public double GetExecutionSkill(Player bowler, BowlingApproach approach)
    {
        double core = Scale(bowler.Bowling.Accuracy) * 0.55
                      + Scale(bowler.Mental.Concentration) * 0.20
                      + Scale(bowler.Bowling.Variation) * 0.10
                      + Scale(bowler.Mental.Composure) * 0.15;

        // Some plans are simply harder to execute than others.
        double difficulty = approach.Length switch
        {
            BowlingLength.Yorker => 0.62,
            BowlingLength.Short => 0.85,
            BowlingLength.BackOfLength => 0.90,
            BowlingLength.Full => 0.92,
            _ => 1.0
        };

        // A specialist bowls his own plan better - but specialism is read from ATTRIBUTES, not from
        // the role label. An opening bowler with genuine death skills is a death bowler when he is
        // handed the ball at the death, which is completely normal in real cricket and impossible
        // to express if a single role enum decides it.
        double specialism = approach.Length switch
        {
            BowlingLength.Yorker => 0.90 + Scale(bowler.Bowling.DeathBowling) / 100.0 * 0.28
                                    + Scale(bowler.Bowling.Yorker) / 100.0 * 0.10,
            BowlingLength.Good => 0.95 + Scale(bowler.Bowling.NewBallBowling) / 100.0 * 0.10
                                  + Scale(bowler.Bowling.MiddleOverBowling) / 100.0 * 0.08,
            BowlingLength.Short => 0.94 + Scale(bowler.Bowling.Bouncer) / 100.0 * 0.16,
            _ => 1.0
        };

        return Math.Clamp(core * difficulty * specialism, 5, 100);
    }

    /// <summary>
    /// How good this bowler's own cricket brain is, 0-100. Separate from execution: plenty of
    /// bowlers know exactly what to bowl and cannot land it, and plenty land whatever they are told
    /// without ever working out what to bowl.
    /// </summary>
    public double GetJudgement(Player bowler) =>
        Math.Clamp(Scale(bowler.Mental.GameAwareness) * 0.40
                   + Scale(bowler.Mental.DecisionMaking) * 0.35
                   + bowler.Experience.Level * 0.25, 0, 100);

    /// <summary>How likely he is to stick to instructions he has been given. Poor discipline abandons plans that are working.</summary>
    public double GetDiscipline(Player bowler) =>
        Math.Clamp(Scale(bowler.Mental.Professionalism) * 0.55 + Scale(bowler.Mental.Concentration) * 0.45, 0, 100);

    /// <summary>
    /// Resolves one delivery from the instruction. Returns what was actually bowled and why it
    /// differed, so post-match analysis can tell the coach whether his plans were followed, missed,
    /// or overruled by a bowler who thought he knew better - and whether he was right.
    /// </summary>
    /// <param name="authority">
    /// Who has the final say on this bowler's plan. Under CoachHasFinalSay he bowls what he was
    /// told - he can still MISS his mark, because execution is a skill and no instruction changes
    /// that, but he does not change his line off his own bat or wander off the plan. If he thinks
    /// something else would work better he says so, and the returned Suggestion is what the coach
    /// reads between overs.
    ///
    /// This is the difference between a management game and a spectator sport: the coach's
    /// decisions are the coach's. Delegating is his choice to make, not the bowler's.
    /// </param>
    public ExecutedDelivery Execute(
        Player bowler,
        BowlingApproach approach,
        SpellReadout readout,
        BattingZoneStrengths? batterZones,
        Random random,
        DecisionAuthority authority = DecisionAuthority.Delegated)
    {
        var bowlerType = BallOutcomeModel.GetBowlerType(bowler);

        // A variation the bowler physically cannot bowl is dropped, and that is worth surfacing -
        // asking a seamer for a googly means the plan was set carelessly.
        var variation = approach.IsVariationValidFor(bowlerType) ? approach.Variation : DeliveryVariation.None;
        string? planWarning = variation == approach.Variation ? null
            : $"{bowler.FullName} cannot bowl a {approach.Variation} - variation ignored.";

        // The warning is about the PLAN, not about this particular ball, so it has to survive every
        // path out of this method. Attaching it only to the "followed instructions" path meant the
        // coach was never told his plan was impossible whenever the bowler also missed his length.
        ExecutedDelivery WithWarning(ExecutedDelivery delivery) =>
            planWarning is null ? delivery
            : delivery with { Note = delivery.Note is null ? planWarning : $"{planWarning} {delivery.Note}" };

        double judgement = GetJudgement(bowler);
        double discipline = GetDiscipline(bowler);
        double insistence = Math.Clamp(approach.Insistence, 0, 100);

        // The coach has the final say: the bowler bowls the plan. He can still miss his mark - that
        // is skill, not obedience - but he does not decide for himself to bowl something else.
        if (authority is DecisionAuthority.CoachHasFinalSay or DecisionAuthority.Consult)
        {
            double coachExecution = RollExecution(bowler, approach, random);

            var suggestion = BuildSuggestion(bowler, approach, readout, batterZones, judgement);

            if (coachExecution >= 0.55)
                return WithWarning(new ExecutedDelivery(approach.Line, approach.Length, variation,
                    coachExecution, PlanDeviationReason.Followed, suggestion));

            var (missedL, missedLen) = MissTowards(approach);
            return WithWarning(new ExecutedDelivery(missedL, missedLen, variation, coachExecution,
                PlanDeviationReason.ExecutionError,
                suggestion is null ? $"{bowler.FullName} misses his length."
                                   : $"{bowler.FullName} misses his length. {suggestion}"));
        }

        // --- does he depart from the plan? ---

        // Being milked is the honest trigger. The better his judgement, the sooner he notices; high
        // insistence from the coach holds him to it longer, for better or worse.
        bool beingMilked = readout.BallsAtThisBatter >= 6 && readout.RunRate > 9;
        if (beingMilked)
        {
            double changeChance = judgement / 100.0 * 0.55 * (1 - insistence / 200.0);
            if (random.NextDouble() < changeChance)
            {
                var adjusted = AdjustAwayFromPunishment(approach, batterZones, bowlerType);
                return WithWarning(new ExecutedDelivery(adjusted.Line, adjusted.Length, adjusted.Variation,
                    RollExecution(bowler, adjusted, random), PlanDeviationReason.PlanNotWorking,
                    $"{bowler.FullName} has changed his line - he was going for runs."));
            }
        }

        // Pressure built, not pressure lost - the opposite state from being milked, and a
        // genuinely different trigger from ExploitWeakness below (which reads the batter's
        // static profile, not what has just happened in this spell). Section B: a bowler who has
        // just strung together a real dot-ball streak against THIS batter has earned the right to
        // go for the kill, and a smart one takes it. High insistence HELPS here, unlike the
        // being-milked retreat above - a captain backing the plan is exactly what lets a bowler
        // commit to finishing off pressure he built himself, rather than losing his nerve.
        bool pressureBuilt = !beingMilked && readout.ConsecutiveDots >= TrapPressureThreshold;
        if (pressureBuilt)
        {
            double trapChance = judgement / 100.0 * 0.18 * (0.5 + insistence / 200.0);
            if (random.NextDouble() < trapChance)
            {
                var trap = BuildTrapDelivery(approach, bowlerType, readout.RecentDeliveries);
                return WithWarning(new ExecutedDelivery(trap.Line, trap.Length, trap.Variation,
                    RollExecution(bowler, trap, random), PlanDeviationReason.ReadTheBatter,
                    $"{bowler.FullName} has built the pressure and goes for the kill."));
            }
        }

        // §2.6 (Follow-up Pass 4): sequencing WITHIN a single over - a genuinely different signal
        // from the cross-spell dot-streak trap above. A real bowler often shapes an over as its
        // own unit: several balls on a consistent line or length, then something different to
        // finish it, whether or not those balls actually went for dots - which is exactly what
        // distinguishes this from the pressure-built trigger. Deliberately mutually exclusive
        // with it (an "else if"), so the two mechanisms can never compound into a double trap on
        // the same ball, and deliberately a smaller, gentler chance than the cross-spell trap -
        // this is a lighter, more common tactic, not the rarer "he's cracked him" moment.
        bool overEndingSetup = !beingMilked && !pressureBuilt && readout.IsLastBallOfOver
            && readout.WithinOverPattern is { Count: >= 3 };
        if (overEndingSetup)
        {
            double setupChance = judgement / 100.0 * 0.14 * (0.5 + insistence / 200.0);
            if (random.NextDouble() < setupChance)
            {
                var finisher = BuildTrapDelivery(approach, bowlerType, readout.WithinOverPattern);
                return WithWarning(new ExecutedDelivery(finisher.Line, finisher.Length, finisher.Variation,
                    RollExecution(bowler, finisher, random), PlanDeviationReason.ReadTheBatter,
                    $"{bowler.FullName} has set the over up and finishes it with something different."));
            }
        }

        // A sharp bowler spots a weakness the plan doesn't address. This is the bowler adding value
        // rather than merely obeying, and it is why an intelligent bowler is worth more.
        if (batterZones is not null && readout.BallsAtThisBatter >= 4)
        {
            double spotChance = Math.Max(0, judgement - 60) / 40.0 * 0.30 * (1 - insistence / 250.0);
            if (random.NextDouble() < spotChance)
            {
                var exploit = ExploitWeakness(approach, batterZones, bowlerType);
                if (exploit != approach)
                    return WithWarning(new ExecutedDelivery(exploit.Line, exploit.Length, exploit.Variation,
                        RollExecution(bowler, exploit, random), PlanDeviationReason.ReadTheBatter,
                        $"{bowler.FullName} has spotted something and adjusted."));
            }
        }

        // Indiscipline: abandoning a plan for no reason. Rare for a professional, common for a
        // bowler who simply does what he fancies - and high insistence suppresses it, because the
        // coach has made himself clear.
        double indisciplineChance = (1 - discipline / 100.0) * 0.12 * (1 - insistence / 150.0);
        if (random.NextDouble() < indisciplineChance)
        {
            var whim = RandomApproach(bowlerType, random);
            return WithWarning(new ExecutedDelivery(whim.Line, whim.Length, whim.Variation,
                RollExecution(bowler, whim, random), PlanDeviationReason.Indiscipline,
                $"{bowler.FullName} has gone away from the plan."));
        }

        // --- he is trying to bowl the plan. Does he land it? ---
        double execution = RollExecution(bowler, approach, random);

        if (execution >= 0.55)
            return WithWarning(new ExecutedDelivery(approach.Line, approach.Length, variation, execution, PlanDeviationReason.Followed));

        // Missed his mark. The ball drifts towards whatever is easiest to bowl, which is how a bad
        // ball actually happens - not as a random line, but as a failed attempt at a good one.
        var (missedLine, missedLength) = MissTowards(approach);
        return WithWarning(new ExecutedDelivery(missedLine, missedLength, variation, execution, PlanDeviationReason.ExecutionError,
            $"{bowler.FullName} misses his length."));
    }

    /// <summary>
    /// What the bowler would tell his coach, if anything. A bowler with a good cricket brain who can
    /// see the plan is not working - or can see a weakness the plan does not address - says so. A
    /// bowler without one has nothing useful to add, which is itself a reason to delegate to some
    /// bowlers and not others.
    /// </summary>
    private static string? BuildSuggestion(
        Player bowler, BowlingApproach approach, SpellReadout readout, BattingZoneStrengths? zones, double judgement)
    {
        if (judgement < 55) return null; // he has no particular view

        if (readout.BallsAtThisBatter >= 6 && readout.RunRate > 9)
            return $"{bowler.FullName} says this plan is going for runs and wants to change his line.";

        // Under CoachHasFinalSay/Consult the bowler never acts on this himself (see the trap-ball
        // branch further down, only reachable once delegated) - but a sharp bowler who has built
        // real pressure still has a view worth surfacing between overs.
        if (readout.ConsecutiveDots >= TrapPressureThreshold)
            return $"{bowler.FullName} reckons he has built enough pressure to go for the kill.";

        // §2.6 (Follow-up Pass 4): the within-over equivalent of the same view, for a coach who
        // wants to see it rather than have it acted on for him.
        if (readout.IsLastBallOfOver && readout.WithinOverPattern is { Count: >= 3 })
            return $"{bowler.FullName} wants to finish the over with something different from what he's just bowled.";

        if (zones is not null && readout.BallsAtThisBatter >= 4 && judgement > 70)
        {
            var weakest = Enum.GetValues<ShotZone>().OrderBy(z => zones[z]).First();
            if (zones[weakest] < 45)
                return $"{bowler.FullName} reckons there is something to attack around {weakest}.";
        }

        return null;
    }

    private double RollExecution(Player bowler, BowlingApproach approach, Random random)
    {
        double skill = GetExecutionSkill(bowler, approach) / 100.0;

        // Centred on skill with real spread - even an accurate bowler sprays one occasionally, and
        // an inaccurate one nails one now and then.
        double roll = skill * 0.7 + random.NextDouble() * 0.45 - 0.1;
        return Math.Clamp(roll, 0, 1);
    }

    /// <summary>A missed ball drifts towards the easy option: a fraction shorter and straighter than intended, which is exactly what gets hit.</summary>
    private static (BowlingLine Line, BowlingLength Length) MissTowards(BowlingApproach approach)
    {
        var length = approach.Length switch
        {
            BowlingLength.Yorker => BowlingLength.Full,        // the missed yorker is a half-volley
            BowlingLength.Full => BowlingLength.Good,
            BowlingLength.Good => BowlingLength.BackOfLength,
            BowlingLength.BackOfLength => BowlingLength.Short,
            _ => BowlingLength.Short
        };

        var line = approach.Line switch
        {
            BowlingLine.WideOutsideOff => BowlingLine.OutsideOff,
            BowlingLine.OutsideOff => BowlingLine.FourthStump,
            BowlingLine.FourthStump => BowlingLine.AtTheStumps,
            BowlingLine.IntoTheBody => BowlingLine.LegStump,
            _ => approach.Line
        };

        return (line, length);
    }

    /// <summary>
    /// Section B: the trap ball a genuine setup earns. What has actually been bowled decides what
    /// the trap IS, not a random wicket-ball dressed up as one:
    /// - Contained on a full-ish length -> go full and quick (yorker) for bowled/lbw - the
    ///   textbook "died for the yorker" sequence.
    /// - Contained on one consistent LINE (regardless of length) -> jag it back at the stumps,
    ///   catching a batter who has settled his aim on one specific channel.
    /// - Contained short -> the well-set-up bouncer, having softened him up for it.
    /// A spinner's trap is the same idea in spin terms: the arm ball after a consistent line (the
    /// batter has read the angle, so straighten it), or a top-spinner/googly for genuine deception
    /// once no consistent line has been established to exploit directly.
    /// </summary>
    private static BowlingApproach BuildTrapDelivery(
        BowlingApproach approach, BowlerType type, IReadOnlyList<(BowlingLine Line, BowlingLength Length)>? recent)
    {
        bool recentlyFull = recent is { Count: > 0 }
            && recent.All(d => d.Length is BowlingLength.Good or BowlingLength.Full or BowlingLength.BackOfLength);
        bool recentlyShort = recent is { Count: > 0 } && recent.All(d => d.Length == BowlingLength.Short);
        bool recentlyOneLine = recent is { Count: >= 2 } && recent.Select(d => d.Line).Distinct().Count() == 1;

        if (type == BowlerType.Pace)
        {
            if (recentlyFull)
                return approach with { Length = BowlingLength.Yorker, Line = BowlingLine.AtTheStumps, Variation = DeliveryVariation.Yorker };
            if (recentlyShort)
                return approach with { Length = BowlingLength.Short, Variation = DeliveryVariation.Bouncer };
            if (recentlyOneLine)
                return approach with { Line = BowlingLine.AtTheStumps, Length = BowlingLength.Full };
            return approach with { Length = BowlingLength.Yorker, Variation = DeliveryVariation.Yorker };
        }

        return approach with
        {
            Variation = recentlyOneLine ? DeliveryVariation.ArmBall : DeliveryVariation.TopSpinner,
            Length = BowlingLength.Good
        };
    }

    /// <summary>Moving away from punishment: go straighter and fuller if he has been driven, wider and shorter if he has been pulled.</summary>
    private static BowlingApproach AdjustAwayFromPunishment(BowlingApproach approach, BattingZoneStrengths? zones, BowlerType type)
    {
        bool punishedOnLegSide = zones is not null
            && (zones[ShotZone.MidWicket] + zones[ShotZone.SquareLeg]) > (zones[ShotZone.Cover] + zones[ShotZone.Point]);

        return punishedOnLegSide
            ? approach with { Line = BowlingLine.WideOutsideOff, Length = BowlingLength.Good, Insistence = approach.Insistence }
            : approach with { Line = BowlingLine.AtTheStumps, Length = type == BowlerType.Pace ? BowlingLength.Full : BowlingLength.Good };
    }

    /// <summary>The bowler backing his own read: attack the batter's weakest area rather than the one the coach named.</summary>
    private static BowlingApproach ExploitWeakness(BowlingApproach approach, BattingZoneStrengths zones, BowlerType type)
    {
        var weakest = Enum.GetValues<ShotZone>().OrderBy(z => zones[z]).First();

        return weakest switch
        {
            ShotZone.SquareLeg or ShotZone.FineLeg when type == BowlerType.Pace
                => approach with { Line = BowlingLine.IntoTheBody, Length = BowlingLength.Short, Variation = DeliveryVariation.Bouncer },
            ShotZone.Cover or ShotZone.Point
                => approach with { Line = BowlingLine.OutsideOff, Length = BowlingLength.Good },
            ShotZone.MidOff or ShotZone.MidOn
                => approach with { Line = BowlingLine.AtTheStumps, Length = BowlingLength.Full },
            _ => approach
        };
    }

    private static BowlingApproach RandomApproach(BowlerType type, Random random)
    {
        var lines = Enum.GetValues<BowlingLine>();
        var lengths = type == BowlerType.Spin
            ? new[] { BowlingLength.Good, BowlingLength.Full, BowlingLength.BackOfLength }
            : Enum.GetValues<BowlingLength>();

        return new BowlingApproach(lines[random.Next(lines.Length)], lengths[random.Next(lengths.Length)]);
    }
}
