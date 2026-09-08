using CricketManager.Domain.Entities;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 6, Slice 6.1: generates the base <see cref="MatchWeather"/> for a scheduled fixture -
/// temperature, humidity, cloud, wind and a rain-risk flag - from the ground's own location and
/// the month it is being played in. This is the piece nothing produced before: RainService could
/// already turn a MatchWeather into rain interruptions, but nothing decided whether a given match
/// in Lahore in January is a cold morning or a warm one.
///
/// Deliberately a SHAPE, not a claim to real climatology: a handful of country climate profiles
/// plus a sensible temperate default, each with a seasonal swing keyed to hemisphere, then noise.
/// The seeded world is one country (Pakistan); other countries get profiles as the world grows,
/// and the default keeps an unknown country playable. Fully deterministic given the Random passed
/// in - the same fixture on the same seed always gets the same conditions.
/// </summary>
public sealed class MatchWeatherService
{
    private readonly record struct ClimateProfile(
        double SummerHighC, double WinterHighC, double BaseHumidity, double BaseCloud,
        double MonsoonPeakMonth, double MonsoonStrength, bool SouthernHemisphere);

    // Rough, honest profiles. SummerHighC/WinterHighC are typical afternoon match-temperature
    // highs, not records. MonsoonStrength 0 = no distinct wet season.
    private static ClimateProfile ProfileFor(string country) => country.Trim().ToLowerInvariant() switch
    {
        "pakistan" or "india" or "bangladesh" or "sri lanka"
            => new(SummerHighC: 38, WinterHighC: 19, BaseHumidity: 55, BaseCloud: 30,
                   MonsoonPeakMonth: 7.5, MonsoonStrength: 0.7, SouthernHemisphere: false),
        "australia"
            => new(32, 17, 45, 35, MonsoonPeakMonth: 1, MonsoonStrength: 0.15, SouthernHemisphere: true),
        "south africa" or "zimbabwe"
            => new(30, 18, 45, 35, MonsoonPeakMonth: 1, MonsoonStrength: 0.35, SouthernHemisphere: true),
        "new zealand"
            => new(23, 12, 65, 55, MonsoonPeakMonth: 0, MonsoonStrength: 0.0, SouthernHemisphere: true),
        "england" or "ireland" or "scotland"
            => new(22, 8, 72, 62, MonsoonPeakMonth: 0, MonsoonStrength: 0.0, SouthernHemisphere: false),
        "west indies" or "windies"
            => new(31, 27, 75, 45, MonsoonPeakMonth: 9, MonsoonStrength: 0.5, SouthernHemisphere: false),
        _ => new(26, 14, 60, 50, MonsoonPeakMonth: 0, MonsoonStrength: 0.0, SouthernHemisphere: false)
    };

    /// <summary>
    /// The base weather for a fixture. dayOfMatch shifts the noise a little so day 3 of a Test is
    /// not identical to day 1. <paramref name="rainRiskMultiplier"/> is the region's own weather
    /// character (CountryProfile.RainRiskMultiplier - an English summer or a monsoon belt loses far
    /// more time than a dry-season venue); 1.0 = the reference.
    /// </summary>
    public MatchWeather Generate(Ground? ground, DateOnly matchDate, Random random, int dayOfMatch = 1, double rainRiskMultiplier = 1.0)
    {
        string country = ground?.Country ?? string.Empty;
        var profile = ProfileFor(country);

        // Seasonal position: 0 at deep winter, 1 at peak summer. Northern hemisphere peaks in
        // July, bottoms out in January; a cosine centred on month 7 does exactly that.
        double monthPos = matchDate.Month - 7 + (matchDate.Day - 1) / 30.0;
        double northSummerness = (1 + Math.Cos(monthPos / 12.0 * 2 * Math.PI)) / 2.0;
        double summerness = profile.SouthernHemisphere ? 1 - northSummerness : northSummerness;

        double seasonalHigh = profile.WinterHighC + (profile.SummerHighC - profile.WinterHighC) * summerness;

        // Altitude cools things down (~6.5C per 1000m) and takes a little sting out of humidity.
        double altitudeCooling = (ground?.AltitudeMetres ?? 0) / 1000.0 * 6.5;

        // A match-day roll: some days are just hotter or cooler than the seasonal norm.
        double dailySwing = (random.NextDouble() - 0.5) * 8 + (random.NextDouble() - 0.5) * (dayOfMatch % 3);
        double temperature = Math.Round(Math.Clamp(seasonalHigh - altitudeCooling + dailySwing, -2, 48), 1);

        // Monsoon: humidity, cloud and rain risk all spike near the wet-season peak month.
        double monsoonCloseness = profile.MonsoonStrength <= 0 ? 0
            : Math.Max(0, 1 - Math.Abs(CircularMonthDistance(matchDate.Month, profile.MonsoonPeakMonth)) / 2.5);
        double monsoonFactor = monsoonCloseness * profile.MonsoonStrength;

        double humidity = Math.Round(Math.Clamp(
            profile.BaseHumidity
            + summerness * 6                              // muggier in summer
            + monsoonFactor * 30
            - altitudeCooling * 1.5
            + (random.NextDouble() - 0.5) * 20, 10, 98), 1);

        double cloud = Math.Round(Math.Clamp(
            profile.BaseCloud
            + monsoonFactor * 40
            - summerness * 10
            + (random.NextDouble() - 0.5) * 40, 0, 100), 1);

        double wind = Math.Round(Math.Clamp(6 + random.NextDouble() * 22 + cloud / 100.0 * 8, 2, 45), 1);

        // Rain risk: driven by cloud, the monsoon, and temperate maritime unpredictability
        // (a high base humidity with no monsoon - England, NZ - still rains a lot).
        double maritime = profile.MonsoonStrength <= 0 && profile.BaseHumidity >= 68 ? 0.18 : 0.0;
        double rainRisk = Math.Round(Math.Clamp(
            (cloud / 100.0 * 25 + monsoonFactor * 55 + maritime * 100) * Math.Clamp(rainRiskMultiplier, 0.4, 2.5)
            + (random.NextDouble() - 0.6) * 15, 0, 97), 1);

        return new MatchWeather
        {
            TemperatureC = temperature,
            Humidity = humidity,
            CloudCover = cloud,
            WindSpeedKph = wind,
            // §19.6: a prevailing wind direction for the day - one of the 8 compass points, derived
            // deterministically from the date and venue so it consumes no RNG (the shared per-fixture
            // stream must not shift).
            WindBearing = ((matchDate.DayOfYear + (ground?.Name.Length ?? 3) + (int)(ground?.AltitudeMetres ?? 0)) % 8) * 45,
            RainRisk = rainRisk
        };
    }

    /// <summary>Shortest distance between two months on the 12-month circle (so Dec and Jan are 1 apart, not 11).</summary>
    private static double CircularMonthDistance(double a, double b)
    {
        double raw = Math.Abs(a - b) % 12;
        return Math.Min(raw, 12 - raw);
    }
}
