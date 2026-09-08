namespace CricketManager.Domain.Services;

/// <summary>
/// Fixes a real gap in the original design: FormState.RecordPerformance accepted an
/// "oppositionAdjustedRating" but nothing actually computed it - opposition strength
/// was captured on Team.Strength but never used to value a performance.
///
/// Uses Team.Strength (0-100, already in the domain) as the opposition-quality signal
/// rather than introducing a separate ranking system - keeps one source of truth and
/// avoids two competing "how good is this team" numbers.
/// </summary>
public sealed class PerformanceValuationService
{
    /// <summary>
    /// Scales a raw performance rating (-100..100) by opposition strength.
    /// Weak opposition (strength ~20) still counts, just discounted (~0.76x).
    /// Elite opposition (strength ~90) is amplified (~1.32x). Never zeroes out
    /// weak-opposition performance entirely - it should count for something.
    /// </summary>
    public double AdjustForOpposition(double rawRating, double oppositionStrength)
    {
        double strength = Math.Clamp(oppositionStrength, 0, 100);
        // Multiplier range roughly 0.6 (strength=0) .. 1.4 (strength=100), centered at 1.0 for strength=50.
        double multiplier = 0.6 + (strength / 100.0) * 0.8;
        return Math.Clamp(rawRating * multiplier, -100, 100);
    }

    /// <summary>
    /// Section requirement: "one fluke innings shouldn't make an average player elite."
    /// Dampens the magnitude of a rating when the player's sample size is small, so an
    /// early career-best innings nudges form/reputation gently rather than spiking it.
    /// confidence approaches 1.0 once ~5+ performances are on record.
    /// </summary>
    public double ApplySampleConfidence(double adjustedRating, int priorSampleCount)
    {
        double confidence = Math.Clamp((priorSampleCount + 1) / 5.0, 0.3, 1.0);
        return adjustedRating * confidence;
    }

    /// <summary>
    /// Phase 3: competition prestige matters alongside opposition strength - a century in a
    /// World Cup final should count for more than the same century in a low-tier domestic
    /// cup, even against similar-strength opposition. Neutral at prestige=50 (multiplier
    /// exactly 1.0) so this is backward-compatible with any caller not yet passing prestige.
    /// Range: ~0.75x (weak domestic cup) .. ~1.25x (World Cup-tier).
    /// </summary>
    public double AdjustForPrestige(double ratingAfterOpposition, double competitionPrestige)
    {
        double prestige = Math.Clamp(competitionPrestige, 0, 100);
        double multiplier = 0.75 + (prestige / 100.0) * 0.5;
        return Math.Clamp(ratingAfterOpposition * multiplier, -100, 100);
    }

    /// <summary>Convenience: opposition + prestige + sample-confidence in one call - the normal path for recording a new performance. competitionPrestige defaults to 50 (neutral) so existing callers are unaffected.</summary>
    public double ValuePerformance(double rawRating, double oppositionStrength, int priorSampleCount, double competitionPrestige = 50)
    {
        double oppAdjusted = AdjustForOpposition(rawRating, oppositionStrength);
        double prestigeAdjusted = AdjustForPrestige(oppAdjusted, competitionPrestige);
        return ApplySampleConfidence(prestigeAdjusted, priorSampleCount);
    }
}
