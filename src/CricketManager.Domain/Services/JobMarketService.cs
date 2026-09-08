using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-6 (sections A + B): the two-directional job market.
///
/// - **Teams advertise vacancies** with listed requirements (licence tier, reputation, role-fit).
/// - **Any coach or staff member - employed or not - can apply**, weighing genuine career benefit
///   (JobMarketApplicationService), so a specialist can step up to a head-coach job at a smaller
///   club.
/// - **The hiring authority decides.** The head-coach job is the board's call. Every other role
///   belongs to the head coach (section B) - or, if he has delegated it, to a GeneralManager
///   staff member (Team.StaffingDelegatedToStaffId) or back to the board. Whoever decides ranks
///   the applicants on role fit AND how well their philosophy sits with the club's identity.
///
/// Runs monthly, after AiClubManagementService's own pass. It does NOT replace the interim-coach
/// mechanism or AiClubManagementService's fallback shortlist hire - it runs first and fills what
/// it can; anything still vacant falls through to those.
/// </summary>
public sealed class JobMarketService
{
    private readonly JobMarketApplicationService _applications = new();
    private readonly RoleFitService _fit = new();
    private readonly CoachJobMarketService _coachMarket = new();
    private readonly StaffCareerService _staffCareer = new();

    private const int AdvertOpenDays = 45;

    /// <summary>The specialist roles a club of real standing advertises for when unfilled.</summary>
    private static readonly StaffRole[] AdvertisedRoles =
    {
        StaffRole.AssistantCoach, StaffRole.BattingCoach, StaffRole.BowlingCoach, StaffRole.FieldingCoach,
        StaffRole.Analyst, StaffRole.ChiefScout, StaffRole.HeadPhysiotherapist, StaffRole.MentalPerformanceCoach
    };

    public IEnumerable<GameEvent> RunMonthly(WorldState world, GameCalendar calendar, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        // 1. Post new adverts for genuine vacancies. A franchise never advertises a permanent job -
        // it appoints a campaign coach at the auction (Section H).
        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise).OrderBy(t => t.Name))
        {
            var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;

            // Head-coach vacancy: no coach and no interim to promote.
            if (team.CurrentCoachId is null && team.InterimCoachStaffId is null
                && !world.Staff.Any(s => s.TeamId == team.Id && s.Role == StaffRole.AssistantCoach)
                && !HasOpenAdvert(world, team.Id, null))
            {
                world.Vacancies.Add(NewAdvert(team, null, date));
                events.Add(new GameEvent(date, GameEventType.CoachJobOffer, $"{team.Name} advertise for a new head coach.", team.Id));
            }

            // Specialist vacancies - only for clubs that can afford a full backroom.
            if (coach is not null && team.Finances.Budget > 0)
                foreach (var role in AdvertisedRoles)
                    if (!world.Staff.Any(s => s.TeamId == team.Id && s.Role == role) && !HasOpenAdvert(world, team.Id, role))
                        world.Vacancies.Add(NewAdvert(team, role, date));
        }

        // 2. Resolve open adverts whose window has run (or that already have a strong field).
        foreach (var advert in world.Vacancies.Where(a => !a.Filled && a.Closes <= date).OrderBy(a => a.Posted).ToList())
        {
            if (!world.Teams.TryGetValue(advert.TeamId, out var team)) { advert.Fill(Guid.Empty); continue; }

            var applications = GatherApplications(world, advert, team, date, random);
            var ranked = _applications.Rank(applications, cultureWeight: CultureWeightFor(team));
            if (ranked.Count == 0) { world.Vacancies.Remove(advert); continue; } // nobody suitable - re-advertise next month

            var winner = ranked[0];
            events.AddRange(Appoint(world, advert, team, winner, date, random));
        }

        // Housekeeping: forget adverts filled a while ago.
        foreach (var stale in world.Vacancies.Where(a => a.Filled && a.Closes < date.AddDays(-120)).ToList())
            world.Vacancies.Remove(stale);

        return events;
    }

    private static bool HasOpenAdvert(WorldState world, Guid teamId, StaffRole? role) =>
        world.Vacancies.Any(a => !a.Filled && a.TeamId == teamId && a.Role == role);

    private static VacancyAdvert NewAdvert(Team team, StaffRole? role, DateOnly date)
    {
        double standing = (team.Strength + team.Reputation.Domestic * 2 + team.Reputation.Continental) / 4;
        return new VacancyAdvert
        {
            TeamId = team.Id,
            Role = role,
            Posted = date,
            Closes = date.AddDays(AdvertOpenDays),
            MinReputation = role is null ? Math.Clamp(standing - 20, 0, 70) : Math.Clamp(standing - 35, 0, 45),
            MinRoleFit = role is null ? Math.Clamp(standing * 0.55, 30, 62) : Math.Clamp(standing * 0.4, 25, 55),
            MinLicense = role is null
                ? (standing >= 72 ? CoachingLicense.Advanced : standing >= 45 ? CoachingLicense.Level2 : CoachingLicense.Level1)
                : CoachingLicense.None
        };
    }

    private double CultureWeightFor(Team team) =>
        // A club with a strong, distinct identity weighs culture fit more heavily.
        team.CulturalIdentity is CoachingPhilosophy.PerformanceFocused ? 0.22 : 0.36;

    private IReadOnlyList<JobApplication> GatherApplications(WorldState world, VacancyAdvert advert, Team team, DateOnly date, Random random)
    {
        var apps = new List<JobApplication>();

        Team? TeamOf(Guid? id) => id is { } tid && world.Teams.TryGetValue(tid, out var t) ? t : null;

        // Determinism: candidates are iterated in a SEED-STABLE order (surname, then forename, ties
        // preserved by the world list's own insertion order). Ordering by .Id here - a fresh
        // Guid.NewGuid() that is never derived from the world seed - made every conditional
        // random.NextDouble() in Consider() land on a different candidate between two runs of the
        // same seed, cascading into different appointments and a different world. (Pre-existing,
        // latent since Post-Phase-6; surfaced by the meeting-driven-selection ticket's slice C.)
        foreach (var coach in world.Coaches.Where(c => !c.IsRetired && !c.IsHumanControlled)
                     .OrderBy(c => c.LastName, StringComparer.Ordinal).ThenBy(c => c.FirstName, StringComparer.Ordinal))
        {
            var app = _applications.Consider(coach, advert, team, TeamOf(coach.CurrentTeamId),
                candidateCurrentRole: coach.CurrentTeamId is null ? null : (StaffRole?)null, date, random);
            if (app is not null) apps.Add(app);
        }

        foreach (var staff in world.Staff.Where(s => s.TeamId != team.Id)
                     .OrderBy(s => s.LastName, StringComparer.Ordinal).ThenBy(s => s.FirstName, StringComparer.Ordinal))
        {
            var app = _applications.Consider(staff, advert, team, TeamOf(staff.TeamId), date, random);
            if (app is not null) apps.Add(app);
        }

        return apps;
    }

    private IEnumerable<GameEvent> Appoint(WorldState world, VacancyAdvert advert, Team team, JobApplication winner, DateOnly date, Random random)
    {
        double standing = (team.Strength + team.Reputation.Domestic * 2 + team.Reputation.Continental) / 4;

        if (advert.Role is null)
        {
            // Head coach - the board's call. Phase 10: a national job is a longer contract, pays
            // more, and its own headline (the national-coach career track).
            bool national = team.IsNational;
            double salary = Math.Round((80_000 + standing * 5_000) * (national ? 1.6 : 1.0), 0);
            int years = national ? 4 : 3;
            var coach = world.Coaches.FirstOrDefault(c => c.Id == winner.CandidateId);
            if (winner.CandidateIsCoach && coach is not null)
            {
                DetachFromCurrent(world, coach);
                var contract = _coachMarket.Hire(coach, team, date, salary, contractYears: years, compensationIfTerminated: salary);
                world.CoachingContracts.Add(contract);
                advert.Fill(coach.Id);
                yield return new GameEvent(date, national ? GameEventType.NationalCoachAppointed : GameEventType.CoachAppointed,
                    national
                        ? $"{team.Name} appoint {coach.FullName} as national head coach - the board expects results at the next global event."
                        : $"{team.Name} appoint {coach.FullName} as head coach - {winner.Note.ToLowerInvariant().Replace(coach.FullName.ToLowerInvariant() + " applies for the head coach job", "chosen from the field")}.",
                    coach.Id, team.Id);
            }
            else
            {
                // A staff member steps up to the top job.
                var staff = world.Staff.FirstOrDefault(s => s.Id == winner.CandidateId);
                if (staff is null) { world.Vacancies.Remove(advert); yield break; }
                var promoted = PromoteStaffToCoach(staff, date);
                if (staff.TeamId is { } fromId && world.Teams.TryGetValue(fromId, out var fromTeam))
                    { staff.TeamId = null; fromTeam.StaffIds.Remove(staff.Id); }
                world.Staff.Remove(staff);
                world.Coaches.Add(promoted);
                var contract = _coachMarket.Hire(promoted, team, date, salary, contractYears: years);
                world.CoachingContracts.Add(contract);
                advert.Fill(promoted.Id);
                yield return new GameEvent(date, GameEventType.StaffPromotedToHeadCoach,
                    $"{promoted.FullName} steps up from a backroom role to become {team.Name}'s head coach.", promoted.Id, team.Id);
            }
            yield break;
        }

        // Specialist role - the head coach's call (section B), unless delegated to a GM or the board.
        var role = advert.Role.Value;
        var winnerStaff = world.Staff.FirstOrDefault(s => s.Id == winner.CandidateId);
        var authority = StaffingAuthority(world, team);

        if (!winner.CandidateIsCoach && winnerStaff is not null)
        {
            Team? poachedFrom = null;
            double poachFee = 0;
            if (winnerStaff.TeamId is { } fromId2 && world.Teams.TryGetValue(fromId2, out var fromTeam2))
            {
                var oldContract = world.StaffContracts.FirstOrDefault(c => c.StaffId == winnerStaff.Id && c.Status == ContractStatus.Active);
                if (oldContract is not null) _staffCareer.OfferRetention(winnerStaff, oldContract, fromTeam2,
                    fromTeam2.CurrentCoachId is { } fc ? world.Coaches.FirstOrDefault(c => c.Id == fc) : null, random);
                if (winnerStaff.TeamId is not null) { world.Vacancies.Remove(advert); yield break; } // his club fought him off

                // §4.9: poaching a still-contracted specialist costs a compensation fee, paid to
                // the club losing him. Larger for a more experienced, more effective name.
                poachedFrom = fromTeam2;
                double comp = oldContract?.CompensationIfTerminated ?? 0;
                poachFee = Math.Round(Math.Max(comp, 20_000 + winnerStaff.YearsExperience * 6_000 + standing * 300), 0);
            }

            winnerStaff.Role = role;
            winnerStaff.BasedInCountry = team.Country;
            var salary = Math.Round(30_000 + standing * 500, 0);
            if (poachedFrom is not null && poachFee > 0)
            {
                team.Finances.Budget -= poachFee;
                poachedFrom.Finances.Budget += poachFee;
                yield return new GameEvent(date, GameEventType.StaffAppointed,
                    $"{team.Name} pay {poachedFrom.Name} {poachFee:N0} in compensation to prise {winnerStaff.FullName} away mid-contract.",
                    winnerStaff.Id, team.Id);
            }
            var newContract = _staffCareer.Hire(winnerStaff, team, date, salary, contractYears: 2, compensationIfTerminated: salary * 0.5);
            world.StaffContracts.Add(newContract);
            advert.Fill(winnerStaff.Id);
            yield return new GameEvent(date, GameEventType.StaffAppointed,
                $"{team.Name}'s {authority} appoint {winnerStaff.FullName} as {Describe(role)}.", winnerStaff.Id, team.Id);
        }
        else
        {
            world.Vacancies.Remove(advert); // a coach won a specialist advert - uncommon; let the fallback handle it
        }
    }

    private static string StaffingAuthority(WorldState world, Team team)
    {
        if (team.StaffingDelegatedToStaffId is { } gmId && world.Staff.Any(s => s.Id == gmId)) return "general manager";
        if (team.DelegatedResponsibilities.Contains(DelegatedResponsibility.StaffHiring)) return "board";
        return "head coach";
    }

    private static void DetachFromCurrent(WorldState world, Coach coach)
    {
        if (coach.CurrentTeamId is not { } tid) return;
        var contract = world.CoachingContracts.FirstOrDefault(c => c.Id == coach.CurrentContractId && c.Status == ContractStatus.Active);
        if (contract is not null) contract.Status = ContractStatus.Resigned;
        if (world.Teams.TryGetValue(tid, out var t) && t.CurrentCoachId == coach.Id) t.CurrentCoachId = null;
        coach.CurrentTeamId = null;
        coach.CurrentContractId = null;
    }

    private static Coach PromoteStaffToCoach(StaffMember staff, DateOnly date)
    {
        var coach = new Coach
        {
            FirstName = staff.FirstName,
            LastName = staff.LastName,
            DateOfBirth = staff.DateOfBirth,
            PlayingExperience = PlayingExperience.FirstClass,
            License = CoachingLicense.Level3,
            Philosophy = staff.Philosophy,
            Reputation = new ValueObjects.Reputation(domestic: Math.Round(staff.Reputation, 1)),
        };
        coach.SeedAttributesFromHistory();
        int lift = (int)Math.Clamp(staff.EffectiveWithExperience / 12, 0, 6);
        coach.Attributes.TacticalKnowledge += lift;
        coach.Attributes.ManManagement += (int)Math.Clamp(staff.Communication - 10, -3, 6);
        coach.Attributes.Adaptability = Math.Clamp(coach.Attributes.Adaptability + staff.Adaptability - 10, 1, 20);
        coach.Attributes.Ambition = Math.Clamp(staff.Ambition, 1, 20);
        return coach;
    }

    private static string Describe(StaffRole role) => role switch
    {
        StaffRole.AssistantCoach => "assistant coach",
        StaffRole.BattingCoach => "batting coach",
        StaffRole.BowlingCoach => "bowling coach",
        StaffRole.FieldingCoach => "fielding coach",
        StaffRole.ChiefScout => "chief scout",
        StaffRole.HeadPhysiotherapist => "head physio",
        StaffRole.MentalPerformanceCoach => "mental performance coach",
        StaffRole.Analyst => "analyst",
        StaffRole.DataAnalyst => "data analyst",
        StaffRole.SportsScientist => "sports scientist",
        StaffRole.GeneralManager => "general manager",
        StaffRole.Mentor => "mentor",
        _ => role.ToString()
    };
}
