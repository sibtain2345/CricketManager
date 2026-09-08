using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>Something that happened as time passed. Typed so the inbox/news system can route and prioritise rather than parse strings.</summary>
public sealed record GameEvent(DateOnly Date, GameEventType Type, string Headline, Guid? SubjectId = null, Guid? SecondarySubjectId = null);

/// <summary>
/// Everything the clock needs to touch as it advances. Passed in rather than injected so the
/// caller keeps ownership of the world's collections and the clock stays a pure processor.
///
/// The due-date indexes are what make a 20-30 year career viable. The naive tick scanned every
/// player and every project on every one of ~11,000 days; at a realistic database size that is
/// hundreds of millions of iterations, almost all of them finding nothing, and "continue" would
/// visibly stall. Injuries and building work both have a KNOWN due date, so they are bucketed by
/// it and the tick does a dictionary lookup per day instead of a full scan.
///
/// Competitions are still scanned each day, deliberately: there are dozens of them, not
/// thousands, and their windows can't be reduced to a single date.
/// </summary>
public sealed class WorldState
{
    public required IDictionary<Guid, Team> Teams { get; init; }
    public required IDictionary<Guid, Ground> Grounds { get; init; }
    public required IList<Player> Players { get; init; }
    public required IList<InfrastructureProject> Projects { get; init; }
    public IList<Competition> Competitions { get; init; } = new List<Competition>();
    public IList<CoachingContract> CoachingContracts { get; init; } = new List<CoachingContract>();

    /// <summary>Phase 5 Part 2: employment contracts behind hired backroom staff - mirrors CoachingContracts exactly. Optional/defaults empty on the same terms as every other list here.</summary>
    public IList<StaffContract> StaffContracts { get; init; } = new List<StaffContract>();
    public IList<CompetitionSeason> CompetitionSeasons { get; init; } = new List<CompetitionSeason>();

    /// <summary>
    /// Coaches with a job in this world - Section D. Optional in the sense that it defaults empty
    /// and every existing save/caller that never populated it simply has no coaches evaluated at
    /// rollover, same as every other list here defaulting empty rather than requiring a caller to
    /// opt in explicitly.
    /// </summary>
    public IList<Coach> Coaches { get; init; } = new List<Coach>();

    /// <summary>
    /// Phase 5: hired backroom staff in this world - mirrors Coaches exactly (optional, defaults
    /// empty, every existing save/caller that never populated it simply has no staff available at
    /// rollover). TrainingService looks a team's relevant specialist coach up here via
    /// Team.StaffIds; with none hired for a given role, training falls back to
    /// Team.Facilities.TrainingQuality alone.
    /// </summary>
    public IList<StaffMember> Staff { get; init; } = new List<StaffMember>();

    /// <summary>Phase 12 (§14.5): the media's pundit roster - each with an old allegiance and a rival, so his takes carry a conflict of interest.</summary>
    public IList<ValueObjects.Pundit> Pundits { get; init; } = new List<ValueObjects.Pundit>();

    /// <summary>Phase 14 (§18.1): the player-to-player relationship graph - friendships, feuds, mentorships.</summary>
    public IList<ValueObjects.PlayerRelationship> PlayerRelationships { get; init; } = new List<ValueObjects.PlayerRelationship>();

    /// <summary>Phase 12 (§4.8): the coach-to-player working relationships. Formed and moved by CoachPlayerRelationshipService on the monthly tick. Optional, defaults empty.</summary>
    public IList<ValueObjects.CoachPlayerBond> CoachPlayerRelationships { get; init; } = new List<ValueObjects.CoachPlayerBond>();

    /// <summary>Phase 12 (§14.9): the fan feed - what the stands are saying, capped. FanReactionService and other services append here; the weekly digest surfaces a "From the stands" section.</summary>
    public IList<ValueObjects.NewsArchiveItem> FanFeed { get; init; } = new List<ValueObjects.NewsArchiveItem>();

    /// <summary>Phase 13 (§9.6): the player agents - persistent entities with a stable of clients and a commission. Seeded by WorldSeeder, managed by AgentService.</summary>
    public IList<PlayerAgent> Agents { get; init; } = new List<PlayerAgent>();

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 5: mentoring pairings across the world's teams -
    /// senior pros working with juniors. Optional, defaults empty. MentoringService reviews these
    /// quarterly (forming new pairings, dissolving stale ones) and applies their effect monthly.
    /// </summary>
    public IList<ValueObjects.MentoringGroup> MentoringGroups { get; init; } = new List<ValueObjects.MentoringGroup>();

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 8: ICC-style ranking rows (one per team per format).
    /// Optional, defaults empty. RankingService moves points after matches; the world clock's
    /// monthly tick reads them to feed team reputation.
    /// </summary>
    public IList<TeamRanking> TeamRankings { get; init; } = new List<TeamRanking>();

    /// <summary>
    /// How many matches each player got this season. Reset on rollover; read by retirement,
    /// because playing time is the strongest predictor there is.
    ///
    /// An EMPTY dictionary means no season has been simulated yet, which retirement treats as
    /// unknown rather than as "nobody was picked". Phase 4's match engine is the writer - it
    /// should call RecordAppearance for every player in every XI.
    /// </summary>
    public Dictionary<Guid, int> MatchesThisSeason { get; } = new();

    /// <summary>
    /// Same idea as MatchesThisSeason, broken down by format - Section H (format-specific
    /// retirement) needs "how much Test cricket did he actually get this season" as its own
    /// signal, separate from his overall workload. Reset on rollover alongside MatchesThisSeason.
    /// </summary>
    public Dictionary<Guid, Dictionary<MatchFormat, int>> MatchesThisSeasonByFormat { get; } = new();

    /// <summary>
    /// Post-Phase-5 rectification pass: how much genuine attribute development each player has
    /// picked up recently, from training AND from match experience (Wave 2), accumulated since
    /// the last quarterly reset. Written by ProcessMonthlyTick's training pass and by
    /// MatchDevelopmentService; READ by Wave 4's ProcessQuarterlyTick, where a specialist coach
    /// whose own cluster of players is genuinely outdeveloping the team baseline grows faster
    /// than tenure alone would give him. Reset to empty each quarterly tick. Inert (nobody reads
    /// it) until Wave 4 wires the staff-development signal.
    /// </summary>
    public Dictionary<Guid, double> RecentDevelopmentByPlayer { get; } = new();

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: captaincy profiles for the world's captains and
    /// vice-captains, keyed by player id - the store CaptaincyProfile never had (see
    /// CaptaincyService's own note on it being ephemeral). Optional, defaults empty; a world that
    /// never populates it simply has no captain growth crystallised at the monthly tick, exactly
    /// like Coaches/Staff defaulting empty. A match's MatchLeadership.Profile should be the SAME
    /// object instance stored here, so per-match accumulation and monthly crystallisation act on
    /// one profile rather than diverging copies.
    /// </summary>
    public IDictionary<Guid, ValueObjects.CaptaincyProfile> CaptaincyProfiles { get; init; } = new Dictionary<Guid, ValueObjects.CaptaincyProfile>();

    /// <summary>
    /// Phase 6, Slice 6.1: the world's scheduled matches. FixtureGenerationService/
    /// PlayoffBracketService produce these; FixturePlayService plays the ones due on the current
    /// day as the clock advances. Optional, defaults empty - a world that never generates fixtures
    /// simply has no matches played automatically, exactly like every other list here.
    /// </summary>
    public IList<Fixture> Fixtures { get; init; } = new List<Fixture>();

    /// <summary>Phase 6, Slice 6.1: team-to-team rivalries. Read by FixturePlayService to lift a derby's importance and widen its morale/reputation swing; reinforced/faded after a meeting that mattered.</summary>
    public IList<Rivalry> Rivalries { get; init; } = new List<Rivalry>();

    /// <summary>Phase 6, Slice 6.2: squads announced by AiClubManagementService (and, for a human club, by the player) ahead of a competition. FixturePlayService restricts an XI to the relevant announced squad.</summary>
    public IList<SquadAnnouncement> SquadAnnouncements { get; init; } = new List<SquadAnnouncement>();

    /// <summary>
    /// Phase 6, Slice 6.3: per-season accumulation of each player's combined match rating (the
    /// same number Player of the Match uses), keyed seasonId -> playerId -> summed rating.
    /// FixturePlayService adds to it after every fixture; CompetitionSeasonRunner reads it to name
    /// the player of the series when the season finishes, then clears that season's entry.
    /// </summary>
    public IDictionary<Guid, Dictionary<Guid, (string Name, double Rating)>> SeasonContributions { get; init; }
        = new Dictionary<Guid, Dictionary<Guid, (string, double)>>();

    /// <summary>
    /// Phase 6, Slice 6.3: the participant list a future competition season should be created with,
    /// keyed (competitionId, year). Written by CompetitionSeasonRunner when a linked two-tier
    /// pair's seasons both finish and promotion/relegation is resolved; read when that year's
    /// season is created, in place of simply carrying the previous participants forward. Keeping
    /// it here rather than recomputing at creation time is what makes promotion/relegation apply
    /// exactly once even when a cross-year window means the just-finished season is not year-1.
    /// </summary>
    public IDictionary<(Guid CompetitionId, int Year), List<Guid>> PlannedRosters { get; init; }
        = new Dictionary<(Guid, int), List<Guid>>();

    /// <summary>
    /// Phase 10 (domestic pyramid): the (topCompetitionId, year) pairs whose promotion/relegation
    /// swap has already been resolved this year - so a division sitting in the MIDDLE of a
    /// three-tier chain (which is the "top" of one linked pair and the "bottom" of another) has
    /// each of its two swaps applied exactly once, regardless of which division finished first.
    /// </summary>
    public ISet<string> PromotionRelegationResolved { get; init; } = new HashSet<string>();

    /// <summary>
    /// Meeting-driven-selection ticket (requirement A): the CompetitionSeason ids for which a
    /// national side has already held its once-ahead-of-a-major-tournament pool meeting - so the
    /// extra pre-tournament refresh (on top of the ordinary annual one) fires exactly once per
    /// tournament instance, not every quarterly tick the lookahead window happens to still be open.
    /// </summary>
    public ISet<Guid> PreTournamentPoolMeetingsHeld { get; init; } = new HashSet<Guid>();

    /// <summary>
    /// Phase 10: a running World-Cup-qualification tally per national team, moved by bilateral
    /// series and the Test/ODI Championships. Feeds seeding / direct entry to the next global event.
    /// </summary>
    public Dictionary<Guid, double> WorldCupQualificationPoints { get; } = new();

    /// <summary>
    /// Post-Phase-6 carry-forward: per-player quarterly attribute-cluster snapshots, so
    /// development is visible over time rather than only in the current numbers. Appended on the
    /// quarterly tick, capped at a few years of history per player. Optional, defaults empty.
    /// </summary>
    public IDictionary<Guid, List<ValueObjects.PlayerDevelopmentSnapshot>> DevelopmentHistory { get; init; }
        = new Dictionary<Guid, List<ValueObjects.PlayerDevelopmentSnapshot>>();

    /// <summary>
    /// Post-Phase-6, section E: the national teams' per-format player pools (one NationalPool per
    /// national team per format). Built and evolved by NationalPoolService; national squad
    /// announcements are drawn FROM these. Optional, defaults empty.
    /// </summary>
    public IList<ValueObjects.NationalPool> NationalPools { get; init; } = new List<ValueObjects.NationalPool>();

    /// <summary>Post-Phase-6 (section A): open (and recently-filled) job adverts. JobMarketService posts and resolves these each month.</summary>
    public IList<VacancyAdvert> Vacancies { get; init; } = new List<VacancyAdvert>();

    // ---------------- Phase 9 (Auction / Contracts / Market) ----------------

    /// <summary>
    /// Phase 9, Slice 9.0: every player's employment contract - domestic (year-round) and franchise
    /// (single-season). Optional, defaults empty: a world with no contract rows falls back to the
    /// WageBillService stub for the finance loop, and the whole market is simply inert (every
    /// pre-Phase-9 test). WorldSeeder.GeneratePlayerContracts fills it for a seeded world.
    /// </summary>
    public IList<PlayerContract> PlayerContracts { get; init; } = new List<PlayerContract>();

    /// <summary>
    /// Phase 9, Slice 9.3: a world-wide market inflation index, 1.0 at world creation. Drifts up
    /// slowly each year as broadcast money grows, so a fee/wage in year 20 of a sim is not a year-1
    /// fee/wage. Every transfer fee, valuation and new wage offer is scaled by it.
    /// </summary>
    public double MarketIndex { get; set; } = 1.0;

    /// <summary>
    /// Post-Phase-7/8/9 rectification (Sections B + C): per-country administrative + economic
    /// profile, keyed by the nationality string. Optional, defaults empty - a world without
    /// profiles uses the neutral defaults everywhere (ClubMembership, allows-foreigners,
    /// EconomicScale 1.0, Northern hemisphere), which is every pre-rectification test.
    /// </summary>
    public IDictionary<string, ValueObjects.CountryProfile> CountryProfiles { get; init; }
        = new Dictionary<string, ValueObjects.CountryProfile>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Section C: the profile for a country, or a neutral default when none is seeded.</summary>
    public ValueObjects.CountryProfile ProfileFor(string? nationality)
    {
        if (nationality is not null && CountryProfiles.TryGetValue(nationality, out var p)) return p;
        return new ValueObjects.CountryProfile { Nationality = nationality ?? string.Empty };
    }

    /// <summary>
    /// Section C: the world market index scaled by a country's relative cricket-economy strength -
    /// a fee or wage denominated against a big-money nation's market is worth more. Neutral (== the
    /// bare MarketIndex) when no profile is seeded, so pre-rectification behaviour is unchanged.
    /// </summary>
    public double EffectiveMarketIndex(string? nationality) =>
        Math.Clamp(MarketIndex * ProfileFor(nationality).EconomicScale, 0.4, 6.0);

    // ---------------- Phase 7 (Board, Media & Finance depth) ----------------

    /// <summary>Phase 7, Slice 7.9: the world's umpire panel. Optional, defaults empty - a world with no umpires simply has umpiring unmodelled (every existing test), byte-identical to before. FixturePlayService assigns from this; UmpireService runs their careers.</summary>
    public IList<Umpire> Umpires { get; init; } = new List<Umpire>();

    /// <summary>Phase 7, Slice 7.5: the rendered news items the NewsEngine has produced, newest last. Optional, defaults empty - populated on the daily tick when Phase 7 news is wired.</summary>
    public IList<ValueObjects.NewsArchiveItem> NewsArchive { get; init; } = new List<ValueObjects.NewsArchiveItem>();

    /// <summary>Phase 7, Slice 7.5: the weekly digests produced on the weekly tick, newest last.</summary>
    public IList<ValueObjects.StoredDigest> Digests { get; init; } = new List<ValueObjects.StoredDigest>();

    /// <summary>Phase 7, Slice 7.7: the all-time record book, keyed by RecordEntry.Key. RecordProgressionService reads and updates it after matches.</summary>
    public IDictionary<string, ValueObjects.RecordEntry> RecordBook { get; init; } = new Dictionary<string, ValueObjects.RecordEntry>();

    /// <summary>Phase 7, Slice 7.7: everyone inducted into the Hall of Fame, in induction order.</summary>
    public IList<ValueObjects.HallOfFameInductee> HallOfFame { get; init; } = new List<ValueObjects.HallOfFameInductee>();

    /// <summary>Phase 7, Slice 7.5: every award handed out - player of the year/month, team of the season, breakthrough player.</summary>
    public IList<ValueObjects.AwardResult> Awards { get; init; } = new List<ValueObjects.AwardResult>();

    /// <summary>
    /// Phase 7, Slice 7.1: per-team matchday income banked this season, keyed by team id. Written
    /// by FixturePlayService as fixtures are played, read and cleared by the annual finance
    /// settlement. A team with no home fixtures this season simply has no entry.
    /// </summary>
    public Dictionary<Guid, double> MatchdayIncomeThisSeason { get; } = new();

    /// <summary>
    /// Phase 7, Slice 7.2: per-season crowd fill rates, keyed by competition-season id -> list of
    /// fill rates (one per fixture). Read by CompetitionReputationService at season end to move a
    /// competition's earned reputation, then cleared.
    /// </summary>
    public Dictionary<Guid, List<double>> SeasonCrowdFill { get; } = new();

    /// <summary>
    /// Phase 7, Slice 7.4: an unresolved press storyline awaiting the coach's response, keyed by
    /// coach id. Set by PressConferenceService when a notable event lands, cleared once he has
    /// faced the media. Most coaches carry none most of the time.
    /// </summary>
    public Dictionary<Guid, ValueObjects.PressStoryline> PendingPressStories { get; } = new();

    /// <summary>Phase 7, Slice 7.5: the flat per-(player,format) career-stats cache the match engine never populated. Keyed "playerId|format". Updated by CareerStatsService as matches are played; read by MilestoneService and the profile/records systems.</summary>
    public Dictionary<string, PlayerCareerStats> CareerStats { get; } = new();

    /// <summary>Phase 7, Slice 7.5: running form tallies for the current YEAR, keyed by player id. Fed by FixturePlayService, read by AwardsService at the annual rollover, cleared there.</summary>
    public Dictionary<Guid, ValueObjects.PlayerFormTally> YearForm { get; } = new();

    /// <summary>Phase 7, Slice 7.5: running form tallies for the current MONTH, keyed by player id. Cleared on the monthly tick after the player-of-the-month award is decided.</summary>
    public Dictionary<Guid, ValueObjects.PlayerFormTally> MonthForm { get; } = new();

    /// <summary>Phase 12: the live season-narrative storylines. Reviewed monthly by NarrativeService; a resolved storyline is kept until it is pruned so the payoff can be read.</summary>
    public IList<ValueObjects.Storyline> Storylines { get; init; } = new List<ValueObjects.Storyline>();

    /// <summary>Phase 16 (§15.5): every Team of the Era ever named, in order - a save's own history of who the panel rated the greats of each period.</summary>
    public IList<ValueObjects.EraTeam> EraTeams { get; init; } = new List<ValueObjects.EraTeam>();

    /// <summary>
    /// Records that a player featured in a match. The match engine's entry point into the
    /// retirement/playing-time loop. Format is optional (defaults to no format-specific record)
    /// so the existing whole-career call sites, and any caller that genuinely doesn't know the
    /// format, keep working unchanged - MatchRecorder/MultiDayMatchRecorder pass it through.
    /// </summary>
    public void RecordAppearance(Guid playerId, MatchFormat? format = null)
    {
        MatchesThisSeason[playerId] = MatchesThisSeason.TryGetValue(playerId, out var n) ? n + 1 : 1;

        if (format is not { } f) return;
        if (!MatchesThisSeasonByFormat.TryGetValue(playerId, out var byFormat))
            MatchesThisSeasonByFormat[playerId] = byFormat = new();
        byFormat[f] = byFormat.TryGetValue(f, out var m) ? m + 1 : 1;
    }

    private readonly PlayerAvailabilityService _availability = new();
    private readonly Dictionary<DateOnly, List<Player>> _injuryReturnsByDate = new();
    private readonly Dictionary<DateOnly, List<InfrastructureProject>> _projectsByDueDate = new();
    private bool _indexed;

    /// <summary>Rebuilds the due-date indexes from current state. Call after loading a save or bulk-mutating the world.</summary>
    public void Reindex()
    {
        // Flag set FIRST: TrackProject and ApplyInjury both index as a side effect, so a
        // re-entrant call during the rebuild would otherwise restart it and mutate the very
        // collection being enumerated. Snapshots are taken with ToList() for the same reason.
        _indexed = true;

        _injuryReturnsByDate.Clear();
        _projectsByDueDate.Clear();

        foreach (var player in Players.Where(p => p.CurrentInjury is not null).ToList())
            Add(_injuryReturnsByDate, player.CurrentInjury!.ExpectedReturnDate, player);

        foreach (var project in Projects.Where(p => p.Status == InfrastructureProjectStatus.UnderConstruction).ToList())
            Add(_projectsByDueDate, project.ExpectedCompletionDate, project);
    }

    /// <summary>
    /// Injures a player AND indexes the return date in one call.
    ///
    /// This is the sanctioned entry point rather than calling PlayerAvailabilityService directly,
    /// because an injury applied without being indexed would never be resolved by the clock - the
    /// player would stay injured forever and nothing would report it. An API where forgetting one
    /// call silently breaks the world is a bad API, so the two steps are welded together here
    /// instead of documented as a rule someone has to remember.
    /// </summary>
    public Injury ApplyInjury(Player player, InjuryType type, InjurySeverity severity, DateOnly date,
        int teamMedicalQuality = 50, GroundFacilities? groundFacilities = null, Random? random = null)
    {
        var injury = _availability.ApplyInjury(player, type, severity, date, teamMedicalQuality, groundFacilities, random);
        Add(_injuryReturnsByDate, injury.ExpectedReturnDate, player);
        return injury;
    }

    /// <summary>Adds a committed project to the world and indexes its completion date. Same reasoning as ApplyInjury - one call, no way to half-do it.</summary>
    public void TrackProject(InfrastructureProject project)
    {
        Projects.Add(project);
        Add(_projectsByDueDate, project.ExpectedCompletionDate, project);
    }

    internal IReadOnlyList<Player> InjuriesDueOn(DateOnly date)
    {
        EnsureIndexed();
        return _injuryReturnsByDate.TryGetValue(date, out var list) ? list : Array.Empty<Player>();
    }

    internal IReadOnlyList<InfrastructureProject> ProjectsDueOn(DateOnly date)
    {
        EnsureIndexed();
        return _projectsByDueDate.TryGetValue(date, out var list) ? list : Array.Empty<InfrastructureProject>();
    }

    private void EnsureIndexed()
    {
        if (!_indexed) Reindex();
    }

    private static void Add<T>(Dictionary<DateOnly, List<T>> index, DateOnly date, T item)
    {
        if (!index.TryGetValue(date, out var list)) index[date] = list = new List<T>();
        list.Add(item);
    }
}

/// <summary>
/// The heartbeat. Advancing the clock is the ONLY way time passes, and every dated consequence
/// in the game is processed here on the day it falls due.
///
/// Why a single tick rather than each system checking dates for itself: the systems have to
/// agree on what day it is, and they have to run in a defined order. A player whose injury
/// ends today must be available for a match today, and a stand that opens today must be
/// counted in today's gate. Scattering date checks across services means each one decides
/// independently when "today" is, which is how a save ends up with a player who is
/// simultaneously injured and selected.
///
/// The tick is day-by-day, not jump-to-date, because skipping intermediate days would skip
/// the events on them. Advancing a year processes 365 days and returns everything that
/// happened along the way - which is exactly what a career game's "continue" button needs.
///
/// What it processes today: injury recoveries, infrastructure completions (including new
/// grounds opening), competition windows opening and closing, coaching-contract expiry, and
/// season rollover. What it deliberately does NOT do yet is stated in the tech-debt notes:
/// nothing plays matches (Phase 4), nothing ages attributes or retires anyone (Phase 8), and
/// no fixtures are generated inside the competition windows it opens (Phase 4's scheduler).
/// Those are missing systems, not missing hooks - the tick is where they will attach.
/// </summary>
public sealed class WorldClockService
{
    private readonly PlayerAvailabilityService _availability = new();
    private readonly InfrastructureService _infrastructure = new();
    private readonly CompetitionCalendarService _competitionCalendar = new();
    private readonly PlayerAgeingService _ageing = new();
    private readonly RetirementService _retirement = new();
    private readonly SquadManagementService _squadManagement = new();
    private readonly MatchupConfidenceService _matchupConfidence = new();
    private readonly CoachCareerService _coachCareer = new();
    private readonly TrainingService _training = new();
    private readonly StaffCareerService _staffCareer = new();
    private readonly PartnershipChemistryService _chemistry = new();       // Wave 3: its own faster partnership decay
    private readonly CaptaincyGrowthService _captaincyGrowth = new();      // Wave 4: monthly captain-accumulator crystallisation
    private readonly InterimCoachService _interimCoach = new();            // Wave 4: assistant-steps-up on a head-coach vacancy
    private readonly DressingRoomService _dressingRoom = new();            // Wave 5: dressing-room hierarchy dynamics
    private readonly MentoringService _mentoring = new();                  // Wave 5: mentoring groups
    private readonly TeamMoraleService _teamMorale = new();                // Wave 6: team-form-momentum decay
    private readonly RankingService _rankings = new();                     // Wave 8: rankings -> reputation
    private readonly FixturePlayService _fixturePlay = new();              // Phase 6, Slice 6.1: plays the fixtures due each day
    private readonly AiClubManagementService _aiClub = new();              // Phase 6, Slice 6.2: AI-run clubs manage themselves
    private readonly CompetitionSeasonRunner _seasonRunner = new();        // Phase 6, Slice 6.3: season lifecycle - playoffs, completion, next season
    private readonly CompetitionLifecycleService _competitionLifecycle = new(); // Phase 10 (§17.5): domestic competitions expand/contract on a reputation trend
    private readonly BoardRelationshipService _board = new();              // Phase 6, Slice 6.5: in-season board confidence, mid-season sackings, offers to the human
    private readonly NationalSelectionService _nationalSelection = new(); // Phase 6, Slice 6.7: keeps each national team's player pool current
    private readonly NationalPoolMeetingService _poolMeeting = new();     // meeting-driven-selection ticket: the pool refresh, narrated as a real meeting
    private readonly JobMarketService _jobMarket = new();                 // Post-Phase-6 (section A/B): two-directional job market - adverts and applications

    // ---- Phase 7 (Board, Media & Finance depth) ----
    private readonly SeasonFinanceService _seasonFinance = new();          // 7.1: the finance loop actually runs
    private readonly BoardService _boardService = new();                   // 7.3: board actor - budgets, fan sentiment, takeovers
    private readonly SponsorshipService _sponsorship = new();              // Phase 13 (§9.7): multi-year sponsorship deals
    private readonly PressConferenceService _press = new();                // 7.4: press conferences
    private readonly StandingStatusService _standingStatus = new();         // meeting ticket fold-in: the "where do we stand" digest
    private readonly PunditService _pundits = new();                       // 7.4: pundit opinion layer
    private readonly FanReactionService _fanReaction = new();              // §14.9/§13.3: fan reactions + protests
    private readonly NarrativeService _narrative = new();                  // Phase 12: season-narrative tracker
    private readonly LeaderboardService _leaderboards = new();             // Phase 12: live leaderboards + team of the tournament
    private readonly FranchiseTradeService _franchiseTrades = new();       // Phase 13: inter-franchise trades
    private readonly AgentService _agents = new();                         // Phase 13 (§9.6): persistent player agents
    private readonly PlayerRelationshipService _relationships = new();     // Phase 14: player-to-player relationship graph
    private readonly CoachPlayerRelationshipService _coachPlayerRel = new(); // Phase 12 (§4.8): coach-to-player working relationships
    private readonly OffFieldLifeService _offField = new();               // Phase 14: off-field life events
    private readonly PersonalityDevelopmentService _personalityDev = new(); // Phase 14: personality arcs
    private readonly ConfidenceContagionService _confidenceContagion = new(); // Phase 14: confidence contagion
    private readonly FlawRemediationService _flawRemediation = new();     // Phase 14: technical flaws
    private readonly SkillRegressionService _skillRegression = new();     // Phase 14: skill regression
    private readonly NewsEngine _news = new();                             // 7.5: news archive + weekly digest
    private readonly AwardsService _awards = new();                        // 7.5: player-of-the-month/year, team of the season
    private readonly DisciplineService _discipline = new();                // 7.6: suspension review, demerit decay
    private readonly RecordProgressionService _recordProgression = new();  // 7.7: annual career-record review
    private readonly HallOfFameService _hallOfFame = new();                // 7.7: induction at retirement
    private readonly AllTimeXiService _allTimeXi = new();                  // Phase 16 (§15.5): team-of-the-era
    private readonly SelectionPanelService _selectionPanel = new();        // 7.8: national selection panel influence
    private readonly CentralContractService _centralContracts = new();     // Phase 10: national central contracts + NOC
    private readonly NationalBoardVerdictService _nationalVerdict = new(); // Phase 10: multi-year national-coach verdict
    private readonly UmpireService _umpires = new();                       // 7.9: umpire season review
    private readonly FinancialFairPlayService _ffp = new();                // 7 (finance follow-up): points deductions + spending embargoes

    // ---- Phase 8 (Training & Player Development) ----
    private readonly AcademyService _academy = new();                      // 8.1-8.3: youth academy - intake, development, promotion/release
    private readonly LoanService _loans = new();                           // 8.7 + 9.6: development + market loans
    private readonly TrainingWeekService _trainingWeek = new();            // 8.5: the weekly training-calendar modulation
    private readonly CoachRecruitmentService _coachRecruitment = new();    // 8.6: a retiring player becomes a rookie coach on the market

    // ---- Phase 9 (Auction / Contracts / Market) ----
    private readonly PlayerContractService _playerContracts = new();        // 9.0-9.1: contract lifecycle + loyalty rewards
    private readonly FreeAgentMarketService _freeAgents = new();            // 9.2: free agents get signed
    private readonly TransferMarketService _transfers = new();             // 9.3: transfers for a fee, window-gated
    private readonly TransferRequestService _transferRequests = new();     // 9.4: transfer requests + unsettled players
    private readonly FranchiseAuctionService _auction = new();            // 9.5: the franchise-league auction
    private readonly FranchiseCoachService _franchiseCoaches = new();     // Section H: campaign-based franchise coaching
    private readonly FranchiseAuctionMediaService _auctionMedia = new();  // follow-up: auction preview/report/press conferences
    private readonly TrainingCampService _camps = new();                   // 9.8: three-tier training camps
    private readonly IccRevenueService _iccRevenue = new();               // S5: ICC annual revenue + a light national finance loop
    private readonly FullMembershipService _fullMembership = new();        // S7: an associate earns Test status (irrevocable)
    private readonly RepresentationDriftService _representation = new();   // S6: opportunity-driven changes of international allegiance
    private readonly PlayerRetirementCareerService _retirementCareers = new(); // NEW-C: a retired player's second career
    private readonly CoachingStructureService _coachingStructure = new();  // S2: national head-coach split / reunify

    /// <summary>Test checked first - it is the format players actually step back from first in reality, and this is also the order a coach/news headline would read most naturally.</summary>
    private static readonly MatchFormat[] FormatRetirementCheckOrder = { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 };

    /// <summary>Advances one day and returns what happened.</summary>
    public IReadOnlyList<GameEvent> AdvanceDay(GameCalendar calendar, WorldState world)
    {
        var previousDate = calendar.CurrentDate;
        var newDate = previousDate.AddDays(1);
        calendar.SetCurrentDate(newDate);

        var events = new List<GameEvent>();

        events.AddRange(ProcessInjuryRecoveries(newDate, world));
        events.AddRange(ProcessInfrastructure(newDate, world));
        events.AddRange(ProcessCompetitionWindows(calendar, previousDate, newDate, world));
        events.AddRange(ProcessContractRenewals(calendar, newDate, world));
        events.AddRange(ProcessContractExpiry(newDate, world));

        // Phase 6, Slice 6.1: play the fixtures scheduled for today. Before the monthly/annual
        // sub-ticks below so a match on the last day of a month or a year is recorded (standings,
        // appearances, form) before the rollover that reads and clears that state.
        var fixtureReport = _fixturePlay.PlayDueFixtures(world, calendar, newDate);
        events.AddRange(fixtureReport.Events);

        // Phase 6, Slice 6.3: advance any season whose stage just finished - draw the playoffs,
        // or complete the season and pay out - right after the day's matches so a season that
        // finishes today is wrapped up today.
        events.AddRange(_seasonRunner.Advance(world, calendar, newDate));

        // Phase 10: when an international competition (a global event, a bilateral trophy series)
        // has just finished this tick, each national board delivers its verdict on its head coach.
        events.AddRange(_nationalVerdict.ReviewCompletedInternationals(world, newDate, events));

        // Post-Phase-6 (section C): the weekly cadence - individual coaching (a batting coach's
        // extra net sessions with a young opener). The first real consumer of RandomForWeek.
        if ((newDate.DayNumber - calendar.StartDate.DayNumber) % 7 == 0)
            events.AddRange(ProcessWeeklyTick(calendar, newDate, world));

        // Post-Phase-5 rectification pass, Wave 4: the quarterly cadence - squad management and the
        // staff-development signal resolve here, a real series/tour cycle rather than once a year.
        if (newDate.Month != previousDate.Month && (newDate.Month - 1) % 3 == 0)
            events.AddRange(ProcessQuarterlyTick(calendar, newDate, world));

        // Post-Phase-5 rectification pass, Wave 1: the new monthly cadence, detected the same way
        // the annual one already is below (a calendar boundary crossed since yesterday). Checked
        // BEFORE the annual block so the smaller period always resolves first, though nothing
        // currently depends on that ordering.
        if (newDate.Month != previousDate.Month)
            events.AddRange(ProcessMonthlyTick(calendar, newDate, world));

        if (newDate.Year != previousDate.Year)
        {
            events.Add(new GameEvent(newDate, GameEventType.SeasonRollover, $"A new year begins: {newDate.Year}."));
            events.AddRange(ProcessAnnualRollover(calendar, newDate, world));
        }

        // Wave 4: LAST - any head-coach chair left vacant by a departure anywhere above (this tick
        // or a prior one) gets the assistant coach installed as interim. A single sweep at the end
        // rather than an AppointInterim call scattered through every path a coach can leave by, and
        // last so a same-tick dismissal/resignation is covered without a day's lag.
        events.AddRange(ProcessVacancies(newDate, world));

        // Phase 7, Slice 7.5: the news layer goes live - every event this day produced becomes a
        // rendered, filterable archive item. A cap keeps a 30-year sim's archive bounded.
        foreach (var ev in events)
            world.NewsArchive.Add(_news.RenderArchive(ev));
        if (world.NewsArchive.Count > 4000)
            for (int i = 0; i < 500 && world.NewsArchive.Count > 0; i++) world.NewsArchive.RemoveAt(0);

        return events;
    }

    /// <summary>
    /// Advances to a target date, processing every intervening day. Deliberately day-by-day:
    /// jumping straight to the target would silently skip every event in between, which is the
    /// single easiest way to make a career simulation lose its own history.
    /// </summary>
    public IReadOnlyList<GameEvent> AdvanceTo(GameCalendar calendar, WorldState world, DateOnly target)
    {
        if (target < calendar.CurrentDate)
            throw new InvalidOperationException($"Cannot advance backwards from {calendar.CurrentDate} to {target}.");

        var events = new List<GameEvent>();
        while (calendar.CurrentDate < target)
            events.AddRange(AdvanceDay(calendar, world));

        return events;
    }

    public IReadOnlyList<GameEvent> AdvanceDays(GameCalendar calendar, WorldState world, int days) =>
        AdvanceTo(calendar, world, calendar.CurrentDate.AddDays(Math.Max(0, days)));

    public IReadOnlyList<GameEvent> AdvanceWeeks(GameCalendar calendar, WorldState world, int weeks) =>
        AdvanceDays(calendar, world, weeks * 7);

    // ---------------- processors ----------------

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 1: Form/Morale/MatchupConfidence decay migrated out
    /// of ProcessAnnualRollover, where all three used to fire once a year with their own
    /// "annual step" default amount. This is the new monthly tick's first real consumer - a
    /// straight proportional migration (each amount divided by twelve, so the total decay over a
    /// full year is unchanged) rather than a recalibration, since the harder semantic work
    /// (freezing decay while a player is genuinely unavailable, matchup's own minimum-sample
    /// threshold and recency-weighted pressure effect, partnership chemistry's own faster and
    /// dual-timescale decay) is Wave 3's job, not this one's. What changes here is only the
    /// CADENCE: a mood or an edge now fades gradually across the season instead of sitting fully
    /// live for eleven months and only being revisited once, at the old annual rollover.
    /// </summary>
    private const double MonthlyDecayFactor = 1.0 / 12.0;

    /// <summary>Wave 2: training now resolves twelve times a year on the monthly tick - this is one twelfth of a year.</summary>
    private const double MonthlyPeriodFraction = 1.0 / 12.0;

    private IEnumerable<GameEvent> ProcessMonthlyTick(GameCalendar calendar, DateOnly date, WorldState world)
    {
        var random = calendar.RandomForMonth(date.Year, date.Month);
        var events = new List<GameEvent>();

        var unavailable = GenuinelyUnavailablePlayerIds(world, date); // Wave 3

        foreach (var player in world.Players.Where(p => !p.IsRetired))
        {
            // Wave 3: a mood/edge/confidence does NOT keep quietly eroding while a player is
            // genuinely out of the game and can do nothing about it - it is frozen until he is
            // available again. A player who is simply not being picked (but is fit and available)
            // still decays, which is correct: that IS his form fading from lack of cricket.
            bool frozen = unavailable.Contains(player.Id);

            // §5.10: bench match-sharpness. It bleeds away every month a player is not in the
            // middle - down toward a "net-fit but ring-rusty" floor - and is topped back up per
            // match by Phase7MatchHooks. Not frozen while unavailable: an injured player genuinely
            // loses match sharpness (it just also can't recover until he is back).
            player.MatchSharpness = Math.Max(62, player.MatchSharpness - 3.5);

            if (!frozen)
            {
                player.Form.DecayTowardNeutral(2.0 * MonthlyDecayFactor);
                player.Form.DecayConfidenceTowardNeutral(1.5 * MonthlyDecayFactor);
                player.Morale.DecayTowardNeutral(1.5 * MonthlyDecayFactor);
                _matchupConfidence.DecayAll(player, 5.0 * MonthlyDecayFactor, decayPartnerKeys: false);
                // Post-Phase-6 carry-forward: trust in the captain drifts back toward neutral the
                // same way, at the same cadence, when it is not being reinforced by matches.
                player.CaptainTrust += (50 - player.CaptainTrust) * 1.5 * MonthlyDecayFactor;
            }

            // Wave 3: partnership chemistry decays FASTER than a general matchup edge and on its
            // own rules - frozen only when the PARTNER (or this player) is genuinely unavailable
            // ("no opportunity"), decaying normally when both are playing but simply not batting
            // together ("had the chance, did not take it").
            _chemistry.DecayPartnerships(player, unavailable, MonthlyPeriodFraction);

            // Wave 2: one twelfth of a year's coach-directed training.
            var (facilityQuality, staff, delegatedFocus) = ResolveTrainingContext(world, player);
            var training = _training.ApplyPeriodicTraining(player, facilityQuality, staff, random, date,
                MonthlyPeriodFraction, reassessFocus: true, focusDelegatedToStaff: delegatedFocus);

            if (training.OvertrainingInjury is { } overtraining)
            {
                int medicalQuality = world.Teams.TryGetValue(player.CurrentTeamId ?? Guid.Empty, out var mt)
                    ? mt.Facilities.MedicalQuality : 50;
                world.ApplyInjury(player, overtraining.Type, overtraining.Severity, date, medicalQuality, random: random);
            }
            if (training.PotentialAbilityRose || training.RoleConverted)
                events.Add(new GameEvent(date, GameEventType.PlayerDeveloped, training.Summary, player.Id));

            if (training.AttributeGrowth > 0)
                world.RecentDevelopmentByPlayer[player.Id] =
                    world.RecentDevelopmentByPlayer.TryGetValue(player.Id, out var acc) ? acc + training.AttributeGrowth : training.AttributeGrowth;
        }

        // Wave 8: rankings feed reputation, once a month, per format the team is ranked in.
        foreach (var format in new[] { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 })
        {
            int fieldSize = world.TeamRankings.Count(r => r.Format == format);
            if (fieldSize == 0) continue;
            foreach (var ranking in world.TeamRankings.Where(r => r.Format == format))
                if (world.Teams.TryGetValue(ranking.TeamId, out var rankedTeam))
                    _rankings.ApplyReputationEffect(rankedTeam, ranking, fieldSize);
        }

        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise))
        {
            team.Morale.DecayTowardNeutral(1.0 * MonthlyDecayFactor);
            _teamMorale.DecayFormMomentum(team, 9.0); // Wave 6: momentum fades between matches faster than mood does

            // Wave 5: dressing-room dynamics - the senior consensus on the coach pulls the room,
            // and Team.DressingRoomHarmony tracks how united it is. Runs after the player training
            // pass above so any CoachTrust movement this month is already in.
            var squad = world.Players.Where(p => p.CurrentTeamId == team.Id && !p.IsRetired).ToList();
            _dressingRoom.ApplyRoomDynamics(team, squad, date, MonthlyPeriodFraction);
        }

        // Wave 5: apply each mentoring pairing's effect for the month. Iterated in the LIST's own
        // insertion order - NOT sorted by any Guid (a group's Id, or the mentor/mentee ids, are all
        // freshly generated per save and differ between two loads of the same seed, so ordering by
        // them would make the world non-reproducible from a save - the exact determinism rule this
        // project's history keeps re-learning). ReviewGroups adds groups in a deterministic order
        // (team list order, then junior-by-potential), so the list order itself is stable.
        foreach (var group in world.MentoringGroups.ToList())
        {
            var mentor = world.Players.FirstOrDefault(p => p.Id == group.MentorId);
            var mentee = world.Players.FirstOrDefault(p => p.Id == group.MenteeId);
            if (mentor is null || mentee is null) continue;
            _mentoring.ApplyMentoring(group, mentor, mentee, random, MonthlyPeriodFraction, date, world);
        }

        // Wave 4: crystallise the captain accumulators - the per-match decision-quality signal
        // built up in RecordCaptaincyOutcomes becomes real, slow Leadership/DecisionMaking
        // movement here, on a monthly cadence (the signal is match-grain, so the cadence is not
        // locked to a campaign's end the way the coach's milestone growth is).
        // Iterated via world.Players (deterministic List order), NOT by iterating the dictionary or
        // sorting its Guid keys - same determinism reasoning as the mentoring loop above.
        foreach (var player in world.Players.Where(p => !p.IsRetired))
        {
            if (world.CaptaincyProfiles.TryGetValue(player.Id, out var profile))
                _captaincyGrowth.Crystallise(player, profile, random);
        }

        // Phase 6, Slice 6.2: AI-run clubs manage themselves - name squads for upcoming
        // competitions, fill a vacant coach/staff chair, appoint a new captain when one retires.
        // Consumes the shared monthly stream at a fixed point in this tick's sequence (after
        // everything above), so it stays deterministic. The human's club is handled by the same
        // call, gated by Team.ManagerPreferences.
        events.AddRange(_aiClub.RunMonthly(world, calendar, date, random));

        // Post-Phase-6 (section A/B): the two-directional job market - teams advertise vacancies,
        // coaches and staff (employed or not) apply, the hiring authority appoints on fit + culture.
        events.AddRange(_jobMarket.RunMonthly(world, calendar, date, random));

        // Phase 6, Slice 6.5: the in-season board relationship - BoardConfidence tracks the
        // current campaign, a collapse can cost a coach his job before the season ends, and a
        // vacancy elsewhere can bring an offer to the human coach.
        events.AddRange(_board.ReviewMonthly(world, calendar, date, random));

        // Phase 7, Slice 7.3: fold each club's financial health into board confidence - a club
        // haemorrhaging money loses faith in the coach whatever the results say.
        foreach (var team in world.Teams.Values.Where(t => !t.IsNational).OrderBy(t => t.Name))
        {
            double factor = _boardService.FinancialHealthFactor(team);
            if (factor != 0)
                team.BoardConfidence = Math.Clamp(team.BoardConfidence + factor * 6, 0, 100);
        }

        // Phase 7, Slice 7.6: clear expired bans and decay demerit points.
        events.AddRange(_discipline.ReviewSuspensions(world, date));

        // Phase 7, Slice 7.4: raise press storylines from this month's events, then hold the
        // conferences - a coach with a storm on his desk faces the media, and how it lands
        // depends on his MediaHandling. Then the pundits weigh in on the biggest stories.
        _press.RaiseStorylines(world, date, events);
        events.AddRange(_press.HoldPendingConferences(world, date, random));

        // Corrections pass (correction 4): a national coach can convene a pool / selection meeting
        // HIMSELF, additive to the automatic annual and pre-major triggers. A newly-appointed coach
        // always calls one on taking the job; a human coach asks via ManagerPreferences; a thorough
        // AI coach occasionally calls an extra one on his own judgement. Consumes the monthly stream
        // at this fixed point (after every appointment source above), so it stays deterministic.
        foreach (var nationalTeam in world.Teams.Values.Where(t => t.IsNational).OrderBy(t => t.Name))
        {
            var natCoach = nationalTeam.CurrentCoachId is { } ncid ? world.Coaches.FirstOrDefault(c => c.Id == ncid) : null;
            if (natCoach is null) continue;

            bool trigger = false;
            if (!natCoach.CalledInitialPoolMeeting) { trigger = true; natCoach.CalledInitialPoolMeeting = true; }
            else if (natCoach.IsHumanControlled && nationalTeam.ManagerPreferences.RequestPoolMeeting)
                { trigger = true; nationalTeam.ManagerPreferences.RequestPoolMeeting = false; }
            else if (!natCoach.IsHumanControlled)
            {
                double thoroughness = Math.Clamp((natCoach.Attributes.WorkEthic + natCoach.Attributes.MatchPreparation) / 40.0, 0.05, 0.4);
                if (random.NextDouble() < thoroughness * 0.06) trigger = true; // rare - the scheduled triggers are the main cadence
            }
            if (!trigger) continue;

            var selectorStaff = world.Staff.Where(s => nationalTeam.StaffIds.Contains(s.Id)
                && s.Role is Enums.StaffRole.Selector or Enums.StaffRole.ChiefSelector).ToList();
            var report = _poolMeeting.Hold(nationalTeam, Array.Empty<string>(), natCoach, selectorStaff, date);
            events.Add(new GameEvent(date, GameEventType.NationalPoolMeeting,
                $"{natCoach.FullName} convenes an early {nationalTeam.Country} selection meeting. {report.Headline}"
                + (report.DissentNote is not null ? $" {report.DissentNote}" : ""),
                nationalTeam.Id));
        }

        // Phase 12: the season-narrative tracker - connect this month's headlines into storylines
        // that build and pay off, feeding PressureMoment / CaptainTrust / the news prominence.
        events.AddRange(_narrative.ReviewMonthly(world, date));

        events.AddRange(_pundits.Opine(events.ToList(), date, world.Pundits.ToList()));

        // Meeting-driven-selection ticket (fold-in): the "where do we stand" digest - the board's
        // mood, the dressing room, the key players. Always for the human's club; for an AI club
        // only when something is genuinely off. Deterministic, RNG-free.
        events.AddRange(_standingStatus.ReviewMonthly(world, date));

        // §14.9 / §13.3: the supporters react - a fan-reaction line and a FanSentiment move for the
        // month's most charged club events, and a protest when sentiment has collapsed.
        var fanEvents = _fanReaction.React(events.ToList(), world, date).ToList();
        events.AddRange(fanEvents);
        // §14.9: the fan feed as its own stream - "From the stands". Capped.
        foreach (var fe in fanEvents)
            world.FanFeed.Add(new ValueObjects.NewsArchiveItem(date, NewsCategory.Media, 25, "From the stands", fe.Headline, fe.SubjectId, fe.SecondarySubjectId));
        while (world.FanFeed.Count > 200) world.FanFeed.RemoveAt(0);

        // Phase 7, Slice 7.5: player of the month, from this month's accumulated form. Then reset.
        var potm = _awards.PlayerOfTheMonth(world.MonthForm, date.Year, date.AddMonths(-1).Month);
        if (potm is not null)
        {
            world.Awards.Add(potm);
            events.Add(new GameEvent(date, GameEventType.SeasonAward, potm.Citation, potm.PlayerId, potm.TeamId));
        }
        events.AddRange(_leaderboards.MonthlyLeaders(world, date)); // Phase 12: leading run-scorer / wicket-taker
        world.MonthForm.Clear();

        _confidenceContagion.ReviewMonthly(world, date); // Phase 14: a squad's mood catches on individuals

        // Phase 12 (§4.8): the coach-to-player working relationships - picked-and-backed vs
        // fringe-and-ignored, personality fit, whether the programme is working. Feeds CoachTrust,
        // development, dressing-room harmony, and a fallout -> transfer-request. Own local RNG.
        events.AddRange(_coachPlayerRel.ReviewMonthly(world, date));

        // Phase 8, Slice 8.7: bring home any player whose development loan has run its course, and
        // credit the game time he got. Deterministic (no RNG), appended last.
        events.AddRange(_loans.ProcessReturns(world, date));

        // Phase 9: the monthly market - free agents, the transfer window, the unsettled-player
        // drag. Consumes the monthly RNG stream LAST, after every pre-Phase-9 monthly consumer,
        // so nothing above shifts.
        events.AddRange(ProcessPhase9Monthly(date, world, random));

        return events;
    }

    /// <summary>
    /// Phase 9: the monthly market pass. Free agents get signed; the transfer market runs when a
    /// window is open; an unsettled player carries a small morale/form drag until the situation
    /// resolves.
    /// </summary>
    private IEnumerable<GameEvent> ProcessPhase9Monthly(DateOnly date, WorldState world, Random random)
    {
        if (world.PlayerContracts.Count == 0) yield break; // no contract data - the whole market is inert

        foreach (var ev in _freeAgents.RunMonthly(world, date, random)) yield return ev;
        foreach (var ev in _transfers.RunWindow(world, date, random)) yield return ev;

        // The unsettled-player drag - deterministic, no RNG.
        foreach (var player in world.Players.Where(p => p.UnsettledUntil is not null).ToList())
        {
            if (player.UnsettledUntil <= date || player.CurrentTeamId is null)
            {
                player.UnsettledUntil = null;
                continue;
            }
            player.Morale.Adjust(-1.5);
            player.Form.RecordPerformance(-1.5);
        }
    }

    /// <summary>Wave 3: player ids that are genuinely unavailable - out of the game, not merely out of the side. Decay is frozen for these, and partnership chemistry with them does not erode.</summary>
    private static HashSet<Guid> GenuinelyUnavailablePlayerIds(WorldState world, DateOnly date) =>
        world.Players.Where(p =>
                (p.CurrentInjury is { } inj && inj.IsActiveOn(date) && inj.Severity >= InjurySeverity.Moderate)
                || p.NonInjuryUnavailability is UnavailabilityReason.Injured or UnavailabilityReason.Resting
                    or UnavailabilityReason.OnDevelopmentAssignment or UnavailabilityReason.Suspended
                    or UnavailabilityReason.InternationalDuty)
            .Select(p => p.Id)
            .ToHashSet();

    /// <summary>
    /// The training context for one player: his team's training-facility quality (the always-there
    /// fallback), every backroom staff member hired at his team, and whether the head coach has
    /// formally delegated training-focus decisions to his specialists (Wave 4). Shared by the
    /// monthly training pass here and the annual ageing pass below, which also reads facility
    /// quality.
    /// </summary>
    private static (int FacilityQuality, IReadOnlyList<StaffMember> Staff, bool DelegatedFocus) ResolveTrainingContext(WorldState world, Player player)
    {
        if (player.CurrentTeamId is not { } teamId || !world.Teams.TryGetValue(teamId, out var team))
            return (40, Array.Empty<StaffMember>(), false);

        var staff = world.Staff.Where(s => team.StaffIds.Contains(s.Id)).ToList();

        // NEW-D: a head coach who has taken a specialist role himself (Coach.AdditionalRoles) stands
        // in for the missing StaffMember - his matching coaching attribute becomes the specialist's
        // effective quality. Deterministic; the synthetic staffer's Guid is never ordered on.
        var headCoach = team.CurrentCoachId is { } hcid ? world.Coaches.FirstOrDefault(c => c.Id == hcid) : null;
        if (headCoach is not null)
            foreach (var extra in headCoach.AdditionalRoles.Where(r => r.TeamId == team.Id))
            {
                if (staff.Any(s => s.Role == extra.Role)) continue;
                int attr = extra.Role switch
                {
                    StaffRole.BattingCoach => headCoach.Attributes.BattingCoaching,
                    StaffRole.BowlingCoach => headCoach.Attributes.BowlingCoaching,
                    StaffRole.FieldingCoach => headCoach.Attributes.FieldingCoaching,
                    _ => headCoach.Attributes.TechnicalKnowledge
                };
                staff.Add(new StaffMember
                {
                    FirstName = headCoach.FirstName, LastName = headCoach.LastName, Role = extra.Role,
                    Nationality = headCoach.Nationality, FromPlayerId = headCoach.FromPlayerId,
                    TechnicalKnowledge = Math.Clamp(attr, 1, 20),
                    Communication = Math.Clamp(headCoach.Attributes.ManManagement, 1, 20),
                    Analysis = Math.Clamp(headCoach.Attributes.OppositionAnalysis, 1, 20),
                    Diligence = Math.Clamp(headCoach.Attributes.WorkEthic, 1, 20),
                    YearsExperience = 4,
                });
            }

        bool delegated = team.DelegatedResponsibilities.Contains(DelegatedResponsibility.TrainingFocus);
        return (team.Facilities.TrainingQuality, staff, delegated);
    }

    // ---------------- Wave 4: the quarterly tick ----------------

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: the quarterly cadence - a real series/tour cycle.
    /// Four things resolve here:
    /// - **Squad management** (moved off annual): a prolonged decline now produces a Roadmap/Rest/
    ///   Drop within a quarter of the evidence being in, and a two-month Rest ends near on time.
    /// - **The staff-development signal**: each specialist is judged on whether HIS cluster of
    ///   players is genuinely outdeveloping the team baseline (Wave 2's own output).
    /// - **Type B coach/staff departures**: a rival comes calling, the club either fights to keep
    ///   him or lets him walk - event-triggered, distinct from the slow-burn Type A resignation.
    /// - **Interim -> permanent**: an assistant who has run things well for long enough gets the
    ///   job for real.
    /// Then the recent-development accumulator is cleared, so each quarter is judged on its own.
    /// </summary>
    private readonly SpecialistStaffService _specialistStaff = new(); // section C

    /// <summary>
    /// Post-Phase-6 (section C): one week of specialist individual coaching. Players with an
    /// assigned specialist coach who is still at the club get a small development roll in that
    /// coach's cluster - the "extra work with a specific player" the brief asks for, tied into the
    /// existing training/development pathway rather than a parallel one.
    /// </summary>
    private IEnumerable<GameEvent> ProcessWeeklyTick(GameCalendar calendar, DateOnly date, WorldState world)
    {
        var random = calendar.RandomForWeek(date);
        foreach (var player in world.Players.Where(p => p.AssignedSpecialistCoachId is not null && !p.IsRetired))
        {
            var coach = world.Staff.FirstOrDefault(s => s.Id == player.AssignedSpecialistCoachId);
            if (coach is null) { player.AssignedSpecialistCoachId = null; continue; }
            _specialistStaff.ApplyIndividualWorkWeek(player, coach, random, world);
        }

        // Phase 8, Slice 8.5: the weekly training-calendar modulation. Classify each club's week
        // once (match week / preparation / off-season), then apply a small seasonal development
        // nudge to its young players - the real-cricket fact that gains are made in pre-season and
        // the off-season, not in a match week. Consumes the same weekly stream, AFTER the
        // specialist pass above, so it appends rather than shifts. Iterated in a deterministic
        // order (players list order, teams by name).
        var weekTypeByTeam = world.Teams.Values
            .Where(t => !t.IsNational && !t.IsFranchise)
            .ToDictionary(t => t.Id, t => _trainingWeek.ClassifyWeek(t, world, date));
        foreach (var player in world.Players.Where(p => !p.IsRetired && p.CurrentTeamId is not null))
        {
            if (weekTypeByTeam.TryGetValue(player.CurrentTeamId!.Value, out var weekType))
                _trainingWeek.ApplyTrainingWeek(player, weekType, random, date);
        }

        // Phase 7, Slice 7.5: the weekly inbox digest - aggregate the last seven days of archived
        // news into a stored digest a UI / the human coach reads. Reads the archive the daily tick
        // already fills; produces no new events.
        var weekStart = date.AddDays(-7);
        var items = world.NewsArchive
            .Where(n => n.Date > weekStart && n.Date <= date)
            .OrderByDescending(n => n.Prominence).ThenByDescending(n => n.Date)
            .ToList();
        if (items.Count > 0)
        {
            var lead = items.Take(4).ToList();
            world.Digests.Add(new ValueObjects.StoredDigest(weekStart, date, items.Count, lead));
            if (world.Digests.Count > 260)
                world.Digests.RemoveAt(0);
        }

        yield break;
    }

    private IEnumerable<GameEvent> ProcessQuarterlyTick(GameCalendar calendar, DateOnly date, WorldState world)
    {
        int quarter = (date.Month - 1) / 3;
        var random = calendar.RandomForQuarter(date.Year, quarter);
        var events = new List<GameEvent>();

        // --- squad management ---
        foreach (var player in world.Players.Where(p => !p.IsRetired).ToList())
        {
            _squadManagement.ReviewDecision(player, date);

            var squadAssessment = _squadManagement.AssessDeclineResponse(player, random);
            if (squadAssessment is not null)
            {
                _squadManagement.Apply(player, squadAssessment, date);
                events.Add(new GameEvent(date, GameEventType.SquadDecisionMade, squadAssessment.Reason, player.Id));
            }
        }

        // --- staff development signal ---
        foreach (var staff in world.Staff.Where(s => s.TeamId is not null).ToList())
        {
            if (!world.Teams.TryGetValue(staff.TeamId!.Value, out var team)) continue;
            // Phase 8: an academy prospect is not part of the senior cluster a specialist coach is
            // judged on - his rapid youth development would otherwise flatter or distort the signal.
            var teamPlayers = world.Players.Where(p => p.CurrentTeamId == team.Id && !p.IsRetired && p.AcademyTeamId is null).ToList();
            _staffCareer.GrowFromDevelopmentSignal(staff, teamPlayers, world.RecentDevelopmentByPlayer, random);
        }

        // --- Type B: rival interest / retention (coaches) ---
        foreach (var coach in world.Coaches.Where(c => c.CurrentTeamId is not null && c.CurrentContractId is not null).ToList())
        {
            var contract = world.CoachingContracts.FirstOrDefault(c => c.Id == coach.CurrentContractId && c.Status == ContractStatus.Active);
            if (contract is null || !world.Teams.TryGetValue(coach.CurrentTeamId!.Value, out var team)) continue;

            var latestStandings = world.CompetitionSeasons
                .Where(s => s.Year >= date.Year - 1)
                .SelectMany(s => s.Standings).Where(s => s.TeamId == team.Id).ToList();
            double performanceScore = _coachCareer.ScoreSeason(team, latestStandings);

            if (_coachCareer.EvaluateRivalInterest(coach, team, performanceScore, random))
            {
                var (retained, reason) = _coachCareer.OfferRetention(coach, contract, team, random);
                events.Add(new GameEvent(date,
                    retained ? GameEventType.CoachRetentionOffer : GameEventType.CoachResigned,
                    reason, coach.Id, team.Id));
            }
        }

        // --- Type B: rival interest / retention (staff, coach-mediated) ---
        foreach (var staff in world.Staff.Where(s => s.TeamId is not null).ToList())
        {
            var staffContract = world.StaffContracts.FirstOrDefault(c => c.StaffId == staff.Id && c.Status == ContractStatus.Active);
            if (staffContract is null || !world.Teams.TryGetValue(staff.TeamId!.Value, out var team)) continue;

            if (!_staffCareer.EvaluateRivalInterest(staff, random)) continue;

            var headCoach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            var (retained, reason) = _staffCareer.OfferRetention(staff, staffContract, team, headCoach, random);
            if (!retained)
                events.Add(new GameEvent(date, GameEventType.StaffResigned, reason, staff.Id, team.Id));
        }

        // --- interim -> permanent ---
        foreach (var team in world.Teams.Values.Where(t => t.InterimCoachStaffId is not null).OrderBy(t => t.Name).ToList())
        {
            var standings = world.CompetitionSeasons
                .Where(s => s.Year >= date.Year - 1)
                .SelectMany(s => s.Standings).Where(s => s.TeamId == team.Id).ToList();
            double performanceScore = _coachCareer.ScoreSeason(team, standings);

            var ev = _interimCoach.ConsiderPermanentPromotion(team, world, date, performanceScore, random);
            if (ev is not null) events.Add(ev);
        }

        // --- Wave 5: review mentoring groups (form new, dissolve stale) ---
        // Ordered by name (deterministic) rather than dictionary order - ReviewGroups appends to
        // world.MentoringGroups, and that list order later drives ApplyMentoring's RNG consumption.
        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise).OrderBy(t => t.Name))
        {
            var squad = world.Players.Where(p => p.CurrentTeamId == team.Id && !p.IsRetired).ToList();
            var teamStaff = world.Staff.Where(s => team.StaffIds.Contains(s.Id)).ToList();
            double mentorBonus = _specialistStaff.MentoringStrengthBoost(teamStaff); // section C
            _mentoring.ReviewGroups(team, squad, world.MentoringGroups, date, mentorBonus);
        }

        // Post-Phase-6 carry-forward: take this quarter's development snapshot for every active
        // player BEFORE clearing RecentDevelopmentByPlayer, so a report can show his trajectory
        // period-over-period. Capped at ~4 years of history per player.
        foreach (var player in world.Players.Where(p => !p.IsRetired))
        {
            if (!world.DevelopmentHistory.TryGetValue(player.Id, out var history))
                world.DevelopmentHistory[player.Id] = history = new List<ValueObjects.PlayerDevelopmentSnapshot>();
            history.Add(ValueObjects.PlayerDevelopmentSnapshot.Of(player, date));
            if (history.Count > 16) history.RemoveAt(0);
        }

        // Phase 9: the quarterly market pass - transfer requests / unsettled players (9.4) and the
        // three-tier training camps (9.8). Consumes the quarterly RNG stream LAST, after every
        // pre-Phase-9 quarterly consumer above, so nothing shifts. Camps feed
        // RecentDevelopmentByPlayer, so they run BEFORE the clear below.
        if (world.PlayerContracts.Count > 0)
        {
            events.AddRange(_agents.ReviewQuarterly(world, date));               // Phase 13 (§9.6): agent rosters
            events.AddRange(_transferRequests.RunQuarterly(world, date, random));
            events.AddRange(_transfers.RunAgentBiddingWars(world, date, random)); // follow-up: agents engineer a bidding contest
            events.AddRange(_camps.RunCamps(world, date, random));
            events.AddRange(_franchiseTrades.RunQuarterly(world, date, random));  // Phase 13: inter-franchise trades
            events.AddRange(_relationships.ReviewQuarterly(world, date, random)); // Phase 14: the player relationship graph
            events.AddRange(_offField.ReviewQuarterly(world, date, random));      // Phase 14: off-field life events
        }

        // Meeting-driven-selection ticket (requirement A): the OTHER pool-meeting cadence - once
        // ahead of a genuine major tournament, on top of (not instead of) the ordinary annual one.
        // Deterministic (a real lookahead check, not a probability roll), so it costs the quarterly
        // RNG stream nothing and can sit outside the Phase-9-gated block above.
        events.AddRange(HoldPreTournamentPoolMeetings(world, date));

        world.RecentDevelopmentByPlayer.Clear();

        return events;
    }

    /// <summary>How far ahead of a major tournament's staging the extra pool meeting is held.</summary>
    private const int PreTournamentPoolMeetingLeadDays = 120;

    /// <summary>
    /// Meeting-driven-selection ticket (requirement A): "once ahead of a major tournament instead
    /// of per-series" - a genuinely separate, EXTRA pool refresh + meeting for a national team with
    /// a real major (Competition.IsMajor) staging within the lead window, deduped per
    /// CompetitionSeason so it fires exactly once for that tournament instance regardless of how
    /// many quarterly ticks the lookahead window spans.
    /// </summary>
    private IEnumerable<GameEvent> HoldPreTournamentPoolMeetings(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();

        foreach (var competition in world.Competitions.Where(c => c.IsMajor))
        {
            var staging = _competitionCalendar.GetStaging(competition, date.Year)
                ?? _competitionCalendar.GetStaging(competition, date.Year + 1);
            if (staging is null) continue;
            bool inLeadWindow = date >= staging.StartDate.AddDays(-PreTournamentPoolMeetingLeadDays) && date < staging.StartDate;
            if (!inLeadWindow) continue;

            var season = world.CompetitionSeasons
                .Where(s => s.CompetitionId == competition.Id && (s.Year == date.Year || s.Year == date.Year + 1))
                .OrderByDescending(s => s.Year).FirstOrDefault();
            if (season is null || world.PreTournamentPoolMeetingsHeld.Contains(season.Id)) continue;

            foreach (var teamId in season.ParticipatingTeamIds)
            {
                if (!world.Teams.TryGetValue(teamId, out var nationalTeam) || !nationalTeam.IsNational) continue;

                var natCoach = nationalTeam.CurrentCoachId is { } ncid ? world.Coaches.FirstOrDefault(c => c.Id == ncid) : null;
                var notes = _nationalSelection.RefreshPool(nationalTeam, world.Players, date, world.NationalPools, natCoach);
                var selectorStaff = world.Staff.Where(s => nationalTeam.StaffIds.Contains(s.Id)
                    && s.Role is Enums.StaffRole.Selector or Enums.StaffRole.ChiefSelector).ToList();
                var poolMeeting = _poolMeeting.Hold(nationalTeam, notes, natCoach, selectorStaff, date, preTournament: true, tournamentName: competition.Name);

                string story = poolMeeting.Headline
                    + (poolMeeting.Findings.Count > 0 ? $" {poolMeeting.Findings[0]}" : "")
                    + (poolMeeting.DissentNote is not null ? $" {poolMeeting.DissentNote}" : "");
                events.Add(new GameEvent(date, GameEventType.NationalPoolMeeting, story, nationalTeam.Id, competition.Id));
            }

            world.PreTournamentPoolMeetingsHeld.Add(season.Id);
        }

        return events;
    }

    /// <summary>
    /// Clears injuries whose layoff has run out. Without this the availability check kept
    /// returning "fit" once the return date passed while `CurrentInjury` stayed set forever -
    /// so a player was quietly both injured and available, and injury history never closed.
    /// </summary>
    private IEnumerable<GameEvent> ProcessInjuryRecoveries(DateOnly date, WorldState world)
    {
        foreach (var player in world.InjuriesDueOn(date))
        {
            var injury = player.CurrentInjury;
            if (injury is null || injury.IsActiveOn(date)) continue;

            _availability.MarkRecovered(player, date);

            yield return new GameEvent(date, GameEventType.PlayerRecovered,
                $"{player.FullName} has recovered from a {injury.Severity} {injury.Type} and is available for selection.",
                player.Id);
        }
    }

    private IEnumerable<GameEvent> ProcessInfrastructure(DateOnly date, WorldState world)
    {
        var due = world.ProjectsDueOn(date);
        if (due.Count == 0) yield break;

        var completions = _infrastructure.AdvanceTo(date, due, world.Teams.AsReadOnly(), world.Grounds);

        foreach (var completion in completions)
        {
            yield return completion.NewGround is not null
                ? new GameEvent(date, GameEventType.NewGroundOpened, completion.Description,
                    completion.Project.TeamId, completion.NewGround.Id)
                : new GameEvent(date, GameEventType.InfrastructureCompleted, completion.Description,
                    completion.Project.TeamId, completion.Project.GroundId);
        }
    }

    /// <summary>
    /// Reports competitions starting and finishing. Compares yesterday to today so a window is
    /// announced exactly once, on the day it turns over.
    ///
    /// Wave 4: a window CLOSING is a milestone - a completed campaign - and it is the hook the
    /// per-role cadence decision puts a HEAD COACH's tactical growth on (not the calendar). Each
    /// coach whose team competed in that competition this year grows a little from the campaign,
    /// scaled by the club's standing and the competition's prestige; an interim in charge grows
    /// the same way, on his StaffMember attributes.
    /// </summary>
    private IEnumerable<GameEvent> ProcessCompetitionWindows(GameCalendar calendar, DateOnly previousDate, DateOnly date, WorldState world)
    {
        var events = new List<GameEvent>();

        foreach (var competition in world.Competitions)
        {
            bool wasActive = _competitionCalendar.IsInWindow(competition, previousDate);
            bool isActive = _competitionCalendar.IsInWindow(competition, date);

            if (isActive && !wasActive)
            {
                events.Add(new GameEvent(date, GameEventType.CompetitionWindowOpened,
                    $"The {competition.Name} gets under way.", competition.Id));

                // Slice 6.7: club vs country - when an international window opens, the players named
                // in each national squad go on international duty and are unavailable for their clubs.
                if (competition.Scope == CompetitionScope.International)
                    SetInternationalDuty(world, competition, date, onDuty: true);

                // §5.3: a pre-series planning conversation for a genuine head-to-head series (not a
                // big multi-team tournament, where there is no single opponent to plan a whole
                // series around). Surfaced once, at the window's open, as a StaffRecommendation - the
                // human sees it before the first ball; an AI side has no coach to show it to, so it
                // is silently skipped for them (the per-fixture AiTacticalPlanner already covers
                // their in-match reads).
                events.AddRange(SurfacePreSeriesPlanning(world, competition, date));

                // Phase 9, Slice 9.5: run the franchise auction the day its window opens, if the
                // franchises do not already have squads for this season. Its OWN RNG stream
                // (RandomForAuction) - never RandomForDay, which the campaign-growth code below
                // already uses this same tick.
                if (competition.IsFranchiseAuctionLeague && world.PlayerContracts.Count > 0)
                {
                    var fSeason = world.CompetitionSeasons
                        .Where(s => s.CompetitionId == competition.Id && s.Year == date.Year && !s.IsCompleted)
                        .OrderByDescending(s => s.Year).FirstOrDefault();
                    int fIdx = world.Competitions.Where(c => c.IsFranchiseAuctionLeague).OrderBy(c => c.Name).ToList().FindIndex(c => c.Id == competition.Id);

                    // Corrections pass (correction 3): the head coach is a genuine year-round
                    // appointment, in post BEFORE the auction so he is in the pre-auction war room.
                    // EnsureCoachInPost keeps the incumbent and only hires an empty chair.
                    if (fSeason is not null)
                    {
                        events.AddRange(_franchiseCoaches.EnsureCoachInPost(world, competition, fSeason, date,
                            calendar.RandomForAuction(date.Year, 900 + fIdx)));
                        // NEW-A: the franchise's specialist staff are year-round too, on real contracts.
                        events.AddRange(_franchiseCoaches.EnsureStaffInPost(world, competition, fSeason, date,
                            calendar.RandomForAuction(date.Year, 970 + fIdx)));
                    }

                    events.AddRange(RunFranchiseAuctionIfNeeded(calendar, date, world, competition));
                }
            }
            else if (wasActive && !isActive)
            {
                events.Add(new GameEvent(date, GameEventType.CompetitionWindowClosed,
                    $"The {competition.Name} has concluded.", competition.Id));
                events.AddRange(GrowCoachesFromCampaign(calendar, date, world, competition));

                // Corrections pass (correction 3): the end-of-campaign review - a title lifts the
                // coach's circuit standing; a genuinely poor finish can end his multi-year deal
                // early (the replacement in post at once). The coach is NOT otherwise released -
                // he is year-round. Only the campaign assistant layer is let go here.
                if (competition.IsFranchiseAuctionLeague)
                {
                    int fIdx = world.Competitions.Where(c => c.IsFranchiseAuctionLeague).OrderBy(c => c.Name).ToList().FindIndex(c => c.Id == competition.Id);
                    events.AddRange(_franchiseCoaches.ReviewAfterCampaign(world, competition, date,
                        calendar.RandomForAuction(date.Year, 950 + fIdx)));
                }

                if (competition.Scope == CompetitionScope.International)
                    SetInternationalDuty(world, competition, date, onDuty: false);
            }
        }

        return events;
    }

    /// <summary>
    /// §5.3: a genuine head-to-head series (exactly two participants - a bilateral international
    /// trophy series, or a two-team domestic League) gets a pre-series planning conversation at the
    /// window's own open, not just a per-fixture read. Deliberately lighter than
    /// <see cref="AiTacticalPlanner"/> (no XI exists yet this early) - a plain relative-strength
    /// read, phrased as a coach's own series-long intent, shown to a human coach who has not
    /// switched TacticalPlan off entirely.
    /// </summary>
    internal static IEnumerable<GameEvent> SurfacePreSeriesPlanning(WorldState world, Competition competition, DateOnly date)
    {
        var season = world.CompetitionSeasons
            .Where(s => s.CompetitionId == competition.Id && s.Year == date.Year)
            .OrderByDescending(s => s.Year).FirstOrDefault();
        if (season is null || season.ParticipatingTeamIds.Count != 2) yield break;

        var teams = season.ParticipatingTeamIds
            .Select(id => world.Teams.TryGetValue(id, out var t) ? t : null)
            .Where(t => t is not null).Select(t => t!).ToList();
        if (teams.Count != 2) yield break;

        foreach (var (side, opponent) in new[] { (teams[0], teams[1]), (teams[1], teams[0]) })
        {
            var coach = side.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            if (coach?.IsHumanControlled != true || !side.ManagerPreferences.Delegation.WantsRecommendation(DecisionArea.TacticalPlan))
                continue;

            double edge = side.Strength - opponent.Strength;
            string intent = edge > 8
                ? $"we are the stronger side here and should look to press for the series win from the first match"
                : edge < -8
                ? $"{opponent.Name} start favourites - the plan should be to compete hard, protect confidence, and take any chances that come"
                : $"this is close to even - the side that settles first tends to take the series";
            yield return new GameEvent(date, GameEventType.StaffRecommendationIssued,
                $"Ahead of the {competition.Name} against {opponent.Name}: {intent}.", side.Id, competition.Id);
        }
    }

    /// <summary>
    /// Slice 6.7: puts the players in each participating national squad on (or off) international
    /// duty as an international window opens/closes. The squad is the SquadAnnouncement
    /// AiClubManagementService named for this competition; failing that, the national team's whole
    /// pool. On duty -> NonInjuryUnavailability.InternationalDuty (a real block his club sees).
    /// Off duty -> only players who were ON international duty are cleared (never touching a player
    /// resting or suspended for another reason).
    /// </summary>
    private static void SetInternationalDuty(WorldState world, Competition competition, DateOnly date, bool onDuty)
    {
        var season = world.CompetitionSeasons
            .Where(s => s.CompetitionId == competition.Id)
            .OrderByDescending(s => s.Year)
            .FirstOrDefault();
        var nationalTeamIds = season?.ParticipatingTeamIds
            ?? world.Teams.Values.Where(t => t.IsNational).Select(t => t.Id).ToList();

        if (!onDuty)
        {
            foreach (var player in world.Players.Where(p => p.NonInjuryUnavailability == UnavailabilityReason.InternationalDuty))
                player.NonInjuryUnavailability = UnavailabilityReason.Available;
            return;
        }

        foreach (var teamId in nationalTeamIds)
        {
            if (!world.Teams.TryGetValue(teamId, out var nationalTeam) || !nationalTeam.IsNational) continue;

            var announced = world.SquadAnnouncements
                .Where(a => a.TeamId == teamId && a.CompetitionId == competition.Id && a.PlayerIds.Count >= 11)
                .OrderByDescending(a => a.AnnouncedDate)
                .FirstOrDefault();

            var calledUp = announced?.PlayerIds ?? (IReadOnlyList<Guid>)nationalTeam.SquadPlayerIds;

            foreach (var playerId in calledUp)
            {
                var player = world.Players.FirstOrDefault(p => p.Id == playerId);
                if (player is null || player.IsRetired) continue;
                if (player.CurrentInjury is { } inj && inj.IsActiveOn(date) && inj.Severity >= InjurySeverity.Moderate) continue;
                if (player.NonInjuryUnavailability is UnavailabilityReason.Available or UnavailabilityReason.InternationalDuty)
                    player.NonInjuryUnavailability = UnavailabilityReason.InternationalDuty;
            }
        }
    }

    /// <summary>
    /// Phase 9, Slice 9.5: runs the franchise auction for a franchise league whose window has just
    /// opened, if the franchises do not already have squads for this year's season. Uses the
    /// dedicated RandomForAuction stream.
    /// </summary>
    private IEnumerable<GameEvent> RunFranchiseAuctionIfNeeded(GameCalendar calendar, DateOnly date, WorldState world, Competition competition)
    {
        var season = world.CompetitionSeasons
            .Where(s => s.CompetitionId == competition.Id && s.Year == date.Year && !s.IsCompleted)
            .OrderByDescending(s => s.Year).FirstOrDefault();
        if (season is null) return Array.Empty<GameEvent>();

        var franchiseIds = season.ParticipatingTeamIds.ToHashSet();
        var franchises = world.Teams.Values.Where(t => franchiseIds.Contains(t.Id)).ToList();
        if (franchises.Count == 0) return Array.Empty<GameEvent>();

        // Has THIS year's auction already run for this competition? (Squads persist between years -
        // RunAuction clears them - so check the contracts, which are dated.)
        bool alreadyRun = world.PlayerContracts.Any(c =>
            c.Kind == ContractKind.Franchise && c.CompetitionId == competition.Id
            && c.Status == ContractStatus.Active && c.StartDate.Year == date.Year);
        if (alreadyRun) return Array.Empty<GameEvent>();

        int compIndex = world.Competitions.Where(c => c.IsFranchiseAuctionLeague).OrderBy(c => c.Name).ToList().FindIndex(c => c.Id == competition.Id);
        var random = calendar.RandomForAuction(date.Year, Math.Max(0, compIndex));
        var mediaRandom = calendar.RandomForAuction(date.Year, 500 + Math.Max(0, compIndex));

        var events = new List<GameEvent>();
        bool mega = competition.LastMegaAuctionYear == 0 || date.Year - competition.LastMegaAuctionYear >= 3;

        // Phase 10: before the auction, national boards decide which centrally-contracted players
        // they withhold (NOC denied) because this franchise window clashes with international duty.
        events.AddRange(_centralContracts.ReviewNoc(world, competition, season, date, calendar.RandomForAuction(date.Year, 700 + Math.Max(0, compIndex))));

        // The media cycle: a preview (needs, purse, marquee names, a pre-auction press conference),
        // then the auction, then a report (biggest buy, the bargain "find of the auction", the
        // notable unsold, a per-franchise verdict, and a post-auction press conference).
        events.AddRange(_auctionMedia.Preview(world, competition, season, date, mega, mediaRandom));
        var summary = _auction.RunAuctionDetailed(world, competition, season, date, random);
        events.AddRange(summary.Events);
        events.AddRange(_auctionMedia.Report(world, competition, season, summary, date, mediaRandom));
        return events;
    }

    /// <summary>Wave 4: milestone tactical growth for coaches whose teams played a just-concluded competition.</summary>
    private IEnumerable<GameEvent> GrowCoachesFromCampaign(GameCalendar calendar, DateOnly date, WorldState world, Competition competition)
    {
        var driftEvents = new List<GameEvent>();
        var seasons = world.CompetitionSeasons
            .Where(s => s.CompetitionId == competition.Id && (s.Year == date.Year || s.Year == date.Year - 1))
            .ToList();
        if (seasons.Count == 0) return driftEvents;

        var random = calendar.RandomForDay(date);

        // Ordered by name (deterministic) - GrowFromMilestone consumes the RNG stream per team.
        foreach (var team in world.Teams.Values.OrderBy(t => t.Name))
        {
            var teamStandings = seasons.SelectMany(s => s.Standings).Where(st => st.TeamId == team.Id).ToList();
            if (teamStandings.Count == 0) continue;

            double performanceScore = _coachCareer.ScoreSeason(team, teamStandings);
            int startYear = date.Year;

            var coach = team.CurrentCoachId is { } coachId
                ? world.Coaches.FirstOrDefault(c => c.Id == coachId)
                : null;

            if (coach is not null)
            {
                var contract = world.CoachingContracts.FirstOrDefault(c => c.Id == coach.CurrentContractId);
                int seasonsAtClub = contract is null ? 1 : Math.Max(0, date.Year - contract.StartDate.Year);
                var drifted = _coachCareer.GrowFromMilestone(coach, team, competition.Prestige, seasonsAtClub, performanceScore, random);
                if (drifted is { } newPhil)
                    driftEvents.Add(new GameEvent(date, GameEventType.CoachPhilosophyDrift,
                        $"{coach.FullName}'s approach has evolved - his time at {team.Name} has shaped him into a {newPhil} coach.", coach.Id, team.Id));
            }
            else if (team.InterimCoachStaffId is { } interimId
                     && world.Staff.FirstOrDefault(s => s.Id == interimId) is { } interim)
            {
                _interimCoach.GrowInterimFromMilestone(interim, team, competition.Prestige, random);
            }
        }

        return driftEvents;
    }

    /// <summary>Wave 4: installs the assistant coach as interim for any team whose head-coach chair is vacant and has no interim already.</summary>
    private IEnumerable<GameEvent> ProcessVacancies(DateOnly date, WorldState world)
    {
        foreach (var team in world.Teams.Values)
        {
            // A franchise's coach vacancy is filled by FranchiseCoachService at window-open, not by
            // a permanent interim (Section H).
            if (team.IsFranchise) continue;
            if (team.CurrentCoachId is not null || team.InterimCoachStaffId is not null) continue;
            var ev = _interimCoach.AppointInterim(team, world, date);
            if (ev is not null) yield return ev;
        }
    }

    /// <summary>
    /// Once a year, the things that only make sense annually: players age, some retire, and the
    /// next edition of each staged competition is created.
    ///
    /// Annual rather than daily on purpose - attributes do not meaningfully move day to day, and
    /// running a decline curve 365 times a year would be both wrong and expensive.
    /// </summary>
    private IEnumerable<GameEvent> ProcessAnnualRollover(GameCalendar calendar, DateOnly date, WorldState world)
    {
        // Deterministic per save and per year - see GameCalendar.WorldSeed for why this must not
        // be derived from HashCode.Combine.
        var random = calendar.RandomForYear(date.Year);
        var events = new List<GameEvent>();

        // Phase 8: snapshot this year's form tallies BEFORE ProcessPhase7AnnualBusiness clears them -
        // the sustained-overperformance -> PotentialAbility-rise check (8.6) reads them at the tail
        // of this rollover, after all the existing RNG consumers, so nothing above shifts.
        var yearForm = world.YearForm.ToDictionary(kv => kv.Key, kv => (Avg: kv.Value.AverageRating, kv.Value.Matches));
        var newlyRetired = new List<Player>();

        foreach (var player in world.Players.Where(p => !p.IsRetired).ToList())
        {
            world.Teams.TryGetValue(player.CurrentTeamId ?? Guid.Empty, out var team);
            int facilityQuality = team?.Facilities.TrainingQuality ?? 40;

            // Phase 8, Slice 8.4: emergent, age-driven role drift - a quick who has lost his pace,
            // a batter sliding down the order. Captured here, compared after ageing has re-derived
            // the roles from what he has actually become.
            var (bowlRoleBefore, batRoleBefore) = (player.BowlingRole, player.BattingRole);

            // Post-Phase-5 rectification pass, Wave 2: training used to run HERE, once a year,
            // before ageing. It now resolves on the MONTHLY tick in twelve small increments (see
            // ProcessMonthlyTick) - a year's worth has already been applied by the time this
            // annual pass runs, so ageing still settles on top of a fully-trained player, exactly
            // as before, just at a cadence a player would actually notice.
            _ageing.ApplyAnnualAgeing(player, date, random, facilityQuality);
            player.Experience.RecordSeasonPlayed();

            // Form/Morale/MatchupConfidence decay used to fire once here, annually. Post-Phase-5
            // Wave 1 moved all three onto the new monthly tick (see ProcessMonthlyTick) so a mood
            // or a matchup edge fades gradually across the season instead of sitting fully live
            // until this one once-a-year pass - nothing left to do for them here.

            // An EMPTY tracker means nothing has simulated a season at all - that is unknown,
            // not "he played none". Once the match engine populates it, a player missing from a
            // populated tracker genuinely did play none, and that counts against him.
            int? matchesLastSeason = world.MatchesThisSeason.Count == 0
                ? null
                : world.MatchesThisSeason.TryGetValue(player.Id, out var n) ? n : 0;
            var assessment = _retirement.Assess(player, date, matchesLastSeason, random);

            if (assessment.ShouldRetire)
            {
                _retirement.Retire(player, date);
                newlyRetired.Add(player); // Phase 8, Slice 8.6: a pedigreed subset becomes rookie coaches at the tail
                events.Add(new GameEvent(date, GameEventType.PlayerRetired, assessment.Reason, player.Id));

                // Phase 7, Slice 7.7: a retiring great goes into the Hall of Fame - a high bar,
                // for the players a generation remembers.
                if (_hallOfFame.ConsiderInduction(player, world, date) is { } induction)
                {
                    events.Add(induction);

                    // Phase 16 (§10.7): a genuine one-club great gets a farewell / testimonial tour -
                    // a multi-match arc, real gate revenue for the club. NEWS + gate money only - no
                    // stat effect on a retiring player or his club that could skew the world.
                    var clubs = world.PlayerContracts.Where(c => c.PlayerId == player.Id).Select(c => c.TeamId).Distinct().ToList();
                    bool oneClub = (clubs.Count <= 1) && player.Experience.SeasonsPlayed >= 10;
                    if (oneClub && player.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var farewellClub))
                    {
                        farewellClub.Finances.Budget += 900_000 + player.Reputation.Worldwide * 12_000;
                        events.Add(new GameEvent(date, GameEventType.TestimonialTour,
                            $"{player.FullName} is given a farewell tour by {farewellClub.Name} - a series of tributes across the season for a one-club servant of {player.Experience.SeasonsPlayed} years.",
                            player.Id, farewellClub.Id));
                    }
                }
            }
            else
            {
                // Phase 8, Slice 8.4: report a genuine, age-driven change in how this player is
                // used - it emerges for free from RoleTraitDeriver re-deriving the role inside
                // ApplyAnnualAgeing; this just surfaces it.
                if (player.BowlingRole != bowlRoleBefore && bowlRoleBefore != BowlingRoleType.NotABowler)
                    events.Add(new GameEvent(date, GameEventType.PlayerDeveloped,
                        $"{player.FullName} is increasingly used as a {DescribeBowlingRole(player.BowlingRole)} as the years catch up with him.", player.Id));
                else if (player.BattingRole != batRoleBefore && BattingSlid(batRoleBefore, player.BattingRole))
                    events.Add(new GameEvent(date, GameEventType.PlayerDeveloped,
                        $"{player.FullName} has drifted down the order to {DescribeBattingRole(player.BattingRole)}.", player.Id));

                // Post-Phase-5 rectification pass, Wave 4: squad-standing decisions
                // (Roadmap/Rest/Drop, and ending a prior one) used to run HERE, annually. They
                // now run on the QUARTERLY tick (see ProcessQuarterlyTick) - a real series/tour
                // cycle, frequent enough that a two-month Rest actually ends near on time rather
                // than up to a year late, without being per-match noise. Retirement and
                // format-retirement stay annual: a decline curve genuinely is a once-a-year thing.

                // Section H: a player not retiring outright this year may still step back from
                // ONE format - Test cricket, in the well-known real pattern - while continuing
                // others. Checked per format, each against ITS OWN age curve and ITS OWN
                // playing-time signal (MatchesThisSeasonByFormat), same null-means-unknown
                // discipline as the whole-career check above: no format data simulated yet at
                // all reads as unknown, a real zero in one specific format (while he played
                // others) reads as a genuine "not picked for this format" signal.
                foreach (var format in FormatRetirementCheckOrder)
                {
                    if (player.RetiredFormats.Contains(format)) continue;

                    int? matchesInFormat = world.MatchesThisSeasonByFormat.Count == 0
                        ? null
                        : world.MatchesThisSeasonByFormat.TryGetValue(player.Id, out var byFormat) && byFormat.TryGetValue(format, out var formatCount)
                            ? formatCount : 0;

                    var formatAssessment = _retirement.AssessFormatRetirement(player, format, date, matchesInFormat, random);
                    if (formatAssessment.ShouldRetire)
                    {
                        _retirement.RetireFromFormat(player, format, date);
                        events.Add(new GameEvent(date, GameEventType.PlayerRetired, formatAssessment.Reason, player.Id));
                    }
                }

                // §5.8: the rare comeback - a name who stepped back from one format is coaxed back.
                foreach (var format in FormatRetirementCheckOrder)
                    if (_retirement.TryComebackFromFormat(player, format, date, random))
                        events.Add(new GameEvent(date, GameEventType.PlayerRetired,
                            $"{player.FullName} reverses his {format} retirement - the itch to play, and a side that wants him.", player.Id));
            }
        }

        // Phase 12 (§13.4): count the young players each club actually blooded this year (for a
        // DevelopYouth board objective), BEFORE the season match counts are cleared below.
        var youngBloodedByTeam = new Dictionary<Guid, int>();
        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise))
            youngBloodedByTeam[team.Id] = team.SquadPlayerIds
                .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Count(p => p is { IsRetired: false } && p.Age(date) <= 22
                            && world.MatchesThisSeasonByFormat.TryGetValue(p.Id, out var mf) && mf.Values.Sum() >= 1);

        world.MatchesThisSeason.Clear();
        world.MatchesThisSeasonByFormat.Clear();

        // §5.14: squad continuity. A settled, low-churn squad plays above the sum of its parts.
        // Compare this year's squad to last year's; a stable one earns a small standing lift, a
        // gutted one a dip. RNG-free.
        foreach (var team in world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise))
        {
            var current = team.SquadPlayerIds.ToHashSet();
            if (team.PreviousSquadIds.Count >= 8 && current.Count >= 8)
            {
                int retained = team.PreviousSquadIds.Count(current.Contains);
                team.SquadContinuity = Math.Clamp(retained / (double)Math.Max(1, team.PreviousSquadIds.Count), 0, 1);
                double target = 50 + (team.SquadContinuity - 0.7) * 30; // a fully-settled squad -> ~59, a gutted one -> ~35
                team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony + (target - team.DressingRoomHarmony) * 0.15, 0, 100);
            }
            team.PreviousSquadIds = team.SquadPlayerIds.ToList();
        }

        // Slice 6.7 + section E: build/evolve each national team's per-format pools now retirements
        // for the year are in - a player who has broken through comes into contention, a retired
        // one drops out. Notes go to the news channel.
        foreach (var nationalTeam in world.Teams.Values.Where(t => t.IsNational).OrderBy(t => t.Name))
        {
            var natCoach = nationalTeam.CurrentCoachId is { } ncid ? world.Coaches.FirstOrDefault(c => c.Id == ncid) : null;
            var notes = _nationalSelection.RefreshPool(nationalTeam, world.Players, date, world.NationalPools, natCoach);
            foreach (var note in notes)
                events.Add(new GameEvent(date, GameEventType.SquadDecisionMade, note, nationalTeam.Id));

            // Phase 7, Slice 7.8: the selection panel / chairman of selectors then leaves its
            // fingerprints on the pool - a reputation recall, a young player overlooked - to the
            // degree the national board is weak and political.
            events.AddRange(_selectionPanel.ApplyPanelInfluence(
                nationalTeam, world.NationalPools.ToList(), world.Players.ToList(), date, random));

            // Meeting-driven-selection ticket (requirement A): the annual pool refresh, narrated as
            // a genuine meeting - attendees, findings, and a dissent note when the panel is weak.
            var selectorStaff = world.Staff.Where(s => nationalTeam.StaffIds.Contains(s.Id)
                && s.Role is Enums.StaffRole.Selector or Enums.StaffRole.ChiefSelector).ToList();
            var poolMeeting = _poolMeeting.Hold(nationalTeam, notes, natCoach, selectorStaff, date);
            string poolStory = poolMeeting.Headline
                + (poolMeeting.Findings.Count > 0 ? $" {poolMeeting.Findings[0]}" : " No real changes this time.")
                + (poolMeeting.DissentNote is not null ? $" {poolMeeting.DissentNote}" : "");
            events.Add(new GameEvent(date, GameEventType.NationalPoolMeeting, poolStory, nationalTeam.Id));
        }

        // Phase 10: with the pools settled, each national board reviews its central contracts.
        events.AddRange(_centralContracts.ReviewAnnually(world, date));

        // TeamMorale decay also moved to the new monthly tick (ProcessMonthlyTick) - see the note
        // by the player loop above.

        // Section D: a coach's own standing - board trust, tactical growth, and genuine job
        // security. Only coaches with an ACTIVE contract at a real team are evaluated; one whose
        // contract already lapsed via ProcessContractExpiry, or who is between jobs, has no
        // season to be judged on. Evaluated against last year's standings (date.Year - 1) because
        // this runs on 1 January of the new year, after the season that just finished.
        foreach (var coach in world.Coaches.Where(c => c.CurrentTeamId is not null && c.CurrentContractId is not null))
        {
            var contract = world.CoachingContracts.FirstOrDefault(c => c.Id == coach.CurrentContractId && c.Status == ContractStatus.Active);
            if (contract is null) continue;
            if (!world.Teams.TryGetValue(coach.CurrentTeamId!.Value, out var team)) continue;

            var seasonsThisYear = world.CompetitionSeasons.Where(s => s.Year == date.Year - 1).ToList();
            var standingsThisYear = seasonsThisYear
                .SelectMany(s => s.Standings)
                .Where(s => s.TeamId == team.Id)
                .ToList();

            double performanceScore = _coachCareer.ScoreSeason(team, standingsThisYear);
            int seasonsAtClub = Math.Max(0, date.Year - contract.StartDate.Year);

            // Wave 4: a head coach's TACTICAL growth no longer runs here - it is milestone-driven
            // (see GrowCoachesFromCampaign, fired on CompetitionWindowClosed), because a coach's
            // influence is a whole campaign's accumulated results settling into his philosophy,
            // not a calendar event. What stays annual is the BOARD's verdict on the season -
            // trust, objectives, dismissal, satisfaction, resignation - which genuinely is a
            // once-a-season judgement.

            // Phase 12 (§4.2): credit this year's salary to the coach's worldwide earnings ledger.
            if (team.IsNational) coach.CareerEarnings.CreditNational(contract.AnnualSalary);
            else coach.CareerEarnings.CreditDomestic(contract.AnnualSalary);

            if (standingsThisYear.Count == 0) continue; // nothing played - no verdict to reach

            // Wave 7: fold this season into the coach's multi-year track record BEFORE the board's
            // verdict, so the "seasons since a title" clock and the achievement leniency the board
            // applies both already account for whatever he did (or did not) win this year.
            bool wonTitle = seasonsThisYear.Any(s => s.ChampionTeamId == team.Id);
            var majorSeasons = seasonsThisYear
                .Where(s => world.Competitions.FirstOrDefault(c => c.Id == s.CompetitionId)?.IsMajor == true)
                .ToList();
            bool wonMajorTitle = majorSeasons.Any(s => s.ChampionTeamId == team.Id);
            bool reachedMajorFinal = majorSeasons.Any(s => s.PlayoffQualifiedTeamIds.Contains(team.Id) || s.ChampionTeamId == team.Id);
            _coachCareer.UpdateCareerRecord(coach, team, performanceScore, wonTitle, wonMajorTitle, reachedMajorFinal);

            // Board-objectives follow-up: whichever structured targets the board actually set for
            // THIS specific contract year. seasonsAtClub is computed above as
            // (date.Year - contract.StartDate.Year) where `date` has already ticked forward to
            // the year AFTER the season being judged - so for a contract starting in 2020, the
            // season completed in 2026 is judged with date.Year=2027 and seasonsAtClub=7, which
            // IS the natural "7th year of the contract" number already, needing no further +1.
            // (A real off-by-one caught by this exact wiring's own integration test: the first
            // draft added one here, which meant year-1 objectives were only ever looked up under
            // key 2 and consequently never found.)
            var objectives = contract.StructuredObjectivesByYear.TryGetValue(seasonsAtClub, out var objs)
                ? objs : new List<BoardObjective>();
            int youngBlooded = youngBloodedByTeam.GetValueOrDefault(team.Id);
            var objectiveResults = objectives.Count == 0
                ? Array.Empty<BoardObjectiveResult>()
                : _coachCareer.EvaluateObjectives(objectives, team, seasonsThisYear, standingsThisYear, youngBlooded);

            var review = _coachCareer.EvaluateSeason(coach, contract, team, performanceScore, seasonsAtClub, random, objectiveResults);
            if (review.Dismissed)
            {
                events.Add(new GameEvent(date, GameEventType.CoachDismissed, review.Reason, coach.Id, team.Id));
                continue; // no longer employed here - nothing left to evaluate for satisfaction/resignation
            }

            // Board-objectives follow-up: report the board's verdict whenever objectives were
            // actually set for the year, even when it did not cost the coach his job - otherwise
            // the (far more common) "missed his target, survived anyway" case would be judged
            // internally and never actually reported to anyone.
            if (objectiveResults.Count > 0)
                events.Add(new GameEvent(date, GameEventType.BoardObjectivesReviewed, review.Reason, coach.Id, team.Id));

            // Section D follow-up: CareerSatisfaction, and the coach's own choice to walk away -
            // evaluated only for a coach who kept his job this rollover.
            _coachCareer.EvaluateSatisfaction(coach, team, performanceScore, seasonsAtClub);
            if (_coachCareer.TryResign(coach, contract, team, random, out string resignReason))
                events.Add(new GameEvent(date, GameEventType.CoachResigned, resignReason, coach.Id, team.Id));
        }

        // Phase 5 Part 2: backroom staff grow with tenure too, on the same annual cadence
        // everything else in this rollover already uses. Only employed staff - an unemployed
        // free agent is not "getting better on the job" because there is no job.
        foreach (var staff in world.Staff.Where(s => s.TeamId is not null).ToList())
        {
            _staffCareer.GrowFromTenure(staff, random);

            // Job-market follow-up: CareerSatisfaction and the staff member's own choice to walk
            // away, the same shape Coach's CareerSatisfaction/TryResign already have above -
            // evaluated only against a real, currently-active contract, so a staff member whose
            // contract has already lapsed some other way this same tick is not double-processed.
            var staffContract = world.StaffContracts.FirstOrDefault(c => c.StaffId == staff.Id && c.Status == ContractStatus.Active);
            if (staffContract is null || !world.Teams.TryGetValue(staff.TeamId!.Value, out var staffTeam)) continue;

            int yearsAtClub = Math.Max(0, date.Year - staffContract.StartDate.Year);
            _staffCareer.EvaluateSatisfaction(staff, staffTeam, yearsAtClub);

            if (_staffCareer.TryResign(staff, staffContract, staffTeam, random, out string staffResignReason))
                events.Add(new GameEvent(date, GameEventType.StaffResigned, staffResignReason, staff.Id, staffTeam.Id));
        }

        // Phase 6, Slice 6.1: an emergent rivalry that has not been renewed by a meeting that
        // mattered slowly cools off. Geographic/historical rivalries do not fade (Rivalry.Fade
        // is a no-op for them). Rivalries are a handful per world, so a yearly scan is cheap.
        foreach (var rivalry in world.Rivalries)
        {
            bool stale = rivalry.LastReinforced is not { } last || (date.DayNumber - last.DayNumber) > 400;
            if (stale) rivalry.Fade(3);
        }

        events.AddRange(ProcessPhase7AnnualBusiness(date, world, random));

        // Phase 10 (§17.5): domestic competitions expand or contract on a sustained reputation
        // trend. RNG-free; must run BEFORE CreateSeasonsForYear so the pending expansion is applied
        // to next season's rosters. `date.Year - 1` is the season that just completed.
        events.AddRange(_competitionLifecycle.ReviewAnnually(world, date, date.Year - 1));

        // Create this year's edition of every competition being staged, so multi-season history
        // accumulates on its own as the years pass. Slice 6.3: this now also carries participants
        // forward, applies promotion/relegation for a linked two-tier pair, and GENERATES the
        // season's fixtures - so a seeded world keeps playing season after season rather than
        // going quiet after the one the seeder scheduled.
        events.AddRange(_seasonRunner.CreateSeasonsForYear(world, date, date.Year));

        // Phase 8: the youth pipeline and its ecosystem loops. Placed LAST, consuming `random` only
        // after every existing annual consumer above, so no pre-Phase-8 behaviour shifts.
        events.AddRange(ProcessPhase8Annual(date, world, random, newlyRetired, yearForm));

        // Phase 9: the annual market business - market-index inflation, contracts for players who
        // have joined a squad without one (academy graduates), the franchise auction, loyalty
        // rewards, and closing out a retired player's contract. Consumes `random` after everything
        // above.
        events.AddRange(ProcessPhase9Annual(date, world, random, newlyRetired));

        // Phase 14: player-life & development depth - personality arcs, skill regression from a
        // narrow diet of cricket, technical flaws seeded and remediated. Consumes `random` at the
        // very tail, after every Phase 8/9 annual consumer, so nothing above shifts.
        events.AddRange(_personalityDev.ReviewAnnually(world, date, random));
        // SkillRegressionService uses its own date-seeded local Random (the nationality-switch roll),
        // so it does not perturb the shared annual stream the consumers below draw from.
        events.AddRange(_skillRegression.ReviewAnnually(world, date));
        // S6: opportunity-driven changes of international allegiance (its own date-seeded Random,
        // like SkillRegressionService - does not perturb the shared annual stream).
        events.AddRange(_representation.ReviewAnnually(world, date));
        events.AddRange(_flawRemediation.ReviewAnnually(world, date, random));

        // S5: the ICC's annual revenue distribution to the member boards + a light national finance
        // loop (national-coach salary + a board operating cost drawn against the ICC share). RNG-FREE
        // - a deterministic distribution formula.
        events.AddRange(_iccRevenue.DistributeAnnually(world, calendar, date));

        // S7: an Associate nation that has sustained a real record earns ICC Full Membership + Test
        // status - a rare, telegraphed, IRREVOCABLE grant (Article 2.7). RNG-free.
        events.AddRange(_fullMembership.ReviewAnnually(world, date));

        // S2: a national board splits its head-coach job across formats under clash pressure, or
        // reunifies a split that has become a coordination headache. Own date-seeded Random.
        events.AddRange(_coachingStructure.ReviewAnnually(world, date));

        // Phase 16 (§15.5): every fifth year the media names a Team of the Era. RNG-FREE, news only.
        if (date.Year % 5 == 0 && world.CareerStats.Count > 0
            && _allTimeXi.NameTeamOfEra(world, date, yearsCovered: 5) is { } xi)
            events.Add(xi);

        // Phase 16 (§15.6): a nation entering a golden generation (or a lean spell) is news.
        // RNG-FREE - a deterministic wave crossing a threshold, reported once. News only.
        foreach (var kv in world.CountryProfiles.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            double now = AcademyService.EraQualityFactor(kv.Value, date.Year);
            double last = AcademyService.EraQualityFactor(kv.Value, date.Year - 1);
            if (now >= 1.11 && last < 1.11)
                events.Add(new GameEvent(date, GameEventType.GoldenGeneration,
                    $"{kv.Key} cricket is producing an exceptional crop of young players - the academies are as strong as they have been in years.", null));
            else if (now <= 0.89 && last > 0.89)
                events.Add(new GameEvent(date, GameEventType.GoldenGeneration,
                    $"A lean spell for {kv.Key} - the age-group ranks look thin, and the next few intakes will be a rebuilding job.", null));
        }

        return events;
    }

    /// <summary>
    /// Phase 9: the once-a-year market business. Runs at the very tail of the rollover, after
    /// Phase 8's academy pass has produced this year's graduates.
    /// </summary>
    private IEnumerable<GameEvent> ProcessPhase9Annual(DateOnly date, WorldState world, Random random, IReadOnlyList<Player> newlyRetired)
    {
        var events = new List<GameEvent>();
        if (world.PlayerContracts.Count == 0) return events; // no contract data - the market is inert

        // --- market-index inflation ---
        world.MarketIndex = Math.Clamp(world.MarketIndex * (1.0 + 0.02 + random.NextDouble() * 0.03), 1.0, 6.0);

        // --- close out retired players' contracts ---
        foreach (var retiree in newlyRetired)
            foreach (var c in world.PlayerContracts.Where(c => c.PlayerId == retiree.Id && c.Status == ContractStatus.Active))
                c.Status = ContractStatus.Terminated;

        // --- a starter contract for anyone now in a senior squad without one (academy graduates) ---
        foreach (var team in world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise).OrderBy(t => t.Name))
        {
            foreach (var pid in team.SquadPlayerIds.ToList())
            {
                var player = world.Players.FirstOrDefault(p => p.Id == pid);
                if (player is null || player.IsRetired || player.AcademyTeamId is not null) continue;
                if (PlayerContractService.ActiveDomesticContract(world, pid) is not null) continue;

                double wage = new PlayerValuationService().MarketWage(player, world.MarketIndex) * 0.7; // a first senior deal is modest
                var contract = _playerContracts.Sign(world, player, team, date, wage, years: random.Next(2, 4), isHomegrown: true);
                events.Add(new GameEvent(date, GameEventType.PlayerContractRenewed,
                    $"{team.Name} hand {player.FullName} his first professional contract, to {contract.EndDate:yyyy}.",
                    player.Id, team.Id));
            }
        }

        // The franchise auction runs on window-open (ProcessCompetitionWindows), not here - it
        // needs to have happened before the franchise season's first fixture, and the annual
        // rollover (1 January) is months before the April window.

        // --- loyalty bonuses + testimonials for one-club servants ---
        events.AddRange(_playerContracts.ProcessLoyaltyRewards(world, date));

        return events;
    }

    /// <summary>
    /// Phase 8: the annual youth-development pass - academy intake and review, the retiring-player
    /// -> coach-market loop, the sustained-overperformance -> PotentialAbility-rise story, and
    /// development loans. Runs at the very end of the rollover.
    /// </summary>
    private IEnumerable<GameEvent> ProcessPhase8Annual(
        DateOnly date, WorldState world, Random random, IReadOnlyList<Player> newlyRetired,
        IReadOnlyDictionary<Guid, (double Avg, int Matches)> yearForm)
    {
        var events = new List<GameEvent>();

        // --- 8.1-8.3: the youth academy, per domestic club. A FRANCHISE does not operate a youth
        // academy in this model (Section M): it is a privately-owned tournament team, not a
        // development club, and it assembles its squad through the auction. If franchise academies
        // are ever added, GraduateToOpenMarket is the path - a graduate goes to the auction pool,
        // never straight into the franchise squad.
        foreach (var team in world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise).OrderBy(t => t.Name).ToList())
        {
            var homeGround = team.HomeGroundId is { } gid && world.Grounds.TryGetValue(gid, out var g) ? g : null;
            int scoutingQuality = ScoutingQualityFor(world, team);

            // Intake: a new cohort every year, appended to the END of the player list (academy
            // players stay at the tail so senior-player RNG in the monthly/annual ticks is unchanged).
            var intake = _academy.GenerateIntake(team, homeGround, date, random, scoutingQuality, world.ProfileFor(team.Country));
            foreach (var prospect in intake) world.Players.Add(prospect);
            if (intake.Count > 0)
                events.Add(new GameEvent(date, GameEventType.SquadDecisionMade,
                    $"{team.Name}'s academy takes in {intake.Count} new prospects.", team.Id));

            // Review: promote graduates, release the plateaued. Reads a SCOUTED estimate, not the
            // truth - a weak-scouting club genuinely misjudges. A human club that kept the call
            // (DelegateAcademy = false) gets recommendations only.
            var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            bool apply = coach?.IsHumanControlled != true || team.ManagerPreferences.DelegateAcademy;
            var academyRoster = team.AcademyPlayerIds
                .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Where(p => p is not null).Select(p => p!).ToList();
            var review = _academy.ReviewAcademy(academyRoster, team, date, random, scoutingQuality, apply,
                graduateToOpenMarket: team.IsFranchise);
            events.AddRange(review.Events);

            // §5.5: the "next in line" pipeline signal - the same roster the academy review just
            // read, one more real thing it can say about it.
            events.AddRange(_aiClub.SurfaceSuccessionSignals(world, team, date));

            // §6.7: academy poaching. A genuine standout at a small club is approached by a bigger
            // one, which pays a development-compensation fee. Rare, and only a real prospect at a
            // club clearly below the poacher's level.
            if (!team.IsFranchise)
            {
                var scout = new ScoutingAccuracyService();
                foreach (var prospect in academyRoster.Where(p => p.AcademyTeamId == team.Id && p.Age(date) is >= 16 and <= 18).ToList())
                {
                    double estCeiling = scout.EstimatePotentialAbility(prospect.PotentialAbility, scoutingQuality);
                    if (estCeiling < 150) continue;
                    var poacher = world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise
                            && t.Country == team.Country && t.Reputation.Domestic > team.Reputation.Domestic + 22
                            && t.Board.Wealth >= 55 && !t.Board.UnderTransferEmbargo)
                        .OrderByDescending(t => t.Reputation.Domestic).ThenBy(t => t.Name).FirstOrDefault();
                    if (poacher is null || random.NextDouble() > 0.18) continue;

                    double fee = Math.Round(120_000 + (estCeiling - 150) * 8_000, 0);
                    poacher.Finances.Budget -= fee;
                    team.Finances.Budget += fee;
                    team.AcademyPlayerIds.Remove(prospect.Id);
                    poacher.AcademyPlayerIds.Add(prospect.Id);
                    prospect.AcademyTeamId = poacher.Id;
                    prospect.CurrentTeamId = poacher.Id;
                    prospect.ParentClubId = poacher.Id;
                    events.Add(new GameEvent(date, GameEventType.PlayerSigned,
                        $"{poacher.Name} poach the highly-rated teenager {prospect.FullName} from {team.Name}'s academy, paying {fee:N0} in compensation.",
                        prospect.Id, poacher.Id));
                }
            }
        }

        // --- NEW-C: this year's retirees take up their second careers - head coach, a specialist
        // coach, scout, national selector, mentor (legends only), or out of the game - weighted by
        // their playing pedigree and driven by what they actually want. Consumes `random` at the
        // same tail position the old flat "pedigreed retiree enters coaching" block did.
        events.AddRange(_retirementCareers.PlaceRetirees(world, newlyRetired, date, random));

        // --- 8.6: coaching-badge progression for employed coaches ---
        foreach (var coach in world.Coaches.Where(c => c.CurrentTeamId is not null && !c.IsRetired).ToList())
        {
            var newLicence = _coachCareer.ProgressLicence(coach, coach.CareerRecord.SeasonsCoached, random);
            if (newLicence is { } lic)
                events.Add(new GameEvent(date, GameEventType.PlayerDeveloped,
                    $"{coach.FullName} earns his {lic} coaching badge.", coach.Id,
                    coach.CurrentTeamId));
        }

        // --- 8.6: sustained real-match overperformance nudges a young player's ceiling up ---
        foreach (var player in world.Players.Where(p => !p.IsRetired && p.AcademyTeamId is null).ToList())
        {
            int age = player.Age(date);
            if (age > 26) continue;
            if (!yearForm.TryGetValue(player.Id, out var yf) || yf.Matches < 6 || yf.Avg < 12) continue;

            double headroomLeft = player.PotentialAbility - player.CurrentAbility;
            if (headroomLeft > 22) continue; // he still has room to grow into his current ceiling - no need to raise it

            // A genuinely strong year for a player already near his expected ceiling: "he has
            // turned out better than we thought". Probabilistic, at most a few points, at most once
            // a year, and never past the composite max.
            double chance = Math.Clamp(0.10 + (yf.Avg - 12) / 60.0, 0.05, 0.45);
            if (random.NextDouble() >= chance) continue;

            int rise = random.Next(1, 4);
            player.PotentialAbility = Math.Min(200, player.PotentialAbility + rise);
            events.Add(new GameEvent(date, GameEventType.PlayerDeveloped,
                $"{player.FullName} has outperformed everything expected of him - the ceiling has been raised.", player.Id, player.CurrentTeamId));
        }

        // --- 8.7: development loans, per club that delegates them ---
        foreach (var team in world.Teams.Values.Where(t => !t.IsNational).OrderBy(t => t.Name).ToList())
        {
            var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            if (coach?.IsHumanControlled == true && !team.ManagerPreferences.DelegateLoans) continue;
            events.AddRange(_loans.ConsiderLoans(world, team, date, random));
        }

        return events;
    }

    private static int ScoutingQualityFor(WorldState world, Team team)
    {
        int q = Math.Clamp(team.Facilities.ScoutingQuality, 0, 100);
        var teamStaff = world.Staff.Where(s => team.StaffIds.Contains(s.Id)).ToList();
        if (teamStaff.Any(s => s.Role == StaffRole.ChiefScout)) q += 10;
        if (teamStaff.Any(s => s.Role == StaffRole.Scout)) q += 6;
        return Math.Clamp(q, 0, 100);
    }

    private static bool BattingSlid(BattingRole from, BattingRole to) => RoleDepth(to) > RoleDepth(from);
    private static int RoleDepth(BattingRole r) => r switch
    {
        BattingRole.Opener => 0, BattingRole.TopOrder => 1, BattingRole.MiddleOrder => 2,
        BattingRole.LowerOrder => 3, _ => 4
    };

    private static string DescribeBattingRole(BattingRole r) => r switch
    {
        BattingRole.Opener => "opener", BattingRole.TopOrder => "the top order",
        BattingRole.MiddleOrder => "the middle order", BattingRole.LowerOrder => "the lower order",
        _ => "the tail"
    };

    private static string DescribeBowlingRole(BowlingRoleType r) => r switch
    {
        BowlingRoleType.OpeningBowler => "new-ball bowler",
        BowlingRoleType.FirstChange => "first-change seamer",
        BowlingRoleType.MiddleOversSpecialist => "middle-overs operator",
        BowlingRoleType.DeathBowler => "death-overs specialist",
        BowlingRoleType.SpecialistSpinner => "front-line spinner",
        BowlingRoleType.PartTimeBowler => "part-time option",
        _ => "change bowler"
    };

    /// <summary>
    /// Phase 7: the annual board / finance / awards / records business, run at the rollover after
    /// retirements and the coach reviews are in. Ordered so each step sees the last one's effect.
    /// </summary>
    private IEnumerable<GameEvent> ProcessPhase7AnnualBusiness(DateOnly date, WorldState world, Random random)
    {
        var events = new List<GameEvent>();
        int yearJustFinished = date.Year - 1;

        // Phase 13 (§9.7): sign / renew the multi-year sponsorship deals before the year is
        // settled, against what each club commands now.
        events.AddRange(_sponsorship.ReviewAnnually(world, date));

        // --- 7.1: settle every club's finances for the year just gone ---
        foreach (var (team, result) in _seasonFinance.SettleYear(world, yearJustFinished))
        {
            events.Add(new GameEvent(date, GameEventType.TeamFinancesSettled,
                $"{team.Name}'s {yearJustFinished} accounts: sponsorship {result.SponsorshipIncome:N0}, matchday {result.MatchdayIncome:N0}, " +
                $"upkeep {result.UpkeepCost:N0}, wages {result.WageCost:N0} - net {result.NetResult:N0}, budget now {result.ClosingBudget:N0}.",
                team.Id));

            // --- 7.3: the board reacts - fan sentiment, next season's budget, and a possible takeover ---
            var standings = world.CompetitionSeasons.Where(s => s.Year == yearJustFinished)
                .SelectMany(s => s.Standings).Where(s => s.TeamId == team.Id).ToList();
            double perf = _coachCareer.ScoreSeason(team, standings);
            bool wonTitle = world.CompetitionSeasons.Any(s => s.Year == yearJustFinished && s.ChampionTeamId == team.Id);

            _boardService.ReviewFanSentiment(team, perf, wonTitle, result.NetResult);

            // Phase 12 (§13.1/§13.2/§13.6): the boardroom arc - unity, the owner's trajectory, a
            // possible boardroom coup or a move onto the market. Before SetSeasonBudget so a fresh
            // agenda / trajectory feeds this year's number.
            events.AddRange(_boardService.ReviewBoardroom(team, date, perf, result.NetResult, random));

            events.Add(new GameEvent(date, GameEventType.SeasonBudgetSet,
                _boardService.SetSeasonBudget(team, result.ClosingBudget, result.NetResult), team.Id));

            if (_boardService.ConsiderTakeover(team, date, random) is { } takeover)
                events.Add(takeover);
        }

        // --- 7 (finance follow-up): financial fair play - a club running a sustained large
        // deficit (which the wage bill above can genuinely produce) gets a points deduction and a
        // spending embargo; a recovered one has the embargo lifted. Runs AFTER the settlement so
        // it judges the freshly-updated books. ---
        events.AddRange(_ffp.Review(world, date, yearJustFinished));

        // --- 7.5: annual awards, from the year's accumulated form ---
        foreach (var award in _awards.AnnualAwards(world.YearForm, world.TeamRankings.ToList(), world.Teams, yearJustFinished))
        {
            world.Awards.Add(award);
            events.Add(new GameEvent(date, GameEventType.SeasonAward, award.Citation, award.PlayerId, award.TeamId));
        }
        world.YearForm.Clear();

        // --- 7.7: career-aggregate records (most runs / wickets / catches) ---
        events.AddRange(_recordProgression.ReviewCareerRecords(world, date));

        // --- 7.9: umpire panel - promotion/demotion, ageing, retirement ---
        if (world.Umpires.Count > 0)
            events.AddRange(_umpires.SeasonReview(world.Umpires, date, random));

        return events;
    }

    /// <summary>How far ahead of a contract's own EndDate the board makes its renewal call - a real club decides before a contract actually lapses, not on the final day.</summary>
    private const int RenewalWindowDays = 90;

    /// <summary>
    /// Job-market follow-up: the board's renewal decision, made once per contract at a fixed
    /// window ahead of its own EndDate (a single calendar day, so this fires exactly once per
    /// contract as the daily tick passes it - no separate "already decided" flag needed). Runs
    /// BEFORE ProcessContractExpiry in the same day's tick: a renewal moves EndDate forward, so a
    /// contract that was just renewed no longer reaches its original EndDate and the expiry sweep
    /// below correctly has nothing left to do for it.
    /// </summary>
    private IEnumerable<GameEvent> ProcessContractRenewals(GameCalendar calendar, DateOnly date, WorldState world)
    {
        var coachRenewalsToday = world.CoachingContracts
            .Where(c => c.Status == ContractStatus.Active && c.EndDate.AddDays(-RenewalWindowDays) == date)
            .ToList();
        var staffRenewalsToday = world.StaffContracts
            .Where(c => c.Status == ContractStatus.Active && c.EndDate.AddDays(-RenewalWindowDays) == date)
            .ToList();
        // Phase 9, Slice 9.1: player contracts renew on the same 90-day window.
        var playerRenewalsToday = world.PlayerContracts
            .Where(c => c.Kind == ContractKind.Domestic && c.Status == ContractStatus.Active && c.EndDate.AddDays(-RenewalWindowDays) == date)
            .ToList();

        if (coachRenewalsToday.Count == 0 && staffRenewalsToday.Count == 0 && playerRenewalsToday.Count == 0) yield break;

        // One stream for the whole day, reused across every contract due today - the same
        // "one seeded Random consumed sequentially" discipline ProcessAnnualRollover already uses
        // for its own per-player loop, just at day granularity instead of year granularity.
        var random = calendar.RandomForDay(date);

        foreach (var contract in coachRenewalsToday)
        {
            var coach = world.Coaches.FirstOrDefault(c => c.Id == contract.CoachId);
            if (coach is null || !world.Teams.TryGetValue(contract.TeamId, out var team)) continue;
            // Corrections pass (correction 3): franchise coaching is managed by FranchiseCoachService
            // (the incumbent is kept and his contract renewed at the end-of-campaign review), not by
            // the generic domestic/national renewal machinery.
            if (team.IsFranchise) continue;

            var decision = _coachCareer.EvaluateRenewal(coach, contract, team, random);
            yield return new GameEvent(date,
                decision.Renewed ? GameEventType.CoachContractRenewed : GameEventType.ContractExpired,
                decision.Reason, coach.Id, team.Id);
        }

        foreach (var contract in staffRenewalsToday)
        {
            var staff = world.Staff.FirstOrDefault(s => s.Id == contract.StaffId);
            if (staff is null || !world.Teams.TryGetValue(contract.TeamId, out var team)) continue;
            // NEW-A: a franchise staff contract is managed by FranchiseCoachService (year-round,
            // like the head coach), not by the generic domestic/national renewal machinery.
            if (team.IsFranchise) continue;

            var decision = _staffCareer.EvaluateRenewal(staff, contract, team, random);
            yield return new GameEvent(date,
                decision.Renewed ? GameEventType.StaffContractRenewed : GameEventType.ContractExpired,
                decision.Reason, staff.Id, team.Id);
        }

        // Phase 9, Slice 9.1: player renewals - consumed AFTER the coach/staff loops so their
        // determinism is untouched. A non-renewal here just means the contract runs to its end
        // date and ProcessContractExpiry turns him into a free agent (the Bosman path).
        foreach (var contract in playerRenewalsToday)
        {
            var player = world.Players.FirstOrDefault(p => p.Id == contract.PlayerId);
            if (player is null || player.IsRetired || !world.Teams.TryGetValue(contract.TeamId, out var team)) continue;

            double agentPush = AgentService.AgentFor(world, player)?.WageDemandMultiplier ?? 1.0;
            var decision = _playerContracts.EvaluateRenewal(contract, player, team, date, random, world.MarketIndex, agentPush);
            yield return new GameEvent(date,
                decision.Renewed ? GameEventType.PlayerContractRenewed : GameEventType.ContractExpired,
                decision.Reason, player.Id, team.Id);
        }
    }

    private IEnumerable<GameEvent> ProcessContractExpiry(DateOnly date, WorldState world)
    {
        foreach (var contract in world.CoachingContracts)
        {
            if (contract.Status != ContractStatus.Active || contract.EndDate >= date) continue;

            // Corrections pass (correction 3): a franchise coaching contract is managed by
            // FranchiseCoachService - the incumbent is kept year-round and his term renewed at the
            // end-of-campaign review, so a passed EndDate here is not a real expiry.
            if (world.Teams.TryGetValue(contract.TeamId, out var ct) && ct.IsFranchise) continue;

            contract.Status = ContractStatus.Expired;

            // Job-market follow-up: this used to leave Coach.CurrentTeamId/CurrentContractId (and
            // Team.CurrentCoachId) pointing at the team even after the contract itself was marked
            // Expired - flagged twice before as a real gap and left alone as shipped code until the
            // job market made it a hard correctness requirement rather than a stylistic nicety (a
            // hiring flow reading Team.CurrentCoachId to check a job is actually vacant would
            // otherwise see it wrongly still filled). Fixed here, alongside the same fix already
            // made to EvaluateSeason's dismissal path and TryResign's resignation path.
            var coach = world.Coaches.FirstOrDefault(c => c.Id == contract.CoachId);
            world.Teams.TryGetValue(contract.TeamId, out var expiredTeam);
            if (coach is not null)
            {
                if (coach.CurrentContractId == contract.Id) { coach.CurrentTeamId = null; coach.CurrentContractId = null; }
                if (expiredTeam is not null && expiredTeam.CurrentCoachId == coach.Id) expiredTeam.CurrentCoachId = null;
            }

            string teamName = expiredTeam?.Name ?? "their club";
            yield return new GameEvent(date, GameEventType.ContractExpired,
                $"A coaching contract at {teamName} has expired.", contract.CoachId, contract.TeamId);
        }

        // Phase 5 Part 2: the same expiry sweep for backroom staff.
        foreach (var contract in world.StaffContracts)
        {
            if (contract.Status != ContractStatus.Active || contract.EndDate >= date) continue;
            // NEW-A: FranchiseCoachService owns a franchise staff contract's lifecycle (year-round).
            if (world.Teams.TryGetValue(contract.TeamId, out var ft) && ft.IsFranchise) continue;

            var staff = world.Staff.FirstOrDefault(s => s.Id == contract.StaffId);
            world.Teams.TryGetValue(contract.TeamId, out var team);
            _staffCareer.HandleExpiry(staff, contract, team);

            string teamName = team?.Name ?? "their club";
            yield return new GameEvent(date, GameEventType.ContractExpired,
                $"A staff contract at {teamName} has expired.", contract.StaffId, contract.TeamId);
        }

        // Phase 9, Slice 9.1: a player's domestic contract has run out with no renewal - he leaves
        // for nothing (Bosman) and becomes a free agent. FreeAgentMarketService picks him up.
        foreach (var contract in world.PlayerContracts.Where(c =>
                     c.Kind == ContractKind.Domestic && c.Status == ContractStatus.Active && c.EndDate < date).ToList())
        {
            var player = world.Players.FirstOrDefault(p => p.Id == contract.PlayerId);
            if (player is null) { contract.Status = ContractStatus.Expired; continue; }
            if (player.IsRetired) { contract.Status = ContractStatus.Expired; continue; }

            _playerContracts.MakeFreeAgent(world, player, contract, date);
            var teamName = world.Teams.TryGetValue(contract.TeamId, out var t) ? t.Name : "his club";
            yield return new GameEvent(date, GameEventType.PlayerBecameFreeAgent,
                $"{player.FullName}'s contract at {teamName} has expired - he leaves on a free transfer.",
                player.Id, contract.TeamId);
        }

        // Phase 9, Slice 9.5: a franchise contract expires the day its window closes for the year -
        // just mark it done (the auction clears the squads and re-signs next year).
        foreach (var contract in world.PlayerContracts.Where(c =>
                     c.Kind == ContractKind.Franchise && c.Status == ContractStatus.Active && c.EndDate < date).ToList())
            contract.Status = ContractStatus.Expired;
    }
}

internal static class DictionaryExtensions
{
    /// <summary>Small adapter so WorldState can hold mutable dictionaries while services that only read them take IReadOnlyDictionary.</summary>
    public static IReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(this IDictionary<TKey, TValue> source) where TKey : notnull =>
        source as IReadOnlyDictionary<TKey, TValue> ?? new Dictionary<TKey, TValue>(source);
}
