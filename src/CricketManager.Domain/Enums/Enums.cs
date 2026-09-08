namespace CricketManager.Domain.Enums;

public enum MatchFormat
{
    Test,
    ODI,
    T20
}

public enum BattingHand
{
    Right,
    Left
}

public enum BowlingStyle
{
    RightArmFast,
    RightArmFastMedium,
    RightArmMediumFast,
    RightArmMedium,
    RightArmOffSpin,
    RightArmLegSpin,
    LeftArmFast,
    LeftArmFastMedium,
    LeftArmMediumFast,
    LeftArmMedium,
    LeftArmOrthodox,
    LeftArmChinaman,
    None
}

public enum PlayerRole
{
    Batsman,
    BowlingAllrounder,
    BattingAllrounder,
    Bowler,
    WicketKeeper
}

// Section 12: player personality traits. Flags so a player can hold several at once.
[Flags]
public enum PersonalityTrait
{
    None = 0,
    Professional = 1 << 0,
    Lazy = 1 << 1,
    Leader = 1 << 2,
    BigMatchPlayer = 1 << 3,
    PressurePlayer = 1 << 4,
    InjuryProne = 1 << 5,
    Aggressive = 1 << 6,
    Defensive = 1 << 7,
    RiskTaker = 1 << 8,
    Consistent = 1 << 9,
    Inconsistent = 1 << 10,
    FastLearner = 1 << 11,
    SlowDeveloper = 1 << 12,
    Ambitious = 1 << 13,
    Loyal = 1 << 14,
    MoneyFocused = 1 << 15,
    TeamOriented = 1 << 16
}

// Section 79: playing experience tier, used to seed a coach's starting profile
public enum PlayingExperience
{
    NeverPlayedProfessionally,
    Amateur,
    DomesticLevel,
    FirstClass,
    International,
    InternationalStar,
    FormerCaptain
}

// Section 80: coaching qualification ladder
public enum CoachingLicense
{
    None,
    Basic,
    Level1,
    Level2,
    Level3,
    Advanced,
    InternationalElite
}

public enum CoachingPhilosophy
{
    YouthDevelopment,
    Aggressive,
    Defensive,
    AnalyticsDriven,
    ExperienceFocused,
    PerformanceFocused,
    ReputationFocused,
    LongTermDevelopment,
    ShortTermResults,
    FitnessFocused,
    TacticalFlexibility,
    /// <summary>Phase 12 (§4.6): values balance and flexibility - genuinely prefers a good allrounder to a marginally better specialist.</summary>
    AllrounderLeaning
}

public enum ContractStatus
{
    Unemployed,
    Active,
    Expired,
    Terminated,
    /// <summary>The coach's own choice to leave, distinct from Terminated (the club's choice). See CoachCareerService.EvaluateSatisfaction.</summary>
    Resigned
}

// --- Roles: WHERE/HOW a player is used. Separate from Traits (WHAT a player is naturally good at). ---

public enum BattingRole
{
    Opener,
    TopOrder,
    MiddleOrder,
    LowerOrder,
    Tailender
}

/// <summary>
/// Post-Phase-6 (section D): how a team splits the captaincy across formats. Real cricket has all
/// four of these, so the model supports all four rather than forcing one shape.
/// </summary>
/// <summary>
/// Seven-suggestions pass (S2): how a national board splits (or does not split) its HEAD-COACH
/// job across formats - the direct mirror of <see cref="CaptaincyPattern"/>. Real and cyclical:
/// England split into a Test coach + a white-ball coach in 2022 and reunified under one man in
/// 2025 because "constant clashes between formats" made the split hard to run.
/// </summary>
public enum CoachingStructure
{
    /// <summary>One head coach across all formats (the default - every nation seeds this).</summary>
    Unified,
    /// <summary>A Test head coach and a separate white-ball (ODI + T20) head coach.</summary>
    RedBallWhiteBall,
    /// <summary>A Test + ODI head coach and a separate T20 head coach.</summary>
    LongFormShortForm,
    /// <summary>A separate head coach for each of Test, ODI and T20.</summary>
    ThreeSeparate
}

public enum CaptaincyPattern
{
    /// <summary>One captain across Test, ODI and T20.</summary>
    Unified,
    /// <summary>One captain for the long format (Test), another for both white-ball formats (ODI + T20).</summary>
    RedBallWhiteBall,
    /// <summary>One captain for the "red-ball-adjacent" formats (Test + ODI), another for T20.</summary>
    LongFormShortForm,
    /// <summary>A separate captain for each of the three formats.</summary>
    ThreeSeparate
}

public enum BowlingRoleType
{
    OpeningBowler,
    FirstChange,
    MiddleOversSpecialist,
    DeathBowler,
    SpecialistSpinner,
    PartTimeBowler,
    NotABowler
}

public enum MatchPhase
{
    Powerplay,
    MiddleOvers,
    DeathOvers
}

public enum HomeAwayNeutral
{
    Home,
    Away,
    Neutral
}

public enum InningsRole
{
    BattingFirst,
    Chasing
}

/// <summary>
/// Distinguishes WHERE a match sits in the competition hierarchy. Combined with
/// MatchFormat (Test/ODI/T20 = the underlying game format), this is what lets
/// "First-Class" (all Test-format matches, international + domestic) and "List A"
/// (all ODI-format matches, international + domestic) exist as broader buckets that
/// contain the pure international record, without needing separate format enums.
/// </summary>
public enum CompetitionScope
{
    International,
    DomesticFirstClass,
    DomesticListA,
    DomesticT20,
    FranchiseLeague
}

/// <summary>
/// How a competition is structured/progresses. Kept separate from CompetitionScope
/// (which is about international-vs-domestic-vs-franchise classification for stats
/// hierarchy purposes) - this is purely about tournament shape, so a World Cup
/// (International, GroupStageKnockout) and a domestic T20 cup (DomesticT20,
/// LeagueWithPlayoffs) can both exist without conflating the two concepts.
/// </summary>
public enum CompetitionStructureType
{
    League,                 // round-robin, table position alone decides the winner
    LeagueWithPlayoffs,     // round-robin -> top N qualify -> playoffs/semifinals -> final
    GroupStageKnockout,     // groups -> knockout bracket (typical World Cup shape)
    Knockout,                // straight knockout, no group stage
    FirstClassChampionship  // multi-day round-robin, often with bonus-point tiebreaks
}

/// <summary>A scheduled fixture's own lifecycle, independent of the eventual MatchOutcome the match itself produces.</summary>
public enum FixtureStatus
{
    Scheduled,
    Completed,
    Cancelled
}

/// <summary>
/// Which shape a LeagueWithPlayoffs competition's top-N playoff stage takes. Deliberately
/// separate from CompetitionStructureType (a tournament SHAPE) - this is a second, narrower
/// choice that only matters once that shape is "has playoffs" at all.
/// </summary>
public enum PlayoffFormat
{
    /// <summary>Standard bracket seeding (1vN, 2v(N-1), ...), single elimination. One bad day and a season is over - the simpler, harsher format.</summary>
    StraightKnockout,

    /// <summary>The format most major T20 leagues actually use for a top-4 finish: Qualifier 1
    /// (1v2), Eliminator (3v4), Qualifier 2 (loser of Q1 vs winner of Eliminator), Final. Exists
    /// specifically so the two best teams over a whole season aren't knocked out by one bad game
    /// the way a straight knockout would allow - the loser of Qualifier 1 gets a second chance.</summary>
    IplStyle
}

/// <summary>
/// Section 31: injuries need a severity that means something mechanically, not a label.
/// Severity drives expected layoff length and how much it degrades a player who plays
/// through it - a Niggle is a "can play, shouldn't" decision for the coach, a Serious
/// injury takes the player out of selection entirely.
/// </summary>
public enum InjurySeverity
{
    None,
    Niggle,       // days; playable at reduced effectiveness if the coach risks it
    Minor,        // 1-3 weeks
    Moderate,     // 3-8 weeks
    Serious,      // 2-6 months
    CareerThreatening
}

/// <summary>
/// The injury types the spec names. Type matters separately from severity because the same
/// severity affects different players differently: a shoulder problem is far worse for a
/// fast bowler than for a batter, and a concussion has its own mandatory stand-down rules.
/// </summary>
public enum InjuryType
{
    None,
    Hamstring,
    BackInjury,
    ShoulderInjury,
    AnkleInjury,
    KneeInjury,
    MuscleStrain,
    SideStrain,
    StressFracture,
    Concussion,
    Illness
}

/// <summary>Why a player can't be picked. Kept as its own enum so "injured" and "on national duty" aren't collapsed into one boolean, which is exactly the distinction NOC/central-contract logic will need later.</summary>
public enum UnavailabilityReason
{
    Available,
    Injured,
    Suspended,
    InternationalDuty,
    Unregistered,   // not registered for this competition (overseas slots, eligibility)
    Retired,

    /// <summary>SquadManagementService's "roadmap" outcome - sent back to domestic cricket with an explicit return path, distinct from injury/suspension. Same single-gate simplification InternationalDuty/Suspended already carry: this blocks ALL selection today, not just the international side, since nothing in the availability model is competition-scoped yet.</summary>
    OnDevelopmentAssignment,

    /// <summary>SquadManagementService's "rest" outcome - a deliberate break to reset technically/mentally (the real-world "a month off, then back" case), distinct from a roadmap (no domestic reassignment) and from injury (nothing physically wrong).</summary>
    Resting,

    /// <summary>Phase 14 (§18.4): an off-field life event - a birth, a bereavement, a visa/legal issue. Cleared when Player.PersonalLeaveUntil passes.</summary>
    PersonalLeave,

    /// <summary>Post-Phase-16 completion pass (§9.2): the player is refusing to play until his contract is renegotiated. Cleared by PlayerContractService.EvaluateRenewal when the club improves his deal.</summary>
    ContractHoldout
}

/// <summary>
/// What an infrastructure project actually builds. Kept as one enum across team-level and
/// ground-level work because they share the same lifecycle (quoted -> funded -> under
/// construction -> completed) and the same fundamental consequence: it costs money now, it
/// pays off later, and the upkeep bill goes up permanently afterwards.
/// </summary>
public enum InfrastructureProjectType
{
    TrainingFacilities,
    YouthDevelopment,
    Scouting,
    Medical,
    CorporateCommercial,
    GroundMediaFacilities,
    GroundHospitality,
    GroundMedicalFacilities,
    GroundPitchInfrastructure,
    GroundYouthFacilities,
    GroundTrainingFacilities,
    StadiumExpansion,      // more seats at an existing ground
    NewStadium             // build a new home ground outright
}

public enum InfrastructureProjectStatus
{
    Proposed,
    UnderConstruction,
    Completed,
    Cancelled
}

/// <summary>
/// A player's standing in the squad - WHETHER he's picked and how much rope he gets, as
/// opposed to BattingRole/BowlingRoleType (where/how he's used once selected). Deliberately
/// separate from Reputation: a player can be a well-known name (high Reputation) who has
/// nonetheless fallen to Backup, and a raw DevelopmentProspect can have almost no reputation
/// yet. SquadStatusService.SuggestStatus derives a real starting value from ability/
/// reputation/age/potential at seed time - see that class for why a bare enum default here
/// would be dangerous (this codebase's recurring "unpopulated default read as a negative
/// signal" bug, five occurrences and counting per CLAUDE.md).
/// </summary>
public enum SquadStatus
{
    FirstChoice,
    SecondChoice,
    Backup,
    Fringe,
    DevelopmentProspect,
    EmergencyReplacement,
    ReturningFromInjury,
    LongTermProject
}

/// <summary>
/// Follow-up pass (§5.4): a standing, cross-series workload plan the coach sets for a player -
/// "manage him toward red-ball, rest him in white-ball" - as opposed to WorkloadRotationService's
/// existing per-fixture congestion read, which only ever looks a fortnight ahead. Balanced (the
/// default) leaves every existing player unaffected.
/// </summary>
public enum WorkloadPriority
{
    Balanced,
    PrioritiseRedBall,
    PrioritiseWhiteBall
}

/// <summary>
/// SquadManagementService's response to a player's decline - the middle ground between
/// "keep picking him" (Continue - the default, no explicit decision needed) and "his career
/// is over" (RetirementService's job, not this one's) that a flat drop/keep binary can't
/// express. Roadmap and Rest both model the real-world "time away, then a genuine return" case
/// (the brief's own Kohli example) rather than treating every decline as either permanent or
/// invisible.
/// </summary>
public enum SquadDecisionType
{
    Continue,
    Roadmap,
    Rest,
    Drop
}

/// <summary>
/// The STORY of a result, from one team's own perspective - the same match is a DominantWin
/// for the winner and a HeavyLoss for the loser, never the same story told twice. This is
/// what lets a morale system react to context instead of a flat "+10 for a win" - see
/// MatchResultContextService, which produces one of these from data the match result already
/// carries (margin, wickets in hand, fall-of-wickets, team strength), and TeamMoraleService/
/// PlayerMoraleService, which turn it into an actual, differentiated morale delta.
/// </summary>
public enum MatchResultStory
{
    /// <summary>A plain result with no particular story - most wins and losses are exactly this, and that is correct: not every match should read as dramatic.</summary>
    OrdinaryWin,
    OrdinaryLoss,
    DominantWin,
    NarrowWin,
    /// <summary>Won after visibly being in trouble - chasing and losing wickets heavily along the way, not cruising.</summary>
    ComebackWin,
    /// <summary>Won as the meaningfully weaker side, by underlying team strength.</summary>
    UpsetWin,
    HeavyLoss,
    CloseLoss,
    /// <summary>Lost via a genuine batting collapse - several wickets for very few runs - rather than being outplayed evenly throughout.</summary>
    CollapseLoss,
    Draw,
    /// <summary>Abandoned/washed out - no result either way, and treated as psychologically neutral rather than a loss.</summary>
    NoResult
}



/// <summary>
/// What happened on a given day as the world clock advanced. Typed rather than free text so
/// the news/inbox system (Phase 7) can filter and prioritise, and so a UI can route an injury
/// to the medical screen and a completed stand to the finance screen without parsing strings.
/// </summary>
public enum GameEventType
{
    InjuryOccurred,
    PlayerRecovered,
    InfrastructureCompleted,
    NewGroundOpened,
    CompetitionWindowOpened,
    CompetitionWindowClosed,
    ContractExpired,
    SeasonRollover,
    PlayerRetired,
    CompetitionSeasonCreated,
    MatchCompleted,
    SquadDecisionMade,
    CoachDismissed,
    CoachResigned,
    /// <summary>Phase 5: a genuinely notable training outcome - a breakthrough past PotentialAbility, or a completed role conversion. Deliberately NOT raised for every ordinary training tick, matching MatchSuggestion's own "not constant noise" discipline elsewhere in this codebase.</summary>
    PlayerDeveloped,
    /// <summary>Job-market follow-up: the board has extended a coach's contract - see CoachCareerService.EvaluateRenewal. A non-renewal is reported as ContractExpired, not a distinct event - the contract has genuinely ended either way, and only the renewed case is a different kind of news.</summary>
    CoachContractRenewed,
    /// <summary>The same idea for a backroom appointment - see StaffCareerService.EvaluateRenewal.</summary>
    StaffContractRenewed,
    /// <summary>Job-market follow-up: a backroom staff member has walked away on his own terms - the CoachResigned event's counterpart, kept distinct rather than reused since the subject is a different kind of person doing a different job. See StaffCareerService.TryResign.</summary>
    StaffResigned,
    /// <summary>
    /// Board-objectives follow-up: the board has judged a coach's season against explicitly
    /// stated targets - a real finding, not an assumption: an earlier draft only ever surfaced
    /// this as a GameEvent when it also triggered a dismissal, so a coach who missed his target
    /// but survived (the common case - one missed objective rarely collapses BoardTrust on its
    /// own) had that judgement computed and then never actually reported to anyone. Raised
    /// whenever objectives were set for the year, UNLESS a dismissal fires instead - that event
    /// already carries the same objective-aware reason text, so this would only duplicate it.
    /// </summary>
    BoardObjectivesReviewed,

    /// <summary>Post-Phase-5 rectification pass, Wave 4: the assistant coach has stepped up as interim head coach on a vacancy. See InterimCoachService.</summary>
    CoachAppointedInterim,
    /// <summary>Wave 4: an interim head coach has been made permanent after a genuine run in charge - converted to a real Coach entity.</summary>
    CoachPromotedToPermanent,
    /// <summary>Wave 4: a rival is interested in a sitting coach and the board has made a retention offer (pay rise + extension) to keep him. See CoachCareerService.EvaluateRivalInterest / OfferRetention.</summary>
    CoachRetentionOffer,
    /// <summary>Wave 4: a backroom staff member (typically an assistant coach) has moved up to a head-coach job elsewhere.</summary>
    StaffPromotedToHeadCoach,

    /// <summary>Phase 6, Slice 6.2: an AI club has appointed a permanent head coach from the open market (distinct from CoachAppointedInterim, which is the assistant stepping up, and CoachPromotedToPermanent, which is that interim being made permanent).</summary>
    CoachAppointed,
    /// <summary>Phase 6, Slice 6.2: an AI club has hired a backroom staff member to fill a vacant specialist role.</summary>
    StaffAppointed,
    /// <summary>Phase 6, Slice 6.1: a scheduled fixture could not be played on its date and needs rescheduling (6.6 owns the actual reschedule).</summary>
    FixturePostponed,

    /// <summary>Phase 6, Slice 6.3: a competition's group/league stage is over and its knockout bracket has been drawn.</summary>
    CompetitionStageAdvanced,
    /// <summary>Phase 6, Slice 6.3: a competition season has finished - champion decided, prize money paid out.</summary>
    SeasonCompleted,
    /// <summary>Phase 6, Slice 6.3: a team has been promoted to a higher division for next season.</summary>
    TeamPromoted,
    /// <summary>Phase 6, Slice 6.3: a team has been relegated to a lower division for next season.</summary>
    TeamRelegated,
    /// <summary>Phase 6, Slice 6.3: the player of the series/tournament has been named.</summary>
    PlayerOfTheSeriesAwarded,

    /// <summary>Phase 6, Slice 6.5: a club has approached a coach (typically the human) about its vacant job.</summary>
    CoachJobOffer,
    /// <summary>Phase 6, Slice 6.5: the board's in-season confidence in a coach has moved sharply (a warning, or a vote of confidence).</summary>
    BoardConfidenceShift,

    // ---- Phase 7 (Board, Media & Finance depth) ----

    /// <summary>Phase 7, Slice 7.1: a club's season finances have been settled - sponsorship, matchday, competition income, upkeep and the wage bill all applied to the budget.</summary>
    TeamFinancesSettled,
    /// <summary>Phase 7, Slice 7.2: a competition has paid out its broadcast/central-distribution pool to the clubs that took part.</summary>
    BroadcastRevenuePaid,
    /// <summary>Phase 7, Slice 7.2: a competition's earned reputation (media profile / standing) has moved after a season.</summary>
    CompetitionReputationShift,
    /// <summary>Phase 7, Slice 7.3: a club has changed hands - a takeover that shifts its ambition, wealth and how patient the board is.</summary>
    ClubTakeover,
    /// <summary>Follow-up pass (§13.2/§13.6): a badly split board changes its chairman and, with him, its whole agenda - distinct from an ordinary BoardConfidenceShift (which is about standing with the COACH, not internal boardroom conflict), so a governance crisis can be told apart from routine coach-confidence news and reacted to on its own terms (e.g. a fan protest).</summary>
    BoardroomCoup,
    /// <summary>Phase 7, Slice 7.3: the board has set (or revised) the coach's budget for the coming season.</summary>
    SeasonBudgetSet,
    /// <summary>Phase 7, Slice 7.4: a coach has faced the media - a press conference keyed to how well he handles it.</summary>
    PressConference,
    /// <summary>Phase 7, Slice 7.4: a pundit / former player has weighed in on a story (a sacking, a bold selection, a slump).</summary>
    PunditOpinion,
    /// <summary>Phase 7, Slice 7.5: a player has reached a career milestone - a cap landmark, a run/wicket landmark, a maiden century, a best-ever performance.</summary>
    PlayerMilestone,
    /// <summary>Phase 7, Slice 7.5: an end-of-period award - player of the year/month, team of the season, breakthrough player.</summary>
    SeasonAward,
    /// <summary>Phase 7, Slice 7.6: a code-of-conduct outcome - a fine, an over-rate penalty, a ban.</summary>
    DisciplinaryAction,
    /// <summary>Phase 7, Slice 7.7: a record has changed hands - the new holder, the value, and how long the old one stood.</summary>
    RecordBroken,
    /// <summary>Phase 7, Slice 7.7: a retiring great has been inducted into the Hall of Fame.</summary>
    HallOfFameInduction,
    /// <summary>Phase 7, Slice 7.9: an umpiring decision (or a run of them) in a match drew genuine controversy.</summary>
    UmpiringControversy,
    /// <summary>Corrections pass (correction 2): a home side over-doctored a bilateral / domestic pitch and it was rated poor - a demerit-style consequence for the board and the venue.</summary>
    PitchRatedPoor,
    /// <summary>Phase 7, Slice 7.8: the national selection panel / chairman of selectors has made a call worth reporting.</summary>
    SelectionPanelNote,

    // ---- Phase 9 (Auction / Contracts / Market) ----

    /// <summary>Slice 9.1: a club and a player have agreed a new/extended contract.</summary>
    PlayerContractRenewed,
    /// <summary>Slice 9.1: a player's contract has run out and he has left his club for nothing - the Bosman path. He is now a free agent.</summary>
    PlayerBecameFreeAgent,
    /// <summary>Slice 9.2: a free agent has signed for a club.</summary>
    PlayerSigned,
    /// <summary>Slice 9.3: a player has been transferred between two clubs for a fee.</summary>
    PlayerTransferred,
    /// <summary>Slice 9.3: a transfer bid was made and rejected (by the selling club, the player, or the buying club's board).</summary>
    TransferBidRejected,
    /// <summary>Slice 9.4: a player has handed in a transfer request.</summary>
    TransferRequestFiled,
    /// <summary>Slice 9.4: a player has been unsettled by interest from a bigger club.</summary>
    PlayerUnsettled,
    /// <summary>Slice 9.5: a player has been bought at a franchise-league auction.</summary>
    PlayerAuctioned,
    /// <summary>Slice 9.6: a loan deal has been agreed - with a fee, a wage split, and/or an option to buy.</summary>
    LoanAgreed,
    /// <summary>Slice 9.7: a one-club servant has been granted a testimonial / benefit year.</summary>
    TestimonialGranted,
    /// <summary>Slice 9.7: a one-club servant has been paid a loyalty bonus at a contract milestone.</summary>
    LoyaltyBonusPaid,
    /// <summary>Slice 9.8: a player has been called up to a training camp.</summary>
    TrainingCampCallUp,

    // ---- Post-Phase-7/8/9 rectification ----

    /// <summary>Section G: a franchise has retained a player ahead of a mega auction (direct retention or RTM).</summary>
    PlayerRetained,
    /// <summary>Section G: a franchise has used a Right-to-Match card to reclaim a released player at (or above) the winning bid.</summary>
    RtmExercised,
    /// <summary>Section A: a head coach has been charged under the code of conduct - typically for a press-conference remark.</summary>
    CoachCharged,

    // ---- Post-rectification follow-up: franchise finance, auction media, national coaching, agents, awards ----

    /// <summary>A franchise league's central pool + prize money has been distributed - a franchise's guaranteed, loss-proof season settlement.</summary>
    FranchiseFinancesSettled,
    /// <summary>Prize money paid to a franchise for its league finishing position.</summary>
    FranchisePrizeMoney,
    /// <summary>An auction preview / build-up news item (franchise needs, purse, marquee names available).</summary>
    AuctionPreview,
    /// <summary>An auction round-up / analysis news item (biggest buy, best bargain, unsold names, per-franchise verdict).</summary>
    AuctionReport,
    /// <summary>A pre- or post-auction press conference held by a franchise's management.</summary>
    AuctionPressConference,
    /// <summary>Meeting-driven-selection ticket (D): a franchise's pre-auction planning meeting - last season reviewed, strengths/weaknesses, targets and releases.</summary>
    PreAuctionMeeting,
    /// <summary>Meeting-driven-selection ticket (E): the all-franchises meeting that converts the Expression-of-Interest register into the actual auction list.</summary>
    EoiConversionMeeting,
    /// <summary>Meeting-driven-selection ticket (F): a franchise's post-auction squad review - what it landed, what it missed, and its likely XI.</summary>
    PostAuctionReview,
    /// <summary>Meeting-driven-selection ticket (B): a franchise's recruitment identity (FranchiseArchetype) has genuinely shifted - through sustained results or a new coach's philosophy.</summary>
    FranchiseIdentityShift,
    /// <summary>Meeting-driven-selection ticket (fold-in): a "where do we stand" digest - the board's mood, the dressing room, the key players - distinct from the weekly news digest.</summary>
    StandingStatusDigest,
    /// <summary>A franchise has kept a young uncapped player on its reserve/development list ahead of the auction, at a nominal fee.</summary>
    ReservePlayerRetained,
    /// <summary>A national team has appointed or dismissed a head coach through the national board.</summary>
    NationalCoachAppointed,
    NationalCoachDismissed,
    /// <summary>A player's agent has taken an offer to several clubs at once and engineered a bidding contest.</summary>
    AgentBiddingWar,
    /// <summary>An end-of-tournament award (player of the tournament, find of the auction, team of the tournament).</summary>
    TournamentAward,

    // ---- Post-Phase-9 wiring pass: match presentation ----

    /// <summary>A pre-match conditions / form / head-to-head preview.</summary>
    MatchPreview,
    /// <summary>A post-match story - a key moment, a passage of play, a highlight line.</summary>
    MatchStory,

    // ---- Phase 10: World Simulation & International Cricket ----

    /// <summary>A bilateral series played for a named trophy has been settled - the trophy changed hands or was retained.</summary>
    TrophyContested,
    /// <summary>A national board has awarded (or withdrawn) a central contract.</summary>
    CentralContractAwarded,
    /// <summary>A national board's multi-year verdict on its head coach after a global event or a marquee series.</summary>
    NationalBoardVerdict,
    /// <summary>A player has been withheld from a franchise auction by his board (NOC denied).</summary>
    NocDenied,

    // ---- Phase 11: Planning, Tactics & Selection ----

    /// <summary>A "Consult"-mode decision produced a staff recommendation for the human coach to see.</summary>
    StaffRecommendationIssued,
    /// <summary>A squad has been announced - the selection meeting's rationale, a surprise pick, a big omission, the captain's comment.</summary>
    SquadAnnounced,
    /// <summary>Meeting-driven-selection ticket: the national selection panel meets to build/evolve the broader player pool - distinct from SquadAnnounced, which is one series/tournament's squad.</summary>
    NationalPoolMeeting,

    // ---- Phase 12: Media, Narrative & Board/Coach Depth ----

    /// <summary>A season-narrative storyline started, was reinforced, resolved or faded.</summary>
    NarrativeUpdate,
    /// <summary>A live competition leaderboard - the leading run-scorer / wicket-taker / MVP.</summary>
    Leaderboard,
    /// <summary>A team of the tournament named at a competition's close.</summary>
    TeamOfTheTournament,
    /// <summary>A fan-reaction thread to a big result / signing / decision.</summary>
    FanReaction,
    /// <summary>A coach's coaching philosophy has drifted with experience and results.</summary>
    CoachPhilosophyDrift,

    // ---- Phase 13: Auction & Market Depth ----

    /// <summary>Two franchises have agreed a mid-cycle player-plus-purse trade.</summary>
    FranchiseTrade,
    /// <summary>A player is holding out - refusing to play until his contract is renegotiated.</summary>
    ContractHoldout,
    /// <summary>A club in financial distress has been forced by its board to sell an asset.</summary>
    ForcedSale,
    /// <summary>A sell-on clause has paid a former club a share of a subsequent transfer fee.</summary>
    SellOnClausePaid,
    /// <summary>Transfer deadline day - a flurry of late business.</summary>
    TransferDeadlineDay,

    // ---- Phase 14: Player Life, Relationships & Development Depth ----

    /// <summary>A player relationship formed, deepened or broke down (a friendship, a feud, a mentorship).</summary>
    PlayerRelationship,
    /// <summary>An off-field life event - a birth, a bereavement, a legal/visa issue.</summary>
    OffFieldEvent,
    /// <summary>A player's personality has developed - a hothead matured, a quiet man grew into a leader.</summary>
    PersonalityDevelopment,
    /// <summary>A technical flaw has been spotted, worked on, or exploited.</summary>
    TechnicalFlaw,

    // ---- Phase 16: Records, Awards & Historical Flavour ----

    /// <summary>A milestone ceremony - a guard of honour, a 100th-cap presentation, a stadium-record celebration.</summary>
    MilestoneCeremony,
    /// <summary>The media has named an all-time / team-of-the-era XI.</summary>
    AllTimeXiNamed,
    /// <summary>A nation has entered (or come out of) a golden generation - the academy intake's quality is unusually high (or low).</summary>
    GoldenGeneration,
    /// <summary>A retiring one-club great is given a farewell / testimonial tour.</summary>
    TestimonialTour,

    // ---- Seven-suggestions pass ----

    /// <summary>S5: the ICC has distributed its annual revenue to the member boards.</summary>
    IccRevenueDistributed,
    /// <summary>S7: an Associate nation has been granted ICC Full Membership and Test status (irrevocable).</summary>
    IccFullMembershipGranted,
    /// <summary>S6: a player has switched (or reversed) his international allegiance, or a switch is brewing.</summary>
    RepresentationSwitch,
    /// <summary>NEW-C: a retired player has taken up a coaching / selection / scouting / mentor role (or left the game).</summary>
    PlayerCareerAfterCricket,
    /// <summary>S2: a national board has changed its coaching structure - split into format-specific head coaches, or reunified under one.</summary>
    CoachingStructureChanged,
    /// <summary>S1: a franchise ownership group has moved a player / coach / staff member within the group, or exercised a group retention.</summary>
    OwnershipGroupMove
}

/// <summary>
/// Phase 10: a player's central-contract tier with his national board. None = not centrally
/// contracted (almost everyone). A / B / C descend in retainer size and in how firmly the board
/// can dictate his availability - a tier-A player is a board asset first.
/// </summary>
public enum CentralContractTier
{
    None,
    C,
    B,
    A
}

/// <summary>
/// Phase 11: how the human coach handles a given decision surface. The unified authority model -
/// one 3-state setting per <see cref="DecisionArea"/> replacing the scattered bool "Delegate*"
/// flags on ManagerPreferences (which are kept as computed shims for back-compat).
/// </summary>
public enum DelegationMode
{
    /// <summary>The human makes this call himself. The sim uses his stored choice, or falls back to the AI's pick only to avoid fielding an illegal side / stalling.</summary>
    DoItMyself,
    /// <summary>The AI proposes; the human sees a StaffRecommendation (who / what / reasoning / confidence) and the proposal is applied unless he has overridden it.</summary>
    Consult,
    /// <summary>Handed over entirely - the AI decides and the human lives with it. The default for every area.</summary>
    Delegate
}

/// <summary>Phase 11: the decision surfaces the human coach can set a <see cref="DelegationMode"/> for.</summary>
/// <summary>Phase 12 (§14.7): how a player handles the media.</summary>
public enum MediaPersona
{
    /// <summary>Says little, gives nothing away - a safe pair of hands in a press conference, no commercial draw.</summary>
    Guarded,
    /// <summary>Straight bat, no drama.</summary>
    Balanced,
    /// <summary>Speaks his mind - great copy, and a liability when it goes wrong.</summary>
    Outspoken,
    /// <summary>A brand - sponsors love him, and his image rights are worth real money.</summary>
    Marketable
}

/// <summary>Phase 14 (§18.5): the shape of a player's career ambition.</summary>
public enum AmbitionDirection
{
    /// <summary>Wants to be a one-club servant - resists moves, re-signs below market, a testimonial in his future.</summary>
    OneClubLegend,
    /// <summary>Chases silverware - agitates to leave a club going nowhere, takes a step down in money for a step up in trophies.</summary>
    TrophyHunter,
    /// <summary>Chases the money and the miles - a franchise circuit career, a new league every year.</summary>
    Globetrotter,
    /// <summary>Takes his career as it comes.</summary>
    Journeyman
}

/// <summary>Phase 14 (§6.3): a specific technical flaw a coach can work on and an opponent can target.</summary>
public enum TechnicalFlaw
{
    /// <summary>A big trigger movement across the stumps - vulnerable bowled / lbw.</summary>
    TriggerMovementFault,
    /// <summary>Fishes outside off - vulnerable caught behind / slips.</summary>
    WeakOutsideOff,
    /// <summary>Plays around his front pad - vulnerable lbw.</summary>
    FrontFootLbwProne,
    /// <summary>A hitchy, front-on bowling action - a genuine stress-fracture risk.</summary>
    StressFractureAction,
    /// <summary>Can't rotate strike square of the wicket - vulnerable to being tied down and bowled a tight line.</summary>
    StrikeRotationGap
}

/// <summary>Phase 13 (§8.1): a franchise's auction philosophy - reshapes its plan and how hard it bids.</summary>
/// <summary>Phase 12 (§4.6): a coach's format specialisation.</summary>
public enum FormatSpecialisation
{
    AllFormats,
    RedBall,
    WhiteBall
}

public enum FranchiseArchetype
{
    /// <summary>Chases value - bids hard below market, folds the moment a price passes fair value.</summary>
    Moneyball,
    /// <summary>Buys the marquees - overpays for star power, banks on the box office and the dressing-room lift.</summary>
    StarHunter,
    /// <summary>A settled, balanced plan - fills every role at a sensible price.</summary>
    Balanced,
    /// <summary>Builds around youth - prioritises the under-24s, plays the long game.</summary>
    YouthBuilder
}

public enum DecisionArea
{
    SquadSelectionDomestic,
    SquadSelectionNational,
    SquadSelectionFranchise,
    XiSelection,
    TacticalPlan,
    TrainingFocus,
    Transfers,
    ContractOffers,
    Loans,
    Academy,
    PressResponses,
    BoardResponses,
    Captaincy,
    StaffHiring
}

/// <summary>Phase 9, Slice 9.0: which kind of deal a PlayerContract is - a year-round domestic registration, or a short single-season franchise-league deal. A player can hold one of each.</summary>
public enum ContractKind
{
    Domestic,
    Franchise
}

/// <summary>
/// Post-Phase-7/8/9 rectification (Section B): how a country administers its DOMESTIC tier.
/// Cricket is not football-club-ownership - a national board can run the regional sides directly.
/// Held on WorldState.CountryProfiles per country.
/// </summary>
public enum DomesticStructureModel
{
    /// <summary>The national board owns the regional sides; a regional association runs operations and appoints the coach, but there is no ownership transaction and a takeover is impossible (Pakistan-style, but the CONCEPT, not a frozen snapshot).</summary>
    BoardControlledRegions,
    /// <summary>Club/county-membership model - members own the club, closest to the FM-style model already built (England-style).</summary>
    ClubMembership
}

/// <summary>Post-Phase-7/8/9 rectification (Section C): a cricketing nation's ICC status - a full member or an associate. Drives the domestic foreign-player sub-quota and the associate growth pathway.</summary>
public enum MembershipStatus
{
    FullMember,
    Associate
}

/// <summary>Post-Phase-7/8/9 rectification (Section G): a franchise auction is either a full mega auction (squad reset, up to 6 retentions) or a lighter mini auction between mega cycles (carry-over + gap-fill only).</summary>
public enum AuctionMode
{
    Mega,
    Mini
}

/// <summary>Post-Phase-7/8/9 rectification (Section F): the auction runs in ordered SETS, not one flat pool.</summary>
public enum AuctionSet
{
    Marquee,
    CappedBatter,
    CappedWicketkeeper,
    CappedAllrounder,
    CappedPace,
    CappedSpin,
    UncappedBatter,
    UncappedWicketkeeper,
    UncappedAllrounder,
    UncappedPace,
    UncappedSpin,
    Accelerated
}

/// <summary>Phase 9, Slice 9.8: the three tiers of training camp - each drawing a different pool of players and each with its own eligibility rules once franchise contracts exist.</summary>
public enum TrainingCampType
{
    /// <summary>A national camp - the national pool, ahead of an international window. A player under a franchise contract in that window may be excused.</summary>
    International,
    /// <summary>A club pre-season / mid-season camp - the senior squad.</summary>
    Club,
    /// <summary>A franchise pre-tournament camp - the franchise's auctioned/retained squad.</summary>
    Franchise
}

/// <summary>
/// Phase 7, Slice 7.3: how a club is owned. Shapes the board's wealth, ambition and patience, and
/// whether a takeover that changes all three is even possible.
/// </summary>
public enum OwnershipModel
{
    /// <summary>Members / a supporters' trust own the club. Steady, rarely wealthy, hard to take over, patient with a coach.</summary>
    MemberOwned,
    /// <summary>A single private owner. Wealth and ambition track the owner's mood; patience varies wildly; a takeover just swaps one owner for another.</summary>
    PrivateOwner,
    /// <summary>A corporation / franchise holding. Deep pockets, results-focused, impatient, and a real takeover target.</summary>
    Corporate,
    /// <summary>A board / association runs it (most national teams, some domestic sides). Bureaucratic, moderate wealth, politically driven, not for sale.</summary>
    Association
}

/// <summary>Phase 7, Slice 7.4: the tenor of a media response - what a coach chooses to project.</summary>
public enum PressTone
{
    /// <summary>Straight bat - measured, gives nothing away. Safe; rarely wins or loses much.</summary>
    Diplomatic,
    /// <summary>Backs his players and himself publicly - lifts the dressing room, but a hostage to fortune if results do not follow.</summary>
    Bullish,
    /// <summary>Takes the blame off the players and onto himself - buys dressing-room goodwill at the cost of board confidence.</summary>
    ShieldsThePlayers,
    /// <summary>Criticises a player, an official or the board in public - a short-term vent that costs him almost every time.</summary>
    Confrontational
}

/// <summary>Phase 7, Slice 7.6: the kind of code-of-conduct breach.</summary>
public enum DisciplinaryOffence
{
    /// <summary>The side bowled its overs too slowly - a team fine, and at the extreme a captain's suspension.</summary>
    SlowOverRate,
    /// <summary>Dissent at an umpire's decision - a player fine, a demerit point.</summary>
    Dissent,
    /// <summary>Audible obscenity / send-off / conduct bringing the game into disrepute - a heavier fine.</summary>
    Conduct,
    /// <summary>A serious on- or off-field incident - a real ban.</summary>
    SeriousMisconduct
}

/// <summary>Phase 7, Slice 7.5: an end-of-period award.</summary>
public enum AwardType
{
    PlayerOfTheYear,
    PlayerOfTheMonth,
    TeamOfTheSeason,
    BreakthroughPlayer,
    BowlerOfTheYear,
    BatterOfTheYear
}

/// <summary>Phase 7, Slice 7.7: a tracked all-time record.</summary>
public enum RecordCategory
{
    HighestTeamTotal,
    LowestTeamTotal,
    HighestIndividualScore,
    BestBowlingInInnings,
    MostCareerRuns,
    MostCareerWickets,
    MostCareerCatches,
    HighestPartnership,

    // ---- Phase 16 (§15.2): more record categories ----
    /// <summary>Most sixes in a single innings.</summary>
    MostSixesInInnings,
    /// <summary>Fewest balls to a fifty (approximated from an innings' runs/balls when it passed 50).</summary>
    FastestFifty,
    /// <summary>Fewest balls to a hundred.</summary>
    FastestHundred,
    /// <summary>Best (lowest) economy rate in a completed bowling spell of real length.</summary>
    BestEconomyInInnings,
    /// <summary>Most catches taken by one fielder in a single match.</summary>
    MostCatchesInMatch,
    /// <summary>Most ducks in one team's innings - the quirky wooden-spoon record.</summary>
    MostDucksInInnings
}

/// <summary>Phase 7, Slice 7.9: which panel an umpire sits on - the pool a fixture draws from, and a career ladder.</summary>
public enum UmpirePanel
{
    /// <summary>The top international panel - the neutral officials for the biggest matches.</summary>
    Elite,
    /// <summary>The international panel - most international cricket, and the strongest domestic finals.</summary>
    International,
    /// <summary>The domestic first-class panel - the bulk of domestic cricket.</summary>
    Domestic,
    /// <summary>Development / reserve - promising officials working their way up, and older ones on the way down.</summary>
    Development
}

/// <summary>
/// Section AA: a pre-match choice the home side's groundstaff make about how to prepare the
/// pitch - within a real range around the ground's own baseline characteristics, not a rewrite
/// of the venue. See PitchPreparationService.
/// </summary>
public enum PitchPreparation
{
    /// <summary>Leave the surface as the ground's own baseline plays. The default - most matches are not specially prepared.</summary>
    Neutral,
    /// <summary>Extra grass left on: more seam and bounce early, less for the surface to offer as the match wears on.</summary>
    Grassy,
    /// <summary>Dried out and rolled hard: turns more, and turns earlier.</summary>
    Dry,
    /// <summary>A flat road: little for anyone, and a batting-friendly surface throughout.</summary>
    Flat
}

/// <summary>
/// How a batter was dismissed. This was flagged as missing tech debt in Phase 3 - the field was
/// deliberately NOT added until something could populate it, and the match engine now can.
/// Needed for dismissal-pattern analytics (Section 34), keeper/fielder credit, and for the
/// bowler-vs-batter matchup history to know WHO got the wicket and HOW.
/// </summary>
public enum DismissalType
{
    NotOut,
    Bowled,
    Caught,
    CaughtBehind,
    CaughtAndBowled,
    LBW,
    RunOut,
    Stumped,
    HitWicket,
    Retired
}

/// <summary>What physically happened on one delivery. Extras are separate outcomes because they behave differently: a wide is not a ball faced, a no-ball is a free hit in some formats, and byes are not credited to the batter.</summary>
public enum DeliveryOutcomeType
{
    DotBall,
    RunsOffBat,
    Boundary4,
    Boundary6,
    Wicket,
    Wide,
    NoBall,
    Bye,
    LegBye
}

/// <summary>
/// How hard the batter is trying to score on this ball. Set by tactics (Section 20) and by the
/// match situation - a side needing 14 an over is forced into Attacking whatever the coach said.
/// Every level trades scoring rate against dismissal risk, which is the fundamental cricket
/// bargain and must never be free.
/// </summary>
public enum BattingIntent
{
    Blocking,     // survival - see off a spell, save a Test
    Anchoring,    // rotate strike, minimal risk
    Normal,
    Attacking,
    AllOut        // slog - maximum risk, death overs or a lost cause
}

/// <summary>Whether the bowler is buying wickets or shutting down runs. Attacking bowling concedes more when it fails; containment takes fewer wickets.</summary>
public enum BowlingIntent
{
    Defensive,
    Balanced,
    Attacking
}

/// <summary>
/// The wagon-wheel segments a shot can go to, from the striker's perspective (right-hander).
/// Eight zones is the standard analytical split used by real cricket data providers - enough to
/// express a batter's strong areas and a captain's field plan without pretending to a precision
/// the rest of the simulation doesn't have.
/// </summary>
public enum ShotZone
{
    ThirdMan,        // behind square, off side
    Point,           // square, off side
    Cover,           // in front of square, off side
    MidOff,          // straight, off side
    MidOn,           // straight, leg side
    MidWicket,       // in front of square, leg side
    SquareLeg,       // square, leg side
    FineLeg          // behind square, leg side
}

/// <summary>
/// Real fielding positions. Each maps to a zone, sits inside or outside the circle, and may or
/// may not be a catching position - which is what lets a field setting actually change what
/// happens rather than being a cosmetic list of names.
/// </summary>
public enum FieldingPosition
{
    WicketKeeper,
    Slip1, Slip2, Slip3, Slip4,
    Gully,
    LegSlip,
    ShortLeg,
    SillyPoint,
    Point,
    BackwardPoint,
    CoverPoint,
    Cover,
    ExtraCover,
    MidOff,
    MidOn,
    MidWicket,
    SquareLeg,
    FineLeg,
    ThirdMan,
    DeepThirdMan,
    DeepPoint,
    DeepCover,
    LongOff,
    LongOn,
    DeepMidWicket,
    DeepSquareLeg,
    DeepFineLeg,
    Bowler
}

/// <summary>What a fielding position demands. A slip fielder and a boundary rider are different specialists, and a player good at one is not automatically good at the other.</summary>
public enum FieldingSkillType
{
    Keeping,
    SlipCatching,      // reflex catching close behind the wicket
    CloseCatching,     // short leg, silly point - bravery and reflexes
    InnerRing,         // ground fielding, saving singles, run-out threat
    BoundaryRiding     // ground covered, catching in the deep, arm
}

/// <summary>
/// Where a bowler is asked to aim. This is the language a coach actually uses - "at the stumps",
/// "test him outside off", "into the body" - not a coordinate on a pitch map. Ball-by-ball line
/// and length micromanagement (the Cricket Coach approach) is deliberately NOT what this is: a
/// coach sets a plan, and the bowler executes it with his own judgement.
/// </summary>
public enum BowlingLine
{
    AtTheStumps,      // attack the stumps - lbw and bowled in play
    FourthStump,      // the corridor - the classic probing line
    OutsideOff,       // invite the drive, look for the edge
    WideOutsideOff,   // deny width to the leg side, choke the scoring (death tactic)
    IntoTheBody,      // cramp him for room, cross the leg side
    LegStump          // block the pads, defensive containment
}

/// <summary>How full to bowl it. Length is what actually decides whether a batter can drive, defend or has to play off the back foot.</summary>
public enum BowlingLength
{
    Yorker,
    Full,             // drive-able, but lbw and bowled come into play
    Good,             // the standard probing length
    BackOfLength,     // awkward, neither forward nor back
    Short             // bouncer territory - test the technique against the rising ball
}

/// <summary>The bowler's go-to variation within the plan. A coach names one; the bowler chooses when to use it.</summary>
public enum DeliveryVariation
{
    None,
    Bouncer,
    SlowerBall,
    Yorker,
    Cutter,
    WideYorker,
    Googly,
    ArmBall,
    TopSpinner,
    CrossSeam,
    RoundTheWicket
}

/// <summary>Why a bowler departed from his instructions - useful for post-match analysis and for the coach to see whether he is being listened to.</summary>
public enum PlanDeviationReason
{
    Followed,
    ExecutionError,      // tried and missed his mark
    ReadTheBatter,       // spotted a weakness the plan didn't cover
    PlanNotWorking,      // being milked, changed it himself
    Indiscipline         // ignored the plan for no good reason
}

/// <summary>
/// Broad bowler type. Derived from attributes rather than stored, so it can never disagree with
/// what the player can actually do - and so a bowler who develops a spinner's skills is treated as
/// one without anyone having to remember to update a field.
/// </summary>
public enum BowlerType
{
    Pace,
    Spin
}

/// <summary>
/// Who has the final say on a decision.
///
/// A human coach is playing a management game, and the point of a management game is that the
/// decisions are yours. So the default for a human coach is that his instruction is carried out -
/// the captain and the bowlers can tell him what they want, but they do not overrule him.
///
/// Delegation is the coach's own choice: he can hand a decision, or a specific player, over
/// entirely - "set your own field", "bowl how you like" - and then live with what they do.
/// </summary>
public enum DecisionAuthority
{
    /// <summary>The coach's instruction is carried out. Players may raise suggestions; they do not act on them unaided.</summary>
    CoachHasFinalSay,

    /// <summary>The coach's instruction stands, but the player's view is put to him first so he can change his mind. Suggestions are surfaced, not applied.</summary>
    Consult,

    /// <summary>Handed over. The player decides for himself, and the coach lives with it. This is how AI-coached sides run by default, and how a human coach can choose to run.</summary>
    Delegated
}

/// <summary>
/// Post-Phase-5 rectification pass, Wave 4: an area of the coach's job he can formally hand to
/// his backroom staff, the OUT-OF-MATCH counterpart to DecisionAuthority (which only ever
/// covered in-match calls). A head coach who trusts his batting coach can let him own training
/// focus; one who trusts his assistant can let him run squad selection. Held on
/// Team.DelegatedResponsibilities.
/// </summary>
public enum DelegatedResponsibility
{
    /// <summary>The relevant specialist (batting/bowling coach) picks each player's training focus and works it a little more effectively - see TrainingService.SuggestFocus / ApplyPeriodicTraining.</summary>
    TrainingFocus,
    /// <summary>The assistant coach owns squad selection. Declared here; the selection services read the flag where they run.</summary>
    SquadSelection,
    /// <summary>The captain (or assistant) owns fielding plans out of the coach's hands entirely, beyond the per-decision DecisionAuthority split.</summary>
    FieldingPlans,
    /// <summary>Post-Phase-6 (section B): hiring / firing / renewing every NON-head-coach staff role. Owned by the head coach by default; he can hand it to a GeneralManager staff member (Team.StaffingDelegatedToStaffId) or back to the board.</summary>
    StaffHiring
}

/// <summary>What a player is asking the coach for.</summary>
public enum SuggestionKind
{
    FieldChange,
    BowlingChange,
    BowlingPlanChange,
    BattingApproachChange,
    TossDecision,
    TakeMeOff,
    KeepMeOn
}

/// <summary>
/// Section 33's backroom staff. These are the people a coach hires around him, and each one does a
/// different job - an analyst is not a bowling coach with a laptop.
/// </summary>
public enum StaffRole
{
    AssistantCoach,
    BattingCoach,
    BowlingCoach,
    FieldingCoach,
    StrengthAndConditioning,
    Physiotherapist,
    Analyst,
    Scout,
    MentalPerformanceCoach,

    // Post-Phase-6 (section C): the fuller backroom roster.
    /// <summary>Runs the scouting department - recommends signings (club) or national-pool additions (international), and manages the club's scouts.</summary>
    ChiefScout,
    /// <summary>Team Director / General Manager - can receive the head coach's delegated staffing authority (section B).</summary>
    GeneralManager,
    /// <summary>Heads the medical department - injury prevention and return-to-play, on top of the individual physio work.</summary>
    HeadPhysiotherapist,
    /// <summary>Modern data/video analysis, distinct from the traditional opposition-scouting Analyst.</summary>
    DataAnalyst,
    /// <summary>Load, recovery and conditioning science - works with the S&amp;C coach and the medical team.</summary>
    SportsScientist,
    /// <summary>A senior figure - often a former player - attached to the club specifically to mentor young players, amplifying the player-to-player mentoring MentoringService already models.</summary>
    Mentor,

    /// <summary>
    /// The meeting-driven selection ticket: national-team staff (never club/franchise) who track and
    /// analyse domestic/national-pool form and feed recommendations into a NationalPoolMeeting or a
    /// per-series SelectionMeeting - never a decider in their own right (see
    /// <see cref="DecisionArea.SquadSelectionNational"/>, which stays coach/captain-owned regardless
    /// of who's on the panel).
    /// </summary>
    Selector,
    /// <summary>Chairs the national selection panel - the same job as Selector, weighted more heavily on the panel's overall rigour (see SelectionMeetingService.PanelQuality / NationalBoard.ChairmanOfSelectorsQuality).</summary>
    ChiefSelector
}

/// <summary>
/// Phase 5: what a player's coach-directed training is actually working on this year - a real,
/// nameable net-session focus, not a single attribute. Each value maps to a cluster of related
/// attributes on TrainingService's own attribute-cluster table, mirroring how PlayerAgeingService
/// already groups attributes into Physical/Fielding/Technical/Mental for involuntary change -
/// training is the VOLUNTARY, coach-DIRECTED counterpart, which is why it can target one specific
/// cluster rather than moving all of them together.
/// </summary>
public enum TrainingFocus
{
    /// <summary>No directed programme this year - the player still gets PlayerAgeingService's ambient growth/decline, just no EXTRA coach-directed push on top.</summary>
    None,
    BattingTechnique,
    BattingPower,
    BattingAgainstPace,
    BattingAgainstSpin,
    BattingFinishing,
    BowlingPaceDevelopment,
    BowlingAccuracy,
    BowlingVariations,
    BowlingSwingSeam,
    BowlingSpinCraft,
    BowlingDeathCraft,
    Fielding,
    Physical,
    Mental,
    /// <summary>A bowler working specifically on his batting - genuinely useful, never a route to becoming a specialist batter. See TrainingService's all-round development cap.</summary>
    AllroundBatting,
    /// <summary>A batter working specifically on his bowling - same cap, mirrored.</summary>
    AllroundBowling,
    /// <summary>Attributes nudged toward whatever PlayerTrainingPlan.TargetBattingRole/TargetBowlingRole calls for - see TrainingService.ApplyRoleConversion.</summary>
    RoleConversion
}

/// <summary>
/// Phase 5: how hard a player is working this year. The annual-resolution stand-in for the
/// camp/in-season-maintenance distinction real training calendars have - see CLAUDE.md's Phase 5
/// writeup for why the full weekly calendar/camp system is deferred rather than built here (the
/// rest of this codebase's player-development tick is annual everywhere, so a finer calendar
/// would have no matching simulation granularity underneath it to drive).
/// </summary>
public enum TrainingIntensity
{
    Light,
    Normal,
    Intensive
}

/// <summary>What kind of weakness an analyst has identified in a batter. Named the way a report would name them.</summary>
public enum IdentifiedWeakness
{
    ShortBall,
    FullAndStraight,
    TheCorridor,
    AgainstSpin,
    AgainstPace,
    Timing,
    ShotSelection,
    LegSideRestriction,
    None
}
