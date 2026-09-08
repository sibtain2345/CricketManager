using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Planning-brief Sections Q/W: two batters who have actually batted together before should run
/// between the wickets differently than two strangers do - not a broader claim about batting
/// quality (PerformanceRecordingService already values that separately), just the narrow,
/// honest thing shared history can say: did these two specific players run well together.
///
/// Deliberately reuses Player.Matchups/MatchupConfidence (MatchupKey.ForPartner) rather than a
/// parallel relationship type - the rolling-weighted-average-with-decay shape is exactly what a
/// "how has this pairing gone lately, and does it fade if they stop batting together" record
/// needs, and it is already persisted, already decayed once a season by the same call site that
/// decays every other matchup dimension (WorldClockService's annual rollover).
///
/// Two independent consumers, deliberately opposite in sign even though they read the same
/// number: good chemistry LOWERS run-out risk (fewer mix-ups) and RAISES strike-rotation
/// comfort (more willingness to take the harder single/two) - see BallOutcomeModel.
/// </summary>
public sealed class PartnershipChemistryService
{
    private readonly MatchupConfidenceService _matchups = new();

    /// <summary>
    /// Records one closed (or unbroken) stand for both batters against each other, from the same
    /// data MatchRecorder/MultiDayMatchRecorder already build a PartnershipRecord from.
    /// </summary>
    public void RecordPartnership(Player batterA, Player batterB, int runs, int balls, MatchFormat format, bool endedInRunOut)
    {
        double rating = RatePartnership(runs, balls, format, endedInRunOut);
        _matchups.RecordOutcome(batterA, MatchupKey.ForPartner(batterB.Id), rating);
        _matchups.RecordOutcome(batterB, MatchupKey.ForPartner(batterA.Id), rating);
    }

    private static double RatePartnership(int runs, int balls, MatchFormat format, bool endedInRunOut)
    {
        double parRate = format switch { MatchFormat.Test => 3.0, MatchFormat.ODI => 5.2, _ => 8.0 };
        double actualRate = balls == 0 ? parRate : (double)runs / balls * 6;
        double rating = Math.Clamp((actualRate - parRate) / parRate, -1, 1) * 40; // tempo alone: -40..40

        // A three-ball stand at the end of an innings shouldn't swing a pairing's read as hard as
        // a settled fifty-ball one does - the same "don't overreact to one data point" discipline
        // used everywhere else in this project (matchup low-sample damping, valuation confidence).
        double sampleWeight = Math.Clamp(balls / 24.0, 0.25, 1.0);
        rating *= sampleWeight;

        // The one direct signal of an actual mix-up between these two specific players - real,
        // and worth more than tempo alone, independent of how well they'd been scoring before it.
        if (endedInRunOut) rating -= 30;

        return Math.Clamp(rating, -100, 100);
    }

    /// <summary>
    /// How safely this specific pair runs between the wickets, from their shared history - ~0.75
    /// (a real, earned risk) to ~1.28 (well below average risk). No shared history, or a thin
    /// sample, stays at neutral (1.0) - a pairing has to actually bat together more than once or
    /// twice before it says anything.
    /// </summary>
    public double GetRunOutRiskMultiplier(Player striker, Guid nonStrikerId)
    {
        if (!striker.Matchups.TryGetValue(MatchupKey.ForPartner(nonStrikerId), out var chemistry) || chemistry.SampleCount == 0)
            return 1.0;

        double sampleWeight = Math.Clamp(chemistry.SampleCount / 5.0, 0.2, 1.0);
        // Positive chemistry (a history of good tempo, no mix-ups) should LOWER risk, so the
        // multiplier moves opposite to the raw swing's sign. Wave 3: reads the recent-weighted /
        // slower-career blend, so a pairing that has genuinely tightened up (or gone stale)
        // lately is felt before the whole-career average catches up.
        double swing = (chemistry.BlendedConfidence / 100.0) * 0.28 * sampleWeight;
        return Math.Clamp(1.0 - swing, 0.75, 1.28);
    }

    /// <summary>
    /// How comfortably this pair rotates strike together - deliberately a smaller swing than the
    /// run-out effect, since this is a softer "do they trust the call" signal rather than the
    /// direct evidence a run-out is.
    /// </summary>
    public double GetStrikeRotationMultiplier(Player striker, Guid nonStrikerId)
    {
        if (!striker.Matchups.TryGetValue(MatchupKey.ForPartner(nonStrikerId), out var chemistry) || chemistry.SampleCount == 0)
            return 1.0;

        double sampleWeight = Math.Clamp(chemistry.SampleCount / 5.0, 0.2, 1.0);
        double swing = (chemistry.BlendedConfidence / 100.0) * 0.12 * sampleWeight;
        return Math.Clamp(1.0 + swing, 0.88, 1.12);
    }

    /// <summary>How fast batting-partnership chemistry fades per year without recent pairing - deliberately far faster than a general matchup's 5.0, because "we have not batted together in a while" is exactly what erodes it.</summary>
    private const double PartnershipDecayPerYear = 14.0;

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 3 (point 8): partnership chemistry decays on its own,
    /// faster, monthly cadence - NOT the general 5.0/year every other matchup dimension gets.
    ///
    /// The one real subtlety, and an explicit branch rather than a flat call: chemistry only
    /// fades when the two players HAD the chance to bat together and did not (a batting-order
    /// change, one moved up the order) - that is real chemistry going stale. It is FROZEN when
    /// one or both of them was genuinely unavailable (injured, rested, suspended, on duty) -
    /// they had no opportunity at all, and eroding a pairing over an injury nobody chose is the
    /// same unfairness point 6 makes about form decay while a player is out.
    /// </summary>
    public void DecayPartnerships(Player player, IReadOnlySet<Guid> genuinelyUnavailablePlayerIds, double periodFraction)
    {
        double amount = PartnershipDecayPerYear * Math.Clamp(periodFraction, 0, 1);
        bool playerHadNoOpportunity = genuinelyUnavailablePlayerIds.Contains(player.Id);

        foreach (var (key, chemistry) in player.Matchups)
        {
            if (!key.StartsWith("partner:", StringComparison.Ordinal)) continue;
            if (playerHadNoOpportunity) continue; // he was out - freeze, don't punish

            if (Guid.TryParse(key.AsSpan("partner:".Length), out var partnerId)
                && genuinelyUnavailablePlayerIds.Contains(partnerId))
                continue; // the partner was out - freeze

            chemistry.DecayTowardNeutral(amount);
        }
    }
}
