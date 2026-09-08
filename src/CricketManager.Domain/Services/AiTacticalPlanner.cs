using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-9 wiring pass: builds a real <see cref="TacticalPlan"/> for an AI side so the match
/// engine acts on a genuine, pre-thought plan rather than deriving everything ball by ball from the
/// situation alone. This is the seam the review's §3.1/§3.2 asked for and the thing that makes
/// <see cref="AnalystService"/> - a whole staff role, built and tested but with no in-engine caller
/// until now - actually matter in a live fixture.
///
/// Deliberately restrained in this pass (Phase 11 makes the situational half vast):
/// - a side with NEITHER a hired analyst NOR a captain who can read the game gets a null plan and
///   plays off the engine's own reads (the pre-follow-up behaviour) - a threadbare side genuinely
///   has no plan;
/// - a side WITH an analyst gets his report (quality scaled by the analyst + any
///   <see cref="SpecialistStaffService.AnalysisQualityBoost"/> from a deeper analysis department)
///   folded into the plan as matchup approaches. A weak analyst's plan is confidently wrong and
///   costs the side - <see cref="AnalystService"/> already models estimate-vs-truth;
/// - a light situational layer on top: a big occasion sharpens the bowling intent; a captain with
///   real tactical judgement sets a sensible per-phase field aggression.
///
/// The plan's authority stays <see cref="DecisionAuthority.Delegated"/> (the AI captain owns his
/// calls) - this planner never touches the human-coach path.
/// </summary>
public sealed class AiTacticalPlanner
{
    private readonly AnalystService _analyst = new();
    private readonly SpecialistStaffService _specialistStaff = new();

    public TacticalPlan? BuildPlan(
        WorldState world, Team side, IReadOnlyList<Player> ourXi,
        Team opponent, IReadOnlyList<Player> oppXi,
        Ground? ground, MatchFormat format, double importance, Random random)
    {
        var analyst = world.Staff.FirstOrDefault(s => side.StaffIds.Contains(s.Id) && s.Role is StaffRole.Analyst or StaffRole.DataAnalyst);

        var captain = side.GetCaptain(format) is { } cid ? ourXi.FirstOrDefault(p => p.Id == cid) : null;
        double judgement = CaptainJudgement(world, captain);

        // No analyst and no captain who can read a game -> no plan (the engine's own reads take over).
        if (analyst is null && judgement < 42) return null;

        var plan = new TacticalPlan(); // DefaultAuthority = Delegated - an AI plan

        var pitch = ground is not null ? PitchConditions.FromGround(ground) : PitchConditions.Neutral;
        var ourBowlers = ourXi.Where(p => p.BowlingRole != BowlingRoleType.NotABowler).ToList();
        var oppBowlers = oppXi.Where(p => p.BowlingRole != BowlingRoleType.NotABowler).ToList();
        var oppBatters = oppXi.Where(IsBatter).ToList();
        var ourBatters = ourXi.Where(IsBatter).ToList();

        if (analyst is not null || judgement >= 55)
        {
            double boost = _specialistStaff.AnalysisQualityBoost(world.Staff.Where(s => side.StaffIds.Contains(s.Id)).ToList());
            var report = _analyst.PrepareReport(analyst, ourBowlers, oppBatters, ourBatters, oppBowlers, pitch, format, random, boost);
            _analyst.ApplyToPlan(report, plan);
        }

        // Phase 11 (§3.1): the situational layer - a real plan shaped by the occasion AND by how
        // the two sides match up. A poor captain (low judgement) reads the matchup weakly, so his
        // plan is blunter; a shrewd one tailors it.
        double ourAbility = ourXi.Take(11).DefaultIfEmpty().Average(p => p?.CurrentAbility ?? 100);
        double oppAbility = oppXi.Take(11).DefaultIfEmpty().Average(p => p?.CurrentAbility ?? 100);
        double edge = (ourAbility - oppAbility) / 30.0; // roughly -3..+3
        double read = judgement / 100.0; // how much the captain actually acts on the matchup

        if (importance >= 75)
            plan.TeamBowlingIntent = BowlingIntent.Attacking; // a big occasion - go for the win

        if (format == MatchFormat.Test)
        {
            // A stronger side pressing for a win bats positively and attacks with the ball; the
            // weaker side digs in. The lean is proportional to the captain's read of the game.
            if (edge * read > 0.6)
            {
                plan.TeamBattingIntent = BattingIntent.Normal;
                plan.TeamBowlingIntent = BowlingIntent.Attacking;
            }
            else if (edge * read < -0.6)
            {
                plan.TeamBattingIntent = BattingIntent.Anchoring;
                plan.FieldAggressionByPhase[MatchPhase.MiddleOvers] = FieldAggression.Balanced;
            }
        }
        else
        {
            // White-ball: a shrewd captain shapes the innings by phase, and tilts more aggressive
            // when his side has the edge.
            if (judgement >= 55)
            {
                plan.BattingIntentByPhase[MatchPhase.Powerplay] = edge * read > 0.3 ? BattingIntent.Attacking : BattingIntent.Normal;
                plan.BattingIntentByPhase[MatchPhase.MiddleOvers] = BattingIntent.Normal;
                plan.BattingIntentByPhase[MatchPhase.DeathOvers] = BattingIntent.Attacking;
            }
            if (judgement >= 60)
            {
                plan.FieldAggressionByPhase[MatchPhase.Powerplay] = FieldAggression.Attacking;
                plan.FieldAggressionByPhase[MatchPhase.MiddleOvers] = edge * read > 0.3 ? FieldAggression.Attacking : FieldAggression.Balanced;
                plan.FieldAggressionByPhase[MatchPhase.DeathOvers] = FieldAggression.Defensive;
            }
        }

        return plan;
    }

    private static bool IsBatter(Player p) =>
        p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper or PlayerRole.BowlingAllrounder;

    private static double CaptainJudgement(WorldState world, Player? captain)
    {
        if (captain is null) return 38;
        if (world.CaptaincyProfiles.TryGetValue(captain.Id, out var prof))
            return prof.TacticalJudgement(captain);
        return AbilityScale.AttributeToHundred((captain.Mental.DecisionMaking + captain.Mental.GameAwareness + captain.Mental.Composure) / 3);
    }
}
