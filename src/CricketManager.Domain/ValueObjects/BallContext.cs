using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Everything that bears on one delivery. Section 18 lists what a ball should consider; this is
/// that list made concrete, minus the parts a later slice supplies (field placement, explicit
/// bowling plans, weather) which default to neutral so they can be added without reshaping this.
///
/// Passed as one object rather than fifteen parameters because the ball model is the single
/// hottest path in the game - a 30-year career simulates tens of millions of deliveries - and
/// because a parameter list that long is impossible to call correctly.
/// </summary>
public sealed record BallContext
{
    public required Player Striker { get; init; }
    public required Player Bowler { get; init; }

    /// <summary>
    /// Who is backing up at the non-striker's end, when known. Optional - null degrades to the
    /// neutral, no-partnership-history behaviour the model always had - but supplied in real play
    /// so run-out risk and strike rotation can read this specific pair's shared history (Section
    /// Q/W - see PartnershipChemistryService).
    /// </summary>
    public Player? NonStriker { get; init; }

    public MatchFormat Format { get; init; } = MatchFormat.T20;
    public MatchPhase Phase { get; init; } = MatchPhase.MiddleOvers;

    /// <summary>How many balls the striker has already faced in this innings. Getting set matters - a batter on 0 from 1 ball is far more likely to get out than the same batter on 40 from 30.</summary>
    public int StrikerBallsFaced { get; init; }
    public int StrikerRuns { get; init; }

    /// <summary>Balls the bowler has already sent down in this spell. Fatigue degrades accuracy and pace.</summary>
    public int BowlerBallsInSpell { get; init; }
    public int BowlerBallsInMatch { get; init; }

    public int WicketsFallen { get; init; }
    public int BallsRemainingInInnings { get; init; }

    /// <summary>Null in the first innings. Set when chasing, which is what creates required-rate pressure.</summary>
    public int? RunsRequired { get; init; }

    public BattingIntent BattingIntent { get; init; } = BattingIntent.Normal;
    public BowlingIntent BowlingIntent { get; init; } = BowlingIntent.Balanced;

    /// <summary>0-100 pitch characteristics for this ball, already adjusted for wear/day/session by the caller.</summary>
    public double PitchPace { get; init; } = 50;
    public double PitchSpin { get; init; } = 50;
    public double PitchBounce { get; init; } = 50;
    public double PitchBattingFriendliness { get; init; } = 50;

    /// <summary>0-100. Drives how much the occasion tells - fed to SituationalPerformanceModifier.</summary>
    public double PressureLevel { get; init; } = 50;

    /// <summary>Balls bowled with this ball since it was new. Governs swing early and reverse late.</summary>
    public int BallAge { get; init; }

    public int TotalScore { get; init; }

    /// <summary>The fielding XI, so catches, run-outs and stumpings can be credited to a real player rather than vanishing. Optional - the model degrades to uncredited dismissals without it.</summary>
    public IReadOnlyList<Player>? FieldingSide { get; init; }

    /// <summary>
    /// The venue. Optional so the model can be tested at a neutral ground, but supplied by the
    /// match simulator in real play - it is what makes boundary dimensions, outfield speed and
    /// altitude affect scoring, which is exactly what Phase 3 added those fields for.
    /// </summary>
    public Entities.Ground? Ground { get; init; }

    /// <summary>
    /// Where the fielders actually are. Optional - a neutral field is assumed when absent - but
    /// supplied in real play, and it is what makes a captain's placement change outcomes:
    /// boundary protection, ring pressure on singles, and whether an edge carries to a cordon or
    /// runs away through the gap.
    /// </summary>
    public FieldSetting? Field { get; init; }

    /// <summary>
    /// What the bowler actually delivered - the coach's plan after the bowler's execution and his
    /// own judgement have been applied. Null means no plan was in force and the ball model behaves
    /// as it did before plans existed.
    /// </summary>
    public ExecutedDelivery? Delivery { get; init; }

    /// <summary>
    /// The last few (line, length) pairs this bowler has actually bowled at this striker, oldest
    /// first - Sections B/C (bowling-plan sequencing / batter pattern-reading). Null/empty means
    /// there is no established pattern yet to read, which is the same as omitting it. Sourced
    /// from SpellReadout.RecentDeliveries, kept as a narrower field here because BallContext only
    /// needs the pattern itself, not the rest of SpellReadout's aggregate figures.
    /// </summary>
    public IReadOnlyList<(BowlingLine Line, BowlingLength Length)>? RecentDeliveryPattern { get; init; }

    /// <summary>
    /// How the striker is going right now - set-ness, confidence, fatigue. Null falls back to the
    /// simple balls-faced approximation the model used before match state existed.
    /// </summary>
    public BatterMatchState? StrikerState { get; init; }

    /// <summary>How the bowler is going - rhythm, confidence, fatigue.</summary>
    public BowlerMatchState? BowlerState { get; init; }

    /// <summary>
    /// The lift the batting side's captain gives his team-mates, as a multiplier. Small on purpose -
    /// a great captain is worth a few percent across eleven players, which is a match over a season
    /// and never a match on its own.
    /// </summary>
    public double CaptainLift { get; init; } = 1.0;

    /// <summary>
    /// Phase 6, Slice 6.1: the home-side on-field edge, as a multiplier on effective skill.
    /// BattingHomeEdge is &gt; 1.0 when the batting side is at home, BowlingHomeEdge when the
    /// bowling side is; both are 1.0 at a neutral venue (the default, and every pre-existing
    /// caller). Deliberately the same shape and the same "small, never decisive" discipline as
    /// CaptainLift - crowd, familiar conditions and a united dressing room are worth a few percent,
    /// not a result on their own.
    /// </summary>
    public double BattingHomeEdge { get; init; } = 1.0;
    public double BowlingHomeEdge { get; init; } = 1.0;

    /// <summary>
    /// Phase 7, Slice 7.9: the umpiring panel's lean on the marginal decision, as a multiplier on
    /// the LBW and caught-behind shares of a wicket. 1.0 (the default and every pre-Phase-7
    /// caller) = a neutral, top-class panel; above 1.0 = a weaker or more rattled panel giving
    /// more of the close ones out, widened further when the home crowd is loud. Deliberately a
    /// deterministic multiplier - it consumes NO new random draw - so a null umpire panel is
    /// byte-identical to the pre-Phase-7 model, and a supplied one still reruns identically from
    /// the same seed.
    /// </summary>
    public double UmpireOutBias { get; init; } = 1.0;

    /// <summary>
    /// Phase 14 (§18.1, in-match): a multiplier on the run-out share of a wicket when the two
    /// batters at the crease have a genuine FEUD in the relationship graph - the calling is sharp,
    /// the backing-up hesitant, and a mix-up is likelier. 1.0 (the default and every pre-Phase-14
    /// caller) = no friction. Bounded and small; consumes no new random draw.
    /// </summary>
    public double PairRunOutExtra { get; init; } = 1.0;

    /// <summary>Phase 15 (§19.6): wind speed (kph) and the bearing it blows toward (0-360). Above ~20 kph the downwind boundary plays shorter and the upwind one longer - a small, roughly-net-zero asymmetry applied when a boundary is resolved. 0/0 (the default) = no wind effect.</summary>
    public double WindSpeedKph { get; init; }
    public double WindBearing { get; init; }

    /// <summary>
    /// Phase 15 (§16.6): a TV / third umpire is available for line calls (a run-out or a stumping
    /// goes upstairs) even though full DRS is not in use - true for first-class and most domestic
    /// competitions. A few of the tightest run-outs / stumpings that the on-field umpire would have
    /// given are overturned on the replay, so those dismissals are marginally rarer. Deterministic
    /// multiplier, no new random draw. False (the default) = no TV umpire, exactly as before.
    /// </summary>
    public bool HasThirdUmpire { get; init; }

    /// <summary>
    /// Phase 15 (§1.7): the fielding side has DRS reviews available. When true AND the panel is
    /// weak (<see cref="UmpireOutBias"/> above 1.0), a small DETERMINISTIC clawback is applied to
    /// the marginal LBW / caught-behind shares of a wicket - the fielding side reclaiming some of
    /// what a cautious umpire turned down. Consumes no random draw. False (the default) = no DRS,
    /// byte-identical to before.
    /// </summary>
    public bool FieldingHasDrs { get; init; }

    /// <summary>
    /// A free hit, following a no-ball in limited-overs cricket. The batter cannot be dismissed
    /// except by running himself out, so he swings at everything - and the model must reflect BOTH
    /// halves of that: no dismissal, and a genuine attempt to maximise runs whatever his intent was.
    /// Not a thing in first-class cricket, which is why it lives on the context rather than being
    /// assumed.
    /// </summary>
    public bool IsFreeHit { get; init; }

    /// <summary>0-100. How hard this pitch is to settle on. A difficult surface can stop a batter ever feeling in, however many runs he makes.</summary>
    public double PitchDifficulty { get; init; }

    /// <summary>The striker's scoring zones. Used to weight where a shot goes and therefore how much the field placement is actually worth.</summary>
    public BattingZoneStrengths? StrikerZones { get; init; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 6: in-match momentum from the BATTING side's
    /// perspective (-100 bowling side utterly on top .. +100 batting side flying), or null when
    /// no momentum is being tracked (every pre-existing caller). A genuine SWING (|value| above
    /// the gate inside MatchMomentum) gives the side that has it a small, capped lift - secondary
    /// to set-ness, form and morale, never decisive on its own.
    /// </summary>
    public double? Momentum { get; init; }
}

/// <summary>
/// What happened on one delivery, in full. Deliberately records the bowler and fielder credited
/// as well as the runs, because a scorecard is not enough: matchup history, honour boards,
/// dismissal patterns and fielding records all need to know who did what.
/// </summary>
public sealed record DeliveryOutcome
{
    public DeliveryOutcomeType Type { get; init; }

    /// <summary>Runs credited to the batter (0 for extras, byes and leg-byes).</summary>
    public int RunsOffBat { get; init; }

    /// <summary>Runs credited to the team but not the batter - wides, no-balls, byes, leg-byes.</summary>
    public int ExtraRuns { get; init; }

    public int TotalRuns => RunsOffBat + ExtraRuns;

    /// <summary>False for wides and no-balls - they do not count as a ball faced or a legal delivery.</summary>
    public bool IsLegalDelivery { get; init; } = true;

    /// <summary>True when the striker faced it, i.e. it counts towards balls faced. A wide is not faced; a no-ball is.</summary>
    public bool CountsAsBallFaced { get; init; } = true;

    public DismissalType Dismissal { get; init; } = DismissalType.NotOut;

    /// <summary>The bowler gets credit for everything except a run-out.</summary>
    public bool WicketCreditedToBowler => Dismissal is not (DismissalType.NotOut or DismissalType.RunOut or DismissalType.Retired);

    /// <summary>Who took the catch, effected the run-out or made the stumping, when a fielder was involved.</summary>
    public Guid? FielderId { get; init; }

    /// <summary>
    /// Which wagon-wheel zone a FOUR actually went to, when one was resolved - Section R (fielding
    /// bait-and-trap). Null for everything else, including a six: BallOutcomeModel deliberately
    /// does not roll a zone for a six, since that would consume an extra random draw on every one
    /// and reshuffle the rest of the match's random stream for a signal the trap-reading does not
    /// need sixes to work - fours alone are a common enough attacking signal on their own. This is
    /// a real, observed signal, not the batter's a-priori BattingZoneStrengths profile - it is what
    /// actually happened on this specific ball, which is what lets a captain notice a live pattern
    /// rather than only ever reading a static ability rating.
    /// </summary>
    public ShotZone? Zone { get; init; }

    /// <summary>True when the run-out or dismissal removed the NON-striker rather than the striker.</summary>
    public bool NonStrikerDismissed { get; init; }

    public bool IsWicket => Dismissal != DismissalType.NotOut;

    /// <summary>Odd runs off the bat change the strike; so do byes and leg-byes run in odd numbers.</summary>
    public bool ChangesStrike => (RunsOffBat + (Type is DeliveryOutcomeType.Bye or DeliveryOutcomeType.LegBye ? ExtraRuns : 0)) % 2 == 1;

    public static DeliveryOutcome Dot() => new() { Type = DeliveryOutcomeType.DotBall };

    public static DeliveryOutcome Runs(int runs) => new()
    {
        Type = runs switch { 4 => DeliveryOutcomeType.Boundary4, 6 => DeliveryOutcomeType.Boundary6, _ => DeliveryOutcomeType.RunsOffBat },
        RunsOffBat = runs
    };

    public static DeliveryOutcome Wicket(DismissalType dismissal, Guid? fielderId = null, int runsOffBat = 0, bool nonStriker = false) => new()
    {
        Type = DeliveryOutcomeType.Wicket,
        Dismissal = dismissal,
        FielderId = fielderId,
        RunsOffBat = runsOffBat,
        NonStrikerDismissed = nonStriker
    };

    public static DeliveryOutcome Wide(int additionalRuns = 0) => new()
    {
        Type = DeliveryOutcomeType.Wide,
        ExtraRuns = 1 + additionalRuns,
        IsLegalDelivery = false,
        CountsAsBallFaced = false
    };

    public static DeliveryOutcome NoBall(int runsOffBat = 0) => new()
    {
        Type = DeliveryOutcomeType.NoBall,
        ExtraRuns = 1,
        RunsOffBat = runsOffBat,
        IsLegalDelivery = false,
        CountsAsBallFaced = true // the batter did face it, and can score off it
    };

    public static DeliveryOutcome Byes(int runs, bool legByes) => new()
    {
        Type = legByes ? DeliveryOutcomeType.LegBye : DeliveryOutcomeType.Bye,
        ExtraRuns = runs
    };
}
