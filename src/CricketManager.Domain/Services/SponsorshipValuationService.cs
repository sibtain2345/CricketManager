using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Sponsorship value is NOT static - it's a function of team reputation (commercial pull),
/// the competition the team is most visibly competing in, recent form, and the commercial
/// infrastructure of the home ground (a venue with strong corporate/hospitality and media
/// facilities is worth materially more to a sponsor than one without).
///
/// Competition standing is read as BOTH prestige and reputation where available: prestige
/// is the structural importance of the tournament, reputation is its current, earned media
/// profile. A domestic league that has grown into a genuinely big draw should sell better
/// than its structural prestige alone suggests, and one in decline should sell worse.
/// </summary>
public sealed class SponsorshipValuationService
{
    private const double BaseValue = 50_000;

    /// <param name="teamReputation">0-100. Team.Reputation.Domestic - the team's commercial pull, which is deliberately NOT the same as how strong the current squad is.</param>
    /// <param name="competitionPrestige">0-100, the most prestigious competition the team currently competes in.</param>
    /// <param name="recentWinRate">
    /// 0-1, or null when the team has not played any matches yet. Null is treated as
    /// NEUTRAL, not as bad form. Passing 0 for "no matches played" would have applied the
    /// full poor-form discount to every team in a freshly seeded world - an unpopulated
    /// default read as a meaningful negative signal, the same bug class already fixed once
    /// in this codebase on trait-derived format suitability.
    /// </param>
    /// <param name="competitionReputation">0-100 dynamic standing of the competition, or null to fall back to prestige alone.</param>
    /// <param name="homeGroundFacilities">Home ground's commercial infrastructure, or null for a neutral 1.0x.</param>
    public double CalculateSponsorshipValue(
        double teamReputation,
        double competitionPrestige,
        double? recentWinRate = null,
        double? competitionReputation = null,
        GroundFacilities? homeGroundFacilities = null)
    {
        double reputationFactor = 1 + Math.Clamp(teamReputation, 0, 100) / 100.0 * 3;   // up to 4x

        // Blend structural prestige with earned reputation when we have both; prestige is
        // weighted higher because it's the more stable signal a sponsor underwrites against.
        double competitionStanding = competitionReputation is null
            ? Math.Clamp(competitionPrestige, 0, 100)
            : Math.Clamp(competitionPrestige, 0, 100) * 0.65 + Math.Clamp(competitionReputation.Value, 0, 100) * 0.35;
        double prestigeFactor = 1 + competitionStanding / 100.0 * 2;                          // up to 3x

        double formFactor = recentWinRate is null
            ? 1.0                                                                             // no data yet -> neutral
            : 0.7 + Math.Clamp(recentWinRate.Value, 0, 1) * 0.6;                              // 0.7x - 1.3x

        double commercialFactor = 1.0;
        if (homeGroundFacilities is not null)
        {
            double commercialQuality = (homeGroundFacilities.CorporateHospitality + homeGroundFacilities.MediaFacilities) / 2.0;
            commercialFactor = 0.85 + Math.Clamp(commercialQuality, 0, 100) / 100.0 * 0.3;    // 0.85x - 1.15x, neutral at 50
        }

        return Math.Round(BaseValue * reputationFactor * prestigeFactor * formFactor * commercialFactor, 0);
    }
}
