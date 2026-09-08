using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// One funded piece of infrastructure work - a facility upgrade, a stadium expansion, or a
/// new ground.
///
/// This is what made facilities a real decision rather than a set of numbers. Before it,
/// every facility level was fixed forever: a team could have poor medical facilities and no
/// way to fix them, and money in the budget had nothing to be spent on. Section 55 is
/// explicit that investment should take TIME to produce results (invest in year one,
/// facilities improve in year two, better prospects appear in year four), which is why this
/// is a dated project with a completion date rather than an instant purchase.
///
/// The permanent consequence is automatic: TeamFinanceService derives annual upkeep from the
/// facilities a team actually owns, so finishing a project raises the running cost forever
/// after. Upgrading everything and going bankrupt is a legitimate way to fail.
/// </summary>
public sealed class InfrastructureProject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TeamId { get; init; }

    /// <summary>Set for ground work on an EXISTING ground. Null for team-level facilities and for a NewStadium project (which has no ground until it completes).</summary>
    public Guid? GroundId { get; init; }

    public InfrastructureProjectType Type { get; init; }

    [JsonInclude] public InfrastructureProjectStatus Status { get; private set; } = InfrastructureProjectStatus.Proposed;

    public int FromLevel { get; init; }
    public int ToLevel { get; init; }

    public double Cost { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly ExpectedCompletionDate { get; init; }
    [JsonInclude] public DateOnly? ActualCompletionDate { get; private set; }

    // NewStadium / StadiumExpansion only.
    public string NewGroundName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public int Capacity { get; init; }

    [JsonIgnore] public bool IsGroundWork => Type is InfrastructureProjectType.NewStadium
        or InfrastructureProjectType.StadiumExpansion
        or InfrastructureProjectType.GroundMediaFacilities
        or InfrastructureProjectType.GroundHospitality
        or InfrastructureProjectType.GroundMedicalFacilities
        or InfrastructureProjectType.GroundPitchInfrastructure
        or InfrastructureProjectType.GroundYouthFacilities
        or InfrastructureProjectType.GroundTrainingFacilities;

    public void Begin() => Status = InfrastructureProjectStatus.UnderConstruction;

    public void Complete(DateOnly date, Guid? createdGroundId = null)
    {
        Status = InfrastructureProjectStatus.Completed;
        ActualCompletionDate = date;
        CreatedGroundId = createdGroundId;
    }

    /// <summary>Set when a NewStadium project completes, so the money spent is traceable to the ground it produced.</summary>
    [JsonInclude] public Guid? CreatedGroundId { get; private set; }

    /// <summary>
    /// Cancels an in-progress project and returns whatever can be recovered - which is half of
    /// the UNSPENT portion only. Work already done and contracts already signed are gone, so
    /// abandoning a half-built stand hurts, and abandoning one that is nearly finished recovers
    /// nothing at all. Without that asymmetry a board could commit to everything and cancel
    /// freely, which makes the whole decision weightless.
    /// </summary>
    public double Cancel(DateOnly asOf)
    {
        if (Status != InfrastructureProjectStatus.UnderConstruction) return 0;
        Status = InfrastructureProjectStatus.Cancelled;

        int totalDays = Math.Max(1, ExpectedCompletionDate.DayNumber - StartDate.DayNumber);
        double proportionRemaining = Math.Clamp((double)(ExpectedCompletionDate.DayNumber - asOf.DayNumber) / totalDays, 0, 1);
        return Math.Round(Cost * proportionRemaining * 0.5, 0);
    }

    public int DaysRemaining(DateOnly asOf) => Math.Max(0, ExpectedCompletionDate.DayNumber - asOf.DayNumber);

    public bool IsDueOn(DateOnly date) =>
        Status == InfrastructureProjectStatus.UnderConstruction && date >= ExpectedCompletionDate;
}
