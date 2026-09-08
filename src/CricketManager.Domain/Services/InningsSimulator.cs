using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>The eleven, in batting order, plus who is allowed to bowl. Slice 3 will let the AI/coach build this; for now the caller supplies it, so the innings engine is testable without a selection system.</summary>
public sealed record InningsSetup(
    IReadOnlyList<Player> BattingOrder,
    IReadOnlyList<Player> FieldingSide,
    IReadOnlyList<Player> AvailableBowlers);

/// <summary>
/// Simulates a complete innings, delivery by delivery.
///
/// What it owns, and why each piece has to live here rather than in the ball model:
/// - **Strike rotation.** Odd runs swap the batters; so does the end of an over. Getting this
///   wrong silently ruins everything downstream, because the wrong player faces the next ball.
/// - **Legal vs illegal deliveries.** Wides and no-balls do not advance the over. An innings that
///   counts them is six balls short every time one is bowled.
/// - **Bowler rotation and spell tracking.** Nobody bowls consecutive overs, per-bowler limits
///   apply in limited-overs cricket, and a bowler taken off and brought back starts a fresh spell -
///   which is what makes the fatigue model mean anything.
/// - **Situational intent.** The batting side does not bat the same way at 12/2 as at 140/3
///   chasing 9 an over. Intent is derived from the match state every ball, so the required rate
///   genuinely forces a side into risk, and a collapse genuinely forces it into caution.
///
/// The ball model decides what happens; this decides what the situation IS. Keeping those apart
/// means the ball model stays a pure function of its context and can be tested on probabilities
/// alone.
/// </summary>
public sealed class InningsSimulator
{
    private readonly BallOutcomeModel _ballModel = new();
    private readonly AutoFieldSetter _fieldSetter = new();
    private readonly BowlerExecutionService _execution = new();
    private readonly FieldSettingRules _rules = new();
    private readonly PlanFitService _planFit = new();
    private readonly CaptaincyService _captaincy = new();
    private readonly OverRateService _overRates = new();
    private readonly DecisionReviewService _drs = new();   // Phase 15 (§1.7)

    /// <summary>Per-bowler over limits in limited-overs cricket. Tests have none, which is why the value is null there.</summary>
    public static int? MaxOversPerBowler(MatchFormat format, int totalOvers) => format switch
    {
        MatchFormat.T20 => Math.Max(1, totalOvers / 5),
        MatchFormat.ODI => Math.Max(1, totalOvers / 5),
        _ => null
    };

    public InningsState Simulate(
        InningsSetup setup,
        MatchFormat format,
        int totalOvers,
        Guid battingTeamId, string battingTeamName,
        Guid bowlingTeamId, string bowlingTeamName,
        Random random,
        int? target = null,
        int inningsNumber = 1,
        PitchConditions? pitch = null,
        double basePressure = 50,
        TacticalPlan? battingPlan = null,
        TacticalPlan? bowlingPlan = null,
        MatchLeadership? battingLeadership = null,
        MatchLeadership? bowlingLeadership = null,
        MatchWeather? weather = null,
        Ground? ground = null,
        int? declarationLead = null,
        int? ballsLeftInMatch = null,
        int? oversPerDay = null,
        int oversAlreadyBowledToday = 0,
        // Issues 1/2/5/6/7 (external probe pass): the running clock for the whole match, when this
        // innings is part of a multi-day one. Ticked one over at a time as this innings is bowled -
        // including advancing to a fresh day, mid-innings, exactly as real cricket resumes overnight.
        // Null (the default, and every existing caller's behaviour) means no clock is tracked -
        // limited-overs cricket and any test context that doesn't need day/session tagging.
        MatchTimeline? timeline = null,
        // Rebuilds the pitch for a new day once the timeline advances to it. Needed alongside
        // `timeline` for a multi-day innings to see day-by-day deterioration correctly if it spans
        // more than one day; ignored if `timeline` is null.
        Func<int, PitchConditions>? pitchForDay = null,
        // Issue 2: a lightweight running clock for LIMITED-OVERS cricket, which has no day/session
        // concept (MatchTimeline is multi-day-shaped) but still deserves a real wall-clock time -
        // when did this over actually happen, so an innings-break duration genuinely moves the
        // clock forward. Ignored when `timeline` is supplied, which already carries its own time.
        TimeOnly? startTime = null,
        // Phase 6, Slice 6.1: the home-side on-field edge for this innings, as a multiplier on
        // effective skill (1.0 = neutral, the default and every existing caller). One of the two
        // is > 1.0 when the batting or the bowling side is playing at home; both stay 1.0 at a
        // neutral venue. MatchSimulator/MultiDayMatchSimulator pass (1 + setup.HomeAdvantage) for
        // whichever side is the home team.
        double battingHomeEdge = 1.0,
        double bowlingHomeEdge = 1.0,
        // Phase 7, Slice 7.9: the umpiring panel's lean on the marginal decision. 1.0 (the default
        // and every pre-Phase-7 caller) = a neutral top-class panel; MatchSimulator /
        // MultiDayMatchSimulator pass MatchSetup.UmpireOutBias, which FixturePlayService computes
        // from the assigned umpires, the occasion and the home crowd. Consumes no RNG.
        double umpireOutBias = 1.0,
        // Phase 15 (§1.7): DRS reviews the batting side has THIS innings. 0 (the default and every
        // pre-Phase-15 caller) = no DRS - the on-field call stands and no extra random draw is made.
        int drsReviewsPerInnings = 0,
        // Phase 14 (§18.1, in-match): a predicate "do these two batters feud?" - when it returns
        // true for the pair at the crease, their run-out risk carries a small extra multiplier.
        // Null (the default and every pre-Phase-14 caller) = no friction anywhere. Consumes no RNG.
        Func<Guid, Guid, bool>? pairHasFriction = null,
        // Phase 15 (§16.6): a TV / third umpire is available for line calls (run-outs, stumpings)
        // even without full DRS. false (the default and every pre-Phase-15 caller) = no TV umpire.
        bool hasThirdUmpire = false,
        // Phase 15 (§19.1): overs at the start of this innings affected by a fresh, damp pitch just
        // after a rain break - the seam bonus decays linearly to nothing across them. 0 (default) =
        // no post-rain state.
        int dampOversAtStart = 0,
        // Phase 15 (§1.6): a WITHIN-innings dew build. 0 (default) = no dew state. Above 0, the
        // ball skids on and grips less for the spinners more and more as the innings goes on -
        // the classic "harder to bowl spin in the second half of a dewy night chase". Scaled 0-1
        // (from Ground.DewTendency and how far into the evening the innings is).
        double dewFactor = 0)
    {
        // Null plans mean "the AI decides everything", which is exactly the previous behaviour -
        // so every existing caller is unaffected and a coach can set as little as he likes.
        battingPlan ??= TacticalPlan.None;
        bowlingPlan ??= TacticalPlan.None;

        // Live per-player match state: how set each batter is, how each bowler is going. Tracked
        // here rather than on the cards because it is match state, not a scorecard statistic - it
        // exists only while the innings is being played.
        var batterStates = new Dictionary<Guid, BatterMatchState>();
        var bowlerStates = new Dictionary<Guid, BowlerMatchState>();

        // Section Q/W follow-up: the order distinct bowlers are first used inside the powerplay -
        // whoever the first two are is this innings' new-ball pair. See BowlingPairSynergyService.
        var newBallBowlerOrder = new List<Guid>();

        // External-probe follow-up: end-wear from finger/wrist spin, indexed by bowling end
        // (approximated as overNumber % 2, since real ends alternate every over exactly like this
        // does - the engine has no separate "end" concept to track more precisely than that).
        // Test/first-class only: a limited-overs innings simply does not bowl enough overs from one
        // end to create a genuine rough patch. See the PitchSpin read in SimulateDelivery below.
        var endWear = new double[2];
        // Phase 15 (§19.2): the rough at each end is created by a specific bowler's follow-through -
        // a left-armer's footmarks land outside a right-hander's off stump, and vice versa. A bowler
        // whose ARM matches the arm that created the rough (it is in his natural landing area, and he
        // turns the ball into that patch) gets the full benefit; a bowler of the other arm gets
        // roughly half, because the rough is on the "wrong" side for the way he spins it.
        var endWearLeftArm = new double[2];
        var endWearRightArm = new double[2];



        var conditions = pitch ?? PitchConditions.Neutral;

        // Phase 15 (§1.7): the batting side's DRS reviews for this innings. ReviewsLeft at 0 (the
        // default) means DRS is not in force at all - no review is ever considered and no extra
        // random number is drawn.
        var drsState = new DrsInningsState { ReviewsLeft = Math.Max(0, drsReviewsPerInnings) };
        bool drsActive = drsReviewsPerInnings > 0;

        // How hard this surface is to settle on. A difficult pitch can stop a batter ever feeling
        // in, however many runs he ends up with.
        double pitchDifficulty = Math.Clamp(100 - conditions.BattingFriendliness, 0, 100);
        FieldSetting? currentField = null;

        // Issue 2's lightweight limited-overs clock. MatchTimeline (multi-day) already carries its
        // own time, so this only ever advances when there is no timeline.
        TimeOnly? runningClock = startTime;

        // A no-ball in limited-overs cricket earns the batting side a free hit off the next legal
        // delivery. First-class cricket has no such thing, which is why this is format-gated.
        bool freeHitPending = false;

        var state = new InningsState
        {
            BattingTeamId = battingTeamId,
            BattingTeamName = battingTeamName,
            BowlingTeamId = bowlingTeamId,
            BowlingTeamName = bowlingTeamName,
            Format = format,
            InningsNumber = inningsNumber,
            // First-class innings have no OVER limit of their own - but a multi-day match still has
            // a finite amount of time, and an innings cannot outlast it. When the caller supplies
            // ballsLeftInMatch (which the multi-day simulator always does) that becomes the cap.
            // Without it a Test innings ran until all out regardless, so a three-day match happily
            // bowled 350 overs and the whole point of time as a resource disappeared.
            MaxLegalBalls = format == MatchFormat.Test
                ? ballsLeftInMatch ?? (totalOvers > 0 ? totalOvers * 6 : null)
                : totalOvers * 6,
            Target = target
        };

        InitialiseCards(state, setup);

        // Openers.
        state.StrikerId = setup.BattingOrder[0].Id;
        state.NonStrikerId = setup.BattingOrder.Count > 1 ? setup.BattingOrder[1].Id : null;
        MarkBatted(state, state.StrikerId!.Value);
        if (state.NonStrikerId is { } nonStriker) MarkBatted(state, nonStriker);

        int nextBatterIndex = 2;
        int overNumber = 0;

        // Phase 15 (§19.1): a gradual post-rain transition. Straight after the covers come off the
        // pitch is damp - a shade more in it for the seamers, harder to time, a little less grip -
        // and it dries out over the next several overs. `dampOversAtStart` is the number of overs
        // that are affected; the effect decays linearly to nothing across them. 0 (the default) =
        // no post-rain state, which is every non-rain-affected innings.
        var baseConditions = conditions;

        while (!state.IsComplete && overNumber < (state.MaxLegalBalls / 6 ?? int.MaxValue))
        {
            if (dampOversAtStart > 0 && overNumber < dampOversAtStart && timeline is null)
            {
                double t = 1.0 - overNumber / (double)dampOversAtStart; // 1 at the start, ~0 at the end
                conditions = baseConditions with
                {
                    Pace = Math.Clamp(baseConditions.Pace + 7 * t, 10, 100),
                    BattingFriendliness = Math.Clamp(baseConditions.BattingFriendliness - 6 * t, 5, 100),
                    Spin = Math.Clamp(baseConditions.Spin - 4 * t, 0, 100)
                };
                pitchDifficulty = Math.Clamp(100 - conditions.BattingFriendliness, 0, 100);
            }
            else if (dampOversAtStart > 0 && overNumber == dampOversAtStart && timeline is null)
            {
                conditions = baseConditions;
                pitchDifficulty = Math.Clamp(100 - conditions.BattingFriendliness, 0, 100);
            }
            else if (dewFactor > 0 && timeline is null)
            {
                // §1.6: dew builds through the innings - negligible early, most pronounced from
                // roughly the second half onward.
                int totalOversEst = (state.MaxLegalBalls ?? 300) / 6;
                double progress = totalOversEst > 0 ? Math.Clamp(overNumber / (double)totalOversEst, 0, 1) : 0;
                double dewNow = dewFactor * Math.Clamp((progress - 0.35) / 0.65, 0, 1);
                if (dewNow > 0.01)
                {
                    conditions = baseConditions with
                    {
                        Spin = Math.Clamp(baseConditions.Spin - 10 * dewNow, 0, 100),
                        BattingFriendliness = Math.Clamp(baseConditions.BattingFriendliness + 4 * dewNow, 5, 100)
                    };
                    pitchDifficulty = Math.Clamp(100 - conditions.BattingFriendliness, 0, 100);
                }
            }

            // Issue 6: bad light, actually acted on. MatchDayClock/OverRateService already knew how
            // to judge it; nothing outside MatchDayClock.cs itself ever called AssessLight before
            // this. Assessed conservatively (as if a pace bowler were up) since that is the stricter
            // real case - a captain who wants to keep going has to turn to spin specifically.
            bool spinOnlyThisOver = false;
            if (timeline is not null)
            {
                var verdict = timeline.AssessLight(nextBowlerIsSpinner: false);
                if (verdict != LightVerdict.Clear)
                {
                    bool hasCapableSpinner = setup.AvailableBowlers.Any(b =>
                        BallOutcomeModel.IsSpinner(b) && b.Id != state.CurrentBowlerId);

                    if (verdict == LightVerdict.SpinOnly && hasCapableSpinner)
                    {
                        double captainJudgement = bowlingLeadership is { } bl ? bl.Profile.TacticalJudgement(bl.Captain) : 55;
                        // Pressing for a wicket right now is the same signal DetermineBowlingIntent
                        // already reads to decide attacking vs defensive bowling - reused rather
                        // than inventing a second "how badly do we want this" read.
                        bool pressingForWin = DetermineBowlingIntent(state, DeterminePhase(state, overNumber)) == BowlingIntent.Attacking;
                        spinOnlyThisOver = _overRates.WantsToContinueInGloom(pressingForWin, hasCapableSpinner, captainJudgement, random);
                    }

                    if (!spinOnlyThisOver)
                    {
                        timeline.EndDayForBadLight();
                        if (!timeline.TryAdvanceDay()) break; // no day left in the match - genuinely over
                        if (pitchForDay is not null)
                        {
                            conditions = pitchForDay(timeline.Day);
                            pitchDifficulty = Math.Clamp(100 - conditions.BattingFriendliness, 0, 100);
                        }
                        foreach (var restingBatterState in batterStates.Values) restingBatterState.TakeBreak(BreakLength.Day);
                        foreach (var restingBowlerState in bowlerStates.Values) restingBowlerState.Rest(BreakLength.Day);
                        ModulateMomentumOnBreak(state, setup, BreakLength.Day); // Wave 6
                        continue; // re-enter the loop fresh on the new day rather than bowling into darkness
                    }
                }
            }

            // Situational pattern memory (Section D follow-up): a coarse, reusable read of "has
            // this captain actually been in a situation like this before" - built once per over
            // from information already in scope, and shared by both Decide calls below so a
            // captain's recollection colours the bowling change and the field placement together,
            // exactly as it would for the same man making both calls in the same over.
            string situationKey = CaptaincyService.BuildSituationKey(
                format, DeterminePhase(state, overNumber), state.Target is not null,
                DeterminePressure(state, basePressure));

            // The bowling change. A captain who reads the game well brings the right man on; one who
            // does not falls back on whoever is nearest to hand.
            var changeCall = bowlingLeadership?.Decide(_captaincy, InMatchDecision.BowlingChange,
                coachHasAPlan: bowlingPlan.NextOverBowlerId is not null, random,
                bowlingPlan.ResolveAuthority(InMatchDecision.BowlingChange), situationKey);

            if (changeCall?.Suggestion is { } changeSuggestion) bowlingLeadership!.Suggestions.Add(changeSuggestion);

            var bowler = SelectBowler(setup, state, format, totalOvers, overNumber, random, bowlingPlan,
                state.StrikerId ?? Guid.Empty, changeCall?.Quality, conditions, weather, ground, bowlerStates,
                spinOnly: spinOnlyThisOver);
            if (bowler is null) break; // nobody left who is allowed to bowl

            // Everyone who is NOT bowling this over is recovering while he fields. This is why a
            // captain rotates his quicks in short spells and brings them back, rather than bowling
            // them out - and it means fatigue unwinds continuously instead of only at intervals.
            foreach (var (bowlerId, bowlerState) in bowlerStates)
            {
                if (bowlerId == bowler.Id) continue;
                var restingBowler = setup.AvailableBowlers.FirstOrDefault(p => p.Id == bowlerId);
                if (restingBowler is not null) bowlerState.RecoverInTheField(restingBowler, 1, weather);
            }

            StartOver(state, bowler, overNumber);

            // Section Q/W follow-up: identify the new-ball pair - the first two distinct bowlers
            // used inside the powerplay window (the same "state.LegalBalls < 36" threshold already
            // used for the newBall field-setting flag below) - so BowlingPairSynergyService has a
            // real pairing to read for the rest of the innings. A third bowler brought on inside
            // the powerplay does not reassign it: the "opening pair" is a real, specific thing, not
            // whoever happens to be bowling early.
            if (state.LegalBalls < 36 && !newBallBowlerOrder.Contains(bowler.Id))
            {
                newBallBowlerOrder.Add(bowler.Id);
                if (newBallBowlerOrder.Count == 2)
                {
                    var (partnerA, partnerB) = (newBallBowlerOrder[0], newBallBowlerOrder[1]);
                    if (!bowlerStates.TryGetValue(partnerA, out var stateA)) bowlerStates[partnerA] = stateA = new BowlerMatchState { PlayerId = partnerA };
                    if (!bowlerStates.TryGetValue(partnerB, out var stateB)) bowlerStates[partnerB] = stateB = new BowlerMatchState { PlayerId = partnerB };
                    stateA.NewBallPartnerId = partnerB;
                    stateB.NewBallPartnerId = partnerA;
                }
            }

            // The captain resets his field at the start of every over: the phase may have
            // changed, the restrictions with it, and there is a new batter on strike. This is
            // also where the field becomes legal by construction - AutoFieldSetter respects the
            // circle limits for the phase and Law 28.4's leg-side caps while building.
            var overPhase = DeterminePhase(state, overNumber);

            // Who made this over's calls, and how well. Field placement is overwhelmingly the
            // captain's - it shifts ball by ball in the middle and a coach cannot do it from the
            // boundary - so a poor captain gives away a worse field than his coach asked for, and a
            // sharp one improves on it.
            var fieldCall = bowlingLeadership?.Decide(_captaincy, InMatchDecision.FieldPlacement,
                coachHasAPlan: bowlingPlan.ResolveManualField(overPhase) is not null
                               || bowlingPlan.ResolveFieldAggression(overPhase) is not null,
                random,
                bowlingPlan.ResolveAuthority(InMatchDecision.FieldPlacement), situationKey);

            if (fieldCall?.Suggestion is { } fieldSuggestion) bowlingLeadership!.Suggestions.Add(fieldSuggestion);
            var strikerForField = setup.BattingOrder.FirstOrDefault(p => p.Id == state.StrikerId);
            // A field the coach placed himself replaces the AI's entirely - but only if it is legal.
            // An illegal field is not overridden silently and then permitted; an umpire would not
            // allow it, so neither does this.
            var coachField = bowlingPlan.ResolveManualField(overPhase);

            // A badly made call loses the coach's field - the captain simply does not set what he
            // was asked for. But NOT when the coach has the final say: an instruction given by a
            // human coach is carried out, and a captain who quietly fails to set it would be taking
            // the game away from the player.
            bool coachDecidesField = bowlingPlan.ResolveAuthority(InMatchDecision.FieldPlacement)
                                     is DecisionAuthority.CoachHasFinalSay or DecisionAuthority.Consult;

            if (!coachDecidesField && fieldCall is { Quality: < 35 } && coachField is not null) coachField = null;

            if (coachField is not null && _rules.Validate(coachField, format, overPhase).IsLegal)
            {
                currentField = coachField;
            }
            else
            {
                // A captain who is not reading the game does not set a field to the batter's
                // profile, does not notice a live attacking pattern, and does not clock that he is
                // being taken apart either - the same gate covers all three, since all three are
                // "is this captain actually watching."
                bool captainNotReadingGame = (!coachDecidesField && fieldCall is { Quality: < 40 }) || strikerForField is null;
                var strikerCardForField = strikerForField is null ? null : state.BatterCards.FirstOrDefault(c => c.PlayerId == strikerForField.Id);

                // §2.1: when the coach has NOT set a field aggression and the captain is reading the
                // game, the AI derives one from the match situation - a bowling side on top (a
                // stalled chase, or wickets taken with the runs dried up) attacks for more; one
                // being taken apart pulls the boundary riders back. Deterministic.
                // §2.1 was tried here directly in the live field-build call and reverted: even
                // decorrelated from captain quality, folding a THIRD source of Attacking/Defensive
                // (on top of the coach's own instruction and the captain's field-quality read) into
                // every single over diluted the established, deliberately-calibrated "poor
                // captaincy costs runs" signal this engine already carries through bowling-change
                // quality and field-PLACEMENT quality (see CaptaincyService) - down to a dead heat
                // even at 900 seeds. The situational read itself is real and available
                // (SituationalFieldAggression below) for a narrower, additive use (e.g. surfaced as
                // a MatchSuggestion) rather than overriding the ball model's own field aggression
                // input on every over.
                var situationalAggression = bowlingPlan.ResolveFieldAggression(overPhase);

                currentField = _fieldSetter.BuildField(
                    setup.FieldingSide, bowler, format, overPhase,
                    newBall: state.LegalBalls < 36,
                    batterZones: captainNotReadingGame ? null : BattingZoneStrengths.FromPlayer(strikerForField!),
                    coachAggression: situationalAggression,
                    liveAttackZone: captainNotReadingGame
                        ? null
                        : batterStates.TryGetValue(strikerForField!.Id, out var strikerMatchState) ? strikerMatchState.DominantAttackZone : null,
                    // The containment half of Section R: a batter genuinely dominating the strike
                    // rate for this format/phase - not just attacking one zone - pulls the field
                    // toward protecting the boundary automatically, whether or not the coach has
                    // separately asked for a defensive setting.
                    strikerBallsFaced: captainNotReadingGame ? 0 : strikerCardForField?.BallsFaced ?? 0,
                    strikerStrikeRate: captainNotReadingGame ? 0 : strikerCardForField?.StrikeRate ?? 0,
                    // §2.2: read only when the captain is actually reading the game - the same gate
                    // every other live field read here already uses.
                    strikerHand: captainNotReadingGame ? null : strikerForField?.BattingHand);
            }
            var bowlerCard = state.BowlerCards.First(c => c.PlayerId == bowler.Id);
            int bowlerRunsAtOverStart = bowlerCard.RunsConceded;
            int runsAtOverStart = state.Runs;          // Wave 6: for the end-of-over momentum swing
            int wicketsAtOverStart = state.Wickets;

            int legalBallsThisOver = 0;
            while (legalBallsThisOver < 6 && !state.IsComplete)
            {
                var striker = setup.BattingOrder.First(p => p.Id == state.StrikerId);
                var nonStrikerPlayer = setup.BattingOrder.FirstOrDefault(p => p.Id == state.NonStrikerId);
                // §19.2: how much of this end's rough THIS bowler can exploit - full value for wear
                // his own arm created, half for the other arm's.
                int roughEnd = overNumber % 2;
                bool bowlerLeftArm = BallOutcomeModel.IsLeftArm(bowler.BowlingStyle);
                double sameArmWear = bowlerLeftArm ? endWearLeftArm[roughEnd] : endWearRightArm[roughEnd];
                double crossArmWear = bowlerLeftArm ? endWearRightArm[roughEnd] : endWearLeftArm[roughEnd];
                double roughForBowler = Math.Min(15.0, sameArmWear + crossArmWear * 0.5);

                var outcome = SimulateDelivery(state, setup, striker, bowler, conditions, basePressure, random, overNumber, currentField, battingPlan, bowlingPlan, legalBallsThisOver, batterStates, bowlerStates, pitchDifficulty, battingLeadership, weather, freeHitPending, ground, nonStrikerPlayer, timeline, runningClock, roughForBowler, battingHomeEdge, bowlingHomeEdge, umpireOutBias, drsActive ? drsState : null, pairHasFriction, hasThirdUmpire);

                // Wave 6: in-match momentum, ball by ball.
                if (outcome.IsWicket && outcome.Dismissal != DismissalType.Retired) state.Momentum.Wicket();
                else if (outcome.RunsOffBat >= 6) state.Momentum.Boundary(six: true);
                else if (outcome.RunsOffBat >= 4) state.Momentum.Boundary(six: false);
                else if (outcome.IsLegalDelivery && outcome.TotalRuns == 0) state.Momentum.Dot();
                else if (outcome.RunsOffBat > 0) state.Momentum.ScoringShot(outcome.RunsOffBat);

                // A no-ball sets up the free hit; any legal delivery consumes it.
                if (format != MatchFormat.Test && outcome.Type == DeliveryOutcomeType.NoBall) freeHitPending = true;
                else if (outcome.IsLegalDelivery) freeHitPending = false;

                if (outcome.IsLegalDelivery) legalBallsThisOver++;

                // §1.8: retired hurt. On a genuinely bouncy surface, a poor player of the short ball
                // can take a blow that forces him off - NOT a dismissal (Wickets is not incremented),
                // and he can return later. Deterministic (no RNG draw, so it never perturbs a
                // stream): fires once, at a specific unlucky ball count, only when the pitch is
                // genuinely quick and the batter genuinely cannot handle it.
                var rhCard = state.BatterCards.FirstOrDefault(c => c.PlayerId == striker.Id);
                if (!outcome.IsWicket && outcome.IsLegalDelivery && outcome.TotalRuns == 0 && rhCard is not null
                    && conditions.Bounce >= 64
                    && conditions.Pace >= 62
                    && Common.AbilityScale.AttributeToHundred(striker.Batting.ShortBallAbility) < 34
                    && rhCard.BallsFaced == 13
                    && !state.RetiredHurtIds.Contains(striker.Id))
                {
                    rhCard.Dismissal = DismissalType.Retired;
                    state.RetiredHurtIds.Add(striker.Id);
                    state.LastDismissedPlayerId = striker.Id;
                    int oversLeftInDayRh = oversPerDay is { } pd && pd > 0
                        ? pd - (oversAlreadyBowledToday + state.LegalBalls / 6) % pd : -1;
                    if (!TryBringInNextBatter(state, setup, ref nextBatterIndex, format, oversLeftInDayRh, battingLeadership, battingPlan, random)) break;
                }

                if (outcome.IsWicket && outcome.Dismissal != DismissalType.Retired)
                {
                    // How much of the day is left, for the nightwatchman decision. -1 when this is
                    // not multi-day cricket, which switches the whole thing off.
                    int oversLeftInDay = oversPerDay is { } perDay && perDay > 0
                        ? perDay - (oversAlreadyBowledToday + state.LegalBalls / 6) % perDay
                        : -1;

                    if (!TryBringInNextBatter(state, setup, ref nextBatterIndex, format, oversLeftInDay, battingLeadership, battingPlan, random)) break;
                }
            }

            // BUG FIX: a maiden is an over from which no runs are charged TO THE BOWLER. Two
            // things were wrong here.
            // 1. It compared total innings runs, so an over containing byes or leg-byes was denied
            //    a maiden - but byes are not the bowler's runs and do not cost him the maiden.
            // 2. It required no wicket to fall. A wicket maiden is a real and celebrated thing;
            //    taking a wicket cannot disqualify an over from being a maiden.
            // Charged runs are now read from the bowler's own card, which is the only figure that
            // actually represents what he conceded.
            if (bowlerCard.RunsConceded == bowlerRunsAtOverStart && legalBallsThisOver == 6)
                bowlerCard.Maidens++;

            EndOver(state);

            // Wave 6: momentum decays toward neutral each over, and a very expensive or a
            // wicket-laden over is itself a swing.
            state.Momentum.EndOver(state.Runs - runsAtOverStart, state.Wickets - wicketsAtOverStart);

            // External-probe follow-up: rough patches. A spinner bowling many overs from the same
            // end wears a genuine patch there over a multi-day match, which is why a later spinner
            // turning the ball INTO that rough gets more out of the same surface than the raw
            // day-based deterioration alone would say. Capped, and Test/first-class only - the
            // day-based EstimateSpinAssistance slope already carries the general wear story;
            // this is the specific, end-targeted addition on top of it.
            if (format == MatchFormat.Test && BallOutcomeModel.IsSpinner(bowler))
            {
                double spinSkill = Common.AbilityScale.AttributeToHundred(bowler.Bowling.Spin) / 100.0;
                int end = overNumber % 2;
                endWear[end] = Math.Min(15.0, endWear[end] + spinSkill * 0.55);
                // §19.2: attribute the wear to the arm that created it.
                if (BallOutcomeModel.IsLeftArm(bowler.BowlingStyle))
                    endWearLeftArm[end] = Math.Min(15.0, endWearLeftArm[end] + spinSkill * 0.55);
                else
                    endWearRightArm[end] = Math.Min(15.0, endWearRightArm[end] + spinSkill * 0.55);
            }

            // The batters get their breath back between overs as well - the walk, the chat, the
            // drinks. Small, but it is why a fit batter can bat all day.
            foreach (var batterId in new[] { state.StrikerId, state.NonStrikerId })
            {
                if (batterId is not { } id || !batterStates.TryGetValue(id, out var batterState)) continue;
                var restingBatter = setup.BattingOrder.FirstOrDefault(p => p.Id == id);
                if (restingBatter is not null) batterState.RecoverBetweenOvers(restingBatter, weather);
            }

            // Issues 1/2/5/7: the clock actually ticks. Advances by however long the over just
            // bowled really took (a four-seam attack's over takes noticeably longer than a
            // spinner's - OverRateService.MinutesPerOver), and auto-claims the extra half hour when
            // the day has fallen behind and the light still allows it.
            if (timeline is not null && !state.IsComplete)
            {
                var sessionBefore = timeline.CurrentSession;
                bool dayScheduledEnd = timeline.BowlOver(bowler);
                if (dayScheduledEnd)
                {
                    if (!timeline.TryAdvanceDay())
                    {
                        overNumber++;
                        break; // no day left in the match - time has genuinely run out
                    }

                    if (pitchForDay is not null)
                    {
                        conditions = pitchForDay(timeline.Day);
                        pitchDifficulty = Math.Clamp(100 - conditions.BattingFriendliness, 0, 100);
                    }
                    foreach (var restingBatterState in batterStates.Values) restingBatterState.TakeBreak(BreakLength.Day);
                    foreach (var restingBowlerState in bowlerStates.Values) restingBowlerState.Rest(BreakLength.Day);
                    ModulateMomentumOnBreak(state, setup, BreakLength.Day); // Wave 6
                }
                else if (timeline.CurrentSession != sessionBefore)
                {
                    // Post-Phase-9 wiring pass (review §1.3): the lunch/tea interval is a real
                    // reset - the batters get a genuine sit-down and the bowlers a proper rest,
                    // and momentum can shift over the break (a side that was under the pump comes
                    // back refreshed). TakeBreak/Rest(BreakLength.Session) had been dead code since
                    // Phase 4 Slice 8 - the clock now actually reaches a session boundary.
                    foreach (var restingBatterState in batterStates.Values) restingBatterState.TakeBreak(BreakLength.Session);
                    foreach (var restingBowlerState in bowlerStates.Values) restingBowlerState.Rest(BreakLength.Session);
                    ModulateMomentumOnBreak(state, setup, BreakLength.Session); // Wave 6
                }
            }
            else if (runningClock is { } t)
            {
                runningClock = t.AddMinutes(_overRates.MinutesPerOver(bowler));
            }

            // Declaration. Only in multi-day cricket, and only when a lead has been established:
            // the captain is buying overs to bowl at the opposition, and he gets it wrong by
            // leaving either too few to force a result or so many that he loses.
            if (declarationLead is { } lead && ballsLeftInMatch is { } ballsLeft
                && ShouldDeclare(state, lead, ballsLeft, overNumber, format, DeclaringCaptainQuality(battingLeadership)))
            {
                state.IsDeclared = true;
                break;
            }

            overNumber++;
        }

        ClosePartnership(state);
        return state;
    }

    // ---------------- one delivery ----------------

    private DeliveryOutcome SimulateDelivery(
        InningsState state, InningsSetup setup, Player striker, Player bowler,
        PitchConditions pitch, double basePressure, Random random, int overNumber,
        FieldSetting? field, TacticalPlan battingPlan, TacticalPlan bowlingPlan, int ballsThisOver,
        Dictionary<Guid, BatterMatchState> batterStates, Dictionary<Guid, BowlerMatchState> bowlerStates,
        double pitchDifficulty, MatchLeadership? battingLeadership, MatchWeather? weather, bool isFreeHit = false,
        Ground? ground = null, Player? nonStriker = null, MatchTimeline? timeline = null, TimeOnly? runningClock = null,
        double roughPatchSpinBonus = 0, double battingHomeEdge = 1.0, double bowlingHomeEdge = 1.0,
        double umpireOutBias = 1.0, DrsInningsState? drs = null, Func<Guid, Guid, bool>? pairHasFriction = null, bool hasThirdUmpire = false)
    {
        if (!batterStates.TryGetValue(striker.Id, out var strikerState))
            batterStates[striker.Id] = strikerState = new BatterMatchState { PlayerId = striker.Id };

        if (!bowlerStates.TryGetValue(bowler.Id, out var bowlerState))
            bowlerStates[bowler.Id] = bowlerState = new BowlerMatchState { PlayerId = bowler.Id };

        var strikerCard = state.BatterCards.First(c => c.PlayerId == striker.Id);
        var bowlerCard = state.BowlerCards.First(c => c.PlayerId == bowler.Id);
        var phase = DeterminePhase(state, overNumber);

        // The coach's bowling plan, then the bowler's own execution and judgement on top of it.
        var approach = bowlingPlan.ResolveBowlingApproach(bowler.Id, striker.Id, phase, overNumber)
                       ?? DefaultApproach(bowler, state, phase);

        var readout = BuildSpellReadout(state, striker.Id, bowler.Id, overNumber, ballsThisOver);
        var executed = _execution.Execute(bowler, approach, readout, BattingZoneStrengths.FromPlayer(striker), random,
            bowlingPlan.ResolveAuthority(InMatchDecision.BowlingPlan, bowler.Id));

        var context = new BallContext
        {
            Delivery = executed,
            RecentDeliveryPattern = readout.RecentDeliveries,
            CaptainLift = battingLeadership?.TeamLift ?? 1.0,
            BattingHomeEdge = battingHomeEdge,
            BowlingHomeEdge = bowlingHomeEdge,
            UmpireOutBias = umpireOutBias,
            FieldingHasDrs = drs is not null,
            StrikerState = strikerState,
            BowlerState = bowlerState,
            PitchDifficulty = pitchDifficulty,
            Striker = striker,
            Bowler = bowler,
            NonStriker = nonStriker,
            PairRunOutExtra = nonStriker is not null && (pairHasFriction?.Invoke(striker.Id, nonStriker.Id) ?? false) ? 1.18 : 1.0,
            WindSpeedKph = weather?.WindSpeedKph ?? 0,
            WindBearing = weather?.WindBearing ?? 0,
            HasThirdUmpire = hasThirdUmpire,
            Format = state.Format,
            Phase = phase,
            Field = field,
            StrikerZones = ApplyZonePreferences(BattingZoneStrengths.FromPlayer(striker), battingPlan.InstructionFor(striker.Id), ground),
            StrikerBallsFaced = strikerCard.BallsFaced,
            StrikerRuns = strikerCard.Runs,
            BowlerBallsInSpell = bowlerCard.BallsInCurrentSpell,
            BowlerBallsInMatch = bowlerCard.LegalBallsBowled,
            WicketsFallen = state.Wickets,
            BallsRemainingInInnings = state.BallsRemaining ?? 300,
            RunsRequired = state.RunsRequired,
            // The coach's instruction wins where he has given one; otherwise the AI's reading of
            // the situation applies. Note the engine does NOT second-guess him: blocking with 40
            // needed off 24 will lose the match, and it is allowed to.
            BattingIntent = ResolveBattingIntent(state, strikerCard, phase, battingPlan, bowler, striker),
            BowlingIntent = bowlingPlan.ResolveBowlingIntent(bowler.Id, phase) ?? DetermineBowlingIntent(state, phase),
            PitchPace = pitch.Pace,
            // Rough-patch wear at this specific bowling end, on top of the day-based deterioration
            // already baked into pitch.Spin - only ever non-zero in a multi-day match (see the
            // accumulation point above), so every other caller/format is unaffected.
            PitchSpin = Math.Clamp(pitch.Spin + roughPatchSpinBonus, 0, 100),
            PitchBounce = pitch.Bounce,
            PitchBattingFriendliness = pitch.BattingFriendliness,
            PressureLevel = DeterminePressure(state, basePressure),
            BallAge = state.LegalBalls,
            TotalScore = state.Runs,
            FieldingSide = setup.FieldingSide,
            Momentum = state.Momentum.Value // Wave 6 - a gated, small feedback into the ball model
        };

        var outcome = _ballModel.SimulateBall(context, random);

        // Phase 15 (§1.7 - DRS): the batting side may review a given-out LBW / caught-behind. Only
        // rolls when DRS is in force AND this was one of those two dismissals (a rare event), so a
        // non-DRS innings draws no extra random numbers. An overturned decision strikes the wicket
        // off - the delivery becomes a dot.
        if (drs is not null && outcome.IsWicket
            && outcome.Dismissal is DismissalType.LBW or DismissalType.CaughtBehind)
        {
            var verdict = _drs.ConsiderBattingReview(outcome.Dismissal, striker, nonStriker,
                state.Wickets, state.RunsRequired, state.BallsRemaining, umpireOutBias, drs, random);
            switch (verdict)
            {
                case ReviewVerdict.Overturned: outcome = DeliveryOutcome.Dot(); state.DrsOverturns++; break;
                case ReviewVerdict.UmpiresCall: state.DrsUmpiresCall++; break;
                case ReviewVerdict.StruckDown: state.DrsStruckDown++; break;
            }
        }

        // Feed the outcome back into both players' state before it is applied to the scorecard.
        bool beatTheBat = outcome.RunsOffBat == 0 && !outcome.IsWicket && outcome.IsLegalDelivery
                          && random.NextDouble() < 0.18;

        strikerState.RecordBall(striker, outcome.RunsOffBat, outcome.RunsOffBat >= 4, beatTheBat, pitchDifficulty, state.Format, weather);
        bowlerState.RecordBall(bowler, outcome.TotalRuns, outcome.IsWicket, beatTheBat, state.Format, weather);

        // Section R: a real, live attacking pattern - not the batter's static ability profile -
        // for AutoFieldSetter to notice and eventually react to.
        if (outcome.RunsOffBat >= 4 && outcome.Zone is { } attackedZone)
            strikerState.RecordZoneAttack(attackedZone);

        // Issue 11: how this batter is actually reading THIS bowler, outcome-gated - beaten or
        // dismissed genuinely costs him ground against this specific bowler, surviving or scoring
        // builds it. Recorded regardless of the trap-ball/pattern mechanics above, since this is a
        // separate, batter-side read of the same ball.
        strikerState.RecordBowlerFamiliarity(bowler.Id, outcome.RunsOffBat, beatTheBat || outcome.WicketCreditedToBowler,
            mysterious: BallOutcomeModel.IsMysterySpinner(bowler));

        // Strike farming: a refused single is a dot, which is exactly the trade the instruction makes.
        if (outcome.RunsOffBat == 1 && outcome.Dismissal == DismissalType.NotOut
            && ShouldRefuseSingleToProtectTail(state, setup, battingPlan, ballsThisOver))
            outcome = DeliveryOutcome.Dot();

        ApplyOutcome(state, outcome, strikerCard, bowlerCard, phase, overNumber, executed, timeline, runningClock);
        return outcome;
    }

    /// <summary>
    /// Applies one delivery to every piece of state it touches. This is the single place the
    /// scorecard is written, deliberately - scattering score updates is how a total ends up
    /// disagreeing with the sum of the batters' runs.
    /// </summary>
    private static void ApplyOutcome(
        InningsState state, DeliveryOutcome outcome, BatterCard strikerCard, BowlerCard bowlerCard,
        MatchPhase phase, int overNumber, ExecutedDelivery? executed = null, MatchTimeline? timeline = null,
        TimeOnly? runningClock = null)
    {
        state.Runs += outcome.TotalRuns;
        strikerCard.Runs += outcome.RunsOffBat;

        // Extras that are not the bowler's fault (byes, leg-byes) still count against the team but
        // NOT against the bowler's figures - a bowler is not charged for the keeper's error.
        bool bowlerCharged = outcome.Type is not (DeliveryOutcomeType.Bye or DeliveryOutcomeType.LegBye);
        bowlerCard.RunsConceded += bowlerCharged ? outcome.TotalRuns : 0;

        if (outcome.Type == DeliveryOutcomeType.Wide) bowlerCard.Wides++;
        if (outcome.Type == DeliveryOutcomeType.NoBall) bowlerCard.NoBalls++;
        if (outcome.ExtraRuns > 0) state.Extras += outcome.ExtraRuns;

        if (outcome.RunsOffBat == 4) strikerCard.Fours++;
        if (outcome.RunsOffBat == 6) strikerCard.Sixes++;

        if (outcome.CountsAsBallFaced)
        {
            strikerCard.BallsFaced++;
            RecordPhaseSplit(strikerCard, phase, outcome.RunsOffBat);
        }

        if (outcome.IsLegalDelivery)
        {
            state.LegalBalls++;
            bowlerCard.LegalBallsBowled++;
            bowlerCard.BallsInCurrentSpell++;
            state.CurrentPartnershipBalls++;
        }

        state.CurrentPartnershipRuns += outcome.TotalRuns;

        state.Deliveries.Add(new DeliveryRecord(
            state.Deliveries.Count + 1, overNumber + 1, bowlerCard.LegalBallsBowled % 6,
            strikerCard.PlayerId, bowlerCard.PlayerId, phase, outcome, state.Runs, state.Wickets,
            executed?.Line, executed?.Length,
            timeline?.Day, timeline?.CurrentSession, timeline?.CurrentTime ?? runningClock));

        if (outcome.IsWicket && outcome.Dismissal != DismissalType.Retired)
        {
            // A run-out can remove the non-striker - but only if there IS one. With the last
            // pair separated, or before a partner has come in, the striker is the only candidate.
            var dismissedCard = outcome.NonStrikerDismissed && state.NonStrikerId is { } nonStrikerId
                ? state.BatterCards.First(c => c.PlayerId == nonStrikerId)
                : strikerCard;

            dismissedCard.Dismissal = outcome.Dismissal;
            dismissedCard.FielderId = outcome.FielderId;
            if (outcome.WicketCreditedToBowler)
            {
                dismissedCard.DismissedByBowlerId = bowlerCard.PlayerId;
                bowlerCard.Wickets++;
            }

            state.Wickets++;
            state.LastDismissedPlayerId = dismissedCard.PlayerId;
            state.FallOfWickets.Add(new FallOfWicket(state.Wickets, state.Runs, state.LegalBalls, dismissedCard.PlayerId));
            ClosePartnership(state);
        }
        else if (outcome.ChangesStrike)
        {
            state.SwapStrike();
        }
    }

    /// <summary>
    /// Farming the strike to shield the tail.
    ///
    /// A real instruction with a real cost: the set batter turns down a single to keep the strike,
    /// which means fewer runs now in exchange for keeping the specialist on strike next ball. It
    /// only applies with a genuine tail-ender at the other end and enough balls left in the over
    /// for it to be worth doing - refusing a single off the last ball of an over achieves the
    /// opposite of what the coach wants.
    /// </summary>
    private static int IndexOfPlayer(IReadOnlyList<Player> order, Guid? playerId)
    {
        if (playerId is null) return -1;
        for (int i = 0; i < order.Count; i++)
            if (order[i].Id == playerId) return i;
        return -1;
    }

    private static bool ShouldRefuseSingleToProtectTail(
        InningsState state, InningsSetup setup, TacticalPlan plan, int legalBallsThisOver)
    {
        if (plan.ProtectLowerOrderCount <= 0) return false;
        if (legalBallsThisOver >= 5) return false; // last ball of the over - taking the single is correct

        // Is the non-striker one of the tail-enders being shielded?
        int battingPositions = setup.BattingOrder.Count;
        int nonStrikerPosition = IndexOfPlayer(setup.BattingOrder, state.NonStrikerId);
        if (nonStrikerPosition < 0) return false;

        bool nonStrikerIsTail = nonStrikerPosition >= battingPositions - plan.ProtectLowerOrderCount;
        if (!nonStrikerIsTail) return false;

        // And is the striker actually a better bet? Shielding a tail-ender with another tail-ender
        // is pointless.
        int strikerPosition = IndexOfPlayer(setup.BattingOrder, state.StrikerId);
        return strikerPosition >= 0 && strikerPosition < battingPositions - plan.ProtectLowerOrderCount;
    }

    /// <summary>
    /// What this bowler has learned about this batter during this spell - how many balls he has had
    /// at him and how they went. This is what lets a bowler notice he is being milked, which is the
    /// difference between a thinking bowler and one who bowls the same thing into the same gap.
    /// </summary>
    /// <summary>
    /// Whether to declare. The calculation a captain actually makes: how many runs ahead am I, and
    /// how many overs would I have to bowl them out in?
    ///
    /// The rule of thumb real captains use is roughly four an over plus a cushion - enough on the
    /// board that they cannot chase it, and enough time left that you can bowl them out. Declaring
    /// too early loses matches; declaring too late draws them. Both are real failures, and a coach
    /// gets to watch either happen.
    /// </summary>
    /// <param name="captainQuality">
    /// §2.4/§2.16: 0 (poor) to 1 (excellent), 0.5 (the default and every pre-§2.4 caller) neutral.
    /// A real captaincy error, scaled by <see cref="ValueObjects.CaptaincyProfile.TacticalJudgement"/>
    /// - and, per this codebase's own established discipline (see CaptaincyService's own doc
    /// comment: "noise is not the same thing as bad judgement"), a SYSTEMATIC bias rather than
    /// random noise. A callow captain is famously the more CAUTIOUS one with the declaration -
    /// he waits for a bigger cushion than the position strictly needs, which risks gifting the
    /// draw by leaving too few overs; a sharp one trusts a thinner lead and buys himself more time
    /// to bowl the opposition out.
    /// </param>
    public static bool ShouldDeclare(InningsState state, int existingLead, int ballsLeftInMatch, int overNumber, MatchFormat format, double captainQuality = 0.5)
    {
        if (format != MatchFormat.Test) return false;
        if (state.Wickets >= 8) return false;   // no point - they will be out shortly anyway

        int totalLead = existingLead + state.Runs;
        int ballsRemaining = Math.Max(0, ballsLeftInMatch - state.LegalBalls);

        // You need a realistic prospect of ten wickets. Below about fifty-five overs that is
        // fanciful, and a captain declaring into it is gifting a draw.
        if (ballsRemaining < 55 * 6) return false;

        // Roughly three and a bit an over plus a cushion. Four an over was too demanding - it meant
        // 450 ahead with 120 overs to bowl was still "not enough", and captains never declared at all.
        double oversRemaining = ballsRemaining / 6.0;
        // Centred so the neutral default (0.5) reproduces the ORIGINAL, unscaled target exactly -
        // every pre-§2.4 caller (no captainQuality argument) is byte-identical. +-18% either side.
        double defendableTarget = (oversRemaining * 3.2 + 40) * (1.18 - Math.Clamp(captainQuality, 0, 1) * 0.36);

        return totalLead >= defendableTarget;
    }

    /// <summary>§2.4/§2.5: a side's own captain quality, 0-1, neutral (0.5) with no leadership tracked - every pre-existing multi-day caller. Internal static (not private) so MultiDayMatchSimulator's follow-on decision reuses the identical read rather than a second formula - the same "internal static, one formula, two callers" pattern EstimateBowlingStrength already established.</summary>
    internal static double DeclaringCaptainQuality(MatchLeadership? leadership) =>
        leadership is { Captain: { } cap, Profile: { } prof } ? prof.TacticalJudgement(cap) / 100.0 : 0.5;

    private static SpellReadout BuildSpellReadout(InningsState state, Guid strikerId, Guid bowlerId, int overNumber = -1, int ballsThisOver = 0)
    {
        var relevant = state.Deliveries.Where(d => d.StrikerId == strikerId && d.BowlerId == bowlerId).ToList();

        // §2.6 (Follow-up Pass 4): the WITHIN-OVER pattern is a genuinely separate read from the
        // cross-spell one below - it can be built even on a batter's/bowler's very first ball
        // together this over, and it resets every over rather than spanning several of this
        // bowler's spells. Computed off state.Deliveries directly (OverNumber/BallInOver already
        // tag every delivery) rather than off `relevant`, which is scoped to this one bowler-vs-
        // batter pairing across the whole innings.
        var withinOverPattern = overNumber < 0 ? null : state.Deliveries
            .Where(d => d.OverNumber == overNumber && d.StrikerId == strikerId && d.BowlerId == bowlerId
                        && d.Line is not null && d.Length is not null)
            .Select(d => (d.Line!.Value, d.Length!.Value))
            .ToList();
        bool isLastBallOfOver = ballsThisOver >= 5;

        if (relevant.Count == 0)
            return SpellReadout.Empty with { WithinOverPattern = withinOverPattern, IsLastBallOfOver = isLastBallOfOver };

        // Trailing streak, not a whole-innings count: how many dots THIS bowler has JUST strung
        // together against THIS batter, walking back from the most recent ball until it breaks.
        int consecutiveDots = 0;
        for (int i = relevant.Count - 1; i >= 0; i--)
        {
            var o = relevant[i].Outcome;
            if (o.IsLegalDelivery && o.RunsOffBat == 0 && !o.IsWicket) consecutiveDots++;
            else break;
        }

        // The raw pattern a trap ball is built from, and a batter reads - oldest first, capped
        // to a short window since a genuine pattern is a recent thing, not a career-long habit.
        var recentDeliveries = relevant
            .Where(d => d.Line is not null && d.Length is not null)
            .TakeLast(4)
            .Select(d => (d.Line!.Value, d.Length!.Value))
            .ToList();

        return new SpellReadout(
            relevant.Count(d => d.Outcome.IsLegalDelivery),
            relevant.Sum(d => d.Outcome.TotalRuns),
            relevant.Count(d => d.Outcome.RunsOffBat >= 4),
            0,
            consecutiveDots,
            recentDeliveries,
            withinOverPattern,
            isLastBallOfOver);
    }

    /// <summary>
    /// What the AI captain would bowl with no instruction from the coach - a sensible default for
    /// the phase, format and bowler type, so an unmanaged side still bowls like a cricket team.
    /// </summary>
    public static BowlingApproach DefaultApproach(Player bowler, InningsState state, MatchPhase phase)
    {
        bool spinner = BallOutcomeModel.IsSpinner(bowler);
        bool newBall = state.LegalBalls < 36;

        if (spinner)
            return phase == MatchPhase.DeathOvers ? BowlingApproach.SpinContainment : BowlingApproach.SpinAttackingLine;

        return phase switch
        {
            MatchPhase.Powerplay when newBall => BowlingApproach.NewBall,
            MatchPhase.DeathOvers => BowlingApproach.DeathYorkers,
            MatchPhase.MiddleOvers when state.Format == MatchFormat.Test => BowlingApproach.CorridorOfUncertainty,
            _ => BowlingApproach.CorridorOfUncertainty
        };
    }

    private static void RecordPhaseSplit(BatterCard card, MatchPhase phase, int runs)
    {
        switch (phase)
        {
            case MatchPhase.Powerplay:
                card.PowerplayRuns += runs; card.PowerplayBalls++; break;
            case MatchPhase.DeathOvers:
                card.DeathOversRuns += runs; card.DeathOversBalls++; break;
            default:
                card.MiddleOversRuns += runs; card.MiddleOversBalls++; break;
        }
    }

    // ---------------- situation ----------------

    /// <summary>
    /// Phase boundaries scale with the innings length rather than being hardcoded to 20 overs, so
    /// the same logic works for a T20, a 50-over game and a rain-reduced 12-over chase.
    /// </summary>
    public static MatchPhase DeterminePhase(InningsState state, int overNumber)
    {
        if (state.Format == MatchFormat.Test) return MatchPhase.MiddleOvers;

        int totalOvers = (state.MaxLegalBalls ?? 120) / 6;

        // Powerplay length is format-specific in reality: 6 of 20 overs in a T20 (30%), 10 of 50
        // in an ODI (20%). Using one proportion for both handed ODI sides five extra overs of
        // fielding restrictions and inflated scores accordingly.
        double powerplayProportion = state.Format == MatchFormat.T20 ? 0.30 : 0.20;
        double powerplayEnd = totalOvers * powerplayProportion;
        double deathStart = totalOvers * 0.8;

        if (overNumber < powerplayEnd) return MatchPhase.Powerplay;
        if (overNumber >= deathStart) return MatchPhase.DeathOvers;
        return MatchPhase.MiddleOvers;
    }

    /// <summary>
    /// How the batting side is trying to score right now. This is where the scoreboard becomes a
    /// decision: a side needing 12 an over cannot block, and a side four down in the first hour
    /// cannot slog. The player's own aggression attribute shades it, so two batters in the same
    /// situation still play differently.
    /// </summary>
    /// <summary>
    /// The intent actually used for this ball, resolving the coach's plan against the AI default.
    ///
    /// Per-bowler instructions are handled here rather than in TacticalPlan because they depend on
    /// who is bowling THIS ball: "take down the part-timer" and "see off their spearhead" are
    /// instructions about a matchup, not about the innings.
    /// </summary>
    public static BattingIntent ResolveBattingIntent(
        InningsState state, BatterCard striker, MatchPhase phase, TacticalPlan plan, Player bowler, Player? strikerPlayer = null)
    {
        if (plan.InstructionFor(striker.PlayerId) is { } instruction)
        {
            // Attack this bowler specifically - one step more aggressive than the plan otherwise says.
            if (instruction.TargetBowlerId == bowler.Id)
                return Escalate(plan.ResolveBattingIntent(striker.PlayerId, phase)
                                ?? DetermineBattingIntent(state, striker, phase, strikerPlayer));

            // See this bowler off - one step more cautious.
            if (instruction.RespectBowlerId == bowler.Id)
                return Soften(plan.ResolveBattingIntent(striker.PlayerId, phase)
                              ?? DetermineBattingIntent(state, striker, phase, strikerPlayer));

            // Attack this TYPE of bowling - a broader tactical read than naming one bowler, so it
            // escalates the same way TargetBowlerId does whenever this bowler matches the type.
            if (instruction.TargetBowlerType is { } wantedType && BallOutcomeModel.GetBowlerType(bowler) == wantedType)
                return Escalate(plan.ResolveBattingIntent(striker.PlayerId, phase)
                                ?? DetermineBattingIntent(state, striker, phase, strikerPlayer));

            // Just arrived at a new partnership and asked to rebuild first - soften for a short
            // window rather than pushing straight on after losing a partner.
            if (instruction.RebuildAfterWicket && state.CurrentPartnershipBalls < RebuildAfterWicketBallWindow)
                return Soften(plan.ResolveBattingIntent(striker.PlayerId, phase)
                              ?? DetermineBattingIntent(state, striker, phase, strikerPlayer));
        }

        return plan.ResolveBattingIntent(striker.PlayerId, phase)
               ?? DetermineBattingIntent(state, striker, phase, strikerPlayer);
    }

    /// <summary>Roughly two overs - long enough to actually settle after a wicket, short enough that "rebuild" doesn't quietly become "never accelerate again".</summary>
    private const int RebuildAfterWicketBallWindow = 12;

    /// <summary>
    /// Applies a batter's own TargetZone/AvoidZone/TargetShorterBoundary preference to his
    /// natural scoring profile before it drives anything else. This is the one adjustment point
    /// for all three - RollZone (which zone a shot actually goes to) and FieldEffectService.
    /// Calculate (how much a field's coverage of that zone matters) both already read
    /// StrikerZones, so biasing it here reaches both without touching either of them. That is
    /// also what makes this a genuine field-manipulation mechanic rather than a cosmetic label:
    /// a zone the batter now favours makes the fielding captain's coverage of it matter
    /// proportionally more, and a zone he is deliberately leaving open costs him less.
    /// </summary>
    public static BattingZoneStrengths ApplyZonePreferences(BattingZoneStrengths baseZones, BatterInstruction? instruction, Ground? ground)
    {
        if (instruction is null) return baseZones;
        if (instruction.TargetZone is null && instruction.AvoidZone is null && !instruction.TargetShorterBoundary)
            return baseZones;

        var adjusted = new BattingZoneStrengths();
        foreach (var zone in Enum.GetValues<ShotZone>()) adjusted[zone] = baseZones[zone];

        if (instruction.TargetZone is { } target) adjusted[target] = adjusted[target] * 1.4;
        if (instruction.AvoidZone is { } avoid) adjusted[avoid] = adjusted[avoid] * 0.5;

        if (instruction.TargetShorterBoundary && ground is not null)
        {
            // A rough but honest split - MidOff/MidOn are the "straight" zones, Point/Cover/
            // MidWicket/SquareLeg the "square" ones; ThirdMan/FineLeg (behind square) are left
            // out of both rather than guessed into either.
            bool squareIsShorter = ground.SquareBoundaryMetres < ground.StraightBoundaryMetres;
            var favoured = squareIsShorter
                ? new[] { ShotZone.Point, ShotZone.Cover, ShotZone.MidWicket, ShotZone.SquareLeg }
                : new[] { ShotZone.MidOff, ShotZone.MidOn };

            foreach (var zone in favoured) adjusted[zone] = adjusted[zone] * 1.25;
        }

        return adjusted;
    }

    private static BattingIntent Escalate(BattingIntent intent) => intent switch
    {
        BattingIntent.Blocking => BattingIntent.Anchoring,
        BattingIntent.Anchoring => BattingIntent.Normal,
        BattingIntent.Normal => BattingIntent.Attacking,
        _ => BattingIntent.AllOut
    };

    private static BattingIntent Soften(BattingIntent intent) => intent switch
    {
        BattingIntent.AllOut => BattingIntent.Attacking,
        BattingIntent.Attacking => BattingIntent.Normal,
        BattingIntent.Normal => BattingIntent.Anchoring,
        _ => BattingIntent.Blocking
    };

    /// <param name="strikerPlayer">
    /// Issue 9: when supplied, lets a batter respond to a genuine recent wicket cluster on his own
    /// initiative - see the automatic-rebuild check this wraps around DetermineBaseBattingIntent.
    /// Optional and defaulting to null so every existing caller (including direct tests of the base
    /// logic) is unaffected; SimulateDelivery is the one live caller that supplies it.
    /// </param>
    public static BattingIntent DetermineBattingIntent(InningsState state, BatterCard striker, MatchPhase phase, Player? strikerPlayer = null)
    {
        var baseIntent = DetermineBaseBattingIntent(state, striker, phase);

        // Issue 9: "batsman awareness to rebuild and build a partnership" - a batter arriving
        // during a genuinely clustered set of wickets (RecentWicketClusterCount, not just any
        // single dismissal) can recognise the danger himself and rebuild, without a coach having
        // set RebuildAfterWicket for him. Gated on his own GameAwareness actually reading the
        // situation - a sharp cricket brain adjusts; a poor one ploughs on at his previous intent,
        // which is exactly how a real collapse claims a fourth wicket. Deliberately deterministic,
        // like every other read in this method, rather than a coin flip on top of one.
        if (strikerPlayer is not null
            && state.CurrentPartnershipBalls < RebuildAfterWicketBallWindow
            && RecentWicketClusterCount(state) >= 2
            && Common.AbilityScale.AttributeToHundred(strikerPlayer.Mental.GameAwareness) >= 45)
        {
            return Soften(baseIntent);
        }

        return baseIntent;
    }

    private static BattingIntent DetermineBaseBattingIntent(InningsState state, BatterCard striker, MatchPhase phase)
    {
        // Chasing: the required rate dictates almost everything.
        if (state.RequiredRunRate is { } required)
        {
            double par = state.Format switch { MatchFormat.T20 => 8.5, MatchFormat.ODI => 6.0, _ => 3.5 };

            // BATTING FOR THE DRAW. In multi-day cricket a target can simply be out of reach, and
            // the correct response is not to chase it - it is to shut up shop and bat out time. A
            // side has three results available to it and taking the draw is one of them.
            //
            // Without this a fourth-innings side chased every target however absurd, got itself
            // bowled out every time, and the draw - the most common result in first-class cricket -
            // never occurred at all. A limited-overs side has no such option, which is exactly what
            // makes the formats different games.
            if (state.Format == MatchFormat.Test && required > par * 1.7)
            {
                // Wickets in hand decide how grimly. With plenty left a side can still keep some
                // scoreboard pressure on in case the chase becomes live again.
                if (state.Wickets >= 4) return BattingIntent.Blocking;
                return BattingIntent.Anchoring;
            }

            if (required > par * 1.6) return BattingIntent.AllOut;
            if (required > par * 1.2) return BattingIntent.Attacking;
            if (required < par * 0.6) return BattingIntent.Anchoring;
            return BattingIntent.Normal;
        }

        // Batting first. A collapse forces caution regardless of format or phase.
        bool inTrouble = state.Wickets >= 6 || (state.Wickets >= 3 && state.LegalBalls < 36);

        if (state.Format == MatchFormat.Test)
        {
            // Anchoring, not Blocking. Genuine shut-up-shop blocking is a deliberate tactical
            // instruction (saving a match, seeing out a session) and belongs with the tactics
            // system - applying it automatically whenever six were down dragged Test run rates
            // well below anything real.
            if (inTrouble) return BattingIntent.Anchoring;
            return striker.BallsFaced < 15 ? BattingIntent.Anchoring : BattingIntent.Normal;
        }

        if (inTrouble && phase != MatchPhase.DeathOvers) return BattingIntent.Anchoring;

        // §2.3: the ODI middle-overs contest (roughly overs 15-40). A side with wickets in hand and
        // a run rate at or above par builds a platform to launch from (Normal); one that has fallen
        // behind and still has wickets pushes earlier (Attacking); one that has lost early wickets
        // and is behind consolidates (Anchoring). Deterministic - it reads the scoreboard.
        if (state.Format == MatchFormat.ODI && phase == MatchPhase.MiddleOvers && state.LegalBalls >= 60)
        {
            double crr = state.LegalBalls > 0 ? state.Runs / (state.LegalBalls / 6.0) : 0;
            bool behind = crr < 5.0;
            bool wicketsInHand = state.Wickets <= 4;
            return (behind, wicketsInHand) switch
            {
                (true, true) => BattingIntent.Attacking,
                (true, false) => BattingIntent.Anchoring,
                (false, _) => striker.BallsFaced < 12 ? BattingIntent.Anchoring : BattingIntent.Normal,
            };
        }

        // §2.12 (Follow-up Pass 4): a finer acceleration band immediately ahead of T20's death
        // overs - real batting sides ramp up progressively through the last few overs of the
        // middle rather than snapping straight from Normal to Attacking the instant the death-
        // overs boundary is crossed. Deliberately T20-only and deliberately placed after the ODI
        // check above: ODI's middle overs already have their own dedicated, more nuanced §2.3
        // contest, and Test has no such thing as a death-overs ramp. Purely deterministic - reads
        // only the scoreboard, consumes no randomness of its own.
        if (state.Format == MatchFormat.T20 && phase == MatchPhase.MiddleOvers && state.Wickets <= 5)
        {
            int totalOversForRamp = (state.MaxLegalBalls ?? 120) / 6;
            double deathStart = totalOversForRamp * 0.8;
            int currentOver = state.LegalBalls / 6;
            if (currentOver >= deathStart - 3) // the last few overs before the death phase proper
                return striker.BallsFaced < 8 ? BattingIntent.Normal : BattingIntent.Attacking;
        }

        // Powerplay is deliberately NOT "attacking by default": the phase multiplier in the ball
        // model already accounts for fielding restrictions creating boundary opportunities.
        // Setting intent to Attacking here as well would multiply the same effect twice.
        // Intent is the side's CHOICE; the phase is the OPPORTUNITY.
        return phase switch
        {
            // AllOut is genuine slog - reserved for the last three overs, when there is nothing
            // left to save wickets for. Applying it across a 50-over death phase turned the last
            // ten overs into ten T20 death overs and inflated ODI scores badly. Expressed in balls
            // remaining rather than over number so it scales to any innings length.
            MatchPhase.DeathOvers => state.Wickets >= 7 ? BattingIntent.Attacking
                : (state.BallsRemaining is { } remaining && remaining <= 18 ? BattingIntent.AllOut : BattingIntent.Attacking),
            MatchPhase.Powerplay => striker.BallsFaced < 8 ? BattingIntent.Normal : BattingIntent.Attacking,
            _ => striker.BallsFaced < 10 ? BattingIntent.Anchoring : BattingIntent.Normal
        };
    }

    /// <summary>
    /// §2.1 (Match-Engine Tactical Pass): the situation-driven field-aggression read - a bowling
    /// side that is genuinely on top presses (Attacking); one being collared protects the rope
    /// (Defensive). Built and correct, but <b>deliberately not wired into the live per-over field
    /// build</b> - it was tried directly there and reverted (see the comment at the call site in
    /// <see cref="Simulate"/>): folding a third source of Attacking/Defensive into every single
    /// over, on top of the coach's own instruction and the captain's own field-quality read
    /// (<c>CaptaincyService</c>), diluted the deliberately-calibrated "poor captaincy costs runs"
    /// signal down to a dead heat even at 900 paired seeds. Left here, unconsumed but stated as
    /// such (not silently dead code - see this project's own "unpopulated default"/"unconsumed
    /// state" history), as the building block for a narrower, additive future use: e.g. surfaced
    /// to the human coach as a <c>MatchSuggestion</c> ("the situation argues for an attacking
    /// field here") rather than an automatic AI override.
    /// </summary>
    private static ValueObjects.FieldAggression? SituationalFieldAggression(InningsState state, MatchPhase phase)
    {
        double crr = state.LegalBalls > 0 ? state.Runs / (state.LegalBalls / 6.0) : 0;

        // Defending a chase: the required rate is the truth.
        if (state.RequiredRunRate is { } req)
        {
            double par = state.Format switch { MatchFormat.T20 => 8.5, MatchFormat.ODI => 6.0, _ => 3.5 };
            if (req > par * 1.5 && state.Wickets >= 4) return ValueObjects.FieldAggression.Attacking; // chase is dead - go for the kill
            if (req < par * 0.75 && state.Wickets <= 3) return ValueObjects.FieldAggression.Defensive; // chase is cruising - contain
            return null;
        }

        // Bowling first: read wickets against the run rate.
        double parFirst = state.Format switch { MatchFormat.T20 => 8.0, MatchFormat.ODI => 5.4, _ => 3.2 };
        if (phase != MatchPhase.Powerplay)
        {
            if (state.Wickets >= 4 && crr < parFirst) return ValueObjects.FieldAggression.Attacking;
            if (state.Wickets <= 1 && crr > parFirst * 1.25) return ValueObjects.FieldAggression.Defensive;
        }
        return null;
    }

    /// <summary>Bowling sides attack when wickets win the game and contain when runs are the threat.</summary>
    public static BowlingIntent DetermineBowlingIntent(InningsState state, MatchPhase phase)
    {
        if (state.RequiredRunRate is { } required)
        {
            double par = state.Format switch { MatchFormat.T20 => 8.5, MatchFormat.ODI => 6.0, _ => 3.5 };
            // Comfortably ahead in the chase - buy wickets. Under threat - shut it down.
            return required > par * 1.3 ? BowlingIntent.Attacking : BowlingIntent.Defensive;
        }

        if (state.Wickets >= 7) return BowlingIntent.Attacking; // tail exposed, go for the throat

        return phase switch
        {
            MatchPhase.Powerplay => BowlingIntent.Attacking,
            MatchPhase.DeathOvers => BowlingIntent.Defensive,
            _ => BowlingIntent.Balanced
        };
    }

    /// <summary>
    /// How big this moment feels. A tight chase in the closing overs is where inexperience shows,
    /// which is exactly what PressureLevel feeds into via the situational modifier.
    /// </summary>
    public static double DeterminePressure(InningsState state, double basePressure)
    {
        double pressure = basePressure;

        if (state.RequiredRunRate is { } required && state.BallsRemaining is { } balls)
        {
            double par = state.Format switch { MatchFormat.T20 => 8.5, MatchFormat.ODI => 6.0, _ => 3.5 };
            pressure += Math.Clamp((required - par) * 3, -10, 25);

            // The closer to the end, the more every ball matters.
            if (balls <= 36) pressure += (36 - balls) / 36.0 * 20;
        }

        if (state.Wickets >= 7) pressure += 10;

        return Math.Clamp(pressure, 0, 100);
    }

    /// <summary>
    /// Issues 8/9: how many wickets have fallen within a short recent window (18 legal balls -
    /// three overs), which is the genuine-clustering signal BallOutcomeModel.CollapsePressureFactor
    /// needs before it does anything at all. Reads FallOfWickets directly rather than tracking a
    /// separate counter, since the fall-of-wickets log already IS the authoritative record of when
    /// each wicket actually fell.
    /// </summary>
    private static int RecentWicketClusterCount(InningsState state)
    {
        const int WindowBalls = 18;
        if (state.FallOfWickets.Count == 0) return 0;

        int currentBall = state.LegalBalls;
        return state.FallOfWickets.Count(f => currentBall - f.BallNumber <= WindowBalls);
    }

    // ---------------- bowler management ----------------

    /// <summary>
    /// Picks who bowls the next over. Enforces the two rules that are absolute in cricket - no
    /// consecutive overs, and per-bowler limits in limited-overs - then prefers whoever is
    /// freshest and most suited to the phase. Slice 3 replaces the preference with real captaincy
    /// AI; the RULES stay here, because they are laws of the game rather than tactics.
    /// </summary>
    /// <param name="overNumber">
    /// The over about to be bowled, in the SAME numbering the innings loop and StartOver use.
    /// It was previously derived here as state.LegalBalls / 6, which is a different base from the
    /// loop's counter - so the "did he bowl the over before last" test was comparing two
    /// numbering schemes and silently off by one.
    /// </param>
    private Player? SelectBowler(InningsSetup setup, InningsState state, MatchFormat format, int totalOvers, int overNumber,
        Random random, TacticalPlan? plan, Guid strikerId, double? captainCallQuality,
        PitchConditions conditions, MatchWeather? weather, Ground? ground,
        IReadOnlyDictionary<Guid, BowlerMatchState> bowlerStates, bool spinOnly = false)
    {
        int? maxOvers = MaxOversPerBowler(format, totalOvers);

        // No consecutive overs, and bad light's spin-only restriction, are true laws - nobody may
        // ever be picked in violation of either, whatever else happens below.
        var lawfulBowlers = setup.AvailableBowlers
            .Where(b => b.Id != state.CurrentBowlerId)
            .Where(b => !spinOnly || BallOutcomeModel.IsSpinner(b))
            .ToList();

        if (lawfulBowlers.Count == 0) return null; // genuinely nobody legal at all - the day/over cannot be bowled

        var eligible = maxOvers is null
            ? lawfulBowlers
            : lawfulBowlers.Where(b =>
            {
                var card = state.BowlerCards.FirstOrDefault(c => c.PlayerId == b.Id);
                return card is null || card.LegalBallsBowled < maxOvers.Value * 6;
            }).ToList();

        if (eligible.Count == 0) return null; // genuinely nobody within quota left to bowl

        // The coach's explicit call for this over beats everything, provided the man is legally
        // able to bowl it. "Him, now" is one of the most basic things a captain does, and the
        // engine must honour it - including when it is a mistake.
        if (plan?.NextOverBowlerId is { } demanded)
        {
            var forced = eligible.FirstOrDefault(b => b.Id == demanded);
            plan.NextOverBowlerId = null; // a single decision, not a standing instruction
            if (forced is not null) return forced;
        }

        // Bowlers the coach has taken out of the rotation. If that leaves nobody, the withdrawal
        // is ignored - somebody has to bowl the over.
        if (plan is not null && plan.WithheldBowlers.Count > 0)
        {
            var permitted = eligible.Where(b => !plan.WithheldBowlers.Contains(b.Id)).ToList();
            if (permitted.Count > 0) eligible = permitted;
        }

        // Feasibility guard - a real, confirmed bug, not a hypothetical one: a T20 innings was
        // observed finishing at 18 overs instead of 20 because preference-weighted selection (the
        // scoring below has no reason on its own to care about the TAIL of the innings) painted
        // itself into a corner - two bowlers who both scored very highly on matchup/conditions kept
        // alternating, both exhausted their quota well before over 20, and by the run-in the only
        // bowler left with overs in hand was the one who had just bowled the previous over, which
        // no law permits.
        //
        // A first attempt here checked only "do the OTHER bowlers' combined remaining quota cover
        // what's left" - necessary, but not sufficient, and it still let the bug through in
        // roughly a third of trial seeds. This is the actual necessary-AND-sufficient condition
        // for "schedule R remaining slots across several sources, never the same source twice in a
        // row, every source capped": a valid arrangement exists if and only if no single source's
        // remaining quota exceeds half of what's left (rounded up) - the same result behind the
        // classic task-scheduling-with-cooldown problem. Checked for every candidate AFTER
        // crediting him with the over he'd bowl right now, so the invariant is maintained at every
        // single step rather than merely glanced at once - which is what makes it a genuine
        // guarantee rather than a heuristic that happens to help most of the time. Provably
        // maintains feasibility from a feasible start, so unlike the first attempt this should
        // never need a "nothing kept it feasible" fallback - the unfiltered set is kept as a
        // defensive no-op should the invariant ever not hold (a squad with genuinely too few
        // bowling options to legally cover the format, which is a squad-depth problem
        // XiSelectionService/SquadSelectionService already flag elsewhere, not a scheduling one).
        if (maxOvers is { } cap && totalOvers > 0 && eligible.Count > 1)
        {
            int oversRemainingAfterThis = Math.Max(0, totalOvers - overNumber - 1);
            var feasible = eligible.Where(candidate =>
            {
                int maxRemainingAfterPick = 0;
                foreach (var b in lawfulBowlers)
                {
                    int used = state.BowlerCards.FirstOrDefault(c => c.PlayerId == b.Id)?.LegalBallsBowled ?? 0;
                    int remaining = cap - used / 6;
                    if (b.Id == candidate.Id) remaining -= 1;
                    if (remaining > maxRemainingAfterPick) maxRemainingAfterPick = remaining;
                }

                return maxRemainingAfterPick <= (oversRemainingAfterThis + 1) / 2; // ceil(remaining/2) via integer math
            }).ToList();

            if (feasible.Count > 0) eligible = feasible;
        }

        // Weighted by how fresh they are and how well they suit the phase, with a little noise so
        // the same match doesn't produce identical bowling changes every time.
        var phase = DeterminePhase(state, state.LegalBalls / 6);

        return eligible
            .OrderByDescending(b =>
            {
                var card = state.BowlerCards.FirstOrDefault(c => c.PlayerId == b.Id);
                double used = card?.LegalBallsBowled ?? 0;

                // AVAILABILITY, not "spread the overs around". The old scoring was dominated by a
                // freshness term measured in overs already bowled, which literally rewarded using a
                // bowler simply because he had not bowled - the exact opposite of what a captain
                // does. A bowler who is bowling well should keep bowling; the whole attack gets used
                // because different bowlers suit different situations, not for its own sake.
                //
                // What remains of "freshness" here is a hard availability check - how much of his
                // quota is left - plus his actual physical condition, which is what really stops a
                // captain bowling his best man out.
                double quotaLeft = maxOvers is { } max ? 1 - used / (max * 6.0) : 1.0;
                if (quotaLeft <= 0) return double.MinValue;

                double condition = bowlerStates.TryGetValue(b.Id, out var bs)
                    ? 1 - bs.Fatigue / 100.0
                    : 1.0;

                // Below roughly a third of his capacity a bowler is doing more harm than good, and
                // that is the point at which a captain takes him off whatever the matchup says.
                double conditionTerm = condition < 0.35 ? -50 : condition * 25;

                double suitability = _planFit.PhaseCompetence(b, phase, newBall: state.LegalBalls < 36) / 5.0;

                // Conditions. A seamer under heavy cloud, a spinner on a turning surface, a bowler
                // with the pace for a bouncy pitch - the right man for THESE conditions, not just
                // for this phase.
                bool spins = BallOutcomeModel.IsSpinner(b);
                double conditionsFit = spins
                    ? Common.AbilityScale.AttributeToHundred(b.Bowling.Spin) * (conditions.Spin / 100.0)
                    : Common.AbilityScale.AttributeToHundred(b.Bowling.Swing) * (weather?.SwingBonus ?? 0) / 100.0
                      + Common.AbilityScale.AttributeToHundred(b.Bowling.Seam) * (conditions.Pace / 100.0) * 0.6
                      + Common.AbilityScale.AttributeToHundred(b.Bowling.Pace) * (conditions.Bounce / 100.0) * 0.4;

                // The matchup against the man actually on strike. This is the single most
                // cricket-literate reason to bring a particular bowler on.
                double matchup = 0;
                var striker = setup.BattingOrder.FirstOrDefault(p => p.Id == strikerId);
                if (striker is not null)
                    matchup = _planFit.RecommendPlan(b, striker, conditions, phase, format).Score * 18;

                // A bowler who is on top stays on. Economy and wickets in the current spell, which
                // is what a captain is actually watching.
                double onTop = 0;
                if (card is { LegalBallsBowled: >= 6 })
                {
                    double economy = card.RunsConceded * 6.0 / card.LegalBallsBowled;
                    double parEconomy = format switch { MatchFormat.T20 => 8.2, MatchFormat.ODI => 5.4, _ => 3.2 };
                    onTop += Math.Clamp((parEconomy - economy) * 6, -25, 25);
                    onTop += card.Wickets * 12;
                }

                // Small boundaries punish a bowler who goes for runs, so containment is worth more.
                double groundFactor = 0;
                if (ground is not null && ground.SquareBoundaryMetres < 64)
                    groundFactor = Common.AbilityScale.AttributeToHundred(b.Bowling.Containment) * 0.12;

                // Issue 11: captains bowl their bowlers in SPELLS, but how LONG a comfortable
                // spell is differs hugely by bowler type - a spinner is not tiring physically the
                // way a quick is and real captains bowl one 6-10 overs at a stretch, not 2-4. This
                // used to apply the same short window to everyone, which never distinguished a
                // frontline spinner from a strike quick at all. Genuinely helpful conditions buy
                // real extra overs on top - a raging turner or a seaming pitch under cloud is
                // exactly when a captain leaves his in-form bowler on longer.
                double spellBonus = 0;
                if (card is { LastOverBowled: >= 0 } && overNumber - card.LastOverBowled == 2)
                {
                    int spellOvers = card.BallsInCurrentSpell / 6;
                    double stamina = Common.AbilityScale.AttributeToHundred(b.Physical.Stamina);

                    int comfortableSpell = spins
                        ? 6 + (int)(stamina / 100.0 * 4)   // 6-10 overs
                        : 2 + (int)(stamina / 100.0 * 3);  // 2-5 overs

                    if (spins && conditions.Spin > 65) comfortableSpell += 3;
                    else if (!spins && (conditions.Pace > 65 || (weather?.SwingBonus ?? 0) > 60)) comfortableSpell += 2;

                    if (spellOvers < comfortableSpell)
                    {
                        spellBonus = 45;
                    }
                    else
                    {
                        // Escalates the further past comfortable he has gone - a real, growing
                        // pull toward rotation rather than one flat step down, which is what let
                        // two heavily-favoured bowlers bowl 40+ overs each in a Test innings while
                        // the rest of the attack barely got a look in (a confirmed, observed bug,
                        // not a hypothetical one).
                        int oversOver = spellOvers - comfortableSpell;
                        spellBonus = -25 - oversOver * 8;
                    }
                }

                // Coach instructions for this bowler.
                double instructionBonus = 0;
                if (plan?.BowlerInstructionFor(b.Id) is { } instruction)
                {
                    if (instruction.ReserveForPhase is { } reserved)
                        instructionBonus += reserved == phase ? 40 : -35;

                    if (instruction.MatchupAgainstBatterId is { } targetBatter && targetBatter == strikerId)
                        instructionBonus += 50;
                }

                // Captaincy quality scales the INFORMATIVE terms rather than merely adding noise.
                // Noise is not the same thing as bad judgement: a poor captain still sees who is
                // fresh, but he misses the matchup, the conditions and when a spell is worth
                // continuing. See the slice 6c note in CLAUDE.md.
                double competence = (captainCallQuality ?? 60) / 100.0;
                double reading = 0.25 + competence * 1.5;
                double judgementNoise = random.NextDouble() * (8 + (1 - competence) * 22);

                return quotaLeft * 12
                       + conditionTerm
                       + suitability * 2.2 * reading
                       + conditionsFit * 0.25 * reading
                       + matchup * reading
                       + onTop * reading
                       + groundFactor * reading
                       + spellBonus * reading
                       + instructionBonus
                       + judgementNoise;
            })
            .First();
    }

    private static void StartOver(InningsState state, Player bowler, int overNumber)
    {
        state.CurrentBowlerId = bowler.Id;

        var card = state.BowlerCards.FirstOrDefault(c => c.PlayerId == bowler.Id);
        if (card is null)
        {
            card = new BowlerCard { PlayerId = bowler.Id, PlayerName = bowler.FullName };
            state.BowlerCards.Add(card);
        }

        // BUG FIX: this compared against PreviousBowlerId - the bowler of the immediately
        // preceding over. Nobody is allowed to bowl consecutive overs, so that comparison was
        // never true and BallsInCurrentSpell was reset to zero at the start of EVERY over. The
        // spell half of the fatigue model was therefore permanently dead: no bowler ever tired
        // within a spell, which removed the entire point of resting a bowler and bringing him back.
        //
        // A bowler continuing his spell bowls every second over, so the real test is whether he
        // bowled the over before last. Anything longer than that is a break, and a break starts
        // a fresh spell - which is exactly what makes rotation a real tactical decision.
        bool continuingSpell = card.LastOverBowled >= 0 && overNumber - card.LastOverBowled <= 2;
        if (!continuingSpell) card.BallsInCurrentSpell = 0;

        card.LastOverBowled = overNumber;
    }

    private static void EndOver(InningsState state)
    {
        state.PreviousBowlerId = state.CurrentBowlerId;
        state.SwapStrike(); // ends of overs change the strike
    }

    /// <summary>
    /// Wave 6: modulates in-match momentum across a break. A composed batting pair holds what it
    /// has built; a fragile one, or a break that lets the bowling side regroup, hands it back.
    /// The two sides' composure is read from the men actually in the contest right now - the pair
    /// at the crease versus the bowler operating - which is a fair proxy for "who steadies over
    /// the interval".
    /// </summary>
    private static void ModulateMomentumOnBreak(InningsState state, InningsSetup setup, BreakLength length)
    {
        double Comp(Player? p) => p is null ? 0.5 : Common.AbilityScale.AttributeToHundred(p.Mental.PressureHandling) / 100.0;

        var striker = setup.BattingOrder.FirstOrDefault(p => p.Id == state.StrikerId);
        var nonStriker = setup.BattingOrder.FirstOrDefault(p => p.Id == state.NonStrikerId);
        double battingComposure = (Comp(striker) + Comp(nonStriker)) / 2;

        var bowler = setup.AvailableBowlers.FirstOrDefault(p => p.Id == state.CurrentBowlerId);
        state.Momentum.OnBreak(battingComposure, Comp(bowler), length);
    }

    // ---------------- batters ----------------

    private static void InitialiseCards(InningsState state, InningsSetup setup)
    {
        for (int i = 0; i < setup.BattingOrder.Count; i++)
        {
            state.BatterCards.Add(new BatterCard
            {
                PlayerId = setup.BattingOrder[i].Id,
                PlayerName = setup.BattingOrder[i].FullName,
                BattingPosition = i + 1
            });
        }
    }

    private static void MarkBatted(InningsState state, Guid playerId)
    {
        var card = state.BatterCards.FirstOrDefault(c => c.PlayerId == playerId);
        if (card is not null) card.HasBatted = true;
    }

    /// <summary>Brings in the next batter. Returns false when there is nobody left, which is what "all out" actually means - ten wickets with one batter stranded.</summary>
    /// <summary>
    /// Whether to send a nightwatchman. A tail-ender is promoted to see out the last few overs of a
    /// day so that a recognised batter does not have to start his innings in fading light against a
    /// new ball, and can begin fresh in the morning instead.
    ///
    /// Only in multi-day cricket, only near the close, and only when there is a specialist worth
    /// protecting - sending one in with forty overs left, or to shield another tail-ender, is not a
    /// nightwatchman, it is just a bad batting order.
    /// </summary>
    public static bool ShouldSendNightwatchman(MatchFormat format, int oversLeftInDay, int wicketsDown, int recognisedBattersLeft)
    {
        if (format != MatchFormat.Test) return false;
        if (oversLeftInDay > 8 || oversLeftInDay <= 0) return false;   // too early in the day to bother
        if (wicketsDown >= 6) return false;                             // nobody left worth protecting
        return recognisedBattersLeft >= 2;
    }

    private static bool HasBatted(InningsState state, Guid playerId)
    {
        foreach (var card in state.BatterCards)
            if (card.PlayerId == playerId && card.HasBatted) return true;
        return false;
    }

    private readonly InMatchTacticalAI _tacticalAi = new();

    private bool TryBringInNextBatter(InningsState state, InningsSetup setup, ref int nextBatterIndex,
        MatchFormat format = MatchFormat.T20, int oversLeftInDay = -1,
        MatchLeadership? battingLeadership = null, TacticalPlan? battingPlan = null, Random? random = null)
    {
        // §1.8: a retired-hurt batter returns if the specialist order is otherwise exhausted (or
        // there is only the last recognised pair left and one has gone). His card's Retired status
        // is cleared so he can be dismissed properly this time.
        if (state.RetiredHurtIds.Count > 0 && nextBatterIndex >= setup.BattingOrder.Count)
        {
            var backId = state.RetiredHurtIds[0];
            state.RetiredHurtIds.RemoveAt(0);
            var card = state.BatterCards.FirstOrDefault(c => c.PlayerId == backId);
            if (card is not null) card.Dismissal = DismissalType.NotOut;
            if (state.LastDismissedPlayerId == state.NonStrikerId) state.NonStrikerId = backId;
            else state.StrikerId = backId;
            return true;
        }

        if (nextBatterIndex >= setup.BattingOrder.Count) return false;

        // Slice 6.4: the in-innings tactical AI may send a pinch-hitter up the order when quick
        // runs are needed. Limited-overs only (the Test order-change is the nightwatchman, below),
        // gated by the batting side's captaincy read and the coach's decision authority.
        if (random is not null && battingPlan is not null
            && _tacticalAi.ChoosePinchHitter(state, setup.BattingOrder, nextBatterIndex, format, battingLeadership, battingPlan, random) is { } promoteIndex
            && promoteIndex > nextBatterIndex && promoteIndex < setup.BattingOrder.Count
            && !HasBatted(state, setup.BattingOrder[promoteIndex].Id))
        {
            var pinchHitter = setup.BattingOrder[promoteIndex];
            MarkBatted(state, pinchHitter.Id);
            if (state.LastDismissedPlayerId == state.NonStrikerId) state.NonStrikerId = pinchHitter.Id;
            else state.StrikerId = pinchHitter.Id;
            return true;
        }

        // §3.6 (Follow-up Pass 4): "hiding the bunny" - a second, format-agnostic order shuffle,
        // tried only once the urgency-driven pinch-hitter above has had (and passed on) its say, so
        // the two decisions never compete for the same promotion in the same over.
        if (random is not null && battingPlan is not null
            && _tacticalAi.ChooseBunnyProtection(state, setup.BattingOrder, nextBatterIndex, battingLeadership, battingPlan, random) is { } protectIndex
            && protectIndex > nextBatterIndex && protectIndex < setup.BattingOrder.Count
            && !HasBatted(state, setup.BattingOrder[protectIndex].Id))
        {
            var protector = setup.BattingOrder[protectIndex];
            MarkBatted(state, protector.Id);
            if (state.LastDismissedPlayerId == state.NonStrikerId) state.NonStrikerId = protector.Id;
            else state.StrikerId = protector.Id;
            return true;
        }

        // Nightwatchman: promote the best available lower-order bat rather than sending the next
        // specialist in. He is drawn from the END of the order, and the man he was protecting keeps
        // his place - he simply comes in later.
        if (oversLeftInDay >= 0
            && ShouldSendNightwatchman(format, oversLeftInDay, state.Wickets, setup.BattingOrder.Count - nextBatterIndex))
        {
            int watchmanIndex = -1;
            int firstAvailable = nextBatterIndex;
            for (int i = setup.BattingOrder.Count - 1; i > firstAvailable; i--)
            {
                if (HasBatted(state, setup.BattingOrder[i].Id)) continue;
                watchmanIndex = i;
                break;
            }

            if (watchmanIndex > nextBatterIndex)
            {
                var watchman = setup.BattingOrder[watchmanIndex];
                MarkBatted(state, watchman.Id);

                if (state.LastDismissedPlayerId == state.NonStrikerId) state.NonStrikerId = watchman.Id;
                else state.StrikerId = watchman.Id;

                return true;
            }
        }

        // Skip anybody who has already batted - which is how a promoted nightwatchman is not sent
        // in a second time when the order catches up with his place. Written as a plain loop over a
        // local because C# will not let a ref parameter be captured by a lambda.
        int scan = nextBatterIndex;
        while (scan < setup.BattingOrder.Count && HasBatted(state, setup.BattingOrder[scan].Id)) scan++;
        nextBatterIndex = scan;

        if (nextBatterIndex >= setup.BattingOrder.Count) return false;

        var incoming = setup.BattingOrder[nextBatterIndex++];
        MarkBatted(state, incoming.Id);

        // The new batter replaces exactly whoever was just dismissed, at that end. Using the
        // recorded id rather than searching the card list is what keeps a non-striker run-out
        // from putting the new man at the wrong end.
        if (state.LastDismissedPlayerId == state.NonStrikerId) state.NonStrikerId = incoming.Id;
        else state.StrikerId = incoming.Id;

        return true;
    }

    private static void ClosePartnership(InningsState state)
    {
        if (state.CurrentPartnershipBalls == 0 && state.CurrentPartnershipRuns == 0) return;
        if (state.StrikerId is null || state.NonStrikerId is null) return;

        state.Partnerships.Add(new Partnership(
            state.Wickets, state.StrikerId.Value, state.NonStrikerId.Value,
            state.CurrentPartnershipRuns, state.CurrentPartnershipBalls));

        state.CurrentPartnershipRuns = 0;
        state.CurrentPartnershipBalls = 0;
    }
}

/// <summary>
/// The surface as it plays for this innings. Separated from the Ground entity because a pitch
/// CHANGES - across a Test match, across a session, under dew - and the ball model needs the
/// state right now, not the venue's long-term average.
/// </summary>
public sealed record PitchConditions(double Pace, double Spin, double Bounce, double BattingFriendliness)
{
    public static PitchConditions Neutral => new(50, 50, 50, 50);

    /// <summary>Reads a ground's baseline characteristics. Deterioration and weather are layered on by the match simulator in the next slice.</summary>
    public static PitchConditions FromGround(Ground ground) =>
        new(ground.PitchPaceRating, ground.PitchSpinRating, ground.PitchBounceRating, ground.PitchBattingFriendliness);
}
