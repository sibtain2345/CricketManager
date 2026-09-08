using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 6, Slice 6.2: the AI club-manager brain for the out-of-match decisions the world
/// simulation would otherwise never make - so an AI-run world actually runs its clubs rather than
/// just playing whatever XI happens to fall out of a full-squad ranking.
///
/// Three jobs, all resolved on the monthly tick:
/// - **Squad announcement** ahead of each competition the team is in - one SquadAnnouncement per
///   competition window, so FixturePlayService can restrict an XI to a genuine named squad.
/// - **Filling vacancies** - a head-coach chair with nobody in it (and no assistant to promote),
///   and the specialist backroom roles a club of any standing expects to have staffed.
/// - **Captaincy succession** - when a format captain retires (fully, or from that format) or is
///   out long-term, the best available leader takes over.
///
/// The human's club is handled by the same code, gated by Team.ManagerPreferences: every
/// delegation flag defaults to true, so a human save behaves like an AI one until the player
/// changes something. AlwaysInclude/NeverSelect are honoured even when selection is delegated.
///
/// Determinism: the caller (WorldClockService.ProcessMonthlyTick) threads in the shared monthly
/// Random, consumed at a fixed point in that tick's sequence. Teams are iterated in a stable
/// order (by name), per this project's determinism history.
/// </summary>
public sealed class AiClubManagementService
{
    /// <summary>NEW-B: a small name pool for the bootstrapping selection panel of a brand-new save (former internationals with no Player entity).</summary>
    private static class SeededVeteranNames
    {
        public static readonly string[] First = { "Ravi", "Graham", "Dennis", "Malik", "Angus", "Roland", "Curtly", "Hamish", "Bevan", "Sanjay" };
        public static readonly string[] Last = { "Marsh", "Crowe", "Border", "Younis", "Fraser", "Hooper", "Vaughan", "Ranatunga", "Cronje", "Amla" };
    }

    private readonly PlayerSelectionEvaluator _evaluator = new();
    private readonly CompetitionCalendarService _calendar = new();
    private readonly CoachRecruitmentService _coachRecruitment = new();
    private readonly CoachJobMarketService _coachMarket = new();
    private readonly StaffRecruitmentService _staffRecruitment = new();
    private readonly StaffCareerService _staffCareer = new();
    private readonly SelectionMeetingService _selectionMeeting = new(); // Phase 11: the rationale behind an announced squad

    /// <summary>How far ahead of a competition window opening a squad is named.</summary>
    private const int SquadLeadTimeDays = 40;

    /// <summary>The specialist roles a club of any real standing is expected to staff, in priority order.</summary>
    private static readonly StaffRole[] ExpectedStaffRoles =
    {
        StaffRole.AssistantCoach, StaffRole.BattingCoach, StaffRole.BowlingCoach, StaffRole.Analyst,
        StaffRole.Physiotherapist, StaffRole.ChiefScout, StaffRole.HeadPhysiotherapist,
        StaffRole.FieldingCoach, StaffRole.MentalPerformanceCoach, StaffRole.DataAnalyst
    };

    public IEnumerable<GameEvent> RunMonthly(WorldState world, GameCalendar calendar, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        foreach (var team in world.Teams.Values.OrderBy(t => t.Name))
        {
            // Post-Phase-7/8/9 rectification (Sections H/L): a FRANCHISE has no year-round club
            // structure - no permanent coach, no backroom staff, no monthly squad announcement or
            // individual-coaching programme. Its squad is assembled at the auction, its coach is
            // appointed for the campaign by FranchiseCoachService, and only its captaincy is
            // reviewed here (it can change mid-campaign).
            if (team.IsFranchise)
            {
                if (team.SquadPlayerIds.Count >= 11) // only once the auction has assembled the squad
                    events.AddRange(ReviewCaptaincy(world, date, team, random));
                continue;
            }

            var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            bool human = coach?.IsHumanControlled == true;
            var prefs = team.ManagerPreferences;

            // Phase 11: the unified authority model. The AI announces the squad when it may act -
            // Delegate (silently) or Consult (and surfaces a StaffRecommendation the human sees).
            var squadArea = team.IsNational ? DecisionArea.SquadSelectionNational : DecisionArea.SquadSelectionDomestic;
            bool aiMayAct = !human || prefs.Delegation.AiMayAct(squadArea);
            bool wantsRecommendation = human && prefs.Delegation.Mode(squadArea) == DelegationMode.Consult;

            if (aiMayAct)
                events.AddRange(AnnounceSquads(world, calendar, date, team, coach, random, wantsRecommendation));
            else
                events.AddRange(SurfaceCaptainSelectionViews(world, date, team)); // section D: the captain still has a voice a human coach should hear

            if (!human || prefs.DelegateStaffAndCaptaincy)
            {
                events.AddRange(FillCoachVacancy(world, date, team, random));
                events.AddRange(FillStaffVacancies(world, date, team, random));
                if (team.IsNational) events.AddRange(FillSelectionPanel(world, date, team, random));
                events.AddRange(ReviewCaptaincy(world, date, team, random));
            }

            // Section C: the specialists get to work - individual coaching assignments, and the
            // scouting department's monthly recommendation.
            events.AddRange(RunSpecialistStaff(world, date, team));
        }

        return events;
    }

    // ---------------- squad announcement ----------------

    private IEnumerable<GameEvent> AnnounceSquads(WorldState world, GameCalendar calendar, DateOnly date, Team team, Coach? coach, Random random, bool asRecommendation = false)
    {
        foreach (var competition in world.Competitions.OrderBy(c => c.Name))
        {
            var season = world.CompetitionSeasons
                .FirstOrDefault(s => s.CompetitionId == competition.Id && s.Year == date.Year && s.ParticipatingTeamIds.Contains(team.Id));
            if (season is null) continue;

            var staging = _calendar.GetStaging(competition, date.Year);
            if (staging is null) continue;

            // Name the squad once the window is within lead time and until it closes.
            bool inNamingPeriod = date >= staging.StartDate.AddDays(-SquadLeadTimeDays) && date <= staging.EndDate;
            if (!inNamingPeriod) continue;

            bool alreadyAnnounced = world.SquadAnnouncements.Any(a =>
                a.TeamId == team.Id && a.CompetitionId == competition.Id
                && a.AnnouncedDate >= staging.StartDate.AddDays(-SquadLeadTimeDays - 5));
            if (alreadyAnnounced) continue;

            var squadIds = PickSquad(world, team, competition.Format, date, coach);
            if (squadIds.Count < 11) continue;

            // Meeting-driven-selection ticket (requirement A, Conflict #2): whether a genuine
            // selection MEETING is held is now discretionary, read BEFORE the announcement record
            // is added (so "previous" genuinely means the prior squad, not this one).
            var previousAnnouncement = world.SquadAnnouncements
                .Where(a => a.TeamId == team.Id && a.CompetitionId == competition.Id)
                .OrderByDescending(a => a.AnnouncedDate).FirstOrDefault();
            bool holdMeeting = ShouldHoldMeeting(team, coach, previousAnnouncement?.PlayerIds, squadIds, random, competition.Id);

            var announcement = new SquadAnnouncement
            {
                TeamId = team.Id,
                CompetitionId = competition.Id,
                SeriesOrTournamentName = $"{competition.Name} {date.Year}",
                AnnouncedDate = date,
                IsTournamentSquad = competition.StructureType is CompetitionStructureType.GroupStageKnockout or CompetitionStructureType.Knockout,
                IsHomeSeries = true
            };
            announcement.Announce(squadIds);
            world.SquadAnnouncements.Add(announcement);

            yield return new GameEvent(date, GameEventType.SquadDecisionMade,
                $"{team.Name} name a {squadIds.Count}-man squad for the {competition.Name}.", team.Id, competition.Id);

            if (!holdMeeting)
            {
                // No meeting called - the squad is announced above (a real decision still happened),
                // it just wasn't argued over in a room. Not mandatory before every series, per the
                // ticket's own framing, and cheaper than the full rationale/dissent theatre below.
                yield return new GameEvent(date, GameEventType.SquadAnnounced,
                    $"{team.Name} name the squad for the {competition.Name} without a formal meeting - little has changed since last time.",
                    team.Id, competition.Id);
                continue;
            }

            // Phase 11: the selection meeting - a rationale, a surprise/omission, the captain's
            // comment, and a dissent note when the meeting was poor. Surfaced as its own headline.
            var pool = team.IsNational
                ? world.Players.Where(p => team.SquadPlayerIds.Contains(p.Id)).ToList()
                : world.Players.Where(p => p.CurrentTeamId == team.Id && p.AcademyTeamId is null).ToList();
            var captainId = team.GetCaptain(competition.Format);
            var captain = captainId is { } cpid ? pool.FirstOrDefault(p => p.Id == cpid) : null;
            var captainProfile = captainId is { } cpid2 && world.CaptaincyProfiles.TryGetValue(cpid2, out var prof) ? prof : null;
            var meeting = _selectionMeeting.Hold(team, competition.Format, squadIds, pool, date, coach, captainProfile, captain);

            string story = meeting.Headline;
            if (meeting.BigOmission is not null) story += $" {meeting.BigOmission} misses out.";
            story += $" {meeting.CaptainComment}";
            if (meeting.DissentNote is not null) story += $" {meeting.DissentNote}";
            yield return new GameEvent(date, GameEventType.SquadAnnounced, story, team.Id, competition.Id);

            // Phase 16 review fold-in, requested alongside A: a genuine Outvoted panel decision has
            // real downstream consequences, not just a news line - a repeated pattern erodes the
            // board's trust in its chairman of selectors and raises a real storyline, and the
            // player carried over the room's own preference feels it too.
            foreach (var e in ApplyOutvotedConsequences(world, team, meeting, date))
                yield return e;

            // Phase 11: under "Consult", the human coach is shown the meeting's reasoning as a
            // recommendation he can accept or override (the squad IS announced - Consult applies
            // unless overridden).
            if (asRecommendation)
            {
                string topRationale = meeting.SlotRationales.Count > 0
                    ? $"{meeting.SlotRationales[0].Player} - {meeting.SlotRationales[0].Rationale}"
                    : "no detailed rationale offered";
                yield return new GameEvent(date, GameEventType.StaffRecommendationIssued,
                    $"Selection recommendation for the {competition.Name}: {meeting.Headline} Key call: {topRationale}. (Panel confidence {meeting.MeetingQuality:F0}/100.)",
                    team.Id, competition.Id);
            }
        }
    }

    /// <summary>
    /// Meeting-driven-selection ticket (requirement A, Conflict #2): literal "at the head coach's
    /// own discretion" cannot be free will pre-Phase-17 - there is no interactive decision loop yet.
    /// Modelled instead as (a) a genuine probabilistic AI trigger - a thorough coach
    /// (WorkEthic/MatchPreparation) calls a meeting more often, and ANY coach calls one when the
    /// squad genuinely needs revisiting - and (b) an explicit human override
    /// (ManagerPreferences.HoldSelectionMeetings) that, when false, skips the meeting outright. A
    /// coach never gets to skip the FIRST squad for a given competition - there is nothing to carry
    /// forward yet.
    /// </summary>
    internal static bool ShouldHoldMeeting(Team team, Coach? coach, IReadOnlyList<Guid>? previousSquad, IReadOnlyList<Guid> newSquad, Random random, Guid competitionId = default)
    {
        bool human = coach?.IsHumanControlled == true;
        if (human && !team.ManagerPreferences.HoldSelectionMeetings) return false;
        // Corrections pass (correction 4): a one-shot per-instance "not this one" skip, consumed here.
        if (human && competitionId != default && team.ManagerPreferences.SkipNextSelectionMeetingFor.Remove(competitionId)) return false;
        if (previousSquad is null) return true;

        int turnover = newSquad.Except(previousSquad).Count();
        if (turnover >= 2) return true; // genuine turnover is always worth discussing

        double thoroughness = coach is null
            ? 0.5
            : Math.Clamp((coach.Attributes.WorkEthic + coach.Attributes.MatchPreparation) / 40.0, 0.1, 0.95);
        return random.NextDouble() < (turnover == 1 ? thoroughness : thoroughness * 0.3);
    }

    /// <summary>
    /// Phase 16 review fold-in: a national panel repeatedly outvoted on its own contested picks is
    /// a real story, not a one-off. Tracks NationalBoard.ConsecutiveOutvotes; crossing the
    /// threshold costs the chairman's own standing and raises a SelectorsAtWar storyline. The
    /// player who was carried over the room's preference gets a small, real confidence dent - he
    /// knows the panel wanted someone else.
    /// </summary>
    internal static IEnumerable<GameEvent> ApplyOutvotedConsequences(WorldState world, Team team, SelectionMeetingReport meeting, DateOnly date)
    {
        if (!team.IsNational || team.NationalBoard is not { } board) yield break;

        if (!meeting.Outvoted) { board.ConsecutiveOutvotes = 0; yield break; }

        board.ConsecutiveOutvotes++;

        if (meeting.OutvotedChosenPlayerId is { } chosenId
            && world.Players.FirstOrDefault(p => p.Id == chosenId) is { } chosenPlayer)
            chosenPlayer.Form.AdjustConfidence(-3);

        const int outvoteThreshold = 3;
        if (board.ConsecutiveOutvotes < outvoteThreshold) yield break;

        board.ChairmanOfSelectorsQuality = Math.Clamp(board.ChairmanOfSelectorsQuality - 4, 10, 100);

        var live = world.Storylines.FirstOrDefault(s => !s.Resolved && s.TeamId == team.Id && s.Kind == ValueObjects.StorylineKind.SelectorsAtWar);
        if (live is not null)
        {
            live.Reinforce(date, 12);
        }
        else
        {
            live = new ValueObjects.Storyline
            {
                Kind = ValueObjects.StorylineKind.SelectorsAtWar,
                SubjectId = team.Id,
                TeamId = team.Id,
                Started = date,
                LastUpdated = date,
                Intensity = 45,
                Summary = $"{team.Name}'s selection panel keeps being overruled on its own picks."
            };
            world.Storylines.Add(live);
            yield return new GameEvent(date, GameEventType.NarrativeUpdate,
                $"{team.Name}'s selectors are increasingly at odds with the final call - a panel repeatedly overruled on its own room.",
                team.Id);
        }
    }

    /// <summary>
    /// Follow-up pass (§5.5): the "next in line" pipeline signal. An ageing FirstChoice/SecondChoice
    /// player at a role, with a genuinely promising young DevelopmentProspect/Fringe player at the
    /// SAME PrimaryRole coming up behind him, is a real succession story - surfaced once a year at
    /// the same point the academy's own graduate/release decisions are made, since it reads the
    /// same roster. Deterministic (a real comparison, not a probability roll) and deliberately quiet
    /// - at most one signal per team per year, and only for a genuinely clear case.
    /// </summary>
    public IEnumerable<GameEvent> SurfaceSuccessionSignals(WorldState world, Team team, DateOnly date)
    {
        if (team.IsFranchise) yield break;
        var squad = team.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is { IsRetired: false }).Select(p => p!).ToList();

        var incumbents = squad.Where(p => p.Age(date) >= 31
            && p.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice).ToList();
        if (incumbents.Count == 0) yield break;

        var prospects = squad.Where(p => p.Age(date) <= 22
            && p.SquadStatus is SquadStatus.DevelopmentProspect or SquadStatus.Fringe).ToList();
        if (prospects.Count == 0) yield break;

        foreach (var incumbent in incumbents.OrderByDescending(p => p.Age(date)))
        {
            var heir = prospects
                .Where(p => p.PrimaryRole == incumbent.PrimaryRole)
                .OrderByDescending(p => p.PotentialAbility)
                .FirstOrDefault(p => p.PotentialAbility >= incumbent.CurrentAbility + 15);
            if (heir is null) continue;

            yield return new GameEvent(date, GameEventType.StaffRecommendationIssued,
                $"{team.Name}'s recruitment staff flag {heir.FullName} as the long-term successor to {incumbent.FullName}, who turns {incumbent.Age(date)} this year.",
                team.Id, heir.Id);
            yield break; // one per team per year - deliberately quiet
        }
    }

    private readonly NationalPoolService _pools = new();

    /// <summary>Ranks the team's roster for the format and takes a squad, honouring the human's AlwaysInclude/NeverSelect and preferred size.</summary>
    public IReadOnlyList<Guid> PickSquad(WorldState world, Team team, MatchFormat format, DateOnly date, Coach? coach)
    {
        var prefs = team.ManagerPreferences;
        int target = Math.Clamp(prefs.PreferredSquadSize, 12, Math.Max(12, team.SquadPlayerIds.Count));

        // Section E: a NATIONAL squad is drawn FROM that format's pool, never built independently
        // of it. AlwaysInclude/NeverSelect still apply on top.
        if (team.IsNational && NationalSelectionService.PoolFor(world.NationalPools, team.Id, format) is { } pool && pool.Entries.Count >= 11)
        {
            var fromPool = _pools.DrawSquad(pool, world.Players, format, date, target).ToList();
            foreach (var id in prefs.AlwaysInclude)
                if (pool.Contains(id) && !fromPool.Contains(id)) fromPool.Insert(0, id);
            return fromPool.Where(id => !prefs.NeverSelect.Contains(id)).Take(target).ToList();
        }

        var roster = team.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null && !p!.IsRetired && !p.RetiredFormats.Contains(format))
            .Select(p => p!)
            .Where(p => !prefs.NeverSelect.Contains(p.Id))
            .ToList();

        var scored = _evaluator.RankAvailable(roster, format, date, coach, nationalSelection: team.IsNational)
            .ToDictionary(s => s.PlayerId, s => s.TotalScore);

        // §7.3, the remaining half ("weak panel -> weak squads"): the same panel-quality read
        // SelectionMeetingService narrates AFTER the fact now genuinely shapes the pick itself,
        // closing the gap where the meeting could say "picked on reputation" about a squad that
        // was, in fact, chosen on pure merit. A weak panel systematically over-favours reputable
        // names over current form/ability - deterministic (no fresh RNG; the bias is a fixed
        // function of panel quality and each player's own reputation), so it never touches the
        // shared RNG stream.
        double panelQuality = SelectionMeetingService.PanelQuality(team, coach);
        if (panelQuality < 55)
        {
            double weakness = (55 - panelQuality) / 55.0; // 0 (borderline) .. ~0.73 (very weak)
            foreach (var p in roster)
                if (scored.ContainsKey(p.Id))
                    scored[p.Id] += (p.Reputation.Domestic - 50) * weakness * 0.18;
        }

        // Section D: the captain has genuine input. For an AI coach it is a modest bump on the
        // players he rates; for a human coach the Head Coach has final say and (unless he has
        // delegated selection) the captain's view is surfaced rather than applied. AlwaysInclude/
        // NeverSelect always win.
        var captainId = team.GetCaptain(format);
        var captain = captainId is { } cid ? roster.FirstOrDefault(p => p.Id == cid) : null;
        var captainPushes = captain is null
            ? Array.Empty<(Guid PlayerId, string Reason)>()
            : _captaincy.CaptainSquadInput(captain, roster, format, date).ToArray();
        bool humanHasFinalSay = coach?.IsHumanControlled == true && !prefs.DelegateSquadSelection;
        if (!humanHasFinalSay)
            foreach (var (pid, _) in captainPushes)
                if (scored.ContainsKey(pid)) scored[pid] += 3.5; // enough to bridge a close call, not to force a poor one

        var ranked = scored.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();

        var chosen = new List<Guid>();

        // Honoured players first (if fit and on the roster).
        foreach (var id in prefs.AlwaysInclude)
            if (roster.Any(p => p.Id == id) && !chosen.Contains(id)) chosen.Add(id);

        // A specialist keeper is not optional - make sure at least one is in.
        var keeper = roster.FirstOrDefault(p => p.PrimaryRole == PlayerRole.WicketKeeper && !chosen.Contains(p.Id));
        if (keeper is not null) chosen.Add(keeper.Id);

        foreach (var id in ranked)
        {
            if (chosen.Count >= target) break;
            if (!chosen.Contains(id)) chosen.Add(id);
        }

        return chosen;
    }

    // ---------------- coach vacancy ----------------

    private IEnumerable<GameEvent> FillCoachVacancy(WorldState world, DateOnly date, Team team, Random random)
    {
        // A vacancy the interim mechanism can't cover: no head coach AND no assistant to step up.
        if (team.CurrentCoachId is not null || team.InterimCoachStaffId is not null) yield break;
        bool assistantAvailable = world.Staff.Any(s => s.TeamId == team.Id && s.Role == StaffRole.AssistantCoach);
        if (assistantAvailable) yield break; // InterimCoachService will promote him

        var shortlist = _coachRecruitment.GenerateShortlist(date, random);
        // Take the best candidate who will actually take the job.
        var hire = shortlist.FirstOrDefault(c => _coachMarket.EvaluateOffer(c, team, random)) ?? shortlist[0];

        double salary = CoachSalaryFor(team) * (team.IsNational ? 1.6 : 1.0); // a national job pays more and carries far more scrutiny
        world.Coaches.Add(hire);
        var contract = _coachMarket.Hire(hire, team, date, salary, contractYears: team.IsNational ? 4 : 3, compensationIfTerminated: salary);
        world.CoachingContracts.Add(contract);

        yield return team.IsNational
            ? new GameEvent(date, GameEventType.NationalCoachAppointed,
                $"{team.Name} appoint {hire.FullName} as national head coach - the board expects results at the next global event.", hire.Id, team.Id)
            : new GameEvent(date, GameEventType.CoachAppointed,
                $"{team.Name} appoint {hire.FullName} as head coach.", hire.Id, team.Id);

        // Phase 12 (§4.6): a well-resourced club whose head coach is not a white-ball man appoints a
        // white-ball SPECIALIST ASSISTANT to run the T20 / 50-over side. He is not a StaffMember -
        // he is a Coach with an AssistantToTeamId, and FixturePlayService hands him the reins for
        // his format.
        if (!team.IsNational && team.Reputation.Domestic >= 62 && team.Finances.Budget > 0
            && hire.FormatFocus != FormatSpecialisation.WhiteBall
            && !world.Coaches.Any(c => c.AssistantToTeamId == team.Id))
        {
            var assistantPick = _coachRecruitment.GenerateShortlist(date, random)
                .FirstOrDefault(c => c.Id != hire.Id);
            if (assistantPick is not null)
            {
                // A retained specialist, not on the head-coach track: AssistantToTeamId only, no
                // CurrentTeamId/contract, so the tenure/dismissal/renewal machinery leaves him alone.
                assistantPick.FormatFocus = FormatSpecialisation.WhiteBall;
                assistantPick.AssistantToTeamId = team.Id;
                world.Coaches.Add(assistantPick);
                yield return new GameEvent(date, GameEventType.StaffAppointed,
                    $"{team.Name} bring in {assistantPick.FullName} as a white-ball specialist coach.", assistantPick.Id, team.Id);
            }
        }
    }

    internal IEnumerable<GameEvent> FillStaffVacancies(WorldState world, DateOnly date, Team team, Random random)
    {
        // Only clubs with the means bother filling every backroom seat; a threadbare one runs light.
        int seatsToFill = team.Finances.Budget > 0 ? ExpectedStaffRoles.Length : 2;

        var headCoach = team.CurrentCoachId is { } hcid ? world.Coaches.FirstOrDefault(c => c.Id == hcid) : null;

        foreach (var role in ExpectedStaffRoles.Take(seatsToFill))
        {
            bool filled = world.Staff.Any(s => s.TeamId == team.Id && s.Role == role)
                          || headCoach?.AdditionalRoles.Any(r => r.TeamId == team.Id && r.Role == role) == true;
            if (filled) continue;

            // NEW-D: a specialist chair (batting / bowling / fielding coach) that a threadbare or
            // small side would otherwise leave empty - if the head coach has a strong matching
            // attribute and the appetite, he doubles up rather than the club hiring for it. At most 2.
            if (headCoach is not null
                && role is StaffRole.BattingCoach or StaffRole.BowlingCoach or StaffRole.FieldingCoach
                && headCoach.AdditionalRoles.Count < 2
                && (team.Finances.Budget <= 0 || team.Reputation.Domestic < 45)
                && CoachCanDoubleUp(headCoach, role))
            {
                headCoach.AdditionalRoles.Add(new CoachExtraRole(team.Id, role));
                yield return new GameEvent(date, GameEventType.StaffAppointed,
                    $"{team.Name} appoint no dedicated {Describe(role)} - {headCoach.FullName} takes it on himself alongside the head-coach job.",
                    headCoach.Id, team.Id);
                continue;
            }

            var shortlist = _staffRecruitment.GenerateShortlist(role, date, random);
            var candidate = shortlist[0]; // AI takes the standout for now - richer choice logic is a later slice

            double salary = StaffSalaryFor(team, role);
            world.Staff.Add(candidate);
            var contract = _staffCareer.Hire(candidate, team, date, salary, contractYears: 2, compensationIfTerminated: salary * 0.5);
            world.StaffContracts.Add(contract);

            yield return new GameEvent(date, GameEventType.StaffAppointed,
                $"{team.Name} hire {candidate.FullName} as {Describe(role)}.", candidate.Id, team.Id);
        }
    }

    /// <summary>NEW-D: whether a head coach has the skill AND the appetite to take on a specialist role himself.</summary>
    private static bool CoachCanDoubleUp(Coach coach, StaffRole role)
    {
        int attr = role switch
        {
            StaffRole.BattingCoach => coach.Attributes.BattingCoaching,
            StaffRole.BowlingCoach => coach.Attributes.BowlingCoaching,
            StaffRole.FieldingCoach => coach.Attributes.FieldingCoaching,
            StaffRole.Selector => (coach.Attributes.OppositionAnalysis + coach.Attributes.Scouting) / 2,
            _ => 0
        };
        bool willing = coach.Attributes.WorkEthic >= 12
                       && (coach.Attributes.Ambition >= 12 || coach.PlayingCareerWeight >= 40);
        return attr >= 14 && willing;
    }

    /// <summary>
    /// Meeting-driven-selection ticket (requirement A): a national side's selection panel is staff
    /// under the coach, never a body with independent authority - filled here the exact same way
    /// FillStaffVacancies fills every other backroom seat (a shortlist, the standout, a contract),
    /// so it is deterministic and needs no special machinery. A Chief Selector plus up to three
    /// ordinary selectors; a national side without a coach yet still gets a panel, since it is the
    /// board's appointment (the RunMonthly gate here is only about who ACTS for a human coach).
    /// Deliberately NOT routed through JobMarketService's two-directional application market - that
    /// path ranks a much larger candidate pool and its per-candidate RNG made a national panel's
    /// (world-wide) shortlist the first case to expose a latent Guid-order non-determinism in
    /// GatherApplications; kept simple and self-contained here instead. See CLAUDE.md's
    /// "MEETING-DRIVEN SELECTION TICKET" for the full note on that latent bug.
    /// </summary>
    /// <summary>
    /// NEW-B: a national selection panel is staffed ONLY by ex-cricketers - retired players who
    /// meet the ICC/BCCI-style threshold (7 Tests OR 30 first-class OR 10 ODIs + 20 first-class,
    /// read from PlayerExperience) and who retired at least 5 years ago. The pool is built up over
    /// time by PlayerRetirementCareerService; if it is thin the panel runs short (a real state) -
    /// nobody is fabricated. The chairman of selectors is the eligible panellist with the most
    /// international caps.
    /// </summary>
    internal IEnumerable<GameEvent> FillSelectionPanel(WorldState world, DateOnly date, Team team, Random random)
    {
        const int totalSeats = 4; // a chairman + 3 selectors

        int seated = world.Staff.Count(s => s.TeamId == team.Id && s.Role is StaffRole.Selector or StaffRole.ChiefSelector);
        if (seated >= totalSeats) yield break;

        var caps = world.Players.Where(p => p.IsRetired).ToDictionary(p => p.Id, p => p.Experience);

        bool Eligible(StaffMember s)
        {
            if (s.RetiredAsPlayerOn is not { } r || r > date.AddYears(-5)) return false;
            if (!string.Equals(s.Nationality, team.Country, StringComparison.OrdinalIgnoreCase)) return false;
            if (s.FromPlayerId is { } pid && caps.TryGetValue(pid, out var xp))
                return xp.MatchesIn(MatchFormat.Test) >= 7
                       || xp.TotalMatches >= 30
                       || (xp.MatchesIn(MatchFormat.ODI) >= 10 && xp.TotalMatches >= 20);
            // A seeded veteran with no Player entity in the world - trust the seed's career-weight bar.
            return s.FromPlayerId is null && s.PlayingCareerWeight >= 40;
        }

        int CapsOf(StaffMember s) => s.FromPlayerId is { } pid && caps.TryGetValue(pid, out var xp)
            ? xp.InternationalMatches : (int)s.PlayingCareerWeight;

        var available = world.Staff
            .Where(s => s.TeamId is null && s.Role is StaffRole.Selector or StaffRole.ChiefSelector && Eligible(s))
            .OrderByDescending(CapsOf)
            .ThenByDescending(s => s.PlayingCareerWeight)
            .ThenBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Take(totalSeats - seated)
            .ToList();

        // Bootstrapping: a brand-new save (or a national team with no panel and no retired-player
        // pool yet) gets a starting panel of former internationals - the panel that already existed
        // when the save began. Real PlayerRetirementCareerService selectors take over as they retire
        // and clear the 5-year cooldown. Never fabricates once a real pool exists.
        if (available.Count == 0 && seated == 0)
        {
            for (int i = 0; i < totalSeats; i++)
            {
                int band = 15 - i * 2;
                available.Add(new StaffMember
                {
                    FirstName = SeededVeteranNames.First[(team.Name.Length * 7 + i) % SeededVeteranNames.First.Length],
                    LastName = SeededVeteranNames.Last[(team.Name.Length * 13 + i * 5) % SeededVeteranNames.Last.Length],
                    Role = StaffRole.Selector,
                    DateOfBirth = date.AddYears(-(52 + i)),
                    Nationality = team.Country,
                    Analysis = band, Statistics = band - 1, TechnicalKnowledge = band - 2,
                    Communication = band - 1, Diligence = band,
                    Reputation = 55 - i * 6,
                    PlayingCareerWeight = 60 - i * 8,
                    RetiredAsPlayerOn = date.AddYears(-(7 + i)),
                });
            }
            foreach (var v in available) world.Staff.Add(v);
        }

        bool needChair = !world.Staff.Any(s => s.TeamId == team.Id && s.Role == StaffRole.ChiefSelector);
        for (int i = 0; i < available.Count; i++)
        {
            var member = available[i];
            bool asChair = needChair && i == 0; // the most-capped available takes the chair
            member.Role = asChair ? StaffRole.ChiefSelector : StaffRole.Selector;

            double salary = Math.Round(28_000 + team.Reputation.Domestic * 350, 0);
            var contract = _staffCareer.Hire(member, team, date, salary, contractYears: 3, compensationIfTerminated: salary * 0.5);
            world.StaffContracts.Add(contract);

            yield return new GameEvent(date, GameEventType.StaffAppointed,
                $"{team.Name} appoint the former {member.Nationality} international {member.FullName} to the selection panel"
                + (asChair ? " as chairman of selectors." : "."),
                member.Id, team.Id);
        }
    }

    // ---------------- captaincy succession ----------------

    private readonly CaptaincyAppointmentService _captaincy = new();
    private readonly SpecialistStaffService _specialists = new();

    /// <summary>Section C: run the club's specialists - assign individual coaching, and surface the scouting department's recommendation.</summary>
    private IEnumerable<GameEvent> RunSpecialistStaff(WorldState world, DateOnly date, Team team)
    {
        var teamStaff = world.Staff.Where(s => team.StaffIds.Contains(s.Id)).ToList();
        if (teamStaff.Count == 0) yield break;

        var squad = team.SquadPlayerIds.Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Select(p => p!).ToList();

        _specialists.AssignIndividualWork(team, squad, teamStaff);

        // The scouting department reports once a month, near the start.
        if (date.Day <= 3)
        {
            var format = world.Competitions.FirstOrDefault(c => c.Format != MatchFormat.Test)?.Format ?? MatchFormat.T20;
            if (team.IsNational)
            {
                var rec = _specialists.RecommendPoolAddition(team, world.Players, teamStaff, format);
                if (rec is { } r && NationalSelectionService.PoolFor(world.NationalPools, team.Id, format) is { } pool && !pool.Contains(r.PlayerId))
                {
                    pool.Add(r.PlayerId, date, r.Reasoning, watchlist: true);
                    yield return new GameEvent(date, GameEventType.SquadDecisionMade, r.Reasoning, r.PlayerId, team.Id);
                }
            }
            else
            {
                var others = world.Players.Where(p => p.CurrentTeamId != team.Id
                    && world.Teams.TryGetValue(p.CurrentTeamId ?? Guid.Empty, out var ot) && !ot.IsNational
                    && ot.Country == team.Country);
                var rec = _specialists.RecommendSigning(team, squad, others, teamStaff, format);
                if (rec is { } r)
                    yield return new GameEvent(date, GameEventType.SquadDecisionMade, r.Reasoning, r.PlayerId, team.Id);
            }
        }
    }

    /// <summary>
    /// Section D: for a human coach who keeps selection to himself, the captain's genuine view is
    /// surfaced as an inbox item (not applied). At most a couple a month, and only a real,
    /// reasoned push - not constant noise.
    /// </summary>
    private IEnumerable<GameEvent> SurfaceCaptainSelectionViews(WorldState world, DateOnly date, Team team)
    {
        if (date.Day > 3) yield break; // once a month, near the start
        foreach (var format in new[] { MatchFormat.Test, MatchFormat.T20 })
        {
            var captainId = team.GetCaptain(format);
            var captain = captainId is { } cid ? world.Players.FirstOrDefault(p => p.Id == cid) : null;
            if (captain is null) continue;

            var roster = team.SquadPlayerIds.Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Where(p => p is not null && !p!.IsRetired).Select(p => p!).ToList();

            foreach (var (_, reason) in _captaincy.CaptainSquadInput(captain, roster, format, date).Take(1))
                yield return new GameEvent(date, GameEventType.SquadDecisionMade,
                    $"[{format}] {reason}.", captain.Id, team.Id);
        }
    }

    /// <summary>
    /// Section D: reviews the captaincy through CaptaincyAppointmentService - which reads the
    /// squad's leadership landscape, applies the merit gate and (for a national side) the higher
    /// scrutiny, and re-picks the whole team's captains per its CaptaincyPattern when any format's
    /// incumbent needs replacing. Also lets the coach occasionally revisit the pattern itself.
    /// </summary>
    private IEnumerable<GameEvent> ReviewCaptaincy(WorldState world, DateOnly date, Team team, Random random)
    {
        var squad = team.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Select(p => p!).ToList();
        if (squad.Count < 11) yield break;

        var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;

        bool anyNeedsReview = new[] { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 }
            .Any(f => _captaincy.NeedsReview(team, f, squad, date, (IReadOnlyDictionary<Guid, ValueObjects.CaptaincyProfile>)world.CaptaincyProfiles));
        // Occasionally the coach reconsiders the whole split even without a vacancy.
        bool reconsiderPattern = random.NextDouble() < 0.03;
        if (!anyNeedsReview && !reconsiderPattern) yield break;

        if (reconsiderPattern || anyNeedsReview)
            team.CaptaincyPattern = _captaincy.SuggestPattern(squad, date,
                (IReadOnlyDictionary<Guid, ValueObjects.CaptaincyProfile>)world.CaptaincyProfiles, team.IsNational);

        var appointments = _captaincy.AppointAll(team, squad, date, coach, world.CaptaincyProfiles, random);

        foreach (var choice in appointments.Select(a => a.Choice).DistinctBy(c => c.CaptainId))
            yield return new GameEvent(date, GameEventType.SquadDecisionMade,
                $"{choice.CaptainName} is appointed as {choice.Reasoning}.", choice.CaptainId, team.Id);
    }

    // ---------------- salary + labels ----------------

    private static double CoachSalaryFor(Team team) =>
        Math.Round(80_000 + team.Strength * 4_000 + team.Reputation.Domestic * 2_500, 0);

    private static double StaffSalaryFor(Team team, StaffRole role)
    {
        double roleBase = role switch
        {
            StaffRole.AssistantCoach => 45_000,
            StaffRole.BattingCoach or StaffRole.BowlingCoach => 35_000,
            StaffRole.Analyst => 28_000,
            _ => 25_000
        };
        return Math.Round(roleBase + team.Reputation.Domestic * 400, 0);
    }

    private static string Describe(StaffRole role) => role switch
    {
        StaffRole.AssistantCoach => "assistant coach",
        StaffRole.BattingCoach => "batting coach",
        StaffRole.BowlingCoach => "bowling coach",
        StaffRole.FieldingCoach => "fielding coach",
        StaffRole.StrengthAndConditioning => "strength and conditioning coach",
        StaffRole.Physiotherapist => "physiotherapist",
        StaffRole.Analyst => "analyst",
        StaffRole.Scout => "scout",
        StaffRole.MentalPerformanceCoach => "mental performance coach",
        _ => role.ToString()
    };
}
