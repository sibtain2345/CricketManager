using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// Post-Phase-6 (section A): a team advertises a vacancy with listed requirements, the way a real
/// job posting works. Coaches and staff - employed OR unemployed - can then apply. The team's
/// board (or, for a non-head-coach role, whoever holds the staffing authority - section B) weighs
/// the applicants against the requirements AND the club's cultural identity.
/// </summary>
public sealed class VacancyAdvert
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid TeamId { get; init; }

    /// <summary>Null = the head-coach job (the board's call). Otherwise a specialist role (the head coach's call, unless delegated).</summary>
    public StaffRole? Role { get; init; }

    public string RoleLabel => Role?.ToString() ?? "Head Coach";

    // Listed requirements - a candidate below any of these does not get a serious look.
    public CoachingLicense MinLicense { get; set; } = CoachingLicense.None;
    public double MinReputation { get; set; }
    public double MinRoleFit { get; set; }

    public DateOnly Posted { get; init; }
    public DateOnly Closes { get; set; }

    [JsonInclude] public bool Filled { get; private set; }
    [JsonInclude] public Guid? AppointeeId { get; private set; }

    public void Fill(Guid appointeeId)
    {
        Filled = true;
        AppointeeId = appointeeId;
    }
}
