using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// Phase 5 Part 2: the employment contract behind a hired StaffMember - the same shape as
/// CoachingContract, deliberately. StaffMember/StaffRole/Team.StaffIds all already existed
/// (Phase 4's analyst work), but nothing tied a staff member to a team through anything more
/// than a bare Guid list - there was no contract, no salary, no way for a job to end. This is
/// that missing piece, mirroring CoachingContract's fields rather than inventing a differently
/// shaped one, since a backroom appointment and a head-coach appointment are the same kind of
/// real-world thing at different pay grades.
/// </summary>
public sealed class StaffContract
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StaffId { get; init; }
    public Guid TeamId { get; init; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public double AnnualSalary { get; set; }

    public ContractStatus Status { get; set; } = ContractStatus.Active;

    /// <summary>Paid out of Team.Finances.Budget on an early dismissal - CoachingContract's own CompensationIfTerminated has exactly this consumer already (CoachCareerService.EvaluateSeason); this is the same clause for a backroom appointment.</summary>
    public double CompensationIfTerminated { get; set; }
}
