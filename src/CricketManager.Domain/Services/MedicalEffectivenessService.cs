using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Better medical staff mitigate injury risk and speed recovery WITHOUT changing the
/// player's own intrinsic attributes (Physical.InjuryProneness/Recovery stay the player's -
/// this is the team-side layer on top). A high-InjuryProneness player at a team with
/// excellent medical facilities still carries real risk, just meaningfully reduced
/// compared to the same player at a team with a poor medical setup.
///
/// Venue-side medical facilities (GroundFacilities.MedicalFacilities) layer on top of the
/// team's own, weighted lower: a team's medical staff travel with the squad and matter far
/// more than what any one ground has on site, but on-site facilities genuinely affect how
/// well an injury is handled at the moment it happens.
/// </summary>
public sealed class MedicalEffectivenessService
{
    /// <summary>
    /// Combines team medical quality with the venue's own medical facilities into one
    /// 0-100 effective quality. Team quality carries 75% of the weight; a null ground
    /// (unknown venue) simply falls back to team quality alone rather than penalising it.
    /// </summary>
    public double GetCombinedMedicalQuality(int teamMedicalQuality, GroundFacilities? groundFacilities = null)
    {
        double team = Math.Clamp(teamMedicalQuality, 0, 100);
        if (groundFacilities is null) return team;
        return team * 0.75 + Math.Clamp(groundFacilities.MedicalFacilities, 0, 100) * 0.25;
    }

    /// <summary>Effective injury risk (0-100) after medical mitigation - up to 40% reduction at max combined quality.</summary>
    public double GetEffectiveInjuryRisk(int playerInjuryProneness, int teamMedicalQuality, GroundFacilities? groundFacilities = null)
    {
        double baseRisk = Math.Clamp(playerInjuryProneness, 1, 20) / 20.0 * 100;
        double mitigation = GetCombinedMedicalQuality(teamMedicalQuality, groundFacilities) / 100.0 * 0.4;
        return Math.Clamp(baseRisk * (1 - mitigation), 0, 100);
    }

    /// <summary>Effective recovery rate (0-100) after medical support - up to 25% faster at max combined quality.</summary>
    public double GetEffectiveRecoveryRate(int playerRecoveryAttribute, int teamMedicalQuality, GroundFacilities? groundFacilities = null)
    {
        double baseRate = Math.Clamp(playerRecoveryAttribute, 1, 20) / 20.0 * 100;
        double bonus = GetCombinedMedicalQuality(teamMedicalQuality, groundFacilities) / 100.0 * 0.25;
        return Math.Clamp(baseRate * (1 + bonus), 0, 100);
    }
}
