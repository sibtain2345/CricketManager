using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// One player's coach-directed training programme for the year. Deliberately as small as
/// TacticalPlan's own single-instruction shape: a focus, an intensity, and (only when the focus
/// is RoleConversion) a target role - not a weekly schedule, because this codebase's
/// player-development tick is annual everywhere (PlayerAgeingService, RetirementService,
/// SquadManagementService all resolve once a year), so a finer calendar would have no matching
/// simulation granularity underneath it to actually drive. See CLAUDE.md's Phase 5 writeup for
/// the full reasoning and what a future slice would need before a real weekly calendar makes sense.
///
/// Section 6's "staff suggests, human head coach has final say": StaffSuggestion is written by
/// TrainingService.SuggestFocus and is purely advisory - Focus is the field that actually governs
/// what happens, and nothing here ever overwrites a Focus a caller has explicitly set. An
/// AI-managed side (or a human player who has left this untouched) auto-adopts the suggestion at
/// the point of application, per WorldClockService's rollover - see TrainingService.
/// </summary>
public sealed class PlayerTrainingPlan
{
    public TrainingFocus Focus { get; set; } = TrainingFocus.None;
    public TrainingIntensity Intensity { get; set; } = TrainingIntensity.Normal;

    /// <summary>Only read when Focus == RoleConversion. The batting role the training is trying to grow the player into - e.g. an Opener being reshaped toward MiddleOrder.</summary>
    public BattingRole? TargetBattingRole { get; set; }

    /// <summary>Only read when Focus == RoleConversion. The bowling role side of the same idea - e.g. a DeathBowler being reshaped toward FirstChange as his pace declines.</summary>
    public BowlingRoleType? TargetBowlingRole { get; set; }

    /// <summary>What the relevant specialist coach would suggest, last time it was asked - advisory only, per the class doc comment above.</summary>
    public TrainingFocus? StaffSuggestion { get; set; }
}
