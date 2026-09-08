using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 6, Slice 6.2: the human coach's standing instructions for the parts of running a club
/// that the world simulation would otherwise auto-resolve on his behalf.
///
/// The locked scope decision (CLAUDE.md "PHASE 6 - PLAN"): until a UI exists, when the sim reaches
/// a human decision point (squad announcement, XI, training focus) it AUTO-PICKS the best option
/// via the AI layer, UNLESS the human has set a stored preference here that overrides it. So every
/// field defaults to "let the AI handle it" - a freshly created human save behaves exactly like an
/// AI-run one until the player changes something, which is what keeps the headless sim and the
/// test suite driving the same code path.
///
/// Only consulted for the team whose coach is IsHumanControlled. An AI club never reads this - its
/// AiClubManagementService decisions are its own.
///
/// Phase 11: the authority model is now unified on <see cref="Delegation"/> (a 3-state
/// <see cref="DelegationProfile"/>). The bool "Delegate*" properties below are kept as computed
/// shims over it so existing call sites and tests keep working unchanged - reading one is
/// "is this area Delegate?", writing one flips between Delegate and DoItMyself.
/// </summary>
public sealed class ManagerPreferences
{
    /// <summary>Phase 11: the unified per-decision-area authority model. Defaults to Delegate everywhere.</summary>
    public DelegationProfile Delegation { get; set; } = new();

    private bool IsDelegated(DecisionArea area) => Delegation.Mode(area) == DelegationMode.Delegate;
    private void SetDelegated(DecisionArea area, bool value) =>
        Delegation.Set(area, value ? DelegationMode.Delegate : DelegationMode.DoItMyself);

    /// <summary>When true (the default), AiClubManagementService announces this team's squads for it. When false, the human names the squad himself and the sim leaves any missing announcement alone.</summary>
    public bool DelegateSquadSelection
    {
        get => IsDelegated(DecisionArea.SquadSelectionDomestic);
        set => SetDelegated(DecisionArea.SquadSelectionDomestic, value);
    }

    /// <summary>When true (the default), the per-fixture XI is chosen by XiSelectionService. When false, the human is expected to have set a fixed XI elsewhere (a future UI concern) - the sim still falls back to auto-selection rather than fielding ten men.</summary>
    public bool DelegateXiSelection
    {
        get => IsDelegated(DecisionArea.XiSelection);
        set => SetDelegated(DecisionArea.XiSelection, value);
    }

    /// <summary>When true (the default), each owned player's training focus is left to the monthly auto-suggest. When false, the human's explicitly set PlayerTrainingPlan.Focus values are respected and never reassessed, and the AI stops proposing role-conversion programmes.</summary>
    public bool DelegateTraining
    {
        get => IsDelegated(DecisionArea.TrainingFocus);
        set => SetDelegated(DecisionArea.TrainingFocus, value);
    }

    /// <summary>When true (the default), the AI fills a vacant head-coach or key-staff chair and handles captaincy succession.</summary>
    public bool DelegateStaffAndCaptaincy
    {
        get => IsDelegated(DecisionArea.StaffHiring);
        set { SetDelegated(DecisionArea.StaffHiring, value); SetDelegated(DecisionArea.Captaincy, value); }
    }

    /// <summary>Target squad size for an announced squad. 15 is the usual tournament number.</summary>
    public int PreferredSquadSize { get; set; } = 15;

    /// <summary>Players the human always wants in the announced squad if they are fit and available - honoured even when squad selection is delegated.</summary>
    public List<Guid> AlwaysInclude { get; set; } = new();

    /// <summary>Players the human never wants selected - honoured even when selection is delegated.</summary>
    public List<Guid> NeverSelect { get; set; } = new();

    /// <summary>When true, the AI rests first-choice players in a dead rubber.</summary>
    public bool RotateInDeadRubbers { get; set; } = true;

    /// <summary>
    /// Phase 8, Slice 8.2: when true (the default), the AI runs this club's academy for the human.
    /// When false, the annual academy pass only SURFACES its recommendations and makes no changes.
    /// </summary>
    public bool DelegateAcademy
    {
        get => IsDelegated(DecisionArea.Academy);
        set => SetDelegated(DecisionArea.Academy, value);
    }

    /// <summary>Phase 8, Slice 8.7: when true (the default), the AI sends buried young players out on development loans and brings them back.</summary>
    public bool DelegateLoans
    {
        get => IsDelegated(DecisionArea.Loans);
        set => SetDelegated(DecisionArea.Loans, value);
    }

    /// <summary>
    /// Meeting-driven-selection ticket (requirement A): a per-series selection meeting is
    /// genuinely discretionary, not mandatory before every series - see
    /// AiClubManagementService.ShouldHoldMeeting. This is the explicit human override: false means
    /// the coach never bothers calling one and the squad is simply carried forward/announced
    /// without the rationale/dissent theatre. True (the default) still doesn't force a meeting
    /// every time - it just allows the same squad-change/thoroughness read an AI coach uses.
    /// </summary>
    public bool HoldSelectionMeetings { get; set; } = true;

    /// <summary>
    /// Corrections pass (correction 4): per-instance "not this one" for a specific competition's
    /// NEXT automatically-triggered selection meeting - distinct from the blanket
    /// <see cref="HoldSelectionMeetings"/> "never bother me" switch. AiClubManagementService.
    /// ShouldHoldMeeting removes the competition id when it consumes it, so this is a one-shot skip.
    /// (The pre-Phase-17 shape of a per-instance human choice, like AlwaysInclude/NeverSelect.)
    /// </summary>
    public HashSet<Guid> SkipNextSelectionMeetingFor { get; set; } = new();

    /// <summary>
    /// Corrections pass (correction 4): the human coach has asked to convene a national-pool
    /// meeting himself, on the next tick - additive to the automatic annual / pre-major triggers.
    /// Consumed (set back to false) when the meeting is held.
    /// </summary>
    public bool RequestPoolMeeting { get; set; }
}
