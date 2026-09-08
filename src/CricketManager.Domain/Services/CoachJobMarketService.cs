using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Job-market follow-up: the hiring half of the coach job market - CoachCareerService already
/// owns the OTHER half (a coach's tenure, dismissal, resignation, and now renewal). Kept as a
/// separate class for the same reason StaffRecruitmentService/StaffCareerService were split:
/// "how good a candidate is and whether he wants this job" is a different concern from "how a
/// sitting coach's own tenure is going", and the two should not have to share one file to make
/// sense of either.
/// </summary>
public sealed class CoachJobMarketService
{
    /// <summary>
    /// Appoints a coach - creates the contract and attaches him on both sides (Coach.CurrentTeamId/
    /// CurrentContractId AND Team.CurrentCoachId), the same "one call, no way to half-do it"
    /// discipline StaffCareerService.Hire already established. A fresh appointment is a genuine
    /// fresh start: BoardTrust and Authority both reset toward neutral/low starting points rather
    /// than carrying over anything from a previous job - he has not yet earned standing at THIS
    /// club, whatever he built at his last one.
    /// </summary>
    public CoachingContract Hire(Coach coach, Team team, DateOnly date, double annualSalary, int contractYears = 3, double compensationIfTerminated = 0)
    {
        var contract = new CoachingContract
        {
            CoachId = coach.Id,
            TeamId = team.Id,
            StartDate = date,
            EndDate = date.AddYears(Math.Max(1, contractYears)),
            AnnualSalary = annualSalary,
            CompensationIfTerminated = compensationIfTerminated,
            Status = ContractStatus.Active
        };

        // Phase 12 (§13.4): the board attaches structured objectives shaped by its chairman's
        // AGENDA - not just "finish top N" but "blood the youngsters" / "balance the books" /
        // "grow the fanbase" for a board that cares about those. Set for every contract year.
        var yearObjectives = ObjectivesForAgenda(team);
        if (yearObjectives.Count > 0)
            for (int y = 1; y <= Math.Max(1, contractYears); y++)
                contract.StructuredObjectivesByYear[y] = yearObjectives;

        coach.CurrentTeamId = team.Id;
        coach.CurrentContractId = contract.Id;
        coach.CalledInitialPoolMeeting = false; // corrections pass (correction 4): a fresh appointment calls its own pool meeting
        coach.CareerTeamIds.Add(team.Id); // requirement C: first-hand knowledge substrate
        coach.BoardTrust = 50;
        coach.Authority = 20;
        coach.CareerRecord.OnNewAppointment(); // Wave 7: a new job resets the club-specific clocks, never the career totals
        team.CurrentCoachId = coach.Id;

        return contract;
    }

    /// <summary>Phase 12 (§13.4): the structured objectives a board's chairman agenda produces. Empty for a national side or an agenda-less board (the default) - the existing implicit results judgement stands.</summary>
    private static List<Domain.ValueObjects.BoardObjective> ObjectivesForAgenda(Team team)
    {
        if (team.IsNational) return new();
        var list = new List<Domain.ValueObjects.BoardObjective>();
        // Only competition-agnostic types here - a hire-time objective has no season to point a
        // FinishTopN at, so those stay the board's implicit results judgement.
        switch (team.Board.Agenda)
        {
            case Domain.ValueObjects.ChairmanAgenda.WinNow:
                list.Add(new() { Type = Domain.ValueObjects.BoardObjectiveType.MinimumWinRate, TargetValue = 55 });
                break;
            case Domain.ValueObjects.ChairmanAgenda.YouthAndAcademy:
                list.Add(new() { Type = Domain.ValueObjects.BoardObjectiveType.DevelopYouth, TargetValue = 2 });
                break;
            case Domain.ValueObjects.ChairmanAgenda.Austerity:
                list.Add(new() { Type = Domain.ValueObjects.BoardObjectiveType.BalanceTheBooks, TargetValue = 0 });
                break;
            case Domain.ValueObjects.ChairmanAgenda.Prestige:
                list.Add(new() { Type = Domain.ValueObjects.BoardObjectiveType.GrowFanbase, TargetValue = (int)Math.Round(team.Board.FanSentiment + 6) });
                break;
        }
        return list;
    }

    /// <summary>
    /// Whether a candidate actually wants this particular job - a real, if simple, read of fit.
    /// A team well below the coach's own standing is a step down he is less likely to take,
    /// especially the more reputable he already is; a step up he takes readily. An unemployed
    /// coach with nothing else on is somewhat more willing throughout, the honest "he needs the
    /// work" adjustment.
    /// </summary>
    public bool EvaluateOffer(Coach candidate, Team offeringTeam, Random random)
    {
        double teamStanding = (offeringTeam.Strength + offeringTeam.Reputation.Domestic * 2) / 3;
        double coachStanding = candidate.Reputation.Domestic;

        double gap = teamStanding - coachStanding; // positive = a step UP for the coach
        double acceptChance = Math.Clamp(0.55 + gap / 150.0, 0.15, 0.95);

        if (candidate.CurrentTeamId is null) acceptChance = Math.Clamp(acceptChance + 0.15, 0.15, 0.97);

        return random.NextDouble() < acceptChance;
    }
}
