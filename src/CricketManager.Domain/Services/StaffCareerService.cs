using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 5 Part 2: the hire/dismiss/tenure lifecycle for a backroom StaffMember - the same real
/// gap CoachCareerService closed for Coach (BoardTrust/Authority/CareerSatisfaction all
/// initialised, never written by anything), applied to staff, plus the piece Coach's own
/// lifecycle still does not have: an actual hiring flow. Nothing before this could put a
/// StaffMember INTO a team at all - Team.StaffIds/WorldState.Staff (Phase 5 Part 1) could only
/// ever be read from, never written to by a real hire.
/// </summary>
public sealed class StaffCareerService
{
    /// <summary>
    /// Signs a candidate to a team - creates the contract, attaches the staff member, and
    /// registers him on the team's own StaffIds list. The one call that does all three, the same
    /// "one call, no way to half-do it" discipline WorldState.ApplyInjury already established -
    /// a staff member attached to a team with no contract, or a contract with nobody attached to
    /// the team, would both be broken states nothing else in this codebase produces deliberately.
    /// </summary>
    public StaffContract Hire(StaffMember staff, Team team, DateOnly date, double annualSalary, int contractYears = 2, double compensationIfTerminated = 0)
    {
        var contract = new StaffContract
        {
            StaffId = staff.Id,
            TeamId = team.Id,
            StartDate = date,
            EndDate = date.AddYears(Math.Max(1, contractYears)),
            AnnualSalary = annualSalary,
            CompensationIfTerminated = compensationIfTerminated,
            Status = ContractStatus.Active
        };

        staff.TeamId = team.Id;
        if (!team.StaffIds.Contains(staff.Id))
            team.StaffIds.Add(staff.Id);

        return contract;
    }

    /// <summary>
    /// Ends a staff member's employment before his contract's own end date - the club's choice,
    /// mirroring CoachCareerService.EvaluateSeason's dismissal side effects exactly: the contract
    /// is terminated, the compensation clause (if any) is actually paid, and he is fully detached
    /// from the team.
    /// </summary>
    public void Dismiss(StaffMember staff, StaffContract contract, Team team, DateOnly date)
    {
        contract.Status = ContractStatus.Terminated;
        if (contract.CompensationIfTerminated > 0)
            team.Finances.Budget -= contract.CompensationIfTerminated;

        Detach(staff, team);
    }

    private static void Detach(StaffMember staff, Team team)
    {
        staff.TeamId = null;
        team.StaffIds.Remove(staff.Id);
    }

    /// <summary>Called by WorldClockService.ProcessContractExpiry once a contract's own end date has passed - properly detaches, unlike the pre-existing coach path, since there is no reason a new mechanism should repeat a gap found in an older one.</summary>
    public void HandleExpiry(StaffMember? staff, StaffContract contract, Team? team)
    {
        contract.Status = ContractStatus.Expired;
        if (staff is not null && team is not null) Detach(staff, team);
    }

    /// <summary>
    /// Job-market follow-up: CareerSatisfaction (added alongside this method) - whether HE still
    /// wants the job, distinct from whether the club rates his work. Staff have no BoardTrust
    /// equivalent (GrowFromTenure's own doc comment already explains why: a specialist's work is
    /// not fairly judged by team results), so this reads the two signals that genuinely ARE his
    /// own to feel: long, unchanging tenure (the same quiet staleness drain
    /// CoachCareerService.EvaluateSatisfaction already models) and whether the club backs his
    /// department with real facility investment - a poorly-resourced job is a real source of
    /// frustration for a specialist regardless of how the team itself is doing on the field.
    /// </summary>
    public double EvaluateSatisfaction(StaffMember staff, Team team, int yearsAtClub)
    {
        double delta = 0;

        if (yearsAtClub > 8 && staff.YearsExperience > 8) delta -= 2.0; // long, unremarkable tenure - the same stagnation CoachCareerService already models

        double facilityQuality = team.Facilities.TrainingQuality;
        delta += (facilityQuality - 50) / 100.0 * 1.5; // well-resourced lifts him a little; under-resourced grinds

        staff.CareerSatisfaction = Math.Clamp(staff.CareerSatisfaction + delta, 0, 100);
        return staff.CareerSatisfaction;
    }

    /// <summary>Same "probability, not a cliff-edge" discipline as CoachCareerService.TryResign - a genuinely unhappy staff member can walk away on his own terms.</summary>
    public bool TryResign(StaffMember staff, StaffContract contract, Team team, Random random, out string reason)
    {
        reason = string.Empty;
        const double floor = 20;
        if (staff.CareerSatisfaction >= floor) return false;

        double shortfall = (floor - staff.CareerSatisfaction) / floor;
        double resignationChance = Math.Clamp(shortfall * 0.4, 0, 0.55);
        if (random.NextDouble() >= resignationChance) return false;

        contract.Status = ContractStatus.Resigned;
        Detach(staff, team);
        staff.CareerSatisfaction = 65;
        reason = $"{staff.FullName} has resigned as {team.Name}'s {staff.Role}.";
        return true;
    }

    /// <summary>
    /// Job-market follow-up: contract renewal for a backroom appointment. No BoardTrust to reuse
    /// (see EvaluateSatisfaction's own reasoning for why), so this blends the two things that
    /// genuinely decide a renewal for this kind of role: is the club satisfied with the work
    /// (EffectiveWithExperience - a real, external-facing quality read) and is HE satisfied enough
    /// to want to stay (CareerSatisfaction) - a club will not fight to keep a mediocre appointment,
    /// and an unhappy specialist will not commit to a new term even if the club wants him to.
    /// </summary>
    public RenewalDecision EvaluateRenewal(StaffMember staff, StaffContract contract, Team team, Random random, int renewalYears = 2)
    {
        double clubWantsHim = staff.EffectiveWithExperience / 100.0;      // 0-1
        double heWantsToStay = staff.CareerSatisfaction / 100.0;          // 0-1
        double chance = Math.Clamp(0.15 + clubWantsHim * 0.55 + heWantsToStay * 0.25, 0.05, 0.92);

        if (random.NextDouble() >= chance)
            return new RenewalDecision(false, $"{team.Name} and {staff.FullName} have parted ways at the end of his contract.");

        contract.EndDate = contract.EndDate.AddYears(Math.Max(1, renewalYears));
        return new RenewalDecision(true, $"{team.Name} have extended {staff.FullName}'s contract to {contract.EndDate:yyyy}.");
    }

    /// <summary>
    /// Tenure growth - the same saturating, reinforced-by-success shape CoachCareerService.
    /// GrowFromTenure already established for a head coach's own tactical sharpness, applied to a
    /// backroom specialist's core attributes. Deliberately NOT reinforced by "how the team did" -
    /// unlike a head coach, a specialist's job performance is not fairly judged by results he has
    /// only a small, indirect hand in, so this grows on tenure alone, the honest signal actually
    /// available for this role.
    /// </summary>
    public void GrowFromTenure(StaffMember staff, Random random)
    {
        double tenureFactor = Math.Exp(-Math.Max(0, staff.YearsExperience) / 6.0);
        double growthChance = Math.Clamp(0.12 * tenureFactor, 0, 0.35);

        if (random.NextDouble() < growthChance) staff.Analysis = Math.Min(20, staff.Analysis + 1);
        if (random.NextDouble() < growthChance) staff.TechnicalKnowledge = Math.Min(20, staff.TechnicalKnowledge + 1);
        if (random.NextDouble() < growthChance) staff.Communication = Math.Min(20, staff.Communication + 1);

        staff.YearsExperience++;

        // Wave 4: a specialist's reputation drifts slowly toward how good he actually is - years
        // of solid work get noticed even without a headline. The quarterly development signal
        // (below) is what moves it faster, up or down, off real results.
        double repTarget = staff.EffectiveWithExperience;
        staff.Reputation = Math.Clamp(staff.Reputation + (repTarget - staff.Reputation) * 0.08, 0, 100);
    }

    // ---------------- Wave 4: the quarterly development signal, and the route to a head-coach job ----------------

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: the second growth channel for a specialist, on a
    /// quarterly cadence (the same tick Wave 2's development report runs on). Tenure alone
    /// measures "how long has he been here", not "is his coaching actually working" - so this
    /// compares the recent development of the players in HIS OWN cluster (a batting coach's
    /// batters, a bowling coach's bowlers) against the team's overall rate. A coach whose players
    /// are genuinely outdeveloping the baseline grows - and builds real reputation - faster than
    /// tenure would give him; one whose players stagnate does not, however many years he has
    /// banked. This is the mechanic behind a real coach's "he turns strugglers into regulars"
    /// standing, and it ties Wave 4 directly to Wave 2's own output.
    ///
    /// Analysts and scouts are skipped - their value is not player development, and judging them
    /// on it would be the wrong signal entirely. Selectors are skipped for the same reason - a
    /// national selector's job is spotting and recommending form, not developing it.
    /// </summary>
    public void GrowFromDevelopmentSignal(
        StaffMember staff, IReadOnlyList<Player> teamPlayers, IReadOnlyDictionary<Guid, double> recentDevelopmentByPlayer, Random random)
    {
        if (staff.Role is StaffRole.Analyst or StaffRole.Scout or StaffRole.Selector or StaffRole.ChiefSelector) return;
        if (teamPlayers.Count == 0) return;

        var cluster = teamPlayers.Where(p => InCluster(staff.Role, p)).ToList();
        if (cluster.Count == 0) return;

        double Dev(Player p) => recentDevelopmentByPlayer.TryGetValue(p.Id, out var d) ? d : 0;

        double clusterAvg = cluster.Average(Dev);
        double teamAvg = teamPlayers.Average(Dev);
        double edge = clusterAvg - teamAvg; // positive: his players are pulling ahead

        // Only a genuine, sustained edge (or shortfall) moves anything - noise around zero does not.
        if (Math.Abs(edge) < 0.15) return;

        if (edge > 0)
        {
            double growthChance = Math.Clamp(edge * 0.35, 0, 0.5);
            if (random.NextDouble() < growthChance) staff.TechnicalKnowledge = Math.Min(20, staff.TechnicalKnowledge + 1);
            if (random.NextDouble() < growthChance) staff.Communication = Math.Min(20, staff.Communication + 1);
            staff.Reputation = Math.Clamp(staff.Reputation + edge * 2.5, 0, 100);
        }
        else
        {
            // His players are going backwards relative to the rest - reputation slips, no growth.
            staff.Reputation = Math.Clamp(staff.Reputation + edge * 1.5, 0, 100);
        }
    }

    private static bool InCluster(StaffRole role, Player p) => role switch
    {
        StaffRole.BattingCoach => p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper,
        StaffRole.BowlingCoach => p.PrimaryRole is PlayerRole.Bowler or PlayerRole.BowlingAllrounder or PlayerRole.BattingAllrounder,
        _ => true // fielding / S&C / mental / assistant work with the whole squad
    };

    /// <summary>
    /// Wave 4: is this staff member a genuine candidate to step up to a head-coach job - his own
    /// or another club's? An assistant coach with real effectiveness, a reputation the game has
    /// noticed, and years behind him. Deliberately a high bar: most specialists never make the
    /// jump.
    /// </summary>
    public bool IsHeadCoachCandidate(StaffMember staff) =>
        staff.Role == StaffRole.AssistantCoach
        && staff.EffectiveWithExperience >= 62
        && staff.Reputation >= 55
        && staff.YearsExperience >= 6;

    /// <summary>
    /// Wave 4: rival interest in a backroom specialist. Lighter than the coach's own mechanism
    /// (per the brief) - a specialist moves jobs more freely and there is no board drama, just
    /// "someone offered him more and his current club did not fight hard enough."
    /// </summary>
    public bool EvaluateRivalInterest(StaffMember staff, Random random)
    {
        // Nobody poaches a specialist the game has not heard of yet - a real reputation has to
        // have been built first (and a fresh appointment simply has not had time).
        if (staff.Reputation < 45) return false;

        double pull = Math.Clamp(staff.EffectiveWithExperience / 100.0 * 0.5
            + staff.Reputation / 100.0 * 0.3
            - Math.Clamp((staff.CareerSatisfaction - 50) / 50.0, 0, 1) * 0.25, 0, 0.8);
        return random.NextDouble() < pull * 0.12;
    }

    /// <summary>
    /// Wave 4: retention for a specialist is mediated by the HEAD COACH, not the board (the brief
    /// is explicit). A coach who rates his man and manages people well fights to keep him - a real
    /// term extension - and it works; a disengaged head coach lets him go. On a failed retention
    /// the staff member leaves on his own terms (contract Resigned, detached).
    /// </summary>
    public (bool Retained, string Reason) OfferRetention(StaffMember staff, StaffContract contract, Team team, Coach? headCoach, Random random)
    {
        double coachWillFight = headCoach is null
            ? 0.25
            : Math.Clamp((Common.AbilityScale.AttributeToHundred(headCoach.Attributes.ManManagement) - 30) / 70.0, 0.1, 0.85);

        if (random.NextDouble() < coachWillFight)
        {
            contract.EndDate = contract.EndDate.AddYears(2);
            contract.AnnualSalary *= 1.15;
            staff.CareerSatisfaction = Math.Clamp(staff.CareerSatisfaction + 12, 0, 100);
            return (true, $"{team.Name} have persuaded {staff.FullName} to stay on improved terms.");
        }

        contract.Status = ContractStatus.Resigned;
        staff.TeamId = null;
        team.StaffIds.Remove(staff.Id);
        staff.CareerSatisfaction = 65;
        return (false, $"{staff.FullName} has left {team.Name} for a role elsewhere.");
    }
}
