using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 11: the human coach's unified authority model. One <see cref="DelegationMode"/> per
/// <see cref="DecisionArea"/> - the single place "who makes this call" is answered, replacing the
/// scattered bool "Delegate*" flags on <see cref="ManagerPreferences"/> (which now compute
/// themselves from this).
///
/// The standing principle (CLAUDE.md, the user's explicit instruction): <b>the human coach always
/// has final authority, with an explicit option to delegate to staff.</b> This VO is that option
/// made concrete and consistent - every area defaults to <see cref="DelegationMode.Delegate"/> so
/// a fresh save behaves like an AI-run one, and the human dials any area back to
/// <see cref="DelegationMode.Consult"/> (see a recommendation first) or
/// <see cref="DelegationMode.DoItMyself"/> (the sim never acts for him there beyond avoiding an
/// illegal/stalled state).
/// </summary>
public sealed class DelegationProfile
{
    private readonly Dictionary<DecisionArea, DelegationMode> _modes = new();

    /// <summary>The mode for an area - <see cref="DelegationMode.Delegate"/> unless explicitly set.</summary>
    public DelegationMode Mode(DecisionArea area) => _modes.TryGetValue(area, out var m) ? m : DelegationMode.Delegate;

    public void Set(DecisionArea area, DelegationMode mode) => _modes[area] = mode;

    /// <summary>True when the AI is allowed to APPLY its own decision in this area (Delegate, or Consult - the proposal is applied unless overridden). False only for DoItMyself.</summary>
    public bool AiMayAct(DecisionArea area) => Mode(area) != DelegationMode.DoItMyself;

    /// <summary>True when the human should be shown a recommendation for this area before it is acted on (Consult), or when he owns it outright (DoItMyself).</summary>
    public bool WantsRecommendation(DecisionArea area) => Mode(area) is DelegationMode.Consult or DelegationMode.DoItMyself;

    /// <summary>For serialization / inspection.</summary>
    public IReadOnlyDictionary<DecisionArea, DelegationMode> Modes => _modes;

    public void SetAll(DelegationMode mode)
    {
        foreach (DecisionArea area in Enum.GetValues<DecisionArea>())
            _modes[area] = mode;
    }
}

/// <summary>
/// Phase 11: what a "Consult"-mode area produces - the AI's proposed choice packaged for the human
/// to see and accept or override. Raised as a <see cref="Enums.GameEventType.StaffRecommendationIssued"/>
/// news item.
/// </summary>
public sealed record StaffRecommendation(
    DecisionArea Area,
    string RecommendedBy,
    string What,
    string Reasoning,
    /// <summary>0-100 - how sure the staff are. A weak analyst / panel is confidently wrong; a strong one hedges honestly.</summary>
    double Confidence);
