using CricketManager.Domain.Entities;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 4: the captain's own learning cadence.
///
/// The per-role growth-cadence decision in CLAUDE.md is explicit that a captain is NOT a coach:
/// he makes dozens of live, personal, attributable calls every single match, so the learning
/// SIGNAL is match-grain - unlike a head coach, whose influence is a whole campaign's accumulated
/// results and whose growth is therefore milestone-driven. But a slow-moving attribute like
/// Leadership must not jump per match, so the signal is banked on CaptaincyProfile per match
/// (RecordCaptaincyOutcomes) and CRYSTALLISED here on a monthly cadence.
///
/// Bounded, slow, and saturating on matches captained, exactly as the brief requires: "learning
/// must be slow, bounded by the person's own attributes, and never escape a ceiling."
/// </summary>
public sealed class CaptaincyGrowthService
{
    /// <summary>
    /// Reads the accumulated per-match decision-quality signal, moves Leadership / DecisionMaking
    /// a little (or, for a run of genuinely poor calls, erodes DecisionMaking a little - never
    /// Leadership, which is the dressing room's job and moves via RecordMatch), then resets the
    /// accumulator. A no-op when nothing has been banked since the last call.
    /// </summary>
    public void Crystallise(Player captain, CaptaincyProfile profile, Random random)
    {
        if (profile.AccumulatedMatches == 0) return;

        double avgQuality = profile.DecisionQualityAccumulator / profile.AccumulatedMatches; // roughly -70..70
        double evidence = Math.Clamp(profile.AccumulatedMatches / 4.0, 0.2, 1.5);
        profile.ResetAccumulator();

        // Saturation - the same shape EffectiveLeadership's own experience term uses. A captain
        // 100 matches into the job barely moves; a new one moves fastest.
        double room = Math.Exp(-Math.Max(0, profile.MatchesCaptained) / 40.0);

        double signal = Math.Clamp(avgQuality / 70.0, -1, 1);

        double growthChance = Math.Clamp(0.20 * room * Math.Max(0, signal) * evidence, 0, 0.45);
        double declineChance = Math.Clamp(0.10 * -Math.Min(0, signal) * evidence, 0, 0.18);

        if (random.NextDouble() < growthChance)
            captain.Mental.DecisionMaking = Math.Min(20, captain.Mental.DecisionMaking + 1);
        if (random.NextDouble() < growthChance * 0.7)
            captain.Mental.Leadership = Math.Min(20, captain.Mental.Leadership + 1);
        if (random.NextDouble() < declineChance)
            captain.Mental.DecisionMaking = Math.Max(1, captain.Mental.DecisionMaking - 1);
    }
}
