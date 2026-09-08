using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 14 (§6.5): skill REGRESSION from a narrow diet of cricket. A player who plays only T20
/// for years genuinely loses a little red-ball technique - the tight defence, the concentration,
/// the leave. It is small, gradual and reversible (a season of first-class cricket brings it
/// back), and it is why a white-ball specialist coming back into a Test side is not the player
/// he was.
///
/// Runs annually, off <see cref="WorldState.MatchesThisSeasonByFormat"/>. Nudges the underlying
/// attributes (which then flow into FormatSuitability), never the suitability directly.
/// </summary>
public sealed class SkillRegressionService
{
    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date, Random? random = null)
    {
        var events = new List<GameEvent>();
        var deriver = new RoleTraitDeriver();
        random ??= new Random(date.Year * 911);

        foreach (var p in world.Players)
        {
            if (p.IsRetired || p.AcademyTeamId is not null) continue;

            // (S6: the nationality-switch that used to live here is now RepresentationDriftService -
            // a fuller model with the ICC 3-year stand-down, heritage routes, personality-shaped
            // outcomes and the reversible Rankin case. It runs from ProcessAnnualRollover alongside
            // this service.)
            if (!world.MatchesThisSeasonByFormat.TryGetValue(p.Id, out var byFormat) || byFormat.Count == 0) continue;

            int t20 = byFormat.GetValueOrDefault(MatchFormat.T20);
            int redBall = byFormat.GetValueOrDefault(MatchFormat.Test);
            int odi = byFormat.GetValueOrDefault(MatchFormat.ODI);
            int total = t20 + redBall + odi;

            // §6.8 / §6.4: BURNOUT as a real, tracked workload axis - not just for teenagers. A
            // heavy season (Tests weighted x3 for the toll) raises injury-proneness a notch, and a
            // genuinely brutal one costs a yard of the physical peak too. A young quick is the most
            // fragile (the classic case), but a 30-year-old bowled into the ground pays as well.
            double workload = redBall * 3 + odi + t20;
            bool quick = p.BowlingRole != BowlingRoleType.NotABowler && p.Bowling.Pace >= 12;
            int young = p.Age(date);
            double burnoutThreshold = quick ? (young <= 21 ? 20 : 30) : 42;

            // Follow-up pass (§12.5/§6.8): a board's real medical/rotation investment (facility
            // quality + a hired physio/sports scientist - the same TeamMedicalQuality
            // MatchRecorder already reads for match-day injury risk) genuinely raises how much
            // workload a player can carry before burnout bites - the strategic EDGE for managing
            // the cost this axis already tracks. 50 (no team on record) reproduces the ORIGINAL
            // threshold exactly.
            int teamMedical = p.CurrentTeamId is { } tmid ? MatchRecorder.TeamMedicalQuality(world, tmid) : 50;
            burnoutThreshold *= 0.8 + Math.Clamp(teamMedical, 0, 100) / 100.0 * 0.4; // 0.8x..1.2x

            if (workload >= burnoutThreshold && p.Physical.InjuryProneness < 18)
            {
                p.Physical.InjuryProneness = Math.Min(20, p.Physical.InjuryProneness + 1);
                bool brutal = workload >= burnoutThreshold * 1.6 || (quick && young <= 19);
                if (brutal && p.Physical.Stamina > 8) p.Physical.Stamina = Math.Max(1, p.Physical.Stamina - 1);
                events.Add(new GameEvent(date, GameEventType.OffFieldEvent,
                    $"{p.FullName}'s workload has caught up with him - the medical team will manage him more carefully from here.", p.Id, p.CurrentTeamId));
            }

            if (total < 6) continue;

            bool t20Only = t20 >= total * 0.85 && redBall == 0;
            bool redBallHeavy = redBall >= total * 0.6;

            if (t20Only && p.Batting.Technique > 6)
            {
                // Lose a touch of the red-ball craft.
                p.Batting.Technique = Math.Max(1, p.Batting.Technique - 1);
                p.Batting.DefensiveAbility = Math.Max(1, p.Batting.DefensiveAbility - 1);
                if (p.Mental.Concentration > 6) p.Mental.Concentration = Math.Max(1, p.Mental.Concentration - 1);
                p.RecalculateFormatSuitability();
                deriver.ApplyTo(p);
            }
            else if (redBallHeavy && total >= 10 && p.Batting.DefensiveAbility >= 12 && p.Batting.PowerHitting < 15)
            {
                // A steady diet of red-ball cricket dulls the T20 gears a fraction.
                if (p.Batting.PowerHitting > 6) p.Batting.PowerHitting = Math.Max(1, p.Batting.PowerHitting - 1);
                p.RecalculateFormatSuitability();
                deriver.ApplyTo(p);
            }
        }

        return events;
    }
}
