using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

public sealed class Team
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    // Section 29: underlying strength affects probability/decision quality, NOT results directly.
    public double Strength { get; set; } = 50; // 0-100

    /// <summary>
    /// Phase 6, Slice 6.7: true for a national team (the side that represents Country in
    /// international cricket) rather than a domestic club. Its SquadPlayerIds is a POOL - the
    /// eligible players of that nationality, refreshed by NationalSelectionService - not a fixed
    /// club roster. Club-vs-country: a player named in a national squad for an active
    /// international window is unavailable for his club (UnavailabilityReason.InternationalDuty).
    /// </summary>
    public bool IsNational { get; set; }

    /// <summary>
    /// Post-Phase-9 rectification (Section K): true for a franchise-league team. A franchise runs
    /// on a genuinely different economic model from a domestic club - a fixed auction purse, equal
    /// for every franchise, reset every year; NO season sponsorship/upkeep loop, NO financial fair
    /// play, and it can never go bankrupt. SeasonFinanceService and FinancialFairPlayService both
    /// skip these; the domestic transfer / free-agent markets ignore them. Replaces the earlier
    /// "[F] " name-prefix check, which is still applied by the seeder for display.
    /// </summary>
    public bool IsFranchise { get; set; }

    // How well known and well regarded the team is - deliberately NOT the same thing as how
    // good they currently are. A fallen giant keeps its reputation (and its commercial pull)
    // for years after the squad declines, and a newly promoted side can be strong and still
    // draw nobody. SponsorshipValuationService and MatchdayRevenueService previously used
    // Strength as a stand-in for this, which made those two things impossible to separate.
    // Tiered like Player/Coach reputation for the same reason: domestic standing and
    // continental/global recognition are different numbers.
    public Reputation Reputation { get; set; } = new(domestic: 40);

    public double BoardConfidence { get; set; } = 60; // 0-100, Section 4/90
    public double FinancialHealth { get; set; } = 60; // 0-100

    public Guid? CurrentCoachId { get; set; }

    /// <summary>
    /// Seven-suggestions pass (S2): a national board's head-coach split, mirroring
    /// <see cref="CaptaincyPattern"/>. Every nation seeds <see cref="CoachingStructure.Unified"/>;
    /// CoachingStructureService can split it (and reunify it) under format-clash pressure.
    /// </summary>
    public CoachingStructure CoachingStructure { get; set; } = CoachingStructure.Unified;

    /// <summary>S2: when the structure is split, the head coach for each format. Empty when Unified - use <see cref="CoachForFormat"/>, which falls back to <see cref="CurrentCoachId"/>.</summary>
    public Dictionary<MatchFormat, Guid> FormatCoachIds { get; set; } = new();

    /// <summary>S2: the head coach responsible for this format - the split's coach if one is set, otherwise the unified <see cref="CurrentCoachId"/>.</summary>
    public Guid? CoachForFormat(MatchFormat format) =>
        FormatCoachIds.TryGetValue(format, out var id) ? id : CurrentCoachId;

    /// <summary>S2: accumulated coordination friction from running a split coaching structure - squad planning, player workload, message consistency. Rises while split, eases while unified. Feeds the reunification decision.</summary>
    public double CoachingCoordinationFriction { get; set; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: while the head-coach chair is vacant, the
    /// assistant coach runs things as interim - this is his StaffMember id. Null when there is a
    /// permanent coach (CurrentCoachId set) or no assistant to step up. Cleared when a permanent
    /// appointment is made, or when the interim himself is promoted to a real Coach entity.
    /// </summary>
    public Guid? InterimCoachStaffId { get; set; }

    /// <summary>Wave 4: when the interim took charge - used to judge whether his run has been long/good enough to justify making him permanent.</summary>
    public DateOnly? InterimSince { get; set; }

    public List<Guid> SquadPlayerIds { get; set; } = new();

    /// <summary>
    /// Phase 8, Slice 8.1: the club's youth academy - prospect player ids, distinct from the senior
    /// squad. These players develop through the normal training tick but are never selected for a
    /// senior XI. AcademyService moves an id out of here on promotion (into SquadPlayerIds) or
    /// release (removed entirely, the player becomes a free agent). Empty for a club with no
    /// academy seeded, so every pre-Phase-8 team is unaffected.
    /// </summary>
    public List<Guid> AcademyPlayerIds { get; set; } = new();

    // Captaincy is per format on purpose - split captaincy is normal in modern cricket
    // (several Test sides field a different white-ball captain), and a single CaptainId
    // would have forced every team into a structure real cricket abandoned years ago.
    public Dictionary<MatchFormat, Guid> CaptainsByFormat { get; set; } = new();

    /// <summary>Backroom staff - analysts, physios, specialist coaches. Section 33. The people a coach hires around him.</summary>
    public List<Guid> StaffIds { get; set; } = new();

    /// <summary>
    /// Post-Phase-6 (section D): how this team splits the captaincy. The Head Coach's call - see
    /// CaptaincyAppointmentService. Defaults to a unified captaincy, which is what every
    /// pre-section-D team effectively had.
    /// </summary>
    public CaptaincyPattern CaptaincyPattern { get; set; } = CaptaincyPattern.Unified;

    public Guid? GetCaptain(MatchFormat format) =>
        CaptainsByFormat.TryGetValue(format, out var id) ? id : null;

    public void SetCaptain(MatchFormat format, Guid playerId) => CaptainsByFormat[format] = playerId;

    /// <summary>The vice-captain per format - the succession pipeline the Wave 4 residual-growth work reads. Section D lets the coach set this deliberately.</summary>
    public Dictionary<MatchFormat, Guid> ViceCaptainsByFormat { get; set; } = new();

    public Guid? GetViceCaptain(MatchFormat format) =>
        ViceCaptainsByFormat.TryGetValue(format, out var id) ? id : null;

    // Phase 3 additions
    public Guid? HomeGroundId { get; set; }
    public TeamFacilities Facilities { get; set; } = new();
    public TeamFinances Finances { get; set; } = new();

    /// <summary>The squad's collective mood - see TeamMorale's own doc comment. Distinct from Reputation (standing) and Strength (underlying quality).</summary>
    public TeamMorale Morale { get; set; } = new();

    /// <summary>Positive = consecutive wins, negative = consecutive losses, 0 = no active streak (a draw/no-result/tie breaks a streak without starting the other one). Set by TeamMoraleService.ApplyMatchResult - what makes "losing after a long winning streak" and "a losing streak" mean something without needing to rescan match history.</summary>
    public int CurrentStreak { get; set; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 6: team-form momentum, -100..100. A DISTINCT mechanic
    /// from CurrentStreak (a raw count) and Morale (mood): momentum is a trajectory - a side on
    /// the up, or in a spiral - that builds and decays FASTER than morale does, and is
    /// personality-modulated on a break. Set by TeamMoraleService.ApplyMatchResult, decayed on
    /// the monthly tick, read as a small term in TeamMoraleService.PerformanceMultiplier.
    /// </summary>
    public double FormMomentum { get; set; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: parts of the head coach's job he has formally
    /// delegated to backroom staff - the out-of-match counterpart to TacticalPlan's
    /// DecisionAuthority. Empty by default (the coach does everything himself), so every existing
    /// team is unaffected.
    /// </summary>
    public HashSet<DelegatedResponsibility> DelegatedResponsibilities { get; set; } = new();

    /// <summary>
    /// Post-Phase-6 (section A/G): the club's cultural identity, in the same vocabulary a coach's
    /// philosophy uses. A board weighs an applicant's own philosophy and personality against this,
    /// not just a reputation number. Defaults to PerformanceFocused (results first, no strong lean).
    /// </summary>
    public CoachingPhilosophy CulturalIdentity { get; set; } = CoachingPhilosophy.PerformanceFocused;

    /// <summary>
    /// Post-Phase-6 (section B): when the head coach has delegated non-head-coach staffing to a
    /// GeneralManager, this is that staff member's id. Null = the head coach handles it himself
    /// (or, if DelegatedResponsibility.StaffHiring is set and this is null, the board does).
    /// </summary>
    public Guid? StaffingDelegatedToStaffId { get; set; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 5: 0-100, 50 neutral. How united the dressing room
    /// is - a group pulling together behind the coach and each other, or a fractured one. Driven
    /// by DressingRoomService from the spread of senior players' CoachTrust, results and morale.
    /// A separate concept from TeamMorale (collective mood) and Strength (quality): a strong,
    /// winning side can still have a poisonous room, and a weak, losing one can be tight-knit.
    /// </summary>
    public double DressingRoomHarmony { get; set; } = 50;

    /// <summary>
    /// Post-Phase-16 completion pass (§5.14): the squad ids as they were at the last annual
    /// rollover, and the resulting continuity (0-1, share of THIS squad that was here last year).
    /// A settled, low-churn squad performs above the sum of its parts - it lifts DressingRoomHarmony
    /// and carries a small on-field cohesion edge. Set by ProcessAnnualRollover.
    /// </summary>
    public List<Guid> PreviousSquadIds { get; set; } = new();
    public double SquadContinuity { get; set; } = 1.0;

    /// <summary>
    /// Phase 6, Slice 6.2: the human coach's standing instructions for the parts of running the
    /// club the world simulation would otherwise auto-resolve. Only read for the team whose coach
    /// is IsHumanControlled; an AI club ignores it entirely. Every field defaults to "let the AI
    /// handle it", so a new save behaves like an AI-run one until the player changes something.
    /// </summary>
    public ManagerPreferences ManagerPreferences { get; set; } = new();

    /// <summary>
    /// Phase 7, Slice 7.3: the board behind the club - ambition, patience, wealth, ownership,
    /// fan sentiment and the season budget. Defaults to a plausible mid-table board so every
    /// existing team is unaffected. BoardService and BoardRelationshipService read and move it.
    /// </summary>
    public ClubBoard Board { get; set; } = new();

    /// <summary>
    /// Phase 7, Slice 7.8: the national board - selection panel, chairman of selectors, and the
    /// political scrutiny a national coach carries. Null for a club; set for a Team with
    /// IsNational = true (WorldSeeder does this, and it defaults to a sensible one if left unset).
    /// </summary>
    public NationalBoard? NationalBoard { get; set; }

    // ---- Phase 13: Auction & Market Depth ----

    /// <summary>Phase 13 (§8.1): a franchise's auction philosophy - reshapes its plan and how hard it bids. Only meaningful for a franchise team; seeded per franchise.</summary>
    public FranchiseArchetype FranchiseArchetype { get; set; } = FranchiseArchetype.Balanced;

    /// <summary>
    /// Seven-suggestions pass (S1): the multi-league OWNERSHIP GROUP this franchise belongs to
    /// (Reliance's Mumbai Indians / MI Cape Town / MI New York; the Sunrisers, Capitals and Royals
    /// groups). Sister franchises share scouting and development and give each other a first-hand
    /// read into an auction and a soft group-retention pull. Null for an independently-owned team.
    /// </summary>
    public Guid? OwnershipGroupId { get; set; }

    /// <summary>
    /// Meeting-driven-selection ticket (B): the year the franchise's <see cref="FranchiseArchetype"/>
    /// last shifted. A hysteresis clock - identity does not change again for a few years after,
    /// which keeps <see cref="CricketManager.Domain.Services.FranchiseIdentityService"/>
    /// deterministic (no dice roll) and stops it flip-flopping year to year.
    /// </summary>
    public int LastIdentityShiftYear { get; set; }

    /// <summary>
    /// Phase 13 (§8.9): 0-100. A franchise's DYNASTY rating - squad continuity year over year plus
    /// trophies. A settled, winning core builds a real aura: it lifts fan sentiment and sponsor
    /// value and gives a small on-field culture edge. Rebuilt every auction cycle.
    /// </summary>
    public double DynastyRating { get; set; } = 40;

    /// <summary>
    /// Phase 13 (§10.4): this season's player-trading profit and loss - sale proceeds minus the
    /// cost of players bought. Feeds BoardConfidence and next season's transfer budget (a club that
    /// trades well earns the board's trust with the chequebook). Reset each annual rollover.
    /// </summary>
    public double PlayerTradingPnL { get; set; }

    /// <summary>
    /// Phase 13 (§9.7): the club's active multi-year sponsorship deals (title, kit, stadium). Signed
    /// and renewed by SponsorshipService against what the club commands at signing; paid by
    /// SeasonFinanceService for their term. Empty (the default) = the per-year sponsorship formula
    /// stands alone, exactly as before.
    /// </summary>
    public List<ValueObjects.SponsorContract> SponsorContracts { get; set; } = new();
}
