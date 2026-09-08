namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// 0-100 each. Every field here has an actual functional consumer today - see
/// ScoutingAccuracyService (ScoutingQuality), MedicalEffectivenessService (MedicalQuality),
/// SponsorshipValuationService (CorporateCommercialQuality indirectly via team reputation).
/// TrainingQuality now has TWO real consumers as of Phase 5: PlayerAgeingService's ambient
/// growth-rate scaling (its first, pre-existing consumer) and TrainingService's directed,
/// coach-chosen growth (its facility-only fallback when no specialist StaffMember is hired for
/// the relevant role). YouthDevelopmentQuality remains intentionally present with no consumer -
/// see CLAUDE.md's Phase 5 writeup for why the youth academy/development pipeline was
/// deliberately deferred rather than built alongside training.
/// </summary>
public sealed class TeamFacilities
{
    public int TrainingQuality { get; set; } = 40;
    public int YouthDevelopmentQuality { get; set; } = 30;
    public int ScoutingQuality { get; set; } = 30;
    public int MedicalQuality { get; set; } = 40;
    public int CorporateCommercialQuality { get; set; } = 30;
}
