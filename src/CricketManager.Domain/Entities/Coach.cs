using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

/// <summary>NEW-D: an extra backroom role a head coach fills himself, alongside the head-coach job.</summary>
public sealed record CoachExtraRole(Guid TeamId, StaffRole Role);

public sealed class Coach
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";
    public DateOnly DateOfBirth { get; set; }
    public string Nationality { get; set; } = string.Empty;

    public bool IsHumanControlled { get; set; }

    public PlayingExperience PlayingExperience { get; set; }
    public CoachingLicense License { get; set; } = CoachingLicense.None;
    public CoachingPhilosophy Philosophy { get; set; } = CoachingPhilosophy.PerformanceFocused;

    public CoachAttributes Attributes { get; set; } = new();
    public Reputation Reputation { get; set; } = new();

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 7: the coach's multi-year track record - titles,
    /// major finals, cumulative overperformance, and the "seasons since a trophy at this club"
    /// clock. What a board's judgment actually weighs, on top of Part 4's single-year objectives.
    /// </summary>
    public CoachCareerRecord CareerRecord { get; set; } = new();

    // Section 93: board trust and authority are distinct from reputation.
    public double BoardTrust { get; set; } = 50;      // 0-100, resets per job
    public double Authority { get; set; } = 30;       // 0-100, grows with reputation + tenure
    public double CareerSatisfaction { get; set; } = 70; // Section 97

    public Guid? CurrentTeamId { get; set; }
    public Guid? CurrentContractId { get; set; }

    /// <summary>
    /// Meeting-driven-selection ticket, requirement C: every team this coach has genuinely worked
    /// with - his year-round clubs, national jobs, and every franchise campaign. Overlap with a
    /// player's <see cref="Player.CareerTeamIds"/> is what gives the coach a real first-hand read
    /// on that player in an auction, on top of (not instead of) the scouting department's numbers.
    /// Populated at every hire/appointment and seeded with his starting team.
    /// </summary>
    public HashSet<Guid> CareerTeamIds { get; set; } = new();

    /// <summary>
    /// Post-Phase-7/8/9 rectification (Section H): franchise coaching is CAMPAIGN-based, not
    /// year-round - a franchise head/specialist coach is free to hold another job (a different
    /// league, a domestic role) as long as the windows genuinely do not clash. This is that
    /// second job: a franchise team id he coaches for the length of that league's campaign only.
    /// An INTERNATIONAL role is year-round and cannot be combined with a franchise role.
    /// </summary>
    public Guid? FranchiseCoachingTeamId { get; set; }

    public bool IsRetired { get; set; }

    /// <summary>Section A: accumulated ICC-code-of-conduct demerit points - team management is covered by the same code. See DisciplineService.</summary>
    public int DemeritPoints { get; set; }

    /// <summary>Section A: total money this coach has been fined over his career - a code-of-conduct charge (usually a press-conference remark) hits this.</summary>
    public double CareerFines { get; set; }

    /// <summary>
    /// Phase 12 (§4.2): the coach's worldwide earnings, split by the kind of job. A modern coach's
    /// living is a portfolio - a domestic salary, maybe a national contract, and franchise campaign
    /// fees on top - and this is what a job offer is weighed against. Credited annually and on each
    /// franchise campaign appointment.
    /// </summary>
    public CoachEarningsLedger CareerEarnings { get; set; } = new();

    /// <summary>
    /// Phase 12 (§4.3): the coach's standing on the FRANCHISE circuit specifically - 0-100, driven
    /// by franchise titles won across all leagues. A big circuit name commands a bigger campaign
    /// fee and gets first refusal on the best franchise jobs, independent of his domestic reputation.
    /// </summary>
    public double CircuitReputation { get; set; } = 25;

    /// <summary>
    /// Corrections pass (correction 4): a national coach calls a selection / pool meeting the
    /// moment his tenure starts, before any automatic trigger would fire. Set true once he has.
    /// Reset when he leaves the national job (so a new appointee calls his own).
    /// </summary>
    public bool CalledInitialPoolMeeting { get; set; }

    /// <summary>
    /// Seven-suggestions pass (NEW-C): 0-100. If this coach is a former player, how substantial his
    /// playing career was (weighted caps, reputation, longevity, leadership). 0 = a career coach who
    /// never played at a meaningful level. Weights his standing and his mentor-role eligibility.
    /// </summary>
    public double PlayingCareerWeight { get; set; }

    /// <summary>NEW-C: the Player id this coach retired from, if he is a former player.</summary>
    public Guid? FromPlayerId { get; set; }

    /// <summary>
    /// Seven-suggestions pass (NEW-D): additional roles this head coach also fills himself - his own
    /// bowling / batting / fielding coach, or a selector - when the team cannot afford or does not
    /// need a separate specialist AND his matching attribute is strong AND he is willing. At most 2.
    /// </summary>
    public List<CoachExtraRole> AdditionalRoles { get; set; } = new();

    /// <summary>
    /// Post-Phase-16 completion pass (§4.7): the one job this coach has always wanted. He will take
    /// it even for a lateral or slightly downward move - a genuine pull the ordinary
    /// career-benefit maths does not capture. Null = no particular dream job (most coaches).
    /// </summary>
    public Guid? DreamJobTeamId { get; set; }

    /// <summary>
    /// Phase 12 (§4.6): a coach's format specialisation. Most are all-format (the default); some
    /// are red-ball purists or white-ball innovators, and a club can appoint a white-ball assistant
    /// alongside an all-format or red-ball head coach. Derived from playing/coaching background;
    /// drifts over a career with the campaigns actually coached.
    /// </summary>
    public FormatSpecialisation FormatFocus { get; set; } = FormatSpecialisation.AllFormats;

    /// <summary>
    /// Phase 12 (§4.6): when set, this coach is a FORMAT-SPECIALIST ASSISTANT to the head coach of
    /// the named team - he runs the side in his format (see <see cref="FormatFocus"/>) and adds his
    /// quality on top of the head coach's in that format only. Null for a head coach.
    /// </summary>
    public Guid? AssistantToTeamId { get; set; }

    public int Age(DateOnly asOf) =>
        asOf.Year - DateOfBirth.Year - (asOf < DateOfBirth.AddYears(asOf.Year - DateOfBirth.Year) ? 1 : 0);

    /// <summary>
    /// Section 79/84: seeds a coach's starting attributes from their background rather
    /// than assigning arbitrary numbers. Called once at coach creation.
    /// </summary>
    public void SeedAttributesFromHistory()
    {
        // Playing pedigree boosts people-facing/tactical-authority attributes.
        int playingBonus = PlayingExperience switch
        {
            PlayingExperience.InternationalStar or PlayingExperience.FormerCaptain => 6,
            PlayingExperience.International => 4,
            PlayingExperience.FirstClass => 2,
            PlayingExperience.DomesticLevel => 1,
            _ => 0
        };
        Attributes.Leadership += playingBonus;
        Attributes.ManManagement += playingBonus;
        Attributes.PlayerManagement += playingBonus;
        Reputation.Adjust(domesticDelta: playingBonus * 2, continentalDelta: playingBonus * 0.5);

        // Coaching license boosts tactical/technical/development attributes.
        int licenseBonus = License switch
        {
            CoachingLicense.InternationalElite => 8,
            CoachingLicense.Advanced => 6,
            CoachingLicense.Level3 => 4,
            CoachingLicense.Level2 => 3,
            CoachingLicense.Level1 => 2,
            CoachingLicense.Basic => 1,
            _ => 0
        };
        Attributes.TacticalKnowledge += licenseBonus;
        Attributes.TechnicalKnowledge += licenseBonus;
        Attributes.PlayerDevelopment += licenseBonus;
        Attributes.MatchPreparation += licenseBonus;

        ClampAllAttributes();
    }

    /// <summary>
    /// Tech-debt item 6: an explicit clamp list rather than reflection over
    /// <c>typeof(CoachAttributes).GetProperties()</c> - refactor-safe (a renamed/added attribute
    /// is a compile error here, not a silent miss) and free of the boxing/reflection cost. Kept as
    /// one method the two places that need it call.
    /// </summary>
    private void ClampAllAttributes()
    {
        var a = Attributes;
        static int C(int v) => Math.Clamp(v, 1, 20);
        a.BattingCoaching = C(a.BattingCoaching); a.BowlingCoaching = C(a.BowlingCoaching);
        a.FieldingCoaching = C(a.FieldingCoaching); a.FitnessCoaching = C(a.FitnessCoaching);
        a.TacticalKnowledge = C(a.TacticalKnowledge); a.MatchPreparation = C(a.MatchPreparation);
        a.PlayerDevelopment = C(a.PlayerDevelopment); a.YouthDevelopment = C(a.YouthDevelopment);
        a.TechnicalKnowledge = C(a.TechnicalKnowledge);
        a.Leadership = C(a.Leadership); a.Motivation = C(a.Motivation); a.Discipline = C(a.Discipline);
        a.PlayerManagement = C(a.PlayerManagement); a.MediaHandling = C(a.MediaHandling);
        a.ManManagement = C(a.ManManagement); a.Adaptability = C(a.Adaptability);
        a.PressureHandling = C(a.PressureHandling); a.DecisionMaking = C(a.DecisionMaking);
        a.OppositionAnalysis = C(a.OppositionAnalysis); a.Statistics = C(a.Statistics);
        a.Scouting = C(a.Scouting); a.TacticalAnalysis = C(a.TacticalAnalysis); a.MatchReading = C(a.MatchReading);
        a.Professionalism = C(a.Professionalism); a.Ambition = C(a.Ambition);
        a.Loyalty = C(a.Loyalty); a.WorkEthic = C(a.WorkEthic);
    }
}
