using CricketManager.Domain.Entities;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 14 (§18.6): confidence CONTAGION at the individual level. A player in a squad riding a
/// genuine high gets a small confidence lift even without personal runs; a player in a squad in
/// crisis is dragged down further than his own form warrants. This is on TOP of the team-morale
/// machinery - it is specifically the individual catching the mood.
///
/// Runs monthly, bounded and small - a squad on a high is worth a point or two of a struggler's
/// confidence, never a transformation.
/// </summary>
public sealed class ConfidenceContagionService
{
    public void ReviewMonthly(WorldState world, DateOnly date)
    {
        foreach (var team in world.Teams.Values)
        {
            // The squad mood: team form momentum (a trajectory) plus dressing-room harmony.
            double mood = team.FormMomentum * 0.6 + (team.DressingRoomHarmony - 50) * 0.8;
            if (Math.Abs(mood) < 20) continue;

            double lift = Math.Clamp(mood / 100.0, -1, 1) * 2.5;

            foreach (var p in world.Players)
            {
                if (p.CurrentTeamId != team.Id || p.IsRetired || p.AcademyTeamId is not null) continue;
                if (p.NonInjuryUnavailability != Enums.UnavailabilityReason.Available) continue;

                // A struggler catches a good mood more than a man already flying; a good mood
                // helps more than a bad one hurts a settled player.
                double personalForm = p.Form.CurrentForm;
                double receptiveness = lift > 0
                    ? Math.Clamp((50 - personalForm) / 50.0 + 0.4, 0.2, 1.2)
                    : Math.Clamp((personalForm - 20) / 60.0 + 0.3, 0.2, 1.0);

                p.Form.AdjustConfidence(lift * receptiveness);
            }
        }
    }
}
