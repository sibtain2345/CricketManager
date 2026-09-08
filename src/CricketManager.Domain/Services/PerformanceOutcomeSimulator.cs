namespace CricketManager.Domain.Services;

/// <summary>
/// Section 19 (controlled randomness) applied at the individual-performance level:
/// situational/trait/matchup multipliers from SituationalPerformanceModifier and
/// MatchupConfidenceService only skew the MEAN of a probability distribution - they
/// never produce a deterministic outcome. A Power Hitter in the death overs gets a
/// better average outcome over many innings, but any single innings can still fail.
///
/// This deliberately sits between the trait/situation system and the future full
/// ball-by-ball match engine (Phase 4) - it's the "one performance instance" sampler
/// that engine will call per innings/spell once it exists.
/// </summary>
public sealed class PerformanceOutcomeSimulator
{
    private readonly Random _random;

    public PerformanceOutcomeSimulator(Random? random = null) => _random = random ?? new Random();

    /// <summary>
    /// Samples a single performance outcome (-100..100).
    /// baseRating: the player's pure skill expectation for this context (e.g. from
    /// PlayerSelectionEvaluator/attributes), BEFORE any situational skew.
    /// combinedMultiplier: situational * trait * matchup multiplier, typically 0.7-1.3.
    /// Noise is deliberately larger than the multiplier's own swing, so multiplier
    /// shifts the odds without dominating any single instance - a trait advantage is
    /// a skew, not a guarantee.
    /// </summary>
    public double SampleOutcome(double baseRating, double combinedMultiplier)
    {
        double skewedMean = Math.Clamp(baseRating * combinedMultiplier, -100, 100);

        // Sum of 3 uniforms approximates a bounded bell curve (Irwin-Hall), cheap and
        // dependency-free. Centered at 0, spread wide enough that even a favorable
        // matchup can still produce a below-average or poor outcome.
        double u = (_random.NextDouble() + _random.NextDouble() + _random.NextDouble()) / 3.0 - 0.5; // -0.5..0.5, bell-shaped
        double noise = u * 2 * 60; // scale to roughly +/-60 - wide enough that even a strong favorite can still fail on the day

        return Math.Clamp(skewedMean + noise, -100, 100);
    }

    /// <summary>Runs many samples - useful for AI expected-value calculations and for testing distribution shape.</summary>
    public IReadOnlyList<double> SampleMany(double baseRating, double combinedMultiplier, int count)
    {
        var results = new List<double>(count);
        for (int i = 0; i < count; i++)
            results.Add(SampleOutcome(baseRating, combinedMultiplier));
        return results;
    }
}
