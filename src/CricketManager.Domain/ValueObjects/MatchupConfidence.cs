using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// One player's confidence against ONE specific context (a team, a ground, or a
/// specific bowler). Same rolling-weighted-average shape as FormState, but tracked
/// per matchup key instead of globally - a player can be shaky in general but have
/// a strong record specifically against, say, a particular bowler.
///
/// Explicitly NOT permanent: DecayTowardNeutral() must be called periodically (e.g.
/// once per season) so a good/bad matchup record fades if it isn't reinforced -
/// matches the requirement that these edges "can change, not always perform the same."
/// </summary>
public sealed class MatchupConfidence
{
    [JsonInclude] public double Confidence { get; private set; } // -100..100, 0 = neutral/no data
    [JsonInclude] public int SampleCount { get; private set; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 3: a SECOND, faster-blending average of the same
    /// outcomes - the "recent" half of the recent-weighted / slower-career blend the source
    /// document asks for (point 7 for matchups, point 8 for partnership chemistry - the same
    /// shape, so it lives here once rather than being reinvented in two services). Confidence is
    /// the slow career picture; RecentConfidence catches a matchup that has genuinely turned
    /// around lately before the career average has caught up. Consumers read BlendedConfidence.
    /// Old saves deserialize this to 0 and it self-corrects on the next recorded outcome.
    /// </summary>
    [JsonInclude] public double RecentConfidence { get; private set; }

    /// <summary>The number consumers should read: the slow career average, weighted with the fast recent one.</summary>
    [JsonIgnore] public double BlendedConfidence => SampleCount == 0 ? 0 : Confidence * 0.6 + RecentConfidence * 0.4;

    /// <summary>
    /// Post-Phase-6 carry-forward: the same blend, but with the recent half weighted by
    /// <paramref name="recentWeight"/> (0..1) instead of the fixed 0.4. A fragile, inconsistent
    /// player lives closer to his last few outings against an opponent (higher recentWeight); a
    /// mentally tough, consistent one leans on the longer record (lower recentWeight). The caller
    /// (MatchupConfidenceService) derives recentWeight from personality.
    /// </summary>
    public double BlendedConfidenceWeighted(double recentWeight)
    {
        if (SampleCount == 0) return 0;
        recentWeight = Math.Clamp(recentWeight, 0, 1);
        return Confidence * (1 - recentWeight) + RecentConfidence * recentWeight;
    }

    public void RecordOutcome(double performanceRating)
    {
        // Weighted rolling average, more weight to recent outcomes - same principle as FormState,
        // but with a slower blend rate so one match doesn't swing a matchup read too hard.
        double blendRate = SampleCount == 0 ? 1.0 : Math.Max(0.15, 1.0 / (SampleCount + 1));
        Confidence = Math.Clamp(Confidence + (performanceRating - Confidence) * blendRate, -100, 100);

        // The recent average blends far faster - it never settles, it always reflects roughly the
        // last handful of outcomes.
        double recentRate = SampleCount == 0 ? 1.0 : 0.4;
        RecentConfidence = Math.Clamp(RecentConfidence + (performanceRating - RecentConfidence) * recentRate, -100, 100);

        SampleCount++;
    }

    /// <summary>Call periodically (e.g. per season) so matchup edges aren't permanent. Wave 3: decays the fast recent average alongside the slow one.</summary>
    public void DecayTowardNeutral(double amount = 5.0)
    {
        Confidence = DecayOne(Confidence, amount);
        RecentConfidence = DecayOne(RecentConfidence, amount);
    }

    private static double DecayOne(double value, double amount)
    {
        if (value > 0) return Math.Max(0, value - amount);
        if (value < 0) return Math.Min(0, value + amount);
        return value;
    }
}
