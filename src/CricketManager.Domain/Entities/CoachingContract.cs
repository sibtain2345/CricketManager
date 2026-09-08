using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

public sealed class CoachingContract
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CoachId { get; init; }
    public Guid TeamId { get; init; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public double AnnualSalary { get; set; }

    public ContractStatus Status { get; set; } = ContractStatus.Active;

    // Section 4: board objectives per contract year, e.g. {1: "Finish top 4"} - narrative/flavor
    // text a human reads. Deliberately never evaluated by code (see CoachCareerService's own
    // doc comment on why prose can't be honestly checked off) - StructuredObjectivesByYear below
    // is what the board actually judges a season against.
    public Dictionary<int, List<string>> ObjectivesByYear { get; set; } = new();

    /// <summary>
    /// Board-objectives follow-up: the real, checkable counterpart to ObjectivesByYear above -
    /// see BoardObjective. Keyed the same way (contract year number, 1-based), so a coach's
    /// second season can carry a different, harder target than his first. Empty by default, so
    /// every existing contract/caller is unaffected - CoachCareerService.EvaluateSeason only
    /// evaluates objectives that were actually set for the year in question.
    /// </summary>
    public Dictionary<int, List<BoardObjective>> StructuredObjectivesByYear { get; set; } = new();

    // Section 89-92: authority/control clauses
    public bool HasSquadSelectionAuthority { get; set; } = true;
    public bool HasStaffAppointmentControl { get; set; }
    public bool HasRecruitmentControl { get; set; }
    public double CompensationIfTerminated { get; set; }
}
