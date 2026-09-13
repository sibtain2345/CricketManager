using CricketManager.Domain.Services;

namespace CricketManager.Api;

/// <summary>
/// Phase 17 Part 2, design decision 3c: information asymmetry is a real game mechanic, not a
/// display choice. A manager does not have perfect knowledge of a player's ceiling - real
/// management sims (Football Manager chief among them) deliberately withhold the exact
/// PotentialAbility number and show only a scout's uncertain estimate. This service is the ONE
/// place raw domain numbers (the 1-200 CurrentAbility/PotentialAbility scale, the 0-100
/// MatchSharpness/CurrentForm scales) get converted into what the API actually serves - a tier
/// label, a bar percentage, or a star-confidence band. No endpoint should read those raw fields
/// directly; routing every DTO through here is what makes it structurally impossible for a future
/// screen to leak the ground-truth number by simply forgetting to call a formatter.
/// </summary>
public static class QualitativeProjectionService
{
    /// <summary>CurrentAbility (1-200 scale) -> a tier label + a 0-100 bar percentage. The scale is intentionally compressed toward the top - most players cluster well below the theoretical maximum, and a bar that only lights up near 200 would read as empty for almost the whole squad.</summary>
    public static (string Tier, int BarPercent) AbilityTier(int currentAbility)
    {
        var percent = Math.Clamp((int)Math.Round(currentAbility / 190.0 * 100), 2, 100);
        var tier = currentAbility switch
        {
            >= 165 => "Elite",
            >= 135 => "Very good",
            >= 100 => "Good",
            >= 65 => "Average",
            _ => "Below average"
        };
        return (tier, percent);
    }

    /// <summary>
    /// The player's TRUE PotentialAbility is never returned by this method - only what a scout of
    /// the given quality would actually report, via the same noisy-estimate model
    /// <see cref="ScoutingAccuracyService.EstimatePotentialAbility"/> already computes for
    /// recruitment decisions elsewhere in the domain. Reused here, not reinvented - the "how sure
    /// is the club" story is the same story whether the club is buying a player or judging one it
    /// already has.
    /// </summary>
    public static (int Stars1To5, string Note) PotentialBand(int truePotentialAbility, int scoutingQuality, Random random)
    {
        var scouting = new ScoutingAccuracyService(random);
        var estimate = scouting.EstimatePotentialAbility(truePotentialAbility, scoutingQuality);
        var stars = estimate switch
        {
            >= 175 => 5,
            >= 145 => 4,
            >= 110 => 3,
            >= 75 => 2,
            _ => 1
        };
        var note = scoutingQuality >= 75
            ? "A confident read from a strong scouting department."
            : scoutingQuality >= 40
                ? "A reasonable estimate - a better scouting department would sharpen it."
                : "An early, uncertain read. Treat it as a guess, not a promise.";
        return (stars, note);
    }

    /// <summary>FormState.CurrentForm (roughly -100..100, 0 neutral) -> a tier + bar percentage centred on the middle of the bar.</summary>
    public static (string Tier, int BarPercent) FormTier(double currentForm)
    {
        var percent = Math.Clamp((int)Math.Round((currentForm + 100) / 2), 2, 100);
        var tier = currentForm switch
        {
            >= 40 => "Excellent",
            >= 15 => "Good",
            >= -15 => "Steady",
            >= -40 => "Out of form",
            _ => "Poor"
        };
        return (tier, percent);
    }

    /// <summary>Player.MatchSharpness (0-100, 100 = fully match-hardened) -> a tier + bar percentage.</summary>
    public static (string Tier, int BarPercent) SharpnessTier(double matchSharpness)
    {
        var percent = Math.Clamp((int)Math.Round(matchSharpness), 0, 100);
        var tier = matchSharpness switch
        {
            >= 85 => "Fresh",
            >= 65 => "Sharp",
            >= 40 => "Undercooked",
            _ => "Rusty"
        };
        return (tier, percent);
    }

    /// <summary>A single 1-20 attribute -> a display tier for a skill bar's colour band. Distinct from AbilityTier's own bands (deliberately - an attribute and the composite ability aren't the same scale and shouldn't share thresholds).</summary>
    public static string AttributeTier(int value1To20) => value1To20 switch
    {
        >= 16 => "elite",
        >= 12 => "strong",
        >= 8 => "solid",
        _ => "weak"
    };
}
