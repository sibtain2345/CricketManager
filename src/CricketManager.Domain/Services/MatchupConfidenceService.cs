using CricketManager.Domain.Entities;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

public static class MatchupKey
{
    public static string ForOpponent(Guid teamId) => $"opp:{teamId}";
    // Keyed on GroundId, not the ground's name. A name key silently split one venue's history
    // in two the moment a ground was renamed, and merged two different venues that happened to
    // share a name in different countries. The name overload is kept only for records that
    // predate a Ground entity (imported historical data).
    public static string ForGround(Guid groundId) => $"ground:{groundId}";
    public static string ForGroundName(string ground) => $"groundname:{ground.Trim().ToLowerInvariant()}";
    public static string ForBowler(Guid bowlerPlayerId) => $"bowler:{bowlerPlayerId}";
    // Section Q/W: a specific batting partner, not an opponent - reuses the same
    // MatchupConfidence rolling-average/decay machinery PartnershipChemistryService drives.
    public static string ForPartner(Guid partnerPlayerId) => $"partner:{partnerPlayerId}";
    // Section Q/W follow-up: the bowler who shared the new ball with this one, this innings -
    // reuses the same rolling-average/decay machinery as ForPartner, for the bowling side of the
    // same idea. See BowlingPairSynergyService.
    public static string ForBowlingPartner(Guid partnerBowlerId) => $"bowlpartner:{partnerBowlerId}";
    // Section D follow-up: a coarse recurring match SITUATION (format/phase/chase-or-defend/
    // pressure tier) rather than an opponent - lets a captain's confidence read "have I actually
    // been here before" separately from his general tactical judgement. See CaptaincyService.
    public static string ForSituation(string situationKey) => $"situation:{situationKey}";
}

/// <summary>
/// Section 22 + follow-up requirement: a player who has repeatedly performed well
/// against a specific opponent/ground/bowler should play with more confidence there
/// - and vice versa - but this must be able to change over time, not be a fixed label.
///
/// This service owns reading/writing Player.Matchups. It does NOT decide match outcomes
/// by itself - it produces a modest multiplier that PerformanceOutcomeSimulator blends
/// with situational trait fit and randomness, so a strong matchup record skews the odds
/// without guaranteeing anything.
/// </summary>
public sealed class MatchupConfidenceService
{
    /// <summary>Records one outcome against a given matchup key (opponent/ground/bowler).</summary>
    public void RecordOutcome(Player player, string matchupKey, double performanceRating)
    {
        if (!player.Matchups.TryGetValue(matchupKey, out var confidence))
        {
            confidence = new MatchupConfidence();
            player.Matchups[matchupKey] = confidence;
        }
        confidence.RecordOutcome(performanceRating);
    }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 3: below this many recorded outcomes a matchup has NO
    /// effect at all - it returns exactly 1.0, not a heavily-damped almost-1.0. One or two
    /// head-to-heads is not a "matchup", it is a coincidence, and the source document asks for a
    /// real threshold rather than a curve that technically starts biting from the first ball.
    /// </summary>
    public const int MinimumSampleForEffect = 3;

    /// <summary>
    /// Multiplier for a specific matchup key: ~0.85 (strong negative history) to ~1.15
    /// (strong positive history). No record yet, or fewer than MinimumSampleForEffect outcomes,
    /// stays at exactly 1.0 - confidence has to be genuinely earned before it swings anything.
    /// </summary>
    /// <param name="pressureLevel">
    /// Wave 3 (point 7): 0-100, 50 neutral (every pre-existing call site's default). A known
    /// matchup edge bites HARDER when it matters - a batter with a genuine problem against a
    /// bowler feels it more in a World Cup knockout than in a dead rubber - so the swing is
    /// amplified up to ~1.4x at maximum pressure. Below-neutral pressure never shrinks it.
    /// </param>
    public double GetMatchupMultiplier(Player player, string matchupKey, double pressureLevel = 50)
    {
        if (!player.Matchups.TryGetValue(matchupKey, out var confidence) || confidence.SampleCount < MinimumSampleForEffect)
            return 1.0;

        // Low-sample matchups are damped, same "don't overreact to one data point" principle as elsewhere.
        double sampleWeight = Math.Clamp(confidence.SampleCount / 6.0, 0.2, 1.0);

        // Wave 3 + Post-Phase-6 carry-forward: read the recent-weighted / slower-career blend, with
        // the recent half's weight set by TEMPERAMENT. An Inconsistent or fragile player lives
        // close to his last few outings against this opponent; a Consistent, mentally tough one
        // leans on the longer record and shrugs off a recent bad day.
        double swing = (confidence.BlendedConfidenceWeighted(RecentWeightFor(player)) / 100.0) * 0.15 * sampleWeight;

        double pressureAmplification = 1 + Math.Clamp((pressureLevel - 50) / 50.0, 0, 1) * 0.4;
        swing *= pressureAmplification;

        return Math.Clamp(1.0 + swing, 0.80, 1.20);
    }

    /// <summary>How much a player weights his RECENT matchup history vs the long record - 0.4 neutral, higher for a volatile temperament, lower for a settled one. Composes with the pressure factor in GetMatchupMultiplier, it does not replace it.</summary>
    private static double RecentWeightFor(Player player)
    {
        double w = 0.40;
        var pers = player.Personality;
        if (pers.HasFlag(Enums.PersonalityTrait.Inconsistent)) w += 0.20;
        if (pers.HasFlag(Enums.PersonalityTrait.Consistent)) w -= 0.15;
        if (pers.HasFlag(Enums.PersonalityTrait.PressurePlayer) || pers.HasFlag(Enums.PersonalityTrait.BigMatchPlayer)) w -= 0.05;
        // A genuinely brittle head (low PressureHandling) also lives in the recent moment.
        if (Common.AbilityScale.AttributeToHundred(player.Mental.PressureHandling) < 40) w += 0.08;
        return Math.Clamp(w, 0.15, 0.75);
    }

    /// <summary>Combined multiplier across several matchup dimensions (e.g. opponent AND this specific bowler).</summary>
    public double GetCombinedMultiplier(Player player, params string[] matchupKeys)
    {
        double product = matchupKeys.Aggregate(1.0, (acc, key) => acc * GetMatchupMultiplier(player, key));
        return Math.Clamp(product, 0.75, 1.25);
    }

    /// <summary>
    /// Call periodically so matchup edges fade if not reinforced.
    /// </summary>
    /// <param name="decayPartnerKeys">
    /// Wave 3 (point 8): batting-partnership chemistry (partner: keys) decays on its OWN, faster
    /// rules via PartnershipChemistryService.DecayPartnerships - so the monthly tick passes false
    /// here to leave those keys alone and hand them to that service instead. Every other caller
    /// keeps the default (decay everything).
    /// </param>
    public void DecayAll(Player player, double amount = 5.0, bool decayPartnerKeys = true)
    {
        foreach (var (key, confidence) in player.Matchups)
        {
            if (!decayPartnerKeys && key.StartsWith("partner:", StringComparison.Ordinal)) continue;
            confidence.DecayTowardNeutral(amount);
        }
    }
}
