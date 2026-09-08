using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Franchise-league coaching. Corrections pass (correction 3): a franchise HEAD COACH is a genuine
/// YEAR-ROUND, multi-year appointment on a real <see cref="CoachingContract"/> - not a campaign
/// fee that switches on at the window and off at its end. Verified (September 2026): Rahul Dravid
/// joined Rajasthan Royals on an explicit multi-year contract, began immediately, and was central
/// to the franchise's retention AND auction planning ahead of a three-year cycle - and later
/// parted ways despite the multi-year deal after a 9th-place season.
///
/// - <b>The coach stays</b> through the off-season, so he is in the pre-auction war room -
///   <see cref="EnsureCoachInPost"/> runs BEFORE the auction and only hires where a chair is
///   genuinely empty.
/// - <b>An end-of-campaign review</b> (Section L: judged at the campaign's end, never mid-season)
///   can end the contract early after a genuinely poor finish. On a sack the vacancy is filled
///   <b>immediately</b> - a new coach is in post through the whole off-season for the next
///   auction's planning, never inheriting a predecessor's plan or having none.
///
/// A coach (and, per the sister-franchise correction, a staff member) can hold a franchise job AND
/// other jobs at the same time:
/// - An <b>unemployed / franchise-only</b> coach can always take one.
/// - A <b>domestic-club</b> coach in ANY country can - the franchise is the SENIOR relationship,
///   so a genuine window clash does not veto the arrangement (the franchise's call wins when the
///   two collide).
/// - A <b>national-team</b> coach can NOT - an international role is year-round - EXCEPT a
///   franchise league hosted in his own country (a national setup's own domestic T20 league).
/// - A coach already holding a DIFFERENT franchise role whose window genuinely overlaps can't
///   take a second clashing one.
///
/// The campaign coach is recorded on <see cref="Coach.FranchiseCoachingTeamId"/> and set as the
/// franchise's <see cref="Team.CurrentCoachId"/> so the match engine (which reads CurrentCoachId)
/// uses him. A campaign ASSISTANT is still a campaign-bound retainer, released at the window close.
/// </summary>
public sealed class FranchiseCoachService
{
    private readonly CoachRecruitmentService _recruitment = new();
    private readonly StaffRecruitmentService _staffRecruitment = new();  // NEW-A: real franchise staff contracts
    private readonly StaffCareerService _staffCareer = new();
    private readonly FranchiseIdentityService _identity = new(); // requirement B: a new coach can redraw the franchise's identity

    private const int ContractYears = 3;

    /// <summary>NEW-A: the specialist chairs a franchise runs year-round alongside its head coach.</summary>
    private static readonly StaffRole[] FranchiseStaffRoles =
        { StaffRole.AssistantCoach, StaffRole.BattingCoach, StaffRole.BowlingCoach, StaffRole.Analyst, StaffRole.Physiotherapist };

    /// <summary>
    /// Make sure every franchise in a league that is about to run has a head coach in post. Keeps
    /// the incumbent (he is year-round now); only hires where a chair is genuinely empty. MUST run
    /// before the auction so the coach is in the pre-auction planning.
    /// </summary>
    public IEnumerable<GameEvent> EnsureCoachInPost(
        WorldState world, Competition franchiseComp, CompetitionSeason season, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var franchiseIds = season.ParticipatingTeamIds.ToHashSet();
        var franchises = world.Teams.Values.Where(t => franchiseIds.Contains(t.Id)).OrderBy(t => t.Name).ToList();

        foreach (var franchise in franchises)
        {
            if (franchise.CurrentCoachId is { } cid && world.Coaches.Any(c => c.Id == cid && !c.IsRetired))
                continue; // the incumbent is still there

            var pick = FindAvailableCoach(world, franchiseComp, date, random)
                       ?? NewFreelanceCoach(world, date, random);
            events.AddRange(Appoint(world, franchiseComp, franchise, pick, date, isReplacement: false));

            // Phase 12 (§4.4): the campaign assistant layer - a big campaign brings in a specialist
            // assistant on his own smaller retainer, released with the rest of the campaign staff.
            if (franchiseComp.Prestige >= 70)
            {
                // Name-ordered, never raw list order - world.Coaches accumulates from paths that
                // may add in a Guid-keyed enumeration order, and this picks a coach who then
                // consumes RNG (or forces a NewFreelanceCoach draw when none is free).
                var asst = world.Coaches.Where(c => !c.IsRetired && c.FranchiseCoachingTeamId is null
                                                    && c.AssistantToTeamId is null && c.Id != pick.Id
                                                    && c.CurrentTeamId is null)
                                        .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).FirstOrDefault()
                           ?? NewFreelanceCoach(world, date, random);
                if (!world.Coaches.Contains(asst)) world.Coaches.Add(asst);
                asst.AssistantToTeamId = franchise.Id;
                asst.FormatFocus = FormatSpecialisation.WhiteBall;
                double asstFee = franchiseComp.Prestige * 3_000;
                asst.CareerEarnings.CreditFranchise(asstFee);
                franchise.Finances.Budget -= asstFee;
                events.Add(new GameEvent(date, GameEventType.StaffAppointed,
                    $"{franchise.Name} add {asst.FullName} to the coaching staff for the campaign.", asst.Id, franchise.Id));
            }
        }

        return events;
    }

    private IEnumerable<GameEvent> Appoint(WorldState world, Competition franchiseComp, Team franchise, Coach pick, DateOnly date, bool isReplacement)
    {
        var events = new List<GameEvent>();

        // A franchise head-coach salary - real, annual, on a multi-year contract, but smaller than
        // a domestic head-coach's (a T20 campaign is a few weeks of matches; the rest is planning).
        double annualSalary = Math.Round(franchiseComp.Prestige * 6_500 * (0.7 + pick.CircuitReputation / 100.0 * 0.9), 0);
        var contract = new CoachingContract
        {
            CoachId = pick.Id,
            TeamId = franchise.Id,
            StartDate = date,
            EndDate = date.AddYears(ContractYears),
            AnnualSalary = annualSalary,
            CompensationIfTerminated = annualSalary,
            Status = ContractStatus.Active,
        };
        world.CoachingContracts.Add(contract);

        pick.FranchiseCoachingTeamId = franchise.Id;
        pick.CareerTeamIds.Add(franchise.Id); // requirement C: first-hand knowledge substrate
        franchise.CurrentCoachId = pick.Id;
        pick.CareerEarnings.CreditFranchise(annualSalary);
        franchise.Finances.Budget -= annualSalary;

        events.Add(new GameEvent(date, GameEventType.CoachAppointed,
            $"{franchise.Name} appoint {pick.FullName} as head coach on a {ContractYears}-year contract"
            + (isReplacement ? " - in post through the off-season for the next auction's planning." : "."),
            pick.Id, franchise.Id));

        // Requirement B, driver 2: a new coach whose philosophy points elsewhere can, with the
        // captain's buy-in, redraw the franchise's whole recruitment identity for the next auction.
        var identityShift = _identity.DriftFromNewCoach(world, franchise, pick, date);
        if (identityShift is not null) events.Add(identityShift);

        return events;
    }

    /// <summary>
    /// The end-of-campaign review. The coach is NOT released (he is year-round). A title lifts his
    /// circuit standing; a genuinely poor finish can end the multi-year deal early, and the
    /// replacement is hired at once so the franchise is never coachless into the off-season.
    /// Also releases the campaign assistant layer.
    /// </summary>
    public IEnumerable<GameEvent> ReviewAfterCampaign(WorldState world, Competition franchiseComp, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var franchiseIds = world.Teams.Values.Where(t => t.IsFranchise).Select(t => t.Id).ToHashSet();

        var latestSeason = world.CompetitionSeasons
            .Where(s => s.CompetitionId == franchiseComp.Id && s.IsCompleted)
            .OrderByDescending(s => s.Year).FirstOrDefault();

        foreach (var coach in world.Coaches
                     .Where(c => c.FranchiseCoachingTeamId is { } fid && franchiseIds.Contains(fid) && !c.IsRetired)
                     .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToList())
        {
            var franchiseId = coach.FranchiseCoachingTeamId!.Value;
            if (!world.Teams.TryGetValue(franchiseId, out var franchise)) continue;

            bool champions = latestSeason?.ChampionTeamId == franchiseId;
            coach.CircuitReputation = Math.Clamp(coach.CircuitReputation + (champions ? 12 : 1.5), 0, 100);

            // Keep the incumbent's multi-year contract current (he is year-round; the generic
            // renewal/expiry machinery deliberately skips a franchise contract).
            var incumbentDeal = world.CoachingContracts.FirstOrDefault(k =>
                k.CoachId == coach.Id && k.TeamId == franchiseId && k.Status == ContractStatus.Active);
            if (incumbentDeal is not null && incumbentDeal.EndDate <= date.AddYears(1))
                incumbentDeal.EndDate = date.AddYears(ContractYears);
            if (champions)
                events.Add(new GameEvent(date, GameEventType.CoachAppointed,
                    $"{coach.FullName}'s stock on the franchise circuit rises after guiding {franchise.Name} to the {franchiseComp.Name} title.",
                    coach.Id, franchiseId));

            // End-of-campaign judgement: a bottom-of-the-table finish can end the deal early
            // (RR / Dravid after 9th), the replacement in post immediately.
            var standings = latestSeason?.Standings
                .OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate).ToList();
            int pos = standings?.FindIndex(s => s.TeamId == franchiseId) + 1 ?? 0;
            int fieldSize = standings?.Count ?? 8;
            if (!champions && pos > 0 && pos >= fieldSize - 1)
            {
                double sackChance = pos == fieldSize ? 0.5 : 0.35;
                if (random.NextDouble() < sackChance)
                {
                    var deal = world.CoachingContracts.FirstOrDefault(k =>
                        k.CoachId == coach.Id && k.TeamId == franchiseId && k.Status == ContractStatus.Active);
                    if (deal is not null)
                    {
                        deal.Status = ContractStatus.Terminated;
                        deal.EndDate = date;
                        franchise.Finances.Budget -= deal.CompensationIfTerminated;
                    }
                    coach.FranchiseCoachingTeamId = null;
                    franchise.CurrentCoachId = null;
                    events.Add(new GameEvent(date, GameEventType.CoachDismissed,
                        $"{franchise.Name} part ways with {coach.FullName} after a {Ordinal(pos)}-place {franchiseComp.Name} finish, despite time left on his contract.",
                        coach.Id, franchiseId));

                    var replacement = FindAvailableCoach(world, franchiseComp, date, random)
                                      ?? NewFreelanceCoach(world, date, random);
                    events.AddRange(Appoint(world, franchiseComp, franchise, replacement, date, isReplacement: true));
                }
            }
        }

        // Release the campaign assistant layer (still campaign-bound).
        foreach (var asst in world.Coaches.Where(c => c.AssistantToTeamId is { } fid && franchiseIds.Contains(fid)).ToList())
            asst.AssistantToTeamId = null;

        return events;
    }

    private Coach? FindAvailableCoach(WorldState world, Competition franchiseComp, DateOnly date, Random random)
    {
        var candidates = world.Coaches
            .Where(c => !c.IsRetired && c.FranchiseCoachingTeamId is null)
            .Where(c => IsAvailableFor(world, c, franchiseComp))
            .OrderByDescending(c => c.CurrentTeamId is null ? 1 : 0)
            // Phase 12 (§4.3): a big FRANCHISE-CIRCUIT name gets first refusal - a different
            // currency from a domestic reputation.
            .ThenByDescending(c => c.CircuitReputation * 0.6 + c.Attributes.TacticalKnowledge + c.Attributes.MatchReading + c.Reputation.Domestic * 0.1)
            .ThenBy(c => c.LastName).ThenBy(c => c.FirstName)
            .ToList();
        return candidates.FirstOrDefault();
    }

    /// <summary>Corrections pass (correction 3): whether <paramref name="coach"/> can take a job at a franchise in <paramref name="franchiseComp"/>.</summary>
    public bool IsAvailableFor(WorldState world, Coach coach, Competition franchiseComp)
    {
        // Already holds a DIFFERENT franchise role whose window genuinely clashes - a real clash.
        if (coach.FranchiseCoachingTeamId is { } existingFid)
        {
            var otherLeague = world.Competitions.FirstOrDefault(c => c.IsFranchiseAuctionLeague
                && c.Id != franchiseComp.Id
                && world.CompetitionSeasons.Any(s => s.CompetitionId == c.Id && s.ParticipatingTeamIds.Contains(existingFid)));
            if (otherLeague is not null && otherLeague.Window.OverlapsMonths(franchiseComp.Window))
                return false;
        }

        if (coach.CurrentTeamId is not { } tid || !world.Teams.TryGetValue(tid, out var team))
            return true; // unemployed / franchise-only

        if (team.IsNational)
            // An international role is exclusive EXCEPT a franchise league hosted in his own country.
            return string.Equals(team.Country, franchiseComp.Country, StringComparison.OrdinalIgnoreCase);

        // A domestic club coach, ANY country: the franchise is the SENIOR relationship - he takes
        // it, and the franchise's call wins whenever the two genuinely collide. Not a hard veto.
        return true;
    }

    // ---------------- NEW-A: real franchise STAFF contracts ----------------

    /// <summary>
    /// NEW-A: fill a franchise's specialist chairs (assistant / batting / bowling coach, analyst,
    /// physio) on real, year-round, multi-year <see cref="StaffContract"/>s - the exact mirror of
    /// <see cref="EnsureCoachInPost"/>. Keeps incumbents; only hires an empty chair. A staff member
    /// may hold MORE THAN ONE franchise contract across non-clashing leagues (the primary in
    /// <see cref="StaffMember.FranchiseStaffTeamId"/>, the rest in
    /// <see cref="StaffMember.AdditionalFranchiseTeamIds"/>). MUST run before the auction so the
    /// support staff are in place for the pre-auction planning.
    /// </summary>
    public IEnumerable<GameEvent> EnsureStaffInPost(
        WorldState world, Competition franchiseComp, CompetitionSeason season, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var franchiseIds = season.ParticipatingTeamIds.ToHashSet();
        var franchises = world.Teams.Values.Where(t => franchiseIds.Contains(t.Id)).OrderBy(t => t.Name).ToList();

        foreach (var franchise in franchises)
        {
            // Only a genuinely resourced franchise runs the full support structure.
            int chairs = franchise.Finances.Budget > 0 ? FranchiseStaffRoles.Length : 2;

            foreach (var role in FranchiseStaffRoles.Take(chairs))
            {
                bool filled = world.Staff.Any(s => role == s.Role
                    && (s.FranchiseStaffTeamId == franchise.Id || s.AdditionalFranchiseTeamIds.Contains(franchise.Id)));
                if (filled) continue;

                var pick = world.Staff
                    .Where(s => s.Role == role && IsStaffAvailableFor(world, s, franchiseComp))
                    .OrderByDescending(s => s.Effectiveness)
                    .ThenBy(s => s.LastName).ThenBy(s => s.FirstName)
                    .FirstOrDefault()
                    ?? NewFreelanceStaff(world, role, date, random);

                if (pick.FranchiseStaffTeamId is null) pick.FranchiseStaffTeamId = franchise.Id;
                else pick.AdditionalFranchiseTeamIds.Add(franchise.Id);

                double salary = Math.Round(franchiseComp.Prestige * 1_800 + pick.Reputation * 400, 0);
                var contract = _staffCareer.Hire(pick, franchise, date, salary, contractYears: ContractYears, compensationIfTerminated: salary);
                world.StaffContracts.Add(contract);
                franchise.Finances.Budget -= salary;

                events.Add(new GameEvent(date, GameEventType.StaffAppointed,
                    $"{franchise.Name} bring {pick.FullName} in as {role} on a {ContractYears}-year deal.", pick.Id, franchise.Id));
            }
        }

        return events;
    }

    /// <summary>NEW-A: the same availability rules as <see cref="IsAvailableFor"/> (the head coach), for a staff member.</summary>
    public bool IsStaffAvailableFor(WorldState world, StaffMember staff, Competition franchiseComp)
    {
        // A clashing OTHER franchise role blocks it (windows overlap).
        foreach (var otherFid in staff.AdditionalFranchiseTeamIds.Append(staff.FranchiseStaffTeamId ?? Guid.Empty))
        {
            if (otherFid == Guid.Empty) continue;
            var otherLeague = world.Competitions.FirstOrDefault(c => c.IsFranchiseAuctionLeague
                && c.Id != franchiseComp.Id
                && world.CompetitionSeasons.Any(s => s.CompetitionId == c.Id && s.ParticipatingTeamIds.Contains(otherFid)));
            if (otherLeague is not null && otherLeague.Window.OverlapsMonths(franchiseComp.Window))
                return false;
        }

        if (staff.TeamId is not { } tid || !world.Teams.TryGetValue(tid, out var team))
            return true; // unattached / franchise-only

        if (team.IsNational)
            return string.Equals(team.Country, franchiseComp.Country, StringComparison.OrdinalIgnoreCase);

        return true; // a domestic-club staff member: the franchise is the senior relationship.
    }

    private StaffMember NewFreelanceStaff(WorldState world, StaffRole role, DateOnly date, Random random)
    {
        var staff = _staffRecruitment.GenerateShortlist(role, date, random, count: 1)[0];
        world.Staff.Add(staff);
        return staff;
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd",
        21 => "21st", 22 => "22nd", 23 => "23rd",
        _ => $"{n}th"
    };

    private Coach NewFreelanceCoach(WorldState world, DateOnly date, Random random)
    {
        var coach = _recruitment.GenerateShortlist(date, random, count: 1)[0];
        world.Coaches.Add(coach);
        return coach;
    }
}
