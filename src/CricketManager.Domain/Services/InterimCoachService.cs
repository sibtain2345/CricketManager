using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 4: a head-coach vacancy is not an empty chair.
///
/// When a coach is dismissed, resigns, leaves for a rival, or his contract lapses, the assistant
/// coach steps up as interim - and, if the run in charge goes well enough for long enough, can be
/// made permanent (converted to a real Coach entity). This is the piece CLAUDE.md's Wave 4
/// scope names directly ("interim-assistant-on-vacancy and possible permanent promotion"), plus
/// the suggestion that an interim be eligible for the SAME milestone tactical growth a permanent
/// coach gets while he is genuinely making head-coach-grade calls.
/// </summary>
public sealed class InterimCoachService
{
    private readonly CoachJobMarketService _jobMarket = new();

    /// <summary>Time an interim needs in the seat before a permanent appointment is even considered - the best part of a season.</summary>
    private const int MinimumInterimDaysForPromotion = 300;

    /// <summary>
    /// Puts the assistant coach in temporary charge of a team whose head-coach chair is vacant.
    /// Returns the event, or null when there is no vacancy or no assistant to step up. The interim
    /// keeps his StaffMember identity (he is still on the books) - Team.InterimCoachStaffId is
    /// what tells the rest of the world who is running things while CurrentCoachId is null.
    /// </summary>
    public GameEvent? AppointInterim(Team team, WorldState world, DateOnly date)
    {
        if (team.CurrentCoachId is not null || team.InterimCoachStaffId is not null) return null;

        var assistant = world.Staff.FirstOrDefault(s => s.TeamId == team.Id && s.Role == StaffRole.AssistantCoach);
        if (assistant is null) return null;

        team.InterimCoachStaffId = assistant.Id;
        team.InterimSince = date;

        return new GameEvent(date, GameEventType.CoachAppointedInterim,
            $"{assistant.FullName} takes charge of {team.Name} on an interim basis.", assistant.Id, team.Id);
    }

    /// <summary>
    /// Wave 4 (suggestion): an interim head coach IS making head-coach-grade calls for the
    /// duration, so a competition milestone that fires during the interim window makes him
    /// eligible for the same growth a permanent coach would get - applied to his StaffMember
    /// attributes (he has no Coach entity yet).
    /// </summary>
    public void GrowInterimFromMilestone(StaffMember interim, Team team, double competitionPrestige, Random random)
    {
        double clubStanding = (team.Strength + team.Reputation.Domestic * 2) / 3;
        double contextFactor = 0.7 + Math.Clamp((clubStanding + Math.Clamp(competitionPrestige, 0, 100)) / 200.0, 0, 1) * 0.7;
        double chance = Math.Clamp(0.10 * contextFactor, 0, 0.30);

        if (random.NextDouble() < chance) interim.Analysis = Math.Min(20, interim.Analysis + 1);
        if (random.NextDouble() < chance) interim.Communication = Math.Min(20, interim.Communication + 1);
        if (random.NextDouble() < chance) interim.TechnicalKnowledge = Math.Min(20, interim.TechnicalKnowledge + 1);
        interim.Reputation = Math.Clamp(interim.Reputation + 1.5, 0, 100);
    }

    /// <summary>
    /// After a genuine run in charge going well, an interim can be made permanent - converted to a
    /// real Coach, given a contract, and detached from the backroom. Rare, gated on both time in
    /// the seat and results. Returns the event, or null if not promoted this check.
    /// </summary>
    public GameEvent? ConsiderPermanentPromotion(Team team, WorldState world, DateOnly date, double lastPerformanceScore, Random random)
    {
        if (team.InterimCoachStaffId is not { } staffId || team.InterimSince is not { } since) return null;
        if (date.DayNumber - since.DayNumber < MinimumInterimDaysForPromotion) return null;

        var interim = world.Staff.FirstOrDefault(s => s.Id == staffId);
        if (interim is null) { team.InterimCoachStaffId = null; team.InterimSince = null; return null; }

        double promoteChance = Math.Clamp(0.12 + Math.Max(0, lastPerformanceScore) * 0.5 + (interim.Reputation - 50) / 100.0 * 0.3, 0, 0.7);
        if (random.NextDouble() >= promoteChance) return null;

        var coach = ToCoach(interim, date);
        world.Coaches.Add(coach);
        var contract = _jobMarket.Hire(coach, team, date, annualSalary: 200_000, contractYears: 3);
        world.CoachingContracts.Add(contract);

        interim.TeamId = null;
        team.StaffIds.Remove(interim.Id);
        team.InterimCoachStaffId = null;
        team.InterimSince = null;

        return new GameEvent(date, GameEventType.CoachPromotedToPermanent,
            $"{team.Name} have handed {coach.FullName} the head-coach job permanently after an impressive interim spell.", coach.Id, team.Id);
    }

    /// <summary>
    /// Wave 4: converts a genuine head-coach candidate on the staff into a Coach entity for a job
    /// at ANOTHER club. The caller owns wiring him into that club (via CoachJobMarketService.Hire)
    /// and removing him from his old team's StaffIds.
    /// </summary>
    public Coach PromoteToHeadCoachElsewhere(StaffMember staff, DateOnly date)
    {
        var coach = ToCoach(staff, date);
        staff.TeamId = null;
        return coach;
    }

    private static Coach ToCoach(StaffMember s, DateOnly date)
    {
        int Map(int v) => Math.Clamp(v, 1, 20);

        var coach = new Coach
        {
            FirstName = s.FirstName,
            LastName = s.LastName,
            DateOfBirth = s.DateOfBirth == default ? date.AddYears(-45) : s.DateOfBirth,
            PlayingExperience = PlayingExperience.FirstClass,
            License = CoachingLicense.Advanced,
            Philosophy = CoachingPhilosophy.PerformanceFocused,
            Attributes = new CoachAttributes
            {
                TacticalKnowledge = Map(s.Analysis),
                TacticalAnalysis = Map(s.Analysis),
                MatchReading = Map((s.Analysis + s.TechnicalKnowledge) / 2),
                TechnicalKnowledge = Map(s.TechnicalKnowledge),
                Statistics = Map(s.Statistics),
                OppositionAnalysis = Map(s.Analysis),
                ManManagement = Map(s.Communication),
                PlayerManagement = Map(s.Communication),
                Motivation = Map(s.Communication),
                WorkEthic = Map(s.Diligence),
                PlayerDevelopment = Map((s.TechnicalKnowledge + s.Communication) / 2),
                DecisionMaking = Map(s.Analysis),
                Adaptability = Map((s.Analysis + s.Communication) / 2)
            }
        };

        coach.Reputation.Adjust(domesticDelta: s.Reputation);
        coach.BoardTrust = 50;
        coach.Authority = 25;
        return coach;
    }
}
