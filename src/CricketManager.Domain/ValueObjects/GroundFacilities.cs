namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// 0-100 each, neutral baseline 50 (same convention as every other 0-100 scale in this
/// codebase, so a facility can be a positive OR negative factor rather than only ever
/// subtracting from a notional perfect ground).
///
/// Functional consumers today:
/// - CorporateHospitality, MediaFacilities -> SponsorshipValuationService (commercial pull)
///   and MatchdayRevenueService (revenue per attendee, not just ticket count).
/// - MedicalFacilities -> MedicalEffectivenessService (venue-side medical support layered
///   on top of the team's own).
///
/// Explicitly NOT consumed yet, stated rather than implied:
/// - PitchInfrastructure (drainage, covers, pitch-prep quality) -> Phase 4, where it will
///   drive rain-interruption recovery and pitch-condition quality.
/// - TrainingFacilities, YouthFacilities -> Phase 8 (Training/Development).
/// These are kept because Ground's shape shouldn't change when those phases land, but they
/// do nothing today and should not be presented in UI as if they did.
/// </summary>
public sealed class GroundFacilities
{
    public int TrainingFacilities { get; set; } = 40;
    public int MediaFacilities { get; set; } = 40;
    public int CorporateHospitality { get; set; } = 40;
    public int MedicalFacilities { get; set; } = 40;
    public int YouthFacilities { get; set; } = 30;
    public int PitchInfrastructure { get; set; } = 50; // drainage, covers, pitch-prep quality
}
