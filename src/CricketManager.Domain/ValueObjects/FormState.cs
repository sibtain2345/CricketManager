using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Section 9/10: Form is separate from underlying ability. It decays toward 0
/// (neutral) over time and is nudged by recent match performances, weighted by
/// opposition quality (Section 9: "500 runs vs weak opposition != 500 vs elite attack").
/// Range: -100 (terrible form) to +100 (career-best form).
/// </summary>
public sealed class FormState
{
    // [JsonInclude] fixes a real bug: System.Text.Json ignores private setters by
    // default, so a saved game would silently reset every player's Form/Confidence
    // to 0 on reload without this. Mutation still only happens through the methods
    // below - this only allows the serializer to round-trip the already-computed state.
    [JsonInclude]
    public double CurrentForm { get; private set; }
    [JsonInclude]
    public double Confidence { get; private set; } = 50; // 0-100

    [JsonInclude]
    private readonly List<double> _recentPerformanceRatings = new();
    [JsonIgnore]
    public IReadOnlyList<double> RecentPerformanceRatings => _recentPerformanceRatings;

    /// <summary>
    /// Records one match performance rating (expected: -100..100, already adjusted
    /// for opposition strength by the caller/simulation layer) and recalculates form.
    /// </summary>
    public void RecordPerformance(double oppositionAdjustedRating)
    {
        _recentPerformanceRatings.Add(oppositionAdjustedRating);
        if (_recentPerformanceRatings.Count > 10)
            _recentPerformanceRatings.RemoveAt(0);

        // Recent performances weighted more heavily than older ones.
        double weightedSum = 0, weightTotal = 0;
        for (int i = 0; i < _recentPerformanceRatings.Count; i++)
        {
            double weight = i + 1; // oldest=1 ... newest=N
            weightedSum += _recentPerformanceRatings[i] * weight;
            weightTotal += weight;
        }
        CurrentForm = weightTotal == 0 ? 0 : Math.Clamp(weightedSum / weightTotal, -100, 100);

        Confidence = Math.Clamp(Confidence + oppositionAdjustedRating * 0.1, 0, 100);
    }

    /// <summary>
    /// How many of the MOST RECENT performances, counting back from now, have all been at or
    /// below the given threshold - the "is this a short bad patch or a genuinely prolonged
    /// decline" signal PlayerSelectionEvaluator's established-player leniency and
    /// SquadManagementService's decline response both need. Stops counting at the first
    /// performance that clears the threshold, so a single good match resets the streak to
    /// zero - exactly what "he's turned it around" should mean, not something that lingers
    /// for the rest of the rolling window.
    /// </summary>
    public int ConsecutivePoorPerformances(double threshold)
    {
        int streak = 0;
        for (int i = _recentPerformanceRatings.Count - 1; i >= 0; i--)
        {
            if (_recentPerformanceRatings[i] > threshold) break;
            streak++;
        }
        return streak;
    }

    /// <summary>Natural decay toward neutral when a player hasn't played recently.</summary>
    public void DecayTowardNeutral(double amount = 2.0)
    {
        if (CurrentForm > 0) CurrentForm = Math.Max(0, CurrentForm - amount);
        else if (CurrentForm < 0) CurrentForm = Math.Min(0, CurrentForm + amount);
    }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 2/3: a direct confidence nudge outside a recorded
    /// performance. Confidence otherwise only ever moved as a side effect of RecordPerformance,
    /// which meant nothing in the game could express "a public failure dented him" or "coming
    /// back from a long injury he is short of belief" without also fabricating a fake innings.
    /// Kept as its own method (not a raw setter) so the 0-100 clamp is never bypassed.
    /// </summary>
    public void AdjustConfidence(double delta) => Confidence = Math.Clamp(Confidence + delta, 0, 100);

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 3: confidence decays toward its own neutral (50), the
    /// same shape CurrentForm decays toward 0. Before this, a player who stopped playing kept
    /// whatever confidence he last had indefinitely - a batter who made a hundred two years ago
    /// and has not batted since was still "riding high". Driven by the monthly tick, frozen while
    /// he is genuinely unavailable (see WorldClockService.ProcessMonthlyTick).
    /// </summary>
    public void DecayConfidenceTowardNeutral(double amount = 1.5)
    {
        if (Confidence > 50) Confidence = Math.Max(50, Confidence - amount);
        else if (Confidence < 50) Confidence = Math.Min(50, Confidence + amount);
    }
}
