using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Section AA: pitch preparation as a genuine pre-match choice, not a fixed venue characteristic.
/// Before this, Ground's pitch fields were set once at ground creation (WorldSeeder) and never
/// moved again for any individual match - there was no decision point at all, despite
/// GroundFacilities.PitchInfrastructure carrying the doc comment "drainage, covers, pitch-prep
/// quality -> Phase 4" since Phase 3b. This is that field's first consumer.
///
/// A request is bounded and damped, deliberately: a groundstaff can tilt a surface toward what
/// their captain wants, not reinvent the venue. A green-mamba request at a historically flat
/// batting road still comes out closer to flat than to Headingley on a damp morning - it is a
/// nudge on top of the ground's own baseline, and how much of the nudge is actually delivered
/// scales with the ground's own PitchInfrastructure quality. Poor infrastructure means the
/// request is executed poorly and regresses toward what the ground would have played like anyway.
/// </summary>
public sealed class PitchPreparationService
{
    /// <summary>The largest single-rating shift a preparation request can achieve, before infrastructure quality damps it further.</summary>
    public const double MaxShift = 18;

    /// <summary>
    /// Returns a NEW Ground snapshot with pitch ratings nudged toward the request. The original
    /// Ground entity is never mutated - a venue's real, long-term characteristics do not change
    /// because one match's coach asked for something different, and the same Ground is shared
    /// across every match ever played there, past and future.
    /// </summary>
    public Ground Apply(Ground ground, PitchPreparation preparation, double homePitchInfluence = 1.0)
    {
        if (preparation == PitchPreparation.Neutral) return ground;

        // Corrections pass (correction 2): the size of the swing scales with how much the home
        // side genuinely controls this match's pitch. A full bilateral / domestic push can move a
        // surface HARD (PAK's Multan road -> a raw turner inside one series); an ICC-event or
        // franchise-league surface barely moves. `MaxShift` is the bilateral ceiling before
        // infrastructure damps it further.
        double effectiveMax = MaxShift * (0.3 + 1.1 * Math.Clamp(homePitchInfluence, 0, 1));

        // Even a poorly-resourced ground staff delivers SOME of what was asked - the floor is a
        // third of the request, not nothing.
        double competence = Math.Clamp(ground.Facilities.PitchInfrastructure, 0, 100) / 100.0;
        double shift = effectiveMax * (0.35 + competence * 0.65);

        var (paceDelta, spinDelta, bounceDelta, battingDelta) = preparation switch
        {
            PitchPreparation.Grassy => (shift, -shift * 0.5, shift * 0.6, -shift * 0.3),
            PitchPreparation.Dry => (-shift * 0.4, shift, -shift * 0.2, -shift * 0.2),
            PitchPreparation.Flat => (-shift * 0.5, -shift * 0.6, -shift * 0.3, shift),
            _ => (0.0, 0.0, 0.0, 0.0)
        };

        return new Ground
        {
            Id = ground.Id,
            Name = ground.Name,
            City = ground.City,
            Country = ground.Country,
            Capacity = ground.Capacity,
            EstablishedYear = ground.EstablishedYear,
            HomeTeamIds = ground.HomeTeamIds,
            Ends = ground.Ends,
            PitchPaceRating = Math.Clamp(ground.PitchPaceRating + paceDelta, 0, 100),
            PitchSpinRating = Math.Clamp(ground.PitchSpinRating + spinDelta, 0, 100),
            PitchBounceRating = Math.Clamp(ground.PitchBounceRating + bounceDelta, 0, 100),
            PitchBattingFriendliness = Math.Clamp(ground.PitchBattingFriendliness + battingDelta, 0, 100),
            PitchWearRate = ground.PitchWearRate,
            SquareBoundaryMetres = ground.SquareBoundaryMetres,
            StraightBoundaryMetres = ground.StraightBoundaryMetres,
            OutfieldSpeed = ground.OutfieldSpeed,
            AltitudeMetres = ground.AltitudeMetres,
            HasFloodlights = ground.HasFloodlights,
            DewTendency = ground.DewTendency,
            Reputation = ground.Reputation,
            IsNationalStadium = ground.IsNationalStadium,
            Facilities = ground.Facilities
        };
    }
}
