using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>Par score plus how confident we are in it. Confidence matters: a coach reading "par is 165 here" should know whether that came from 40 matches or from the pitch report alone.</summary>
public sealed record ParScoreEstimate(int ParScore, int InningsSampled, bool FromHistoricalData);

/// <summary>
/// Turns a ground into the two things a coach actually asks before a match: what is a
/// par score here, and what will the pitch do.
///
/// Historical data wins when there is enough of it; below the threshold it falls back to
/// the pitch characteristics, and says which one it used. It deliberately does NOT quietly
/// average one innings and present that as the ground's par score - a single 90 all out
/// would then read as "this is a 90-run ground" forever.
/// </summary>
public sealed class GroundConditionsService
{
    /// <summary>Below this many innings the historical average is too noisy to trust over the pitch report.</summary>
    public const int MinimumInningsForHistoricalPar = 5;

    private readonly GroundRecordsService _records = new();

    public ParScoreEstimate EstimateParScore(Ground ground, MatchFormat format, IEnumerable<TeamInningsRecord> allInnings)
    {
        var relevant = allInnings.Where(i => i.GroundId == ground.Id && i.Format == format && i.InningsNumber == 1).ToList();

        if (relevant.Count >= MinimumInningsForHistoricalPar)
        {
            double historical = relevant.Average(i => (double)i.Runs);
            return new ParScoreEstimate((int)Math.Round(historical), relevant.Count, FromHistoricalData: true);
        }

        // Fallback: format baseline shifted by how batting-friendly the surface is, plus the
        // boundary/outfield profile. Neutral (battingFriendliness 50, standard boundaries)
        // returns the format baseline unchanged.
        int baseline = format switch
        {
            MatchFormat.T20 => 165,
            MatchFormat.ODI => 265,
            MatchFormat.Test => 340,
            _ => 200
        };

        double pitchFactor = 0.75 + Math.Clamp(ground.PitchBattingFriendliness, 0, 100) / 100.0 * 0.5;  // 0.75x - 1.25x
        double boundaryFactor = 1 + (65 - Math.Clamp(ground.SquareBoundaryMetres, 45, 90)) / 100.0;     // short boundary -> higher scores
        double outfieldFactor = 0.95 + Math.Clamp(ground.OutfieldSpeed, 0, 100) / 100.0 * 0.1;

        int estimate = (int)Math.Round(baseline * pitchFactor * boundaryFactor * outfieldFactor);
        return new ParScoreEstimate(estimate, relevant.Count, FromHistoricalData: false);
    }

    /// <summary>
    /// Whether this ground has historically favoured the side batting first. Null when no
    /// match here has produced a result yet - the honest answer, rather than 50/50.
    /// </summary>
    public double? GetBattingFirstAdvantage(Guid groundId, IEnumerable<TeamInningsRecord> allInnings, MatchFormat? format = null) =>
        _records.GetResultSplit(allInnings, groundId, format).BattingFirstWinPercentage;

    /// <summary>
    /// Expected spin assistance for a given day of a match. Multi-day pitches deteriorate:
    /// a Test surface that starts at 40 turn is a different pitch on day five. Limited-overs
    /// matches are single-day, so dayOfMatch is ignored for them.
    /// Dew is subtracted for evening play because a wet ball grips less - this is why chasing
    /// under lights is easier at dew-prone venues.
    ///
    /// The day-over-day gain scales with Ground.PitchWearRate - a real per-ground property, not
    /// the single global slope every ground used to share regardless of how it actually plays.
    /// A wear rate of 50 (the field's default) reproduces the original fixed +7.5/day exactly.
    /// </summary>
    public double EstimateSpinAssistance(Ground ground, MatchFormat format, int dayOfMatch = 1, bool eveningSession = false)
    {
        double spin = Math.Clamp(ground.PitchSpinRating, 0, 100);
        double wearScale = Math.Clamp(ground.PitchWearRate, 0, 100) / 50.0;

        if (format == MatchFormat.Test)
        {
            int day = Math.Clamp(dayOfMatch, 1, 5);
            spin += (day - 1) * 7.5 * wearScale; // day 5 surface offers substantially more than day 1
        }

        if (eveningSession && ground.HasFloodlights)
            spin -= Math.Clamp(ground.DewTendency, 0, 100) * 0.25;

        return Math.Clamp(spin, 0, 100);
    }
}
