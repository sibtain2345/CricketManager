using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Section 31. A current or historical injury.
///
/// This existed nowhere before, which meant `MedicalEffectivenessService` computed an
/// injury risk with nothing to apply it TO, and `PhysicalAttributes.InjuryProneness` and
/// `Recovery` were attributes no system could ever act on. The state model has to exist
/// before Phase 4 can generate injuries during matches, otherwise Phase 4 would invent it
/// ad hoc alongside everything else it has to build.
///
/// RecurrenceRisk is on the injury rather than the player because it's specific: a player
/// coming back from a hamstring tear is at elevated risk of THAT injury again, not of
/// everything.
/// </summary>
public sealed class Injury
{
    [JsonInclude] public InjuryType Type { get; private set; }
    [JsonInclude] public InjurySeverity Severity { get; private set; }
    [JsonInclude] public DateOnly StartDate { get; private set; }
    [JsonInclude] public DateOnly ExpectedReturnDate { get; private set; }
    [JsonInclude] public DateOnly? ActualReturnDate { get; private set; }

    /// <summary>0-100. Elevated chance of the SAME injury recurring after return.</summary>
    [JsonInclude] public double RecurrenceRisk { get; private set; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 3 (point 6): the player's Form.Confidence at the
    /// moment the injury happened, captured by PlayerAvailabilityService.ApplyInjury. On return,
    /// belief is read back from HERE - pulled toward where it was before the layoff (never all
    /// the way; there is rust), shaped by temperament and by how long he was out - rather than
    /// from whatever number the monthly decay left it at, or worse, whatever a replacement's hot
    /// streak did to the team picture. Defaults to 50 (neutral) so a hand-built Injury or an old
    /// save reads as "no special belief either way".
    /// </summary>
    [JsonInclude] public double ConfidenceAtOnset { get; private set; } = 50;

    public Injury() { }

    public Injury(InjuryType type, InjurySeverity severity, DateOnly startDate, int expectedDaysOut, double recurrenceRisk = 0)
    {
        Type = type;
        Severity = severity;
        StartDate = startDate;
        ExpectedReturnDate = startDate.AddDays(Math.Max(1, expectedDaysOut));
        RecurrenceRisk = Math.Clamp(recurrenceRisk, 0, 100);
    }

    [JsonIgnore] public bool IsResolved => ActualReturnDate is not null;

    public bool IsActiveOn(DateOnly date) =>
        !IsResolved && date >= StartDate && date < ExpectedReturnDate;

    public void MarkReturned(DateOnly returnDate) => ActualReturnDate = returnDate;

    /// <summary>Wave 3: records the player's confidence at the moment of injury - see ConfidenceAtOnset. Called once, by PlayerAvailabilityService.ApplyInjury.</summary>
    public void CaptureOnsetState(double confidence) => ConfidenceAtOnset = Math.Clamp(confidence, 0, 100);

    /// <summary>Total expected layoff in days, for reasoning about rust on return.</summary>
    [JsonIgnore] public int ExpectedDaysOut => Math.Max(1, ExpectedReturnDate.DayNumber - StartDate.DayNumber);

    /// <summary>
    /// Recovery can beat or miss the original estimate - a player with strong Recovery and
    /// good medical support comes back sooner. Never shortens below the day after the injury:
    /// an instant return would make the whole system cosmetic.
    /// </summary>
    public void AdjustExpectedReturn(int dayDelta)
    {
        var adjusted = ExpectedReturnDate.AddDays(dayDelta);
        var floor = StartDate.AddDays(1);
        ExpectedReturnDate = adjusted < floor ? floor : adjusted;
    }

    /// <summary>
    /// Multiplier on a player's effectiveness if they play through this. Only a Niggle is
    /// realistically playable; anything worse should be a selection block, not a penalty -
    /// which is why the harder severities return a value low enough that no sane selection
    /// model would pick them, on top of PlayerAvailabilityService excluding them outright.
    /// </summary>
    public double PlayingThroughEffectiveness => Severity switch
    {
        InjurySeverity.None => 1.0,
        InjurySeverity.Niggle => 0.88,
        InjurySeverity.Minor => 0.7,
        _ => 0.4
    };

    /// <summary>Typical layoff in days, used when generating an injury. Ranges are the realistic ones for each severity band.</summary>
    public static (int MinDays, int MaxDays) TypicalLayoff(InjurySeverity severity) => severity switch
    {
        InjurySeverity.Niggle => (2, 6),
        InjurySeverity.Minor => (7, 21),
        InjurySeverity.Moderate => (21, 56),
        InjurySeverity.Serious => (60, 180),
        InjurySeverity.CareerThreatening => (180, 540),
        _ => (0, 0)
    };
}
