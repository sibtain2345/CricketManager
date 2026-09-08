using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>One batter's live card. Phase-split runs are tracked as they happen because reconstructing them afterwards from a ball log is both slower and lossy.</summary>
public sealed class BatterCard
{
    public required Guid PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public required int BattingPosition { get; init; }

    public int Runs { get; set; }
    public int BallsFaced { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }

    public int PowerplayRuns { get; set; }
    public int PowerplayBalls { get; set; }
    public int MiddleOversRuns { get; set; }
    public int MiddleOversBalls { get; set; }
    public int DeathOversRuns { get; set; }
    public int DeathOversBalls { get; set; }

    public DismissalType Dismissal { get; set; } = DismissalType.NotOut;
    public Guid? DismissedByBowlerId { get; set; }
    public Guid? FielderId { get; set; }

    /// <summary>Has batted at all. A player who never came to the crease is "did not bat", which is different from "not out 0".</summary>
    public bool HasBatted { get; set; }

    public bool IsOut => Dismissal is not (DismissalType.NotOut or DismissalType.Retired);
    public double StrikeRate => BallsFaced == 0 ? 0 : (double)Runs / BallsFaced * 100;
}

/// <summary>One bowler's live figures.</summary>
public sealed class BowlerCard
{
    public required Guid PlayerId { get; init; }
    public required string PlayerName { get; init; }

    public int LegalBallsBowled { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int Maidens { get; set; }
    public int Wides { get; set; }
    public int NoBalls { get; set; }

    /// <summary>Balls in the current unbroken spell - reset when the bowler is taken off. Drives fatigue.</summary>
    public int BallsInCurrentSpell { get; set; }

    /// <summary>
    /// The over number this bowler last bowled, or -1 if he hasn't. Needed to tell a CONTINUING
    /// spell from a fresh one: because nobody may bowl consecutive overs, a bowler carrying on
    /// bowls every second over, so "did he bowl the over before last" is the real test. Comparing
    /// against the immediately previous over's bowler can never be true and silently reset every
    /// spell to zero.
    /// </summary>
    public int LastOverBowled { get; set; } = -1;

    public double Overs => LegalBallsBowled / 6 + LegalBallsBowled % 6 / 10.0;
    public double Economy => LegalBallsBowled == 0 ? 0 : RunsConceded / (LegalBallsBowled / 6.0);
    public string Figures => $"{Wickets}/{RunsConceded}";
}

/// <summary>A completed partnership. Section 14 asks for partnership records; they are recorded as they break, because after the innings the information is gone.</summary>
public sealed record Partnership(int WicketNumber, Guid BatterAId, Guid BatterBId, int Runs, int Balls);

/// <summary>Where a wicket fell - the "34/3" line on a scorecard.</summary>
public sealed record FallOfWicket(int WicketNumber, int Score, int BallNumber, Guid BatterOutId);

/// <summary>
/// The live state of one innings. Deliberately mutable and single-purpose: the simulator advances
/// it ball by ball, and it is converted into immutable records once the innings ends.
///
/// It holds everything a scorecard shows AND everything the statistics systems later need, so
/// that nothing has to be reconstructed after the fact - reconstruction is where detail gets lost.
/// </summary>
public sealed class InningsState
{
    public required Guid BattingTeamId { get; init; }
    public required Guid BowlingTeamId { get; init; }
    public required string BattingTeamName { get; init; }
    public required string BowlingTeamName { get; init; }
    public required MatchFormat Format { get; init; }
    public int InningsNumber { get; init; } = 1;

    /// <summary>Total legal balls available. Null for a Test innings, which ends on wickets, declaration or time rather than a ball count.</summary>
    public int? MaxLegalBalls { get; init; }

    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int LegalBalls { get; set; }
    public int Extras { get; set; }

    /// <summary>Target to win. Set only when chasing, and it is what turns a scoreboard into pressure.</summary>
    public int? Target { get; set; }

    public List<BatterCard> BatterCards { get; } = new();
    public List<BowlerCard> BowlerCards { get; } = new();
    public List<Partnership> Partnerships { get; } = new();
    public List<FallOfWicket> FallOfWickets { get; } = new();

    /// <summary>Every delivery, in order. The raw material for commentary, wagon wheels, dismissal patterns and post-match analysis.</summary>
    public List<DeliveryRecord> Deliveries { get; } = new();

    public Guid? StrikerId { get; set; }
    public Guid? NonStrikerId { get; set; }
    public Guid? CurrentBowlerId { get; set; }
    public Guid? PreviousBowlerId { get; set; }

    public int CurrentPartnershipRuns { get; set; }
    public int CurrentPartnershipBalls { get; set; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 6: in-match momentum from this batting side's
    /// perspective - see MatchMomentum. Fresh at the start of every innings, updated ball by ball
    /// and over by over by InningsSimulator, and (in multi-day cricket) modulated on session
    /// breaks. Read into BallContext.Momentum for its small, gated feedback into ball outcomes,
    /// and by RecordCaptaincyOutcomes for whether the captain's side finished on top of the game.
    /// </summary>
    public MatchMomentum Momentum { get; } = new();

    /// <summary>
    /// Who was dismissed by the most recent wicket. Recorded explicitly because the incoming
    /// batter must replace THAT player - searching the card list for "the last one who is out"
    /// returns the highest batting position who has been dismissed, not the most recent
    /// dismissal, and on a non-striker run-out that put the new batter at the wrong end and
    /// left a dismissed card still on strike.
    /// </summary>
    public Guid? LastDismissedPlayerId { get; set; }

    /// <summary>
    /// Phase 15 (§1.8): batters who have RETIRED HURT (a blow to the body, not a dismissal) and have
    /// not yet returned. On the fall of a later wicket, if the specialist order is exhausted, a
    /// retired-hurt batter comes back rather than the innings ending. His card shows
    /// <see cref="Enums.DismissalType.Retired"/> until he resumes.
    /// </summary>
    public List<Guid> RetiredHurtIds { get; } = new();

    /// <summary>
    /// Phase 15 (§1.7 - DRS): how the batting side's reviews went this innings. Only ever non-zero
    /// when DRS was in force for the fixture. DrsOverturns is the count of given-out decisions the
    /// batting side successfully overturned (a wicket saved); DrsStruckDown is the reviews it burned
    /// on a decision that stood; DrsUmpiresCall is a review retained because the ball-tracking was
    /// "umpire's call".
    /// </summary>
    public int DrsOverturns { get; set; }
    public int DrsStruckDown { get; set; }
    public int DrsUmpiresCall { get; set; }

    public bool IsDeclared { get; set; }
    public bool IsAllOut => Wickets >= 10;
    public bool BallsExhausted => MaxLegalBalls is { } max && LegalBalls >= max;

    /// <summary>True when the chasing side has passed the target - the innings ends the instant it happens, mid-over.</summary>
    public bool TargetReached => Target is { } target && Runs >= target;

    public bool IsComplete => IsAllOut || BallsExhausted || IsDeclared || TargetReached;

    public double RunRate => LegalBalls == 0 ? 0 : Runs / (LegalBalls / 6.0);

    public int? RunsRequired => Target is { } target ? Math.Max(0, target - Runs) : null;
    public int? BallsRemaining => MaxLegalBalls is { } max ? Math.Max(0, max - LegalBalls) : null;

    public double? RequiredRunRate =>
        RunsRequired is { } required && BallsRemaining is { } balls && balls > 0
            ? required / (balls / 6.0)
            : null;

    public BatterCard? Striker => BatterCards.FirstOrDefault(c => c.PlayerId == StrikerId);
    public BatterCard? NonStriker => BatterCards.FirstOrDefault(c => c.PlayerId == NonStrikerId);
    public BowlerCard? CurrentBowler => BowlerCards.FirstOrDefault(c => c.PlayerId == CurrentBowlerId);

    public string ScoreLine => $"{Runs}/{Wickets} ({LegalBalls / 6}.{LegalBalls % 6} ov)";

    public void SwapStrike() => (StrikerId, NonStrikerId) = (NonStrikerId, StrikerId);
}

/// <summary>One delivery as it happened, kept in full. This is the ball-by-ball log that partnership records, dismissal patterns, wagon wheels and commentary all read from - none of which are reconstructable from a scorecard.</summary>
public sealed record DeliveryRecord(
    int BallNumber,
    int OverNumber,
    int BallInOver,
    Guid StrikerId,
    Guid BowlerId,
    MatchPhase Phase,
    DeliveryOutcome Outcome,
    int ScoreAfter,
    int WicketsAfter,
    /// <summary>What was actually bowled - null only when no ExecutedDelivery existed for this ball (a wide/no-ball's re-bowl path, or a test context with no plan). Added for bowling-plan sequencing/batter pattern-reading (Sections B/C): the ball-by-ball log previously carried outcomes but not deliveries, so nothing could answer "what has this bowler actually been bowling."</summary>
    BowlingLine? Line = null,
    BowlingLength? Length = null,
    /// <summary>Which day of a multi-day match this ball was bowled on. Null for limited-overs cricket (a single-day format has no such question) and was ALWAYS null for multi-day cricket too before MatchTimeline existed - InningsState genuinely had no notion of the calendar day a delivery fell on, which is why every multi-day key moment used to print "[Day ?]".</summary>
    int? Day = null,
    /// <summary>Morning/Afternoon/Evening/ExtraHalfHour - null on the same terms as Day.</summary>
    SessionType? Session = null,
    /// <summary>The clock's local time when this ball was bowled - multi-day only, same null convention.</summary>
    TimeOnly? ClockTime = null);
