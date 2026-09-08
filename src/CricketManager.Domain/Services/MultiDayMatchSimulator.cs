using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>Setup for a first-class or Test match. Multi-day cricket is a genuinely different game from limited overs, not a longer version of it.</summary>
public sealed record MultiDayMatchSetup
{
    public required MatchSide Home { get; init; }
    public required MatchSide Away { get; init; }

    /// <summary>Four for a county/first-class game, five for a Test.</summary>
    public int Days { get; init; } = 5;

    /// <summary>Overs scheduled per day. Ninety is the standard; a shortened day reduces it.</summary>
    public int OversPerDay { get; init; } = 90;

    /// <summary>
    /// The lead that lets the side batting first enforce the follow-on. 200 for a five-day match,
    /// 150 for three or four days, 100 for two - the actual Law 14 thresholds.
    /// </summary>
    public int FollowOnMargin => Days >= 5 ? 200 : Days >= 3 ? 150 : 100;

    public Ground? Ground { get; init; }
    public Competition? Competition { get; init; }
    public MatchWeather? Weather { get; init; }
    public DateOnly MatchDate { get; init; } = new(2026, 6, 1);
    public int Season { get; init; } = 2026;
    public double BaseImportance { get; init; } = 55;

    public TacticalPlan? HomePlan { get; init; }
    public TacticalPlan? AwayPlan { get; init; }

    /// <summary>
    /// The points on offer. A competition where a win is worth three times a draw makes both sides
    /// go for a result; one where it is not produces a very different match from the same players.
    /// </summary>
    public CompetitionPointsSystem? Points { get; init; }

    /// <summary>0-100. A World Test Championship fixture is not a dead rubber, and sides do not play them the same way.</summary>
    public double CompetitionImportance { get; init; } = 50;
    public MatchLeadership? HomeLeadership { get; init; }
    public MatchLeadership? AwayLeadership { get; init; }

    /// <summary>
    /// Section AA: how the home side's groundstaff have prepared the surface for this match.
    /// Null/Neutral (the default) means the ground plays exactly as its own baseline characteristics
    /// say - no existing caller needs to change. See PitchPreparationService for how a request is
    /// bounded and damped by the ground's own PitchInfrastructure quality.
    /// </summary>
    public PitchPreparation HomePitchPreparation { get; init; } = PitchPreparation.Neutral;

    /// <summary>
    /// Phase 6, Slice 6.1: the home side's on-field edge for this match, as a fraction (0 = a
    /// neutral venue). Applied as a small multiplier to the home side's batter and bowler
    /// effective skill in every innings. Default 0 so every existing caller and test is unchanged.
    /// </summary>
    public double HomeAdvantage { get; init; }

    /// <summary>Phase 7, Slice 7.9: the on-field umpires (and TV/reserve) for this match - identity, review and display. Null for every pre-Phase-7 caller.</summary>
    public IReadOnlyList<Umpire>? Umpires { get; init; }

    /// <summary>Phase 7, Slice 7.9: the umpiring panel's marginal-decision lean, a multiplier. 1.0 (default, and byte-identical to the pre-Phase-7 engine).</summary>
    public double UmpireOutBias { get; init; } = 1.0;

    /// <summary>Phase 15 (§1.7): DRS reviews per side per innings. 0 (default) = no DRS. FixturePlayService reads it from Competition.EffectiveConditions (a Test international runs 3).</summary>
    public int DrsReviewsPerInnings { get; init; }

    /// <summary>
    /// Post-Phase-16 completion pass (§11.4): a day-night (pink-ball) Test. The ball seams and
    /// swings more under lights and the twilight session is the hardest passage of the day to bat -
    /// which is what makes a pink-ball Test a genuinely different game. Default false.
    /// </summary>
    public bool DayNight { get; init; }

    /// <summary>Phase 14 (§18.1, in-match): unordered feuding player-id pairs; when both bat together their run-out risk carries a small extra multiplier. Empty (default) = no friction.</summary>
    public IReadOnlyCollection<(Guid A, Guid B)> FeudingPairs { get; init; } = Array.Empty<(Guid, Guid)>();

    /// <summary>
    /// Phase 15 (§19.4): this match is played on a RE-USED strip (the ground hosted a match in the
    /// last ~2 weeks and there was no time to prepare a fresh one). A used pitch is worn from ball
    /// one - less pace and bounce, more turn, harder to bat on - and a drop-in used strip less so.
    /// Default false.
    /// </summary>
    public bool PitchIsReused { get; init; }

    /// <summary>Predicate form of <see cref="FeudingPairs"/> for the innings simulator.</summary>
    public bool PairFeuds(Guid a, Guid b) =>
        FeudingPairs.Count > 0 && FeudingPairs.Any(p => (p.A == a && p.B == b) || (p.A == b && p.B == a));

    /// <summary>Phase 15 (§16.6): a TV / third umpire for line calls even without full DRS. Default false. A first-class match always has one.</summary>
    public bool HasThirdUmpire { get; init; }
}

/// <summary>A completed multi-day match. Up to four innings, and a result that may legitimately be a draw.</summary>
public sealed record MultiDayMatchResult
{
    public required Guid MatchId { get; init; }
    public required MultiDayMatchSetup Setup { get; init; }

    /// <summary>In batting order. Two, three or four entries depending on how the match went.</summary>
    public required IReadOnlyList<InningsState> Innings { get; init; }

    /// <summary>
    /// What each side was playing for, session by session. This is the strategic story of the match -
    /// where a side gave up on the win, or decided one was back on - and it is what a post-match
    /// report reads from.
    /// </summary>
    public IReadOnlyList<SessionAssessment> Assessments { get; init; } = Array.Empty<SessionAssessment>();

    public Guid TossWinnerTeamId { get; init; }
    public bool TossWinnerChoseToBat { get; init; }
    public bool FollowOnEnforced { get; init; }

    public Guid? WinningTeamId { get; init; }
    public MatchOutcome ResultForHome { get; init; }
    public int? WinMargin { get; init; }
    public bool WonByInnings { get; init; }
    public bool WonByWickets { get; init; }

    /// <summary>Overs actually bowled across the match. A draw with 400 overs bowled is a different match from one washed out after 60.</summary>
    public double TotalOvers { get; init; }

    /// <summary>Every stoppage, in order. A draw caused by rain is a different story from one earned by batting out time, and a post-match report needs to be able to tell them apart.</summary>
    public IReadOnlyList<RainInterruption> Interruptions { get; init; } = Array.Empty<RainInterruption>();

    /// <summary>Overs the weather took out of the match, before any were made up.</summary>
    public int OversLostToRain { get; init; }

    /// <summary>
    /// Overs clawed back through early starts and extra time. Real playing conditions do not write
    /// off a washed-out session - the following days start early and run late until the arrears are
    /// cleared, which is why plenty of rain-hit Tests still produce a result.
    /// </summary>
    public int OversRecovered { get; init; }

    /// <summary>The day-by-day recovery plan, so a report can say how the lost time was made up.</summary>
    public LostTimeRecoveryPlan? RecoveryPlan { get; init; }

    public required string Summary { get; init; }
}

/// <summary>
/// Multi-day cricket: four innings, declarations, the follow-on, and the draw.
///
/// This is not a longer limited-overs match and cannot be simulated as one. Three things make it
/// its own game, and all three are modelled here:
///
/// 1. **The draw is a real result.** Running out of time with the match unfinished is the single
///    most common outcome in first-class cricket, and it is what makes a declaration a decision
///    rather than a formality. `CompetitionPointsSystem.FirstClass` has been waiting for this since
///    Phase 3 - a drawn match scores real points.
/// 2. **The pitch changes underneath the players.** A surface on day five is a different
///    proposition from the one on day one: it wears, it turns, and batting last is genuinely
///    harder. `GroundConditionsService.EstimateSpinAssistance` already knew how to model that and
///    had nothing to call it.
/// 3. **Time is a resource both captains are spending.** A side declares to buy overs to bowl at
///    the opposition, and gets it wrong when it leaves either too few or too many.
/// </summary>
public sealed class MultiDayMatchSimulator
{
    private readonly InningsSimulator _innings = new();
    private readonly GroundConditionsService _conditions = new();
    private readonly CaptaincyService _captaincy = new();
    private readonly OverRateService _overRates = new();
    private readonly MatchAmbitionService _ambition = new();
    private readonly RainService _rain = new();
    private readonly LostTimeRecoveryService _recovery = new();
    private readonly PitchPreparationService _pitchPrep = new();

    public MultiDayMatchResult Simulate(MultiDayMatchSetup setup, Random random)
    {
        var matchId = Guid.NewGuid();

        // Section AA: the home side's prepared surface, not the ground's raw long-term baseline -
        // applied once, up front, so every downstream read (toss, day-by-day deterioration, par
        // score) sees the SAME prepared pitch rather than some reading the request and others not.
        if (setup.Ground is { } rawGround && setup.HomePitchPreparation != PitchPreparation.Neutral)
            setup = setup with { Ground = _pitchPrep.Apply(rawGround, setup.HomePitchPreparation,
                setup.Competition?.EffectiveHomePitchInfluence ?? 1.0) };

        // How many overs the days will ACTUALLY yield, rather than the scheduled ninety. A four-seam
        // attack simply cannot get through ninety overs in six hours, and the light goes in the
        // evening - both of which cost a match real overs and can be the difference between a result
        // and a draw.
        int totalBallsAvailable = EstimateMatchBalls(setup);

        // The weather, day by day. In multi-day cricket rain does not change the target - it takes
        // away the overs a side needed to convert an advantage, which is a completely different and
        // far more frustrating thing, and it is the single biggest cause of real Test draws.
        var (oversLostToRain, interruptions, lostByDay) = _rain.SimulateMatchWeatherByDay(
            setup.Weather, setup.Days, setup.OversPerDay, random);

        // Playing conditions then claw back what they can: the days after a washout start early,
        // run late, and carry a larger over quota until the arrears are cleared. Without this every
        // wet match quietly became a draw, which is not what happens - plenty of rain-hit Tests are
        // won because the lost overs were made up.
        //
        // How much an hour of extra time is worth depends on the over rate, so a four-seam attack
        // recovers materially fewer overs from the same allowance than a spin-heavy one.
        double minutesPerOver = AverageMinutesPerOver(setup);
        var recovery = _recovery.Plan(lostByDay, setup.Days, setup.OversPerDay, minutesPerOver);

        int netLost = Math.Max(0, oversLostToRain - recovery.OversRecovered);
        totalBallsAvailable = Math.Max(setup.OversPerDay * 6, totalBallsAvailable - netLost * 6);
        int ballsBowled = 0;

        bool homeWinsToss = random.NextDouble() < 0.5;
        var tossWinner = homeWinsToss ? setup.Home : setup.Away;
        var tossLoser = homeWinsToss ? setup.Away : setup.Home;

        bool chooseToBat = DecideToBat(setup, random, tossWinner);
        var first = chooseToBat ? tossWinner : tossLoser;
        var second = chooseToBat ? tossLoser : tossWinner;

        var innings = new List<InningsState>();
        var assessments = new List<SessionAssessment>();

        // How the two sides compare, as they see it - which is what their ambitions are built on.
        double firstStrength = SideStrength(first);
        double secondStrength = SideStrength(second);

        var points = setup.Points ?? CompetitionPointsSystem.FirstClass;
        var firstAmbition = _ambition.InitialAmbition(firstStrength, secondStrength, points, setup.CompetitionImportance);
        var secondAmbition = _ambition.InitialAmbition(secondStrength, firstStrength, points, setup.CompetitionImportance);

        // Issues 1/2/5/6/7: one clock for the whole match, carried across every innings-call below
        // so day numbering, session tagging and bad light are all driven from one continuous,
        // live source of truth rather than recomputed per call from a naive balls-bowled division.
        var timeline = new MatchTimeline(1, setup.Days, setup.OversPerDay, _overRates, setup.Weather, setup.MatchDate.Month);

        // --- First innings ---
        var firstInnings = PlayInnings(setup, first, second, 1, day: 1, target: null,
            ballsAvailable: RemainingBalls(totalBallsAvailable, ballsBowled), random,
            oversBowledToday: OversBowledToday(ballsBowled, setup), timeline: timeline, oversLostByDay: lostByDay);
        innings.Add(firstInnings);
        ballsBowled += firstInnings.LegalBalls;

        // --- Second innings ---
        var secondInnings = PlayInnings(setup, second, first, 2, DayFor(ballsBowled, setup), target: null,
            ballsAvailable: RemainingBalls(totalBallsAvailable, ballsBowled), random,
            oversBowledToday: OversBowledToday(ballsBowled, setup), timeline: timeline, oversLostByDay: lostByDay);
        innings.Add(secondInnings);
        ballsBowled += secondInnings.LegalBalls;

        int firstInningsLead = firstInnings.Runs - secondInnings.Runs;

        // --- The follow-on ---
        // Enforcing it buys time but costs a tiring attack a rest, which is exactly the trade-off
        // real captains agonise over. A side that is a long way ahead with plenty of time left
        // enforces; one whose bowlers are spent, or who is short of time, does not.
        bool canEnforce = firstInningsLead >= setup.FollowOnMargin && secondInnings.IsAllOut;
        // §2.5: the side with the big first-innings lead - the one deciding whether to enforce - is
        // whoever bowled the second innings, i.e. whoever batted FIRST (`first`).
        double followOnCaptainQuality = InningsSimulator.DeclaringCaptainQuality(LeadershipFor(setup, first.TeamId));
        bool enforced = canEnforce && ShouldEnforceFollowOn(setup, ballsBowled, totalBallsAvailable, firstInningsLead, random, followOnCaptainQuality);

        if (ballsBowled < totalBallsAvailable)
        {
            if (enforced)
            {
                // They bat again, needing to wipe out the deficit before they even start.
                var thirdInnings = PlayInnings(setup, second, first, 3, DayFor(ballsBowled, setup),
                    target: firstInningsLead + 1,
                    ballsAvailable: RemainingBalls(totalBallsAvailable, ballsBowled), random,
                    oversBowledToday: OversBowledToday(ballsBowled, setup), timeline: timeline, oversLostByDay: lostByDay);
                innings.Add(thirdInnings);
                ballsBowled += thirdInnings.LegalBalls;

                int deficit = firstInningsLead - thirdInnings.Runs;

                if (deficit >= 0 && thirdInnings.IsAllOut)
                {
                    // An innings victory - they never had to bat again.
                    var inningsWinResult = BuildResult(matchId, setup, innings, tossWinner.TeamId, chooseToBat, enforced,
                        winner: first.TeamId, margin: deficit, byInnings: true, byWickets: false, ballsBowled, assessments)
                        with
                        {
                            Interruptions = interruptions,
                            OversLostToRain = oversLostToRain,
                            OversRecovered = recovery.OversRecovered,
                            RecoveryPlan = recovery
                        };
                    RecordCaptaincyOutcomes(setup, inningsWinResult, random);
                    return inningsWinResult;
                }

                if (deficit < 0 && ballsBowled < totalBallsAvailable)
                {
                    // They have turned it around and set a target after all.
                    var fourth = PlayInnings(setup, first, second, 4, DayFor(ballsBowled, setup),
                        target: -deficit + 1,
                        ballsAvailable: RemainingBalls(totalBallsAvailable, ballsBowled), random,
                        oversBowledToday: OversBowledToday(ballsBowled, setup), timeline: timeline, oversLostByDay: lostByDay);
                    innings.Add(fourth);
                    ballsBowled += fourth.LegalBalls;
                }
            }
            else
            {
                // Normal order: the side that batted first bats again, and decides when to declare.
                var thirdInnings = PlayInnings(setup, first, second, 3, DayFor(ballsBowled, setup), target: null,
                    ballsAvailable: RemainingBalls(totalBallsAvailable, ballsBowled), random,
                    declarationLead: firstInningsLead,
                    ballsLeftInMatch: totalBallsAvailable - ballsBowled, timeline: timeline, oversLostByDay: lostByDay);
                innings.Add(thirdInnings);
                ballsBowled += thirdInnings.LegalBalls;

                int target = firstInningsLead + thirdInnings.Runs + 1;

                if (ballsBowled < totalBallsAvailable && target > 0)
                {
                    var fourth = PlayInnings(setup, second, first, 4, DayFor(ballsBowled, setup),
                        target: target,
                        ballsAvailable: RemainingBalls(totalBallsAvailable, ballsBowled), random,
                        oversBowledToday: OversBowledToday(ballsBowled, setup), timeline: timeline, oversLostByDay: lostByDay);
                    innings.Add(fourth);
                    ballsBowled += fourth.LegalBalls;
                }
            }
        }

        // Session-by-session reassessment, reconstructed at each innings break. A side that walked
        // in playing for the draw can walk out going for the win, and this is where that is recorded.
        RecordAssessments(setup, innings, assessments, first, second, firstStrength, secondStrength,
            ref firstAmbition, ref secondAmbition, totalBallsAvailable);

        var result = DetermineResult(matchId, setup, innings, tossWinner.TeamId, chooseToBat, enforced,
            ballsBowled, totalBallsAvailable, assessments);

        var finalResult = result with
        {
            Interruptions = interruptions,
            OversLostToRain = oversLostToRain,
            OversRecovered = recovery.OversRecovered,
            RecoveryPlan = recovery
        };
        RecordCaptaincyOutcomes(setup, finalResult, random);
        return finalResult;
    }

    /// <summary>
    /// Section D follow-up: the multi-day counterpart to MatchSimulator's identically-named method.
    /// No MatchResultContextService equivalent exists for a multi-day result (that classifier only
    /// ever handled MatchResult - building a full parallel MultiDayMatchResultStory system just for
    /// this would be a large, separate undertaking), so this reads the simpler signal a multi-day
    /// result already carries directly: how the match was won or lost, which is honestly what a
    /// multi-day captain actually remembers about a finish.
    /// </summary>
    private void RecordCaptaincyOutcomes(MultiDayMatchSetup setup, MultiDayMatchResult result, Random random)
    {
        if (result.WinningTeamId is null && result.ResultForHome != MatchOutcome.Draw) return; // nothing resolved

        foreach (var (leadership, teamId) in new[]
        {
            (setup.HomeLeadership, setup.Home.TeamId),
            (setup.AwayLeadership, setup.Away.TeamId)
        })
        {
            if (leadership is null) continue;

            bool won = result.WinningTeamId == teamId;
            bool drew = result.ResultForHome == MatchOutcome.Draw;
            leadership.Profile.RecordMatch(won, handledWell: won || drew);

            double rating = !won && !drew ? -50
                : drew ? 8 // a secured draw under pressure is itself a competent outcome, mildly positive
                : result.WonByInnings ? 70
                : result.WonByWickets ? 55  // won it chasing
                : 50;                        // defended a target

            // Wave 6: momentum from the final innings, and the captain's own redemption arc.
            if (result.Innings.Count > 0)
            {
                var last = result.Innings[^1];
                double fromThisSidesView = teamId == last.BattingTeamId ? last.Momentum.Value : -last.Momentum.Value;
                rating += Math.Clamp(fromThisSidesView * 0.12, -10, 10);
            }
            double homeStrength = SideStrength(setup.Home), awayStrength = SideStrength(setup.Away);
            double teamStrength = teamId == setup.Home.TeamId ? homeStrength : awayStrength;
            double oppStrength = teamId == setup.Home.TeamId ? awayStrength : homeStrength;
            var profile = leadership.Profile;
            if (!won && !drew && teamStrength > oppStrength + 8)
            {
                profile.RecordBoldFailure(setup.MatchDate, 55);
            }
            else if (profile.PendingBoldFailure is not null && won && result.WonByInnings)
            {
                double composure = AbilityScale.AttributeToHundred(leadership.Captain.Mental.PressureHandling) / 100.0;
                rating += 15;
                if (random.NextDouble() < 0.3 + composure * 0.4)
                    leadership.Captain.Mental.DecisionMaking = Math.Min(20, leadership.Captain.Mental.DecisionMaking + 1);
                profile.ClearBoldFailure();
            }

            // Wave 4: bank this match's decision-quality reading, captain and vice-captain.
            leadership.Profile.AccumulateDecisionQuality(rating);
            leadership.ViceCaptainProfile?.AccumulateDecisionQuality(rating, weight: 0.35);

            // Post-Phase-6 carry-forward: the XI's trust in the captain.
            CaptaincyService.ApplyCaptainTrust(
                (teamId == setup.Home.TeamId ? setup.Home : setup.Away).BattingOrder, rating, won);

            bool chased = result.Innings.Count >= 4 && result.Innings[3].BattingTeamId == teamId;
            string situationKey = CaptaincyService.BuildSituationKey(MatchFormat.Test, MatchPhase.DeathOvers, chased, pressureLevel: 70);
            _captaincy.RecordSituationOutcome(leadership.Captain, situationKey, rating);
        }
    }

    /// <summary>
    /// Bat or bowl. In multi-day cricket the surface is the whole argument: you take first use of a
    /// good one and you make somebody else bat last on a bad one. The old cliche about batting
    /// first unless there is a compelling reason is genuinely how captains behave.
    /// </summary>
    public bool DecideToBat(MultiDayMatchSetup setup, Random random, MatchSide tossWinner)
    {
        var plan = tossWinner.TeamId == setup.Home.TeamId ? setup.HomePlan : setup.AwayPlan;
        var leadership = tossWinner.TeamId == setup.Home.TeamId ? setup.HomeLeadership : setup.AwayLeadership;

        if (plan?.TossDecision is { } instructed
            && plan.ResolveAuthority(InMatchDecision.Toss) is DecisionAuthority.CoachHasFinalSay or DecisionAuthority.Consult)
            return instructed;

        double batFirst = 72; // the default in first-class cricket, and it is a strong default

        if (setup.Ground is { } ground)
        {
            // A surface that will turn badly makes batting last miserable - take first use of it.
            batFirst += (ground.PitchSpinRating - 50) * 0.25;
            // A green seaming pitch under cloud is the classic reason to bowl.
            batFirst -= (ground.PitchSeamFriendliness() - 50) * 0.30;

            // Section T: forecasting, not just reading today's surface. Two grounds can look
            // identical on the morning of day one and still be completely different propositions -
            // one barely changes across five days, the other turns square by day three - and a real
            // captain reasons about where the pitch is HEADED, not just where it starts.
            // GroundConditionsService.EstimateSpinAssistance already knew how to project this; the
            // toss decision previously never asked it to look past day one at all.
            double day1Spin = _conditions.EstimateSpinAssistance(ground, MatchFormat.Test, dayOfMatch: 1);
            double finalDaySpin = _conditions.EstimateSpinAssistance(ground, MatchFormat.Test, dayOfMatch: setup.Days);
            double projectedDeterioration = finalDaySpin - day1Spin; // 0 on a road, large on a genuine wearer

            // The worse it is going to get, the stronger the pull toward taking first use of it now -
            // batting last on a pitch that has fallen apart is what this is actually avoiding.
            batFirst += projectedDeterioration * 0.35;
        }

        if (setup.Weather is { } weather)
            batFirst -= weather.SwingBonus * 0.6; // heavy cloud is the other classic reason

        double probability = Math.Clamp(batFirst / 100.0, 0.15, 0.95);

        if (leadership is not null)
        {
            var call = leadership.Decide(_captaincy, InMatchDecision.Toss, coachHasAPlan: false, random,
                situationKey: CaptaincyService.BuildSituationKey(MatchFormat.Test, MatchPhase.Powerplay, chasing: false, setup.CompetitionImportance));
            double clarity = call.Quality / 100.0;
            probability = 0.5 + (probability - 0.5) * (0.25 + clarity * 0.85);
        }

        return random.NextDouble() < probability;
    }

    /// <summary>
    /// Whether to enforce the follow-on. Time is the argument for; tired bowlers and the risk of
    /// batting last on a worn surface are the arguments against - which is why modern captains
    /// enforce it far less often than they used to.
    /// </summary>
    /// <param name="captainQuality">
    /// §2.5: 0 (poor) to 1 (excellent), 0.5 (the default and every pre-§2.5 caller) neutral. Per
    /// this codebase's own established discipline (CaptaincyService: "noise is not the same thing
    /// as bad judgement"), quality scales how much the INFORMATIVE terms below actually move the
    /// decision, not a fresh random draw - a sharp captain reads the lead/time/pitch/fatigue
    /// signals fully; a callow one is closer to a coin flip even with the same facts in front of
    /// him. Still exactly one random draw either way, so the shared match RNG stream is unaffected
    /// by this parameter's mere presence.
    /// </param>
    public bool ShouldEnforceFollowOn(MultiDayMatchSetup setup, int ballsBowled, int totalBalls, int lead, Random random, double captainQuality = 0.5)
    {
        double ballsLeftFraction = 1 - (double)ballsBowled / totalBalls;

        // Not enough time left and there is no point - you cannot bowl them out twice.
        if (ballsLeftFraction < 0.28) return false;

        double signal = (lead - setup.FollowOnMargin) / 10.0;   // a huge lead makes it easy
        signal += (ballsLeftFraction - 0.4) * 120;               // plenty of time makes it easy

        // Batting last on a surface that is breaking up is the real deterrent.
        if (setup.Ground is { } ground) signal -= (ground.PitchSpinRating - 50) * 0.4;

        // And nobody enforces it with a spent attack in the heat.
        if (setup.Weather is { } weather) signal -= (weather.FatigueMultiplier - 1) * 45;

        // Centred so the neutral default (0.5) reproduces the ORIGINAL, unscaled formula exactly -
        // every pre-§2.5 caller (no captainQuality argument) is byte-identical. 0.6x for a poor
        // captain, 1.4x for an excellent one.
        double sharpness = 0.6 + Math.Clamp(captainQuality, 0, 1) * 0.8;
        double enforce = 40 + signal * sharpness;

        return random.NextDouble() < Math.Clamp(enforce / 100.0, 0.05, 0.9);
    }

    private InningsState PlayInnings(
        MultiDayMatchSetup setup, MatchSide batting, MatchSide bowling, int inningsNumber, int day,
        int? target, int ballsAvailable, Random random,
        int? declarationLead = null, int? ballsLeftInMatch = null, int oversBowledToday = 0,
        MatchTimeline? timeline = null, IReadOnlyList<int>? oversLostByDay = null)
    {
        var pitch = BuildPitch(setup, timeline?.Day ?? day, oversLostByDay);

        // Overs available to this innings are what is left in the match, not a fixed allocation -
        // which is what makes running out of time possible at all.
        int oversAvailable = Math.Max(1, ballsAvailable / 6);

        var innings = _innings.Simulate(
            new InningsSetup(batting.BattingOrder, bowling.All, bowling.AvailableBowlers),
            MatchFormat.Test, oversAvailable,
            batting.TeamId, batting.TeamName,
            bowling.TeamId, bowling.TeamName,
            random,
            target: target, inningsNumber: inningsNumber, pitch: pitch,
            basePressure: setup.BaseImportance,
            battingPlan: PlanFor(setup, batting.TeamId),
            bowlingPlan: PlanFor(setup, bowling.TeamId),
            battingLeadership: LeadershipFor(setup, batting.TeamId),
            bowlingLeadership: LeadershipFor(setup, bowling.TeamId),
            weather: setup.Weather, ground: setup.Ground,
            // Slice 6.1: the home side's on-field edge, applied to whichever side is batting or
            // bowling in this innings. 1.0 (neutral) for every pre-existing caller.
            battingHomeEdge: batting.TeamId == setup.Home.TeamId ? 1.0 + Math.Clamp(setup.HomeAdvantage, 0, 0.15) : 1.0,
            bowlingHomeEdge: bowling.TeamId == setup.Home.TeamId ? 1.0 + Math.Clamp(setup.HomeAdvantage, 0, 0.15) : 1.0,
            umpireOutBias: setup.UmpireOutBias,
            drsReviewsPerInnings: setup.DrsReviewsPerInnings,
            pairHasFriction: setup.PairFeuds,
            hasThirdUmpire: setup.HasThirdUmpire,
            declarationLead: declarationLead,
            // Every innings is bounded by the time actually left in the match, not just the one
            // that might be declared - running out of time is how a draw happens.
            ballsLeftInMatch: ballsAvailable,
            oversPerDay: setup.OversPerDay,
            oversAlreadyBowledToday: oversBowledToday,
            // Issues 1/2/5/6/7: the live match clock, carried across every innings (an innings can
            // genuinely span more than one real day) so day/session tagging, bad light and the
            // extra half hour are all driven by one continuous source of truth rather than each
            // innings-call recomputing a static day number in isolation.
            timeline: timeline,
            pitchForDay: timeline is null ? null : d => BuildPitch(setup, d, oversLostByDay));

        return innings;
    }

    /// <summary>
    /// The surface on a given day. Wear is the whole story of a multi-day match: the same ground
    /// offers a seamer something on day one, nothing on day two, and a spinner everything by day five.
    ///
    /// The batting-ease and pace slopes below scale with Ground.PitchWearRate, same as spin -
    /// a ground that wears fast loses its pace and its batting ease faster too, since all three
    /// are the same underlying "how quickly does this surface break up" property. A wear rate of
    /// 50 (the field's default) reproduces the original fixed slopes exactly.
    /// </summary>
    public PitchConditions BuildPitch(MultiDayMatchSetup setup, int day, IReadOnlyList<int>? oversLostByDay = null)
    {
        if (setup.Ground is not { } ground) return PitchConditions.Neutral;

        var basePitch = PitchConditions.FromGround(ground);
        double wearScale = Math.Clamp(ground.PitchWearRate, 0, 100) / 50.0;

        // Phase 15 (§19.4): a drop-in pitch wears far more evenly - it holds its shape and its
        // batting ease across the days rather than breaking up into a day-five minefield.
        if (ground.UsesDropInPitch) wearScale *= 0.55;

        double spin = _conditions.EstimateSpinAssistance(ground, MatchFormat.Test, Math.Clamp(day, 1, 5));
        // Phase 15 (§19.4): a drop-in wears evenly - it never breaks up into a day-five turner, so
        // the day-over-day GROWTH in spin above the ground's own baseline is heavily damped.
        if (ground.UsesDropInPitch)
            spin = basePitch.Spin + (spin - basePitch.Spin) * 0.5;

        // Hot dry weather bakes it faster, which is exactly why a dry Asian Test turns square by day three.
        double dryingBonus = setup.Weather is { } weather ? weather.PitchDryingRate * (day - 1) * 5 : 0;

        double battingEase = Math.Clamp(basePitch.BattingFriendliness - (day - 1) * 4.5 * wearScale - dryingBonus * 0.4, 5, 100);

        // Seam movement is a new-ball, early-days phenomenon; the shine and the grass both go.
        double pace = Math.Clamp(basePitch.Pace - (day - 1) * 3 * wearScale, 10, 100);

        // External-probe follow-up: gradual post-rain transition. A damp start to a day that lost
        // real time to rain grips less for spin and offers a shade more seam - the classic "hard to
        // settle straight after a shower" passage of play. A whole-day approximation, deliberately:
        // PitchConditions is computed once per day here, not per over, so this is the honest
        // resolution this architecture can express - a finer, per-over fade would need per-ball
        // pitch tracking that does not exist anywhere else in this engine either.
        double rainSeverity = oversLostByDay is not null && day - 1 < oversLostByDay.Count
            ? Math.Clamp(oversLostByDay[day - 1] / (double)Math.Max(1, setup.OversPerDay), 0, 1)
            : 0;
        double spinDamp = 12 * rainSeverity;
        double seamBoost = 6 * rainSeverity;

        // §11.4: a pink-ball day-night Test - the ball seams and swings more under lights, and
        // batting is harder overall (the twilight session especially, which a whole-day pitch model
        // approximates as a blanket dip).
        double dnSeam = setup.DayNight ? 8 : 0;
        double dnBatDip = setup.DayNight ? 7 : 0;

        // §19.4: a RE-USED strip is worn from ball one - more turn, less pace, harder to bat on. A
        // drop-in used strip holds together better, so the effect is halved there.
        double reuse = setup.PitchIsReused ? (setup.Ground?.UsesDropInPitch == true ? 0.5 : 1.0) : 0.0;
        double reuseSpin = 10 * reuse;
        double reuseBatDip = 8 * reuse;
        double reusePace = 6 * reuse;

        return basePitch with
        {
            Spin = Math.Clamp(spin + dryingBonus - spinDamp + reuseSpin, 0, 100),
            BattingFriendliness = Math.Clamp(battingEase - dnBatDip - reuseBatDip, 5, 100),
            Pace = Math.Clamp(pace + seamBoost + dnSeam - reusePace, 10, 100)
        };
    }

    /// <summary>
    /// The overs a match will realistically contain, day by day, from the attacks on show and the
    /// light. This is where a slow over rate and a gloomy evening turn into fewer overs rather than
    /// into nothing at all - and it is why a side that plays four quicks quietly gives up cricket.
    /// </summary>
    public int EstimateMatchBalls(MultiDayMatchSetup setup)
    {
        var attacks = new[] { setup.Home.AvailableBowlers, setup.Away.AvailableBowlers };
        int total = 0;

        for (int day = 1; day <= setup.Days; day++)
        {
            var clock = new MatchDayClock(day, setup.OversPerDay);
            double playingMinutes = clock.MinutesRemaining;

            // The light decides how much of the evening session is usable at all.
            // Judged at six o'clock, because it is the last hour that is actually at risk - at half
            // past five almost any evening is playable.
            double eveningLight = _overRates.LightLevel(new TimeOnly(18, 0), setup.Weather, setup.MatchDate.Month);
            if (eveningLight < 22) playingMinutes -= 45;          // last three quarters of an hour lost
            else if (eveningLight < 45) playingMinutes -= 15;     // spinners only, and slower going

            // Both attacks bowl, so the rate is the average of the two.
            int dayOvers = attacks.Sum(a => _overRates.EstimateOversInDay(a, Math.Max(60, playingMinutes))) / 2;

            // A side can claim the extra half hour when it is behind, which claws some of it back.
            if (dayOvers < setup.OversPerDay) dayOvers += (int)(30 / 4.0);

            total += Math.Clamp(dayOvers, 40, setup.OversPerDay + 8);
        }

        return total * 6;
    }

    /// <summary>The two attacks' average time per over, which decides how many overs an hour of extra time actually buys back.</summary>
    private double AverageMinutesPerOver(MultiDayMatchSetup setup)
    {
        var allBowlers = setup.Home.AvailableBowlers.Concat(setup.Away.AvailableBowlers).ToList();
        return allBowlers.Count == 0 ? 4.0 : allBowlers.Average(_overRates.MinutesPerOver);
    }

    /// <summary>How strong a side is, from its players. Batting is weighted a little above bowling because a first-class match is usually decided by whether you can bat twice.</summary>
    private static double SideStrength(MatchSide side) =>
        side.BattingOrder.Count == 0 ? 50
        : side.BattingOrder.Average(p => AbilityScale.CompositeAbilityToHundred(p.CurrentAbility));

    /// <summary>
    /// Walks the match innings by innings and records what each side was playing for. Reconstructed
    /// at the breaks rather than every ball - a dressing room reassesses at intervals, not
    /// continuously, and that is also where a coach gets to intervene.
    /// </summary>
    private void RecordAssessments(
        MultiDayMatchSetup setup, List<InningsState> innings, List<SessionAssessment> assessments,
        MatchSide first, MatchSide second, double firstStrength, double secondStrength,
        ref MatchAmbition firstAmbition, ref MatchAmbition secondAmbition, int totalBalls)
    {
        int ballsSoFar = 0;
        int runningLead = 0;

        for (int i = 0; i < innings.Count; i++)
        {
            var state = innings[i];
            ballsSoFar += state.LegalBalls;

            bool firstSideBatted = state.BattingTeamId == first.TeamId;
            runningLead += firstSideBatted ? state.Runs : -state.Runs;

            double timeLeft = totalBalls <= 0 ? 0 : Math.Clamp(1 - (double)ballsSoFar / totalBalls, 0, 1);

            var firstPosition = new MultiDayMatchPosition
            {
                EffectiveLead = runningLead,
                OwnWicketsInHand = firstSideBatted ? 10 - state.Wickets : 10,
                OppositionWicketsInHand = firstSideBatted ? 10 : 10 - state.Wickets,
                TimeRemainingFraction = timeLeft,
                InningsNumber = i + 1,
                IsChasing = state.Target is not null && firstSideBatted,
                RunsRequired = firstSideBatted ? state.RunsRequired : null
            };

            var secondPosition = firstPosition with
            {
                EffectiveLead = -runningLead,
                OwnWicketsInHand = firstPosition.OppositionWicketsInHand,
                OppositionWicketsInHand = firstPosition.OwnWicketsInHand,
                IsChasing = state.Target is not null && !firstSideBatted,
                RunsRequired = firstSideBatted ? null : state.RunsRequired
            };

            var session = (SessionType)Math.Clamp(i % 3, 0, 2);
            int day = Math.Clamp(ballsSoFar / (setup.OversPerDay * 6) + 1, 1, setup.Days);

            var firstAssessment = _ambition.Assess(first.TeamId, firstPosition, firstStrength, secondStrength, firstAmbition, day, session);
            var secondAssessment = _ambition.Assess(second.TeamId, secondPosition, secondStrength, firstStrength, secondAmbition, day, session);

            firstAmbition = firstAssessment.Ambition;
            secondAmbition = secondAssessment.Ambition;

            assessments.Add(firstAssessment);
            assessments.Add(secondAssessment);
        }
    }

    private static int RemainingBalls(int total, int bowled) => Math.Max(0, total - bowled);

    /// <summary>Overs already bowled in the current day, so a nightwatchman decision knows how close the close of play is.</summary>
    private static int OversBowledToday(int ballsBowled, MultiDayMatchSetup setup) =>
        ballsBowled / 6 % Math.Max(1, setup.OversPerDay);

    private static int DayFor(int ballsBowled, MultiDayMatchSetup setup) =>
        Math.Clamp(ballsBowled / (setup.OversPerDay * 6) + 1, 1, setup.Days);

    private static TacticalPlan? PlanFor(MultiDayMatchSetup setup, Guid teamId) =>
        teamId == setup.Home.TeamId ? setup.HomePlan : teamId == setup.Away.TeamId ? setup.AwayPlan : null;

    private static MatchLeadership? LeadershipFor(MultiDayMatchSetup setup, Guid teamId) =>
        teamId == setup.Home.TeamId ? setup.HomeLeadership : teamId == setup.Away.TeamId ? setup.AwayLeadership : null;

    private static MultiDayMatchResult DetermineResult(
        Guid matchId, MultiDayMatchSetup setup, List<InningsState> innings, Guid tossWinnerId,
        bool choseToBat, bool enforced, int ballsBowled, int totalBalls, List<SessionAssessment> assessments)
    {
        // Fewer than four innings and time gone: a draw, whatever the scores say. This is the
        // outcome limited-overs cricket simply does not have.
        if (innings.Count < 4)
            return BuildResult(matchId, setup, innings, tossWinnerId, choseToBat, enforced,
                winner: null, margin: null, byInnings: false, byWickets: false, ballsBowled, assessments);

        var fourth = innings[3];
        var chasingTeam = fourth.BattingTeamId;
        var defendingTeam = innings[2].BattingTeamId == chasingTeam ? innings[0].BattingTeamId : innings[2].BattingTeamId;

        if (fourth.TargetReached)
            return BuildResult(matchId, setup, innings, tossWinnerId, choseToBat, enforced,
                winner: chasingTeam, margin: 10 - fourth.Wickets, byInnings: false, byWickets: true, ballsBowled, assessments);

        if (fourth.IsAllOut)
            return BuildResult(matchId, setup, innings, tossWinnerId, choseToBat, enforced,
                winner: defendingTeam, margin: (fourth.Target ?? 0) - fourth.Runs, byInnings: false, byWickets: false, ballsBowled, assessments);

        // They survived. Out of time with wickets in hand - the classic hard-fought draw.
        return BuildResult(matchId, setup, innings, tossWinnerId, choseToBat, enforced,
            winner: null, margin: null, byInnings: false, byWickets: false, ballsBowled, assessments);
    }

    private static MultiDayMatchResult BuildResult(
        Guid matchId, MultiDayMatchSetup setup, List<InningsState> innings, Guid tossWinnerId,
        bool choseToBat, bool enforced, Guid? winner, int? margin, bool byInnings, bool byWickets, int ballsBowled,
        List<SessionAssessment>? assessments = null)
    {
        string homeName = setup.Home.TeamName, awayName = setup.Away.TeamName;

        string summary;
        if (winner is null)
        {
            summary = innings.Count < 4
                ? $"{homeName} v {awayName} - drawn, the match never got close to a finish."
                : $"{homeName} v {awayName} - drawn, with the last pair holding out.";
        }
        else
        {
            string winnerName = winner == setup.Home.TeamId ? homeName : awayName;
            string loserName = winner == setup.Home.TeamId ? awayName : homeName;

            summary = byInnings
                ? $"{winnerName} beat {loserName} by an innings and {margin} run{(margin == 1 ? "" : "s")}"
                : byWickets
                    ? $"{winnerName} beat {loserName} by {margin} wicket{(margin == 1 ? "" : "s")}"
                    : $"{winnerName} beat {loserName} by {margin} run{(margin == 1 ? "" : "s")}";
        }

        MatchOutcome resultForHome =
            winner is null ? MatchOutcome.Draw
            : winner == setup.Home.TeamId ? MatchOutcome.Win
            : MatchOutcome.Loss;

        return new MultiDayMatchResult
        {
            MatchId = matchId,
            Setup = setup,
            Innings = innings,
            TossWinnerTeamId = tossWinnerId,
            TossWinnerChoseToBat = choseToBat,
            FollowOnEnforced = enforced,
            WinningTeamId = winner,
            ResultForHome = resultForHome,
            WinMargin = margin,
            WonByInnings = byInnings,
            WonByWickets = byWickets,
            TotalOvers = Math.Round(ballsBowled / 6.0, 1),
            Assessments = assessments ?? new List<SessionAssessment>(),
            Summary = summary
        };
    }
}

internal static class GroundPitchExtensions
{
    /// <summary>How much this surface offers a seamer, blended from its pace and bounce ratings. Used by the toss decision, where a green seaming pitch is the classic reason to bowl.</summary>
    public static double PitchSeamFriendliness(this Ground ground) =>
        ground.PitchPaceRating * 0.55 + ground.PitchBounceRating * 0.45;
}
