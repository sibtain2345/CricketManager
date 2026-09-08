using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>How aggressively to set the field, independent of what the batters are doing.</summary>
public enum FieldAggression
{
    /// <summary>Catchers in, boundaries unguarded. Buy a wicket and accept the runs.</summary>
    Attacking,
    Balanced,
    /// <summary>Sweepers out, catchers withdrawn. Concede the singles, protect the rope.</summary>
    Defensive
}

/// <summary>
/// A standing instruction for one batter, set by the coach before or during an innings.
///
/// The zone/boundary/bowler-type fields (added alongside the post-Phase-4 planning brief's
/// batting-plan expansion) are deliberately NOT their own parallel "BattingApproach" type the
/// way BowlingApproach is - a bowler gets a fresh plan every spell, a batter does not, so the
/// per-over/per-phase resolution machinery BowlingApproach needs would be unused weight here.
/// A single record with named factory presets covers the same ground with less to maintain.
/// </summary>
public sealed record BatterInstruction(
    Guid PlayerId,
    BattingIntent? Intent = null,
    /// <summary>Target a specific bowler for attack, e.g. take down the part-timer. Null for no preference.</summary>
    Guid? TargetBowlerId = null,
    /// <summary>See off a specific bowler - block out the spell of the one man who is a threat.</summary>
    Guid? RespectBowlerId = null,
    /// <summary>Look to score here preferentially. This genuinely manipulates the field: a zone the batter now favours makes the fielding captain's coverage of it matter more, and an open zone he attacks pays off more - see InningsSimulator.ApplyZonePreferences.</summary>
    ShotZone? TargetZone = null,
    /// <summary>Avoid this zone - a strong fielder is posted there, or it is a known weak zone the coach wants managed rather than tested.</summary>
    ShotZone? AvoidZone = null,
    /// <summary>Look to attack whichever boundary dimension is actually shorter at this ground.</summary>
    bool TargetShorterBoundary = false,
    /// <summary>Attack this TYPE of bowling specifically ("go after the spinners") - a broader tactical read than naming one bowler.</summary>
    BowlerType? TargetBowlerType = null,
    /// <summary>Rebuild rather than push on for a short window after a new partnership begins, instead of coming out swinging straight after losing a partner.</summary>
    bool RebuildAfterWicket = false)
{
    /// <summary>Attack a specific bowler harder than the standing plan otherwise would.</summary>
    public static BatterInstruction CounterAttack(Guid playerId, Guid targetBowlerId) =>
        new(playerId, BattingIntent.Attacking, TargetBowlerId: targetBowlerId);

    /// <summary>Look to work the gap in a specific zone - the batter's own version of "manipulate the field".</summary>
    public static BatterInstruction TargetTheGap(Guid playerId, ShotZone zone) =>
        new(playerId, TargetZone: zone);

    /// <summary>Steer away from a zone - a strong fielder, or a shot the coach doesn't trust yet.</summary>
    public static BatterInstruction AvoidTheTrap(Guid playerId, ShotZone zone) =>
        new(playerId, AvoidZone: zone);

    /// <summary>Favour whichever boundary is genuinely shorter at this ground.</summary>
    public static BatterInstruction AttackTheShortSide(Guid playerId) =>
        new(playerId, TargetShorterBoundary: true);

    public static BatterInstruction AttackPace(Guid playerId) =>
        new(playerId, TargetBowlerType: BowlerType.Pace);

    public static BatterInstruction AttackSpin(Guid playerId) =>
        new(playerId, TargetBowlerType: BowlerType.Spin);

    /// <summary>Preserve the wicket against the one bowler who is a genuine threat, rather than looking to score off him.</summary>
    public static BatterInstruction PreserveWicket(Guid playerId, Guid respectBowlerId) =>
        new(playerId, BattingIntent.Blocking, RespectBowlerId: respectBowlerId);

    /// <summary>
    /// Steady the innings after a wicket rather than pushing straight on - rebuild the
    /// partnership first. Deliberately does NOT set Intent = Anchoring here: that would make
    /// it a STANDING override (TacticalPlan.ResolveBattingIntent checks instruction.Intent
    /// before anything else), permanently overriding the AI's own reading even once the
    /// partnership is long established - exactly the bug a test caught before this shipped.
    /// Leaving Intent null lets RebuildAfterWicket's windowed softening in
    /// InningsSimulator.ResolveBattingIntent do the actual work, and only for as long as the
    /// window says it should.
    /// </summary>
    public static BatterInstruction RotateAndRebuild(Guid playerId) =>
        new(playerId, RebuildAfterWicket: true);
}

/// <summary>A standing instruction for one bowler.</summary>
public sealed record BowlerInstruction(
    Guid PlayerId,
    BowlingIntent? Intent = null,
    /// <summary>Hold this bowler back for a specific phase - the classic "save an over for the death".</summary>
    MatchPhase? ReserveForPhase = null,
    /// <summary>Bring this bowler on against a specific batter - matchup bowling.</summary>
    Guid? MatchupAgainstBatterId = null);

/// <summary>
/// The coach's tactical plan. Everything here is an OVERRIDE: anything left null falls through to
/// the AI's own reading of the situation, so a coach can set as much or as little as he wants and
/// a plan is never a requirement to micromanage.
///
/// This is the difference between watching a simulation and managing a team. Sections 20 and 21
/// list the tactical options; the machinery to act on them (batting intent, bowling intent, field
/// settings, bowler rotation) has existed since the earlier slices and was driven entirely by the
/// AI. This is the layer that lets the coach take the wheel - and, importantly, lets him be WRONG:
/// blocking with 40 needed off 24 will lose the game, and the simulation will let it happen.
/// </summary>
public sealed class TacticalPlan
{
    // ---- batting ----

    /// <summary>Overrides the derived intent for every batter. A single instruction for the whole innings.</summary>
    public BattingIntent? TeamBattingIntent { get; set; }

    /// <summary>Per-phase batting intent - "see off the new ball, then go" as an actual instruction.</summary>
    public Dictionary<MatchPhase, BattingIntent> BattingIntentByPhase { get; set; } = new();

    /// <summary>Instructions for individual batters, which beat both of the above for that player.</summary>
    public List<BatterInstruction> BatterInstructions { get; set; } = new();

    /// <summary>
    /// Protect the lower order: shield a specified number of tail-enders by having the set batter
    /// keep the strike where possible. A real instruction with a real cost - farming the strike
    /// means turning down singles.
    /// </summary>
    public int ProtectLowerOrderCount { get; set; }

    // ---- bowling and fielding ----

    public BowlingIntent? TeamBowlingIntent { get; set; }
    public Dictionary<MatchPhase, BowlingIntent> BowlingIntentByPhase { get; set; } = new();
    public List<BowlerInstruction> BowlerInstructions { get; set; } = new();

    /// <summary>
    /// Bowling plans, in the coach's own language. Resolution mirrors the batting side:
    /// per bowler-vs-batter, then per bowler, then per phase, then side-wide.
    /// </summary>
    public BowlingApproach? TeamBowlingApproach { get; set; }
    public Dictionary<MatchPhase, BowlingApproach> BowlingApproachByPhase { get; set; } = new();
    public Dictionary<Guid, BowlingApproach> BowlingApproachByBowler { get; set; } = new();

    /// <summary>
    /// A specific plan for a specific bowler against a specific batter - the most granular
    /// instruction a coach gives, and the one an analyst's report actually produces:
    /// "when the left-armer is on to their number three, go round the wicket and full."
    /// Keyed (bowlerId, batterId).
    /// </summary>
    public Dictionary<(Guid BowlerId, Guid BatterId), BowlingApproach> MatchupApproaches { get; set; } = new();

    /// <summary>
    /// Over-by-over plans, keyed by over number. Deliberately supported but NOT the intended
    /// primary interface: a coach who wants to script the eighteenth over may, but nobody should
    /// have to plan twenty overs individually to play a match.
    /// </summary>
    public Dictionary<int, BowlingApproach> BowlingApproachByOver { get; set; } = new();

    public FieldAggression? FieldAggression { get; set; }
    public Dictionary<MatchPhase, FieldAggression> FieldAggressionByPhase { get; set; } = new();

    /// <summary>
    /// Force a specific bowler for the next over, overriding rotation. Cleared once used, because
    /// it is a single in-match decision rather than a standing plan - the coach saying "him, now".
    /// </summary>
    public Guid? NextOverBowlerId { get; set; }

    /// <summary>Bowlers the coach has explicitly taken out of the rotation for this innings (an injury doubt, or simply being milked).</summary>
    public HashSet<Guid> WithheldBowlers { get; set; } = new();

    /// <summary>
    /// A field the coach has placed himself, fielder by fielder. When set, this replaces the AI's
    /// field entirely - the coach decides who stands where, which is the level of control the
    /// design asks for.
    ///
    /// It is still validated against the laws: an illegal field is rejected and the AI's field is
    /// used instead, because an umpire would not permit it either. Use
    /// FieldSettingRules.Validate to check a field before committing to it.
    /// </summary>
    public FieldSetting? ManualField { get; set; }

    /// <summary>Manual fields for specific phases - "this is my death field" without having to place it again every over.</summary>
    public Dictionary<MatchPhase, FieldSetting> ManualFieldByPhase { get; set; } = new();

    public FieldSetting? ResolveManualField(MatchPhase phase) =>
        ManualFieldByPhase.TryGetValue(phase, out var phaseField) ? phaseField : ManualField;

    /// <summary>
    /// What the coach wants at the toss: true to bat, false to bowl, null to leave it to the
    /// captain's own reading of the conditions. Under CoachHasFinalSay this is what happens.
    /// </summary>
    public bool? TossDecision { get; set; }

    // ---- authority ----

    /// <summary>
    /// Who has the final say, by default, for this side.
    ///
    /// A HUMAN coach defaults to CoachHasFinalSay: this is a management game, and an instruction he
    /// gives is carried out. His captain and bowlers still tell him what they want - those arrive as
    /// MatchSuggestions he can act on between overs - but they do not overrule him.
    ///
    /// An AI-coached side defaults to Delegated, because its captain genuinely is making his own
    /// calls out in the middle and there is no human to consult.
    /// </summary>
    public DecisionAuthority DefaultAuthority { get; set; } = DecisionAuthority.Delegated;

    /// <summary>Authority for specific decisions - a coach can keep the field and hand over the bowling changes, or the reverse.</summary>
    public Dictionary<Services.InMatchDecision, DecisionAuthority> AuthorityByDecision { get; set; } = new();

    /// <summary>
    /// Players the coach has explicitly told to do as they see fit. "Bowl how you like" to a senior
    /// bowler is a real and common instruction, and it is the coach's choice to give it - which is
    /// the difference between delegating and being overruled.
    /// </summary>
    public HashSet<Guid> DelegatedPlayers { get; set; } = new();

    public DecisionAuthority ResolveAuthority(Services.InMatchDecision decision, Guid? playerId = null)
    {
        if (playerId is { } id && DelegatedPlayers.Contains(id)) return DecisionAuthority.Delegated;
        if (AuthorityByDecision.TryGetValue(decision, out var specific)) return specific;
        return DefaultAuthority;
    }

    /// <summary>A plan for a human coach: his word is final everywhere until he chooses otherwise.</summary>
    public static TacticalPlan ForHumanCoach() => new() { DefaultAuthority = DecisionAuthority.CoachHasFinalSay };

    // ---- resolution ----

    /// <summary>
    /// The intent for this batter, most specific instruction first: the player's own instruction,
    /// then the phase plan, then the whole-innings plan, then null to let the AI decide.
    /// </summary>
    public BattingIntent? ResolveBattingIntent(Guid batterId, MatchPhase phase)
    {
        var instruction = BatterInstructions.FirstOrDefault(i => i.PlayerId == batterId);
        if (instruction?.Intent is { } specific) return specific;
        if (BattingIntentByPhase.TryGetValue(phase, out var phaseIntent)) return phaseIntent;
        return TeamBattingIntent;
    }

    public BowlingIntent? ResolveBowlingIntent(Guid bowlerId, MatchPhase phase)
    {
        var instruction = BowlerInstructions.FirstOrDefault(i => i.PlayerId == bowlerId);
        if (instruction?.Intent is { } specific) return specific;
        if (BowlingIntentByPhase.TryGetValue(phase, out var phaseIntent)) return phaseIntent;
        return TeamBowlingIntent;
    }

    public FieldAggression? ResolveFieldAggression(MatchPhase phase)
    {
        if (FieldAggressionByPhase.TryGetValue(phase, out var phaseSetting)) return phaseSetting;
        return FieldAggression;
    }

    public BatterInstruction? InstructionFor(Guid batterId) =>
        BatterInstructions.FirstOrDefault(i => i.PlayerId == batterId);

    public BowlerInstruction? BowlerInstructionFor(Guid bowlerId) =>
        BowlerInstructions.FirstOrDefault(i => i.PlayerId == bowlerId);

    /// <summary>
    /// The plan for this bowler against this batter in this over, most specific first: the explicit
    /// matchup, then this over, then this bowler, then this phase, then the side-wide plan, then
    /// null so the AI picks something sensible for the conditions.
    /// </summary>
    public BowlingApproach? ResolveBowlingApproach(Guid bowlerId, Guid batterId, MatchPhase phase, int overNumber)
    {
        if (MatchupApproaches.TryGetValue((bowlerId, batterId), out var matchup)) return matchup;
        if (BowlingApproachByOver.TryGetValue(overNumber, out var overPlan)) return overPlan;
        if (BowlingApproachByBowler.TryGetValue(bowlerId, out var bowlerPlan)) return bowlerPlan;
        if (BowlingApproachByPhase.TryGetValue(phase, out var phasePlan)) return phasePlan;
        return TeamBowlingApproach;
    }

    /// <summary>An empty plan - everything falls through to the AI. This is what a match with no coach input uses, so existing behaviour is unchanged.</summary>
    public static TacticalPlan None => new();
}
