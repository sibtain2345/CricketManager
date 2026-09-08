using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

public sealed class Player
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public DateOnly DateOfBirth { get; set; }
    public string Nationality { get; set; } = string.Empty;
    public BattingHand BattingHand { get; set; }
    public BowlingStyle BowlingStyle { get; set; } = BowlingStyle.None;
    public PlayerRole PrimaryRole { get; set; }

    // Section 13: current ability vs potential ability drive development, not randomness.
    // INVARIANT (enforced only at WorldSeeder time today, not after any other mutation):
    // CurrentAbility should never exceed PotentialAbility. Nothing currently guards this on
    // the setters themselves - Phase 8 (Training & Player Development) must either clamp
    // CurrentAbility to PotentialAbility on every mutation, or treat a breach as a signal
    // that PotentialAbility itself needs to rise (a player exceeding their "ceiling" through
    // sustained real performance is a legitimate emergent story per Section 11, not a bug -
    // but that has to be a deliberate decision Phase 8 makes, not an accidental gap).
    public int CurrentAbility { get; set; }   // 1-200 composite scale (FM-style)
    public int PotentialAbility { get; set; } // 1-200 composite scale, ceiling of CurrentAbility

    public BattingAttributes Batting { get; set; } = new();
    public BowlingAttributes Bowling { get; set; } = new();
    public FieldingAttributes Fielding { get; set; } = new();
    public MentalAttributes Mental { get; set; } = new();
    public PhysicalAttributes Physical { get; set; } = new();
    public FormatSuitability FormatSuitability { get; set; } = new();

    public FormState Form { get; set; } = new();
    public PersonalityTrait Personality { get; set; } = PersonalityTrait.None;

    // Section 11/45: a player's standing in the game, which is what drives selectorial and
    // franchise INTEREST - distinct from ability (how good they are) and form (how they're
    // going right now). It was missing entirely: Coach had tiered reputation and Player did
    // not, so "an average domestic player has an outstanding season and gets noticed" had
    // nothing to actually move. Tiered for the same reason the coach's is - a player can be
    // a household name domestically and unknown abroad.
    public Reputation Reputation { get; set; } = new(domestic: 5);

    // Section 31: current injury, if any. Historical injuries are kept because injury
    // HISTORY is what makes a player "injury-prone" in the eyes of a recruiter, and
    // recurrence risk is specific to what they've had before.
    public Injury? CurrentInjury { get; set; }
    public List<Injury> InjuryHistory { get; set; } = new();

    /// <summary>
    /// Phase 15 (§1.8): the concussion protocol. A blow to the head brings a MANDATORY minimum
    /// stand-down regardless of how the player feels the next morning - he cannot be picked until
    /// this date, and his club may name a like-for-like concussion substitute in the meantime.
    /// Null (the default) = no concussion in progress. Cleared by PlayerAvailabilityService.MarkRecovered.
    /// </summary>
    public DateOnly? ConcussionStandDownUntil { get; set; }

    /// <summary>
    /// Seven-suggestions pass (S4): while set and in the future, this player is back from a long
    /// injury but not yet match-fit for INTERNATIONAL cricket - he needs a spell of domestic / 'A'
    /// cricket first. National selection skips him until this date; club/domestic selection does
    /// not. Set by PlayerAvailabilityService.MarkRecovered after a Serious+ layoff.
    /// </summary>
    public DateOnly? NeedsMatchFitnessUntil { get; set; }

    /// <summary>Non-injury reasons a player can't be picked (suspension, international duty, competition registration). Set by the systems that own each reason; PlayerAvailabilityService reads them all together.</summary>
    public UnavailabilityReason NonInjuryUnavailability { get; set; } = UnavailabilityReason.Available;

    /// <summary>
    /// Seven-suggestions pass (S6): nations this player has a genuine HERITAGE link to (a parent's
    /// or grandparent's country of birth) beyond his current <see cref="Nationality"/>. 0-2 entries,
    /// seeded rarely. A heritage nation is one of the routes RepresentationDriftService can move him
    /// to (subject to the ICC 3-year stand-down); a residency route uses his club's country instead.
    /// </summary>
    public List<string> HeritageNations { get; set; } = new();

    /// <summary>Seven-suggestions pass (S6): the nation this player represented / was eligible for BEFORE his most recent switch of allegiance. Null if he has never switched. Used for the reversible-switch case (a player going back once his old nation gains Test status).</summary>
    public string? FormerNationality { get; set; }

    // Section 22: opponent/ground/bowler-specific confidence. Key format: "opp:{teamId}",
    // "ground:{name}", "bowler:{playerId}" - see MatchupKey. Deliberately a flat dictionary
    // rather than separate fields per matchup type, since the set of opponents/grounds/bowlers
    // a player has faced is open-ended and grows over a career.
    public Dictionary<string, MatchupConfidence> Matchups { get; set; } = new();

    // Role = WHERE/HOW the player is used. Trait = WHAT the player is naturally good at.
    // Kept as two separate concepts deliberately (see RoleTraitDeriver) rather than folding
    // traits into role, since a player can be a MiddleOrder batter who is ALSO a Finisher.
    public BattingRole BattingRole { get; set; } = BattingRole.MiddleOrder;
    public BowlingRoleType BowlingRole { get; set; } = BowlingRoleType.NotABowler;

    /// <summary>
    /// Follow-up pass (§5.13): a role the COACH has explicitly given this player - "you are our
    /// finisher", "you open regardless of what BattingRole says" - distinct from BattingRole
    /// (which is DERIVED from his attributes by RoleTraitDeriver, a technical description of what
    /// he naturally is). A player can be a naturally MiddleOrder batter who is ASSIGNED to open
    /// because the coach trusts him there - the derived role stays honest about his technique, the
    /// assignment is the coach's own call. Null (the default) means no explicit assignment - every
    /// pre-existing player is unaffected, and SituationalPerformanceModifier's role-clarity term is
    /// simply a no-op.
    /// </summary>
    public BattingRole? AssignedRole { get; set; }

    /// <summary>Follow-up pass (§5.4): a standing, cross-format workload plan the coach has set for this player - read by WorkloadRotationService alongside its own per-fixture congestion signal.</summary>
    public WorkloadPriority WorkloadPriority { get; set; } = WorkloadPriority.Balanced;

    /// <summary>Follow-up pass (§6.4): how many consecutive monthly training periods this player has just spent on an Intensive programme - TrainingService's own soft-tissue axis, distinct from SkillRegressionService's match-load burnout. Resets the moment the load eases.</summary>
    public int ConsecutiveIntensiveTrainingPeriods { get; set; }
    public BattingTraits BattingTraits { get; set; } = new();
    public BowlingTraits BowlingTraits { get; set; } = new();

    // Experience is COUNTED, not derived from age - see PlayerExperience. A 30-year-old with
    // twenty first-class games is not experienced, and no birthday-based shortcut can say so.
    public PlayerExperience Experience { get; set; } = new();

    public Guid? CurrentTeamId { get; set; }

    /// <summary>
    /// Meeting-driven-selection ticket, requirement C (first-hand knowledge): the set of team ids
    /// this player has genuinely been part of over his career - his clubs, the national pool, every
    /// franchise that has bought him. It is the substrate for "who has personally watched whom":
    /// a coach or captain whose own <see cref="Coach.CareerTeamIds"/> / career overlaps this set
    /// has a real first-hand read on the player, distinct from stats and reputation. Populated at
    /// the join points (contract signing, transfer, franchise auction, national-pool entry) and
    /// seeded with the player's starting club. Never removed - a shared past is a shared past.
    /// </summary>
    public HashSet<Guid> CareerTeamIds { get; set; } = new();

    /// <summary>Whole-career retirement - nothing left to play, anywhere. Deliberately left as a
    /// real, independently-settable field rather than folded into RetiredFormats, so every
    /// existing whole-career consumer (WorldClockService's annual rollover, CaptaincyService,
    /// SquadManagementService, PlayerAvailabilityService) keeps meaning exactly what it always
    /// meant - "this player plays no cricket at all" - without having to be rewritten around a
    /// derived condition. RetirementService.RetireFromFormat sets this automatically once
    /// RetiredFormats covers all three formats, since retiring from Test, ODI AND T20 IS full
    /// retirement by definition; nothing else sets it implicitly.</summary>
    public bool IsRetired { get; set; }
    public DateOnly? RetirementDate { get; set; }

    /// <summary>
    /// Format-specific retirement (Section H): a player can step back from Test cricket while
    /// continuing white-ball, the well-known real-world pattern (Kohli/Root/Williamson-style) -
    /// RetirementService.AssessFormatRetirement runs each format's own age curve independently.
    /// A format in this set is a genuinely separate fact from IsRetired: a player can be
    /// RetiredFormats={Test} and still very much an active white-ball cricketer.
    /// </summary>
    public HashSet<MatchFormat> RetiredFormats { get; set; } = new();

    /// <summary>WHETHER this player is picked and how much rope he gets - see SquadStatus's own doc comment. Defaults to SecondChoice, a genuinely neutral placeholder (neither "trusted" nor "expendable"), until SquadStatusService.SuggestStatus assigns a real value from the player's actual ability/reputation/age at seed time - the same pattern RoleTraitDeriver already established for traits, deliberately reused rather than inventing a new one.</summary>
    public SquadStatus SquadStatus { get; set; } = SquadStatus.SecondChoice;

    /// <summary>An active Roadmap or Rest from SquadManagementService, or null (the common case - most players are simply selected or not on merit each match, no standing decision in force).</summary>
    public SquadDecision? CurrentSquadDecision { get; set; }

    /// <summary>How he's feeling, not how he's playing (Form) or how he's regarded (Reputation) - see PlayerMorale's own doc comment. Driven by selection/squad decisions and personal performance in context; SituationalPerformanceModifier reads it back as a small, capped multiplier.</summary>
    public PlayerMorale Morale { get; set; } = new();

    /// <summary>0-100, 50 = neutral. Trust in the CURRENT coaching setup specifically - how backing decisions (Continue/Roadmap/Rest vs Drop) and honest communication have landed with this player. Deliberately a single current-relationship number rather than a per-historical-coach dictionary: a coach change is exactly the kind of fresh start that should let this reset toward neutral rather than carry an old grudge forward indefinitely.</summary>
    public double CoachTrust { get; set; } = 50;

    /// <summary>
    /// Post-Phase-16 completion pass (§5.10): match sharpness, 0-100 (100 = fully match-hardened).
    /// Drops slowly for a fit player who is not being selected - a batter loses timing, a bowler
    /// his rhythm - and recovers when he plays again. A small multiplier on effective skill in the
    /// ball model. Distinct from Form (recent scores) and Fitness (the body) - this is "has he
    /// actually been in the middle lately". Default 100 so a player generated before this is sharp.
    /// </summary>
    public double MatchSharpness { get; set; } = 100;

    /// <summary>
    /// Post-Phase-6 carry-forward: 0-100, 50 = neutral. This player's opinion of the CAPTAIN's
    /// on-field judgement specifically - a distinct question from CoachTrust (the coaching setup)
    /// and from the captain's own DressingRoomBacking. Moved by how the captain's in-match calls
    /// have actually gone (CaptaincyService's per-match decision-quality reading) and by results.
    /// Read by the captaincy system - a captain the room genuinely rates carries more weight when
    /// he pushes a selection view or a tactical change - and, like CoachTrust, resets toward
    /// neutral when the captaincy changes hands.
    /// </summary>
    public double CaptainTrust { get; set; } = 50;

    /// <summary>
    /// Post-Phase-6 (section C): the id of a specialist coach (batting/bowling/fielding/mental)
    /// assigned to do extra individual work with this player - net sessions, video, targeted
    /// drills. Feeds a weekly development channel on top of the normal monthly training, and the
    /// coach's own quarterly effectiveness signal reflects whether it worked. Null for most
    /// players - individual attention goes to the young and the out-of-nick.
    /// </summary>
    public Guid? AssignedSpecialistCoachId { get; set; }

    /// <summary>
    /// Phase 5: this year's coach-directed training programme - see PlayerTrainingPlan and
    /// TrainingService. Defaults to a real, inert plan (Focus = None) rather than null, matching
    /// TacticalPlan.None's own convention: "no programme set" is a normal, common state every
    /// player starts in, not a special case a caller has to null-check for.
    /// </summary>
    public PlayerTrainingPlan TrainingPlan { get; set; } = new();

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 2 (point 3): an unresolved big-match failure awaiting
    /// a redemption arc, or null (the common case - most players are not carrying one). Set and
    /// resolved by MatchDevelopmentService; read by SituationalPerformanceModifier so a player
    /// with something to prove genuinely walks out under a heavier weight until he clears it.
    /// </summary>
    public PressureMoment? PendingPressureMoment { get; set; }

    /// <summary>
    /// Phase 7, Slice 7.6: the date a code-of-conduct ban runs until, or null (the overwhelming
    /// common case). While set and in the future, DisciplineService keeps
    /// NonInjuryUnavailability = Suspended; DisciplineService.ReviewSuspensions clears it on expiry.
    /// Distinct from an injury (nothing physically wrong) and from a Rest/Roadmap (this is a
    /// sanction, not a development decision).
    /// </summary>
    public DateOnly? SuspendedUntil { get; set; }

    /// <summary>
    /// Phase 7, Slice 7.6: accumulated code-of-conduct demerit points - the CACHE of
    /// DemeritPointsIn(now), kept for cheap reads. The authoritative record is <see cref="DemeritLog"/>;
    /// DisciplineService keeps this in sync. Post-Phase-7/8/9 rectification made the window a real
    /// rolling 24 months rather than a slow decay.
    /// </summary>
    public int DemeritPoints { get; set; }

    /// <summary>
    /// Post-Phase-7/8/9 rectification (Section A): the dated log of every code-of-conduct charge,
    /// so demerit points are counted over a real ROLLING 24-month window (the actual ICC rule)
    /// rather than the old "lose a point every six months" approximation. Empty for the vast
    /// majority of players.
    /// </summary>
    public List<ValueObjects.DemeritEntry> DemeritLog { get; set; } = new();

    /// <summary>Demerit points on record within the ICC's 24-month rolling window as of <paramref name="asOf"/>.</summary>
    public int DemeritPointsIn(DateOnly asOf) =>
        DemeritLog.Where(e => e.Date > asOf.AddMonths(-24)).Sum(e => e.Points);

    /// <summary>Phase 7, Slice 7.6: total money this player has been fined over his career - a small window on his disciplinary record, read by scouting/recruitment context.</summary>
    public double CareerFines { get; set; }

    /// <summary>Phase 7, Slice 7.7: set once, on induction. A retired great in the Hall of Fame - HallOfFameService decides, at retirement.</summary>
    public bool IsHallOfFamer { get; set; }

    /// <summary>
    /// Phase 8, Slice 8.1: the club whose YOUTH SETUP this player is in, or null (the overwhelming
    /// common case - he is a senior player, a free agent, or retired). While set, he is a genuine
    /// player of that club (CurrentTeamId points at it too) and develops through the normal monthly
    /// training tick, but he is NOT in Team.SquadPlayerIds and so is never picked for a senior XI
    /// or a national pool. AcademyService clears this on promotion (added to SquadPlayerIds) or
    /// release (CurrentTeamId cleared too - he becomes a free agent).
    /// </summary>
    public Guid? AcademyTeamId { get; set; }

    /// <summary>
    /// Phase 8, Slice 8.4: individual peak-timing variation, in years. 0 is the average curve;
    /// a positive value is a LATE developer (his physical/technical peak, and the decline that
    /// follows, both arrive later), a negative value an early one. PlayerAgeingService subtracts
    /// this from the player's real age before reading its trend curves. Seeded RNG-free from the
    /// player's own already-rolled attributes + personality (WorldSeeder), and rolled freshly for
    /// an academy intake (AcademyService), so a genuine late bloomer and a teenage prodigy are
    /// both real, seeded stories rather than noise.
    /// </summary>
    public int PeakAgeOffset { get; set; }

    /// <summary>
    /// Phase 8, Slice 8.7: while on a development loan, the club that actually owns his registration
    /// (CurrentTeamId points at the borrowing club during the loan). Null when he is not on loan.
    /// LoanService returns him to this club when LoanReturnDate passes.
    /// </summary>
    public Guid? ParentClubId { get; set; }

    /// <summary>Phase 8, Slice 8.7: the date a development loan ends and the player returns to ParentClubId. Null when he is not on loan.</summary>
    public DateOnly? LoanReturnDate { get; set; }

    /// <summary>Phase 9, Slice 9.6: the terms of an active loan - fee, wage split, an option/obligation to buy. Null for a plain Phase 8 development loan or no loan at all.</summary>
    public ValueObjects.LoanAgreement? CurrentLoan { get; set; }

    /// <summary>
    /// Phase 9, Slice 9.4: the player has asked to leave - a buried fringe player wanting regular
    /// cricket, or one whose head has been turned. Raises his availability on the transfer market
    /// and lowers his fee. Cleared when he moves, or when the club talks him round.
    /// </summary>
    public bool TransferRequested { get; set; }

    /// <summary>
    /// Phase 9, Slice 9.3/9.4: the club has made the player available for transfer (listed him).
    /// A listed player is offered around actively rather than only sold if someone asks.
    /// </summary>
    public bool TransferListed { get; set; }

    /// <summary>
    /// Phase 9, Slice 9.4: while set and in the future, the player is unsettled by concrete
    /// interest from a bigger club - a morale/form drag until the situation resolves (he moves,
    /// or the window shuts and the interest goes away). Null the vast majority of the time.
    /// </summary>
    public DateOnly? UnsettledUntil { get; set; }

    /// <summary>
    /// Phase 10: the player's central-contract tier with his national board (None for the vast
    /// majority - only genuine internationals are centrally contracted). A higher tier means a
    /// bigger retainer and, crucially, the board's leverage to protect international priority: a
    /// tier-A/B player can be denied an NOC for a franchise league that clashes with an
    /// international commitment. Reviewed annually by CentralContractService.
    /// </summary>
    public Enums.CentralContractTier CentralContractTier { get; set; } = Enums.CentralContractTier.None;

    /// <summary>Phase 10: while set and in the future, the player's board has denied him an NOC - he is out of the next franchise auction. Set by CentralContractService before an auction, expires on its own.</summary>
    public DateOnly? NocWithheldUntil { get; set; }

    /// <summary>
    /// Phase 12 (§14.7): how the player handles the media - derived from personality, so it needs
    /// no seeding and can never drift out of sync. Drives press-conference contribution, how big a
    /// fan reaction a result about him draws, and (Phase 13) his commercial / image-rights value.
    /// </summary>
    public Enums.MediaPersona MediaPersona =>
        Personality.HasFlag(Enums.PersonalityTrait.Aggressive) || Personality.HasFlag(Enums.PersonalityTrait.RiskTaker)
            ? Enums.MediaPersona.Outspoken
        : Personality.HasFlag(Enums.PersonalityTrait.MoneyFocused) || Personality.HasFlag(Enums.PersonalityTrait.Ambitious)
            ? Enums.MediaPersona.Marketable
        : Personality.HasFlag(Enums.PersonalityTrait.Professional) || Personality.HasFlag(Enums.PersonalityTrait.TeamOriented)
            ? Enums.MediaPersona.Balanced
        : Enums.MediaPersona.Guarded;

    /// <summary>
    /// Seven-suggestions pass (S3): the player's individual COMMERCIAL APPEAL, 0-100 - his brand /
    /// endorsement value, distinct from his match fee and contract wage. Reputation-driven, scaled
    /// by how marketable a figure he is (<see cref="MediaPersona"/>) and nudged by recent form.
    /// A small, bounded flavour layer: it feeds the club's image-rights revenue precisely, a
    /// morale term when a big-appeal player is stuck at a club that cannot showcase him, and a
    /// transfer-wish nudge when a bigger-market club comes in. Computed - no seeding.
    /// </summary>
    public double CommercialAppeal
    {
        get
        {
            double basis = Reputation.Worldwide * 0.6 + Reputation.Continental * 0.3 + Reputation.Domestic * 0.1;
            double persona = MediaPersona switch
            {
                Enums.MediaPersona.Marketable => 1.35,
                Enums.MediaPersona.Outspoken => 1.12,
                Enums.MediaPersona.Balanced => 1.0,
                _ => 0.8
            };
            double form = 1.0 + Math.Clamp(Form.CurrentForm, -40, 40) / 400.0;
            return Math.Clamp(basis * persona * form, 0, 100);
        }
    }

    /// <summary>
    /// Phase 14 (§18.5): the shape of the player's career ambition - derived from personality, so
    /// it needs no seeding. Drives how he weighs a contract renewal, a transfer and a request to leave.
    /// </summary>
    public Enums.AmbitionDirection AmbitionDirection =>
        Personality.HasFlag(Enums.PersonalityTrait.Loyal)
            ? Enums.AmbitionDirection.OneClubLegend
        : Personality.HasFlag(Enums.PersonalityTrait.MoneyFocused)
            ? Enums.AmbitionDirection.Globetrotter
        : Personality.HasFlag(Enums.PersonalityTrait.Ambitious)
            ? Enums.AmbitionDirection.TrophyHunter
        : Enums.AmbitionDirection.Journeyman;

    /// <summary>Phase 14 (§6.3): a technical flaw a good coach can slowly remediate and a scout can spot - a small, exploitable in-match vulnerability while it stands. Null for the vast majority.</summary>
    public Enums.TechnicalFlaw? TechnicalFlaw { get; set; }

    /// <summary>Phase 14: while set and in the future, the player is on personal leave (a birth, a bereavement, a legal/visa issue) and unavailable.</summary>
    public DateOnly? PersonalLeaveUntil { get; set; }

    /// <summary>Phase 13 (§9.4): the club this player would take a pay cut to join. Derived from AmbitionDirection + reputation the first time it is asked, then fixed.</summary>
    public Guid? DreamClubId { get; set; }

    /// <summary>Phase 13 (§9.2): the player is holding out - refusing to play until his contract is renegotiated. Cleared when the club improves his terms or he is sold.</summary>
    public bool HoldingOut { get; set; }

    /// <summary>Phase 13 (§9.6): the player's agent, when he has one. A well-regarded agent wins him better wages and bigger moves (and takes a cut). Null = no agent - most fringe / young players.</summary>
    public Guid? AgentId { get; set; }

    public int Age(DateOnly asOf) =>
        asOf.Year - DateOfBirth.Year - (asOf < DateOfBirth.AddYears(asOf.Year - DateOfBirth.Year) ? 1 : 0);

    /// <summary>
    /// Section 7/10: recomputes format suitability from raw attributes.
    /// Deliberately NOT a single overall rating - weights differ per format.
    /// Called whenever Batting/Bowling/Mental attributes or derived traits change.
    /// Independent of current form by design - see the note inside.
    /// </summary>
    public void RecalculateFormatSuitability()
    {
        // Test: technique, defense, concentration, patience matter most; SR barely matters.
        double test = Weighted(
            (Batting.Technique, 0.22),
            (Batting.DefensiveAbility, 0.20),
            (Mental.Concentration, 0.18),
            (Mental.PressureHandling, 0.12),
            (Batting.AgainstPace, 0.10),
            (Batting.AgainstSpin, 0.10),
            (Batting.RiskManagement, 0.08));

        // ODI: balance of technique and strike rotation/boundary hitting. PressureHandling
        // matters here too - a chase comes down to a real crunch at some point - just less than
        // it does in T20, where almost every ball carries that weight.
        double odi = Weighted(
            (Batting.Technique, 0.12),
            (Batting.StrikeRotation, 0.16),
            (Batting.BoundaryHitting, 0.14),
            (Mental.Adaptability, 0.12),
            (Batting.DeathOverBatting, 0.14),
            (Batting.AgainstPace, 0.11),
            (Batting.AgainstSpin, 0.11),
            (Mental.PressureHandling, 0.10));

        // T20: power hitting, SR, death overs dominate; technique matters least. PressureHandling
        // carries the HEAVIEST weight of all three formats here - the format with the fewest
        // balls and the tightest margins is exactly where a player's head under pressure shows
        // up the most, for batting and bowling alike (see the mirrored bowling formula below).
        double t20 = Weighted(
            (Batting.PowerHitting, 0.22),
            (Batting.BoundaryHitting, 0.18),
            (Batting.DeathOverBatting, 0.16),
            (Batting.StrikeRotation, 0.12),
            (Mental.Adaptability, 0.10),
            (Batting.Aggression, 0.08),
            (Mental.PressureHandling, 0.14));

        // Form is deliberately NOT blended in here any more. Format suitability answers
        // "how well does this player's SKILL SET fit this format" - a stable property of who
        // the player is. Folding current form into it meant (a) a player's format fit drifted
        // every time they had a bad week, which is not what the number means, and (b) any
        // consumer that also weighed form - the selection evaluator does - counted form twice
        // and let it swamp everything else. Form is applied once, by the consumer.
        // Traits nudge format fit too - a player who is genuinely a Finisher/PowerHitter
        // by trait (not just raw attributes) should skew further toward white-ball formats,
        // and a genuine ClassicalBatter/Anchor should skew toward Test cricket. Kept as a
        // modest nudge (not a dominant term) since attributes remain the primary driver.
        //
        // Guard: traits default to 0 until RoleTraitDeriver.ApplyTo() runs. Without this
        // check, an undapted player (traits all 0) would read as "definitely bad at every
        // trait" against the neutral baseline of 50 below, applying a spurious penalty to
        // every format. Skip the nudge entirely until real trait data exists.
        bool hasTraitData = BattingTraits.Finisher + BattingTraits.PowerHitter + BattingTraits.ClassicalBatter + BattingTraits.Anchor > 0;
        double whiteBallTraitNudge = hasTraitData ? (BattingTraits.Finisher + BattingTraits.PowerHitter) / 2.0 - 50 : 0; // -50..+50
        double testTraitNudge = hasTraitData ? (BattingTraits.ClassicalBatter + BattingTraits.Anchor) / 2.0 - 50 : 0;

        FormatSuitability.TestSuitability = Math.Clamp(test + testTraitNudge * 0.15, 0, 100);
        FormatSuitability.OdiSuitability = Math.Clamp(odi + whiteBallTraitNudge * 0.08, 0, 100);
        FormatSuitability.T20Suitability = Math.Clamp(t20 + whiteBallTraitNudge * 0.15, 0, 100);

        // Bowling side of the same idea (squad-selection follow-up, allrounder-preference rule) -
        // Bowling.*/Mental.* only, mirroring the batting formulas' own attribute-domain
        // discipline above. No trait nudge: BowlingTraits exist but aren't format-specific
        // labels the way Finisher/ClassicalBatter are, so there is nothing equivalent to nudge
        // with yet.
        double testBowling = Weighted(
            (Bowling.Accuracy, 0.22),
            (Bowling.AttackingAbility, 0.20),
            (Bowling.NewBallBowling, 0.12),
            (Bowling.Seam, 0.10),
            (Bowling.Swing, 0.10),
            (Bowling.Spin, 0.06),
            (Mental.Concentration, 0.12),
            (Mental.PressureHandling, 0.08));

        double odiBowling = Weighted(
            (Bowling.Accuracy, 0.16),
            (Bowling.Containment, 0.16),
            (Bowling.MiddleOverBowling, 0.14),
            (Bowling.DeathBowling, 0.14),
            (Bowling.Variation, 0.11),
            (Bowling.AttackingAbility, 0.11),
            (Mental.Adaptability, 0.08),
            (Mental.PressureHandling, 0.10));

        // Heaviest PressureHandling weight of the three, same reasoning as the batting T20
        // formula above - bowling the last over of a T20 is as pressured a job as cricket has.
        double t20Bowling = Weighted(
            (Bowling.DeathBowling, 0.18),
            (Bowling.Yorker, 0.14),
            (Bowling.SlowerBall, 0.12),
            (Bowling.Variation, 0.14),
            (Bowling.Containment, 0.12),
            (Mental.Adaptability, 0.08),
            (Bowling.AttackingAbility, 0.08),
            (Mental.PressureHandling, 0.14));

        FormatSuitability.TestBowlingSuitability = Math.Clamp(testBowling, 0, 100);
        FormatSuitability.OdiBowlingSuitability = Math.Clamp(odiBowling, 0, 100);
        FormatSuitability.T20BowlingSuitability = Math.Clamp(t20Bowling, 0, 100);
    }

    private static double Weighted(params (int value, double weight)[] terms)
        => Common.AbilityScale.AttributeSumToHundredRaw(terms.Sum(t => t.value * t.weight));
}
