using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>One side as it takes the field: who bats, in what order, and who can bowl.</summary>
public sealed record MatchSide(
    Guid TeamId,
    string TeamName,
    IReadOnlyList<Player> BattingOrder,
    IReadOnlyList<Player> AvailableBowlers)
{
    public IReadOnlyList<Player> All => BattingOrder;
}

/// <summary>Everything needed to stage a fixture.</summary>
public sealed record MatchSetup
{
    public required MatchSide Home { get; init; }
    public required MatchSide Away { get; init; }
    public MatchFormat Format { get; init; } = MatchFormat.T20;
    public int OversPerInnings { get; init; } = 20;
    public Ground? Ground { get; init; }
    public Competition? Competition { get; init; }
    public DateOnly MatchDate { get; init; } = new(2026, 6, 1);
    public int Season { get; init; } = 2026;

    /// <summary>0-100. A league fixture sits near 50; a World Cup final is 95. Feeds the experience/temperament system, which is what makes a big occasion play differently.</summary>
    public double BaseImportance { get; init; } = 50;

    /// <summary>
    /// Phase 6, Slice 6.1: the home side's on-field edge for this fixture, as a fraction (0 = a
    /// neutral venue, ~0.03-0.05 = a full house behind a united dressing room in a derby). Applied
    /// as a small multiplier to the home side's batter and bowler effective skill. Default 0 - a
    /// neutral game - so every existing caller and test is byte-identical. FixturePlayService
    /// computes it from crowd fill rate, dressing-room harmony and any rivalry.
    /// </summary>
    public double HomeAdvantage { get; init; }

    /// <summary>
    /// Phase 7, Slice 7.9: the on-field umpires (and TV/reserve) for this fixture. Null for a
    /// background AI-vs-AI fixture with no umpiring modelled and for every pre-Phase-7 test - kept
    /// for identity, the post-match review and display. The ball-model effect goes through
    /// <see cref="UmpireOutBias"/>, which FixturePlayService computes from this list.
    /// </summary>
    public IReadOnlyList<Umpire>? Umpires { get; init; }

    /// <summary>
    /// Phase 7, Slice 7.9: the umpiring panel's lean on the marginal LBW / caught-behind, as a
    /// multiplier. 1.0 (the default and every pre-Phase-7 caller) = a neutral top-class panel.
    /// Consumes no RNG in the ball model, so 1.0 is byte-identical to the pre-Phase-7 engine.
    /// </summary>
    public double UmpireOutBias { get; init; } = 1.0;

    /// <summary>
    /// Phase 15 (§1.7): DRS reviews available to each side per innings. 0 (the default and every
    /// pre-Phase-15 caller) = no DRS - the on-field call stands. FixturePlayService reads this from
    /// Competition.EffectiveConditions.
    /// </summary>
    public int DrsReviewsPerInnings { get; init; }

    /// <summary>
    /// Phase 15 (§16.5): resolve a tied match with a Super Over (repeated until decisive, then
    /// boundary count). Default false - a pre-Phase-15 tie stays a tie. FixturePlayService sets it
    /// from the competition's playing conditions for limited-overs fixtures.
    /// </summary>
    public bool ResolveTiesWithSuperOver { get; init; }

    /// <summary>Day of a multi-day match, used for pitch deterioration. Ignored in limited-overs cricket.</summary>
    public int DayOfMatch { get; init; } = 1;

    public bool EveningSession { get; init; }

    /// <summary>The conditions the match is played in. Drives how hard the work is on the players, and how much the ball moves.</summary>
    public MatchWeather? Weather { get; init; }

    /// <summary>
    /// The coaches' tactical plans. Null means that side is fully AI-directed, which is what every
    /// AI-vs-AI fixture in the world simulation uses - so the human coach's plan is the exception,
    /// not a requirement.
    /// </summary>
    public TacticalPlan? HomePlan { get; init; }
    public TacticalPlan? AwayPlan { get; init; }

    /// <summary>
    /// Each side's captain and coach. Null means leadership is not modelled for that side, which is
    /// what background AI-versus-AI fixtures use - the world simulation cannot afford to resolve a
    /// captaincy decision for every over of every match it plays in the background.
    /// </summary>
    public MatchLeadership? HomeLeadership { get; init; }
    public MatchLeadership? AwayLeadership { get; init; }

    /// <summary>
    /// Phase 14 (§18.1, in-match): unordered pairs of player ids who genuinely feud. When both are
    /// at the crease together their run-out risk carries a small extra multiplier. Empty (the
    /// default and every pre-Phase-14 caller) = no friction. FixturePlayService fills it from
    /// WorldState.PlayerRelationships.
    /// </summary>
    public IReadOnlyCollection<(Guid A, Guid B)> FeudingPairs { get; init; } = Array.Empty<(Guid, Guid)>();

    /// <summary>A predicate form of <see cref="FeudingPairs"/> for the innings simulator.</summary>
    public bool PairFeuds(Guid a, Guid b) =>
        FeudingPairs.Count > 0 && FeudingPairs.Any(p => (p.A == a && p.B == b) || (p.A == b && p.B == a));

    /// <summary>Phase 15 (§16.6): a TV / third umpire for line calls even without full DRS. Default false. FixturePlayService sets it for first-class, international and franchise fixtures.</summary>
    public bool HasThirdUmpire { get; init; }
}

/// <summary>A completed match, with both innings intact so a scorecard, a records update and a post-match analysis can all read the same underlying data.</summary>
public sealed record MatchResult
{
    public required Guid MatchId { get; init; }
    public required MatchSetup Setup { get; init; }
    public required InningsState FirstInnings { get; init; }
    public required InningsState SecondInnings { get; init; }

    public Guid TossWinnerTeamId { get; init; }
    public bool TossWinnerChoseToBat { get; init; }

    public Guid? WinningTeamId { get; init; }
    public Guid? LosingTeamId { get; init; }
    public MatchOutcome ResultForHome { get; init; }

    /// <summary>Margin in runs when defending, or wickets when chasing. Null on a tie.</summary>
    public int? WinMargin { get; init; }
    public bool WonByWickets { get; init; }

    public required string Summary { get; init; }

    /// <summary>Stoppages during the match, in order.</summary>
    public IReadOnlyList<RainInterruption> Interruptions { get; init; } = Array.Empty<RainInterruption>();

    /// <summary>Set when rain shortened the chase and the target was recalculated. A DLS result is not the same as an ordinary one and a scorecard has to say so.</summary>
    public int? DlsRevisedTarget { get; init; }
    public int? RevisedOvers { get; init; }
    public bool NoResult { get; init; }

    /// <summary>Phase 15 (§16.5): the match was level after the scheduled overs and was decided by a Super Over (or, if that too was tied, by boundary count). The Summary carries the detail.</summary>
    public bool SuperOverPlayed { get; init; }

    public InningsState InningsFor(Guid teamId) =>
        FirstInnings.BattingTeamId == teamId ? FirstInnings : SecondInnings;
}

/// <summary>
/// Stages a complete limited-overs match: toss, first innings, chase, result.
///
/// The chase is the reason this exists as its own layer rather than "run the innings simulator
/// twice". A second innings is a fundamentally different thing - it has a target, which drives
/// batting intent every single ball, ends the moment the target is passed, and turns the last
/// overs into the highest-pressure passage in the game. Simulating it as an ordinary innings
/// and comparing totals afterwards would lose all of that.
///
/// Multi-day cricket (four innings, declarations, follow-on, draws) is deliberately NOT here
/// yet - see the class note at the bottom of this file.
/// </summary>
public sealed class MatchSimulator
{
    private readonly InningsSimulator _innings = new();
    private readonly GroundConditionsService _conditions = new();
    private readonly CaptaincyService _captaincy = new();
    private readonly RainService _rain = new();
    private readonly MatchResultContextService _resultContext = new();

    public MatchResult Simulate(MatchSetup setup, Random random)
    {
        var matchId = Guid.NewGuid();
        var pitch = BuildPitch(setup);

        // Toss. The decision is a real read of the conditions, not a coin flip twice over: sides
        // chase at venues where chasing works and bat first on a surface that will deteriorate.
        bool homeWinsToss = random.NextDouble() < 0.5;
        var tossWinner = homeWinsToss ? setup.Home : setup.Away;
        var tossLoser = homeWinsToss ? setup.Away : setup.Home;

        // The toss is the captain's call and nobody else's - he makes it in the middle, in front of
        // everybody, and no coach can make it for him. A captain who reads conditions well gets it
        // right more often; one who does not is closer to a coin flip on top of a coin flip.
        var tossLeadership = LeadershipFor(setup, tossWinner.TeamId);
        var tossPlan = PlanFor(setup, tossWinner.TeamId);
        bool chooseToBat = DecideToBat(setup, pitch, random, tossLeadership, tossPlan);

        var firstBatting = chooseToBat ? tossWinner : tossLoser;
        var secondBatting = chooseToBat ? tossLoser : tossWinner;

        // Rain before the second innings is the interesting case: the chase is shortened and the
        // target has to be recalculated on RESOURCES, not on runs per over. Rolled once, between the
        // innings, which is when a real interruption most often bites.
        var interruptions = new List<RainInterruption>();

        // Issue 2: limited-overs cricket has no session structure, but it still deserves a real
        // wall-clock time - a T20 that starts at 19:00 and an ODI that starts at 09:30 are genuinely
        // different occasions (evening dew, a longer day), and an innings break has to actually move
        // the clock forward for that to mean anything.
        var matchStartTime = setup.Format == MatchFormat.T20 ? new TimeOnly(19, 0) : new TimeOnly(9, 30);
        int inningsBreakMinutes = setup.Format == MatchFormat.T20 ? 20 : 45;

        // Slice 6.1: the home side's on-field edge, applied to whichever innings it is batting or
        // bowling in. Zero (a neutral venue) for every pre-existing caller.
        double homeMultiplier = 1.0 + Math.Clamp(setup.HomeAdvantage, 0, 0.15);
        double HomeEdge(Guid teamId) => teamId == setup.Home.TeamId ? homeMultiplier : 1.0;

        var first = _innings.Simulate(
            new InningsSetup(firstBatting.BattingOrder, secondBatting.All, secondBatting.AvailableBowlers),
            setup.Format, setup.OversPerInnings,
            firstBatting.TeamId, firstBatting.TeamName,
            secondBatting.TeamId, secondBatting.TeamName,
            random,
            target: null, inningsNumber: 1, pitch: pitch, basePressure: setup.BaseImportance,
            battingPlan: PlanFor(setup, firstBatting.TeamId),
            bowlingPlan: PlanFor(setup, secondBatting.TeamId),
            battingLeadership: LeadershipFor(setup, firstBatting.TeamId),
            bowlingLeadership: LeadershipFor(setup, secondBatting.TeamId),
            weather: setup.Weather, ground: setup.Ground, startTime: matchStartTime,
            battingHomeEdge: HomeEdge(firstBatting.TeamId), bowlingHomeEdge: HomeEdge(secondBatting.TeamId),
            umpireOutBias: setup.UmpireOutBias, drsReviewsPerInnings: setup.DrsReviewsPerInnings, pairHasFriction: setup.PairFeuds, hasThirdUmpire: setup.HasThirdUmpire);

        // The second innings starts once the changeover is actually over, not the moment the first
        // one ends - and it starts from where the first innings' clock actually finished, not a
        // fixed schedule, so a rain-hit or slow-over-rate first innings pushes the second back too.
        TimeOnly firstInningsEnd = first.Deliveries.Count > 0 && first.Deliveries[^1].ClockTime is { } endTime
            ? endTime
            : matchStartTime;
        var secondInningsStart = firstInningsEnd.AddMinutes(inningsBreakMinutes);

        // Rain in the interval. Overs come off the chase and the target moves with them.
        int chaseOvers = setup.OversPerInnings;
        int? revisedTarget = null;
        bool abandoned = false;

        var breakRain = _rain.RollForRain(setup.Weather, day: 1, new TimeOnly(15, 30), minutesAtRisk: 150, random);
        if (breakRain is not null)
        {
            interruptions.Add(breakRain);

            var reduction = _rain.ApplyToLimitedOvers(setup.Format, setup.OversPerInnings,
                oversBowledInSecondInnings: 0, wicketsDownInSecondInnings: 0,
                firstInningsScore: first.Runs,
                oversAvailableToFirstInnings: setup.OversPerInnings,
                minutesLost: breakRain.MinutesLost);

            chaseOvers = reduction.RevisedOvers;
            revisedTarget = reduction.RevisedTarget;
            abandoned = reduction.MatchAbandoned;
        }

        if (abandoned)
        {
            // Abandoned before the chase could begin. There is no second innings to report, so the
            // first is used for both slots and the result is explicitly a no-result rather than a
            // tie - a washed-out match is not a match anybody drew.
            var washedOut = BuildResult(matchId, setup, first, first, tossWinner.TeamId, chooseToBat);
            return washedOut with
            {
                Interruptions = interruptions,
                NoResult = true,
                RevisedOvers = chaseOvers,
                WinningTeamId = null,
                LosingTeamId = null,
                WinMargin = null,
                ResultForHome = MatchOutcome.NoResult,
                Summary = $"{setup.Home.TeamName} v {setup.Away.TeamName} - abandoned, no result."
            };
        }

        // The ball is new again for the second innings, and the surface has taken an innings of
        // wear - a genuine, observable effect in limited-overs cricket, not just a Test one.
        var secondInningsPitch = pitch with
        {
            BattingFriendliness = Math.Clamp(pitch.BattingFriendliness - 2, 0, 100),
            Spin = Math.Clamp(pitch.Spin + 3, 0, 100)
        };

        // Phase 15 (§19.1): a real rain break in the interval leaves the track damp on the
        // resumption - a shade more in it for the seamers, harder to time, less grip for the
        // spinners - which is the classic "watch out straight after the covers come off" passage.
        // Bounded and modest (a limited-overs chase is short); it does not decay per-over here, the
        // same whole-innings approximation the multi-day post-rain model uses.
        int dampOvers = 0;
        if (breakRain is { MinutesLost: > 25 })
        {
            double severity = Math.Clamp((breakRain.MinutesLost - 25) / 90.0, 0, 1);
            // §19.1: rather than a flat whole-innings adjustment, the freshness is concentrated in
            // the overs straight after the resumption and decays away - InningsSimulator applies the
            // decaying seam bonus over `dampOvers`.
            dampOvers = (int)Math.Round(4 + severity * 6); // ~4-10 overs
        }

        var second = _innings.Simulate(
            new InningsSetup(secondBatting.BattingOrder, firstBatting.All, firstBatting.AvailableBowlers),
            setup.Format, chaseOvers,
            secondBatting.TeamId, secondBatting.TeamName,
            firstBatting.TeamId, firstBatting.TeamName,
            random,
            target: revisedTarget ?? first.Runs + 1, inningsNumber: 2, pitch: secondInningsPitch,
            // A chase is played under materially more pressure than a first innings, and the
            // required-rate logic inside the innings simulator layers on top of this baseline.
            basePressure: Math.Clamp(setup.BaseImportance + 10, 0, 100),
            battingPlan: PlanFor(setup, secondBatting.TeamId),
            bowlingPlan: PlanFor(setup, firstBatting.TeamId),
            battingLeadership: LeadershipFor(setup, secondBatting.TeamId),
            bowlingLeadership: LeadershipFor(setup, firstBatting.TeamId),
            weather: setup.Weather, ground: setup.Ground, startTime: secondInningsStart,
            battingHomeEdge: HomeEdge(secondBatting.TeamId), bowlingHomeEdge: HomeEdge(firstBatting.TeamId),
            umpireOutBias: setup.UmpireOutBias, drsReviewsPerInnings: setup.DrsReviewsPerInnings, pairHasFriction: setup.PairFeuds, hasThirdUmpire: setup.HasThirdUmpire,
            dampOversAtStart: dampOvers,
            // §1.6: a within-innings dew build for a night chase at a dewy ground.
            dewFactor: setup.EveningSession && setup.Ground?.HasFloodlights == true
                ? Math.Clamp((setup.Ground.DewTendency - 25) / 75.0, 0, 1)
                : 0);

        var result = BuildResult(matchId, setup, first, second, tossWinner.TeamId, chooseToBat);

        // Phase 15 (§16.5): a tied match, broken by a Super Over when the competition's playing
        // conditions call for one. The side that batted second in the match bats first in the
        // eliminator (the real rule). If the Super Over is also tied, boundary count decides.
        if (result.WinningTeamId is null && !result.NoResult && setup.ResolveTiesWithSuperOver
            && setup.Format != MatchFormat.Test)
        {
            result = ResolveWithSuperOver(setup, result, firstBatting, secondBatting, first, second, pitch, random);
        }

        RecordCaptaincyOutcomes(setup, result, random);
        return result with
        {
            Interruptions = interruptions,
            DlsRevisedTarget = revisedTarget,
            RevisedOvers = chaseOvers == setup.OversPerInnings ? null : chaseOvers
        };
    }

    /// <summary>
    /// Phase 15 (§16.5): a one-over-per-side eliminator. Top three by hitting power bat, two
    /// wickets ends it, the death bowler bowls. Repeated (up to a few times) until decisive, then
    /// resolved on boundary count.
    /// </summary>
    private MatchResult ResolveWithSuperOver(
        MatchSetup setup, MatchResult tied, MatchSide firstBatting, MatchSide secondBatting,
        InningsState matchFirst, InningsState matchSecond, PitchConditions pitch, Random random)
    {
        // Batting order for the eliminator: the three most dangerous strikers.
        static MatchSide Trim(MatchSide side) => side with
        {
            BattingOrder = side.BattingOrder
                .OrderByDescending(p => p.Batting.PowerHitting + p.Batting.BoundaryHitting + p.Batting.DeathOverBatting)
                .Take(3).ToList()
        };
        var sideA = Trim(secondBatting); // batted second in the match -> bats first in the SO
        var sideB = Trim(firstBatting);

        int matchBoundariesA = MatchBoundaries(matchSecond);
        int matchBoundariesB = MatchBoundaries(matchFirst);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            var soA = PlaySuperOverInnings(sideA, sideB, pitch, target: null, random);
            var soB = PlaySuperOverInnings(sideB, sideA, pitch, target: soA.Runs + 1, random);

            if (soA.Runs != soB.Runs)
            {
                var winner = soA.Runs > soB.Runs ? sideA : sideB;
                var loser = soA.Runs > soB.Runs ? sideB : sideA;
                return WithSuperOverWinner(setup, tied, winner.TeamId, loser.TeamId,
                    $"Super Over: {sideA.TeamName} {soA.Runs}/{soA.Wickets}, {sideB.TeamName} {soB.Runs}/{soB.Wickets} - {winner.TeamName} win the eliminator.");
            }

            matchBoundariesA += MatchBoundaries(soA);
            matchBoundariesB += MatchBoundaries(soB);
        }

        // Still level after repeated Super Overs - boundary count across the match plus the eliminators.
        if (matchBoundariesA != matchBoundariesB)
        {
            var winner = matchBoundariesA > matchBoundariesB ? sideA : sideB;
            var loser = matchBoundariesA > matchBoundariesB ? sideB : sideA;
            return WithSuperOverWinner(setup, tied, winner.TeamId, loser.TeamId,
                $"Super Over tied - {winner.TeamName} win on boundary count ({Math.Max(matchBoundariesA, matchBoundariesB)} to {Math.Min(matchBoundariesA, matchBoundariesB)}).");
        }

        return tied with { SuperOverPlayed = true, Summary = tied.Summary + " (Super Over also tied)" };
    }

    private InningsState PlaySuperOverInnings(MatchSide batting, MatchSide bowling, PitchConditions pitch, int? target, Random random)
    {
        var bowlers = bowling.AvailableBowlers.Count > 0
            ? bowling.AvailableBowlers.OrderByDescending(p => p.Bowling.DeathBowling + p.Bowling.Yorker).Take(1).ToList()
            : bowling.BattingOrder.Take(1).ToList();

        return _innings.Simulate(
            new InningsSetup(batting.BattingOrder, bowling.BattingOrder, bowlers),
            MatchFormat.T20, totalOvers: 1,
            batting.TeamId, batting.TeamName, bowling.TeamId, bowling.TeamName, random,
            target: target, inningsNumber: target is null ? 1 : 2, pitch: pitch, basePressure: 92);
    }

    private static MatchResult WithSuperOverWinner(MatchSetup setup, MatchResult tied, Guid winnerId, Guid loserId, string detail)
    {
        MatchOutcome forHome = winnerId == setup.Home.TeamId ? MatchOutcome.Win : MatchOutcome.Loss;
        return tied with
        {
            WinningTeamId = winnerId,
            LosingTeamId = loserId,
            ResultForHome = forHome,
            SuperOverPlayed = true,
            WinMargin = null,
            Summary = tied.Summary + " | " + detail
        };
    }

    private static int MatchBoundaries(InningsState innings) =>
        innings.Deliveries.Count(d => d.Outcome.RunsOffBat is 4 or 6);

    /// <summary>
    /// Toss decision. Weighted by what the conditions actually say rather than a fixed
    /// preference: a turning, deteriorating surface argues for batting first, a ground where
    /// chasing has historically worked argues for bowling, and dew under lights strongly favours
    /// chasing because the ball gets wet and the bowlers lose grip.
    /// </summary>
    public bool DecideToBat(MatchSetup setup, PitchConditions pitch, Random random,
        MatchLeadership? leadership = null, TacticalPlan? plan = null)
    {
        // The coach has told him what to do at the toss. The captain still walks out and makes the
        // call, but he makes the one he was told to make - and says so if he disagrees.
        if (plan?.TossDecision is { } instructed
            && plan.ResolveAuthority(InMatchDecision.Toss) is DecisionAuthority.CoachHasFinalSay or DecisionAuthority.Consult)
        {
            if (leadership is not null)
            {
                var call = leadership.Decide(_captaincy, InMatchDecision.Toss, coachHasAPlan: true, random,
                    plan.ResolveAuthority(InMatchDecision.Toss),
                    CaptaincyService.BuildSituationKey(setup.Format, MatchPhase.Powerplay, chasing: false, setup.BaseImportance));
                if (call.Suggestion is { } suggestion) leadership.Suggestions.Add(suggestion);
            }
            return instructed;
        }

        double batFirst = 50;

        // A pitch that will turn or break up gets worse to bat on - take first use of it.
        batFirst += (pitch.Spin - 50) * 0.25;
        batFirst += (50 - pitch.BattingFriendliness) * 0.15;

        if (setup.Ground is { } ground)
        {
            // Dew is the single biggest toss factor in day-night white-ball cricket.
            if (setup.EveningSession && ground.HasFloodlights)
                batFirst -= Math.Clamp(ground.DewTendency, 0, 100) * 0.35;
        }

        // Real captains have a mild, well-documented bias towards chasing in T20.
        if (setup.Format == MatchFormat.T20) batFirst -= 8;

        double probability = Math.Clamp(batFirst / 100.0, 0.1, 0.9);

        // A poor reader of conditions drifts towards a coin flip; a shrewd one follows what the
        // conditions actually say. This is one of the clearest places captaincy shows up.
        if (leadership is not null)
        {
            var call = leadership.Decide(_captaincy, InMatchDecision.Toss, coachHasAPlan: false, random,
                situationKey: CaptaincyService.BuildSituationKey(setup.Format, MatchPhase.Powerplay, chasing: false, setup.BaseImportance));
            double clarity = call.Quality / 100.0;
            probability = 0.5 + (probability - 0.5) * (0.25 + clarity * 0.85);
        }

        return random.NextDouble() < probability;
    }

    /// <summary>
    /// Builds the surface. Reads the ground's baseline, then applies deterioration and dew via
    /// GroundConditionsService - which Phase 3 built and left with no consumer.
    /// </summary>
    public PitchConditions BuildPitch(MatchSetup setup)
    {
        if (setup.Ground is not { } ground) return PitchConditions.Neutral;

        var basePitch = PitchConditions.FromGround(ground);

        double spin = _conditions.EstimateSpinAssistance(ground, setup.Format, setup.DayOfMatch, setup.EveningSession);

        // A multi-day surface loses its batting ease as it wears.
        double battingEase = setup.Format == MatchFormat.Test
            ? Math.Clamp(basePitch.BattingFriendliness - (setup.DayOfMatch - 1) * 4, 0, 100)
            : basePitch.BattingFriendliness;

        return basePitch with { Spin = spin, BattingFriendliness = battingEase };
    }

    private static MatchLeadership? LeadershipFor(MatchSetup setup, Guid teamId) =>
        teamId == setup.Home.TeamId ? setup.HomeLeadership
        : teamId == setup.Away.TeamId ? setup.AwayLeadership
        : null;

    private static TacticalPlan? PlanFor(MatchSetup setup, Guid teamId) =>
        teamId == setup.Home.TeamId ? setup.HomePlan
        : teamId == setup.Away.TeamId ? setup.AwayPlan
        : null;

    private MatchResult BuildResult(
        Guid matchId, MatchSetup setup, InningsState first, InningsState second, Guid tossWinnerId, bool choseToBat)
    {
        Guid? winner = null, loser = null;
        int? margin = null;
        bool byWickets = false;
        string summary;

        if (second.Runs > first.Runs)
        {
            // Chasing side won - the margin is wickets in hand, which is how cricket reports it.
            winner = second.BattingTeamId;
            loser = first.BattingTeamId;
            margin = 10 - second.Wickets;
            byWickets = true;
            summary = $"{second.BattingTeamName} beat {first.BattingTeamName} by {margin} wicket{(margin == 1 ? "" : "s")}";
        }
        else if (first.Runs > second.Runs)
        {
            winner = first.BattingTeamId;
            loser = second.BattingTeamId;
            margin = first.Runs - second.Runs;
            summary = $"{first.BattingTeamName} beat {second.BattingTeamName} by {margin} run{(margin == 1 ? "" : "s")}";
        }
        else
        {
            summary = $"{first.BattingTeamName} tied with {second.BattingTeamName}";
        }

        MatchOutcome resultForHome =
            winner is null ? MatchOutcome.Tie
            : winner == setup.Home.TeamId ? MatchOutcome.Win
            : MatchOutcome.Loss;

        return new MatchResult
        {
            MatchId = matchId,
            Setup = setup,
            FirstInnings = first,
            SecondInnings = second,
            TossWinnerTeamId = tossWinnerId,
            TossWinnerChoseToBat = choseToBat,
            WinningTeamId = winner,
            LosingTeamId = loser,
            ResultForHome = resultForHome,
            WinMargin = margin,
            WonByWickets = byWickets,
            Summary = $"{summary} ({first.Runs}/{first.Wickets} v {second.Runs}/{second.Wickets})"
        };
    }

    /// <summary>
    /// Section D follow-up: closes the loop on situational pattern memory - and, in the same pass,
    /// on CaptaincyProfile.RecordMatch itself, which was dead code (confirmed by grep before this
    /// change - nothing anywhere called it, despite MatchesCaptained/DressingRoomBacking existing
    /// specifically to be updated by it). Both updates only ever touch the CaptaincyProfile object
    /// the caller already owns (via setup.HomeLeadership/AwayLeadership) - there is no persistence
    /// step here, matching how every other field on that object already works.
    /// </summary>
    private void RecordCaptaincyOutcomes(MatchSetup setup, MatchResult result, Random random)
    {
        if (result.WinningTeamId is null && result.ResultForHome != MatchOutcome.Tie) return; // a genuine no-result teaches nothing

        foreach (var (leadership, teamId, opponentId) in new[]
        {
            (setup.HomeLeadership, setup.Home.TeamId, setup.Away.TeamId),
            (setup.AwayLeadership, setup.Away.TeamId, setup.Home.TeamId)
        })
        {
            if (leadership is null) continue;

            double teamStrength = SideStrength(teamId == setup.Home.TeamId ? setup.Home : setup.Away);
            double opponentStrength = SideStrength(teamId == setup.Home.TeamId ? setup.Away : setup.Home);
            var story = _resultContext.Classify(result, teamId, teamStrength, opponentStrength);

            bool won = result.WinningTeamId == teamId;
            bool handledWell = story is not (MatchResultStory.HeavyLoss or MatchResultStory.CollapseLoss);
            leadership.Profile.RecordMatch(won, handledWell);

            double rating = story switch
            {
                MatchResultStory.DominantWin => 70,
                MatchResultStory.ComebackWin => 55,
                MatchResultStory.UpsetWin => 60,
                MatchResultStory.NarrowWin => 35,
                MatchResultStory.OrdinaryWin => 25,
                MatchResultStory.HeavyLoss => -70,
                MatchResultStory.CollapseLoss => -55,
                MatchResultStory.CloseLoss => -25,
                _ => -15
            };

            // Wave 6: momentum feeds the captain's read - a side that finished genuinely on top
            // of the game reflects well on the man making the calls; one that let a winning
            // position slip does not.
            double lastInningsMomentum = result.SecondInnings.Momentum.Value; // + = the last batting side was on top
            double fromThisSidesView = teamId == result.SecondInnings.BattingTeamId ? lastInningsMomentum : -lastInningsMomentum;
            rating += Math.Clamp(fromThisSidesView * 0.15, -12, 12);

            // Wave 6 (suggestion): the captain's own redemption arc - the mirror of a player's
            // Wave 2 PressureMoment. A heavy loss as the FAVOURITE is a public misjudgement that
            // hangs over him; a subsequent big win answers it, with a real lift.
            var profile = leadership.Profile;
            bool wasFavourite = teamStrength > opponentStrength + 8;
            if (story is MatchResultStory.HeavyLoss or MatchResultStory.CollapseLoss && wasFavourite)
            {
                profile.RecordBoldFailure(setup.MatchDate, 55);
            }
            else if (profile.PendingBoldFailure is not null
                     && story is MatchResultStory.DominantWin or MatchResultStory.ComebackWin or MatchResultStory.UpsetWin)
            {
                double composure = AbilityScale.AttributeToHundred(leadership.Captain.Mental.PressureHandling) / 100.0;
                rating += 15;
                if (random.NextDouble() < 0.3 + composure * 0.4)
                    leadership.Captain.Mental.DecisionMaking = Math.Min(20, leadership.Captain.Mental.DecisionMaking + 1);
                if (random.NextDouble() < 0.15 + composure * 0.3)
                    leadership.Captain.Mental.PressureHandling = Math.Min(20, leadership.Captain.Mental.PressureHandling + 1);
                profile.ClearBoldFailure();
            }

            // Wave 4: bank this match's decision-quality reading for the monthly captain-growth
            // crystallisation. The vice-captain banks a smaller fraction of the same signal.
            leadership.Profile.AccumulateDecisionQuality(rating);
            leadership.ViceCaptainProfile?.AccumulateDecisionQuality(rating, weight: 0.35);

            // Post-Phase-6 carry-forward: the XI's trust in the captain moves on how his calls
            // actually went and on the result - a small, bounded per-match nudge.
            CaptaincyService.ApplyCaptainTrust(
                (teamId == setup.Home.TeamId ? setup.Home : setup.Away).BattingOrder, rating, won);

            bool chased = result.InningsFor(teamId).InningsNumber == 2;
            string situationKey = CaptaincyService.BuildSituationKey(
                setup.Format, MatchPhase.DeathOvers, chased, pressureLevel: 70);
            _captaincy.RecordSituationOutcome(leadership.Captain, situationKey, rating);
        }
    }

    private static double SideStrength(MatchSide side) =>
        side.BattingOrder.Count == 0 ? 50 : side.BattingOrder.Average(p => AbilityScale.CompositeAbilityToHundred(p.CurrentAbility));
}
