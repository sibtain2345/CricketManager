using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// The conditions the match is actually being played in. Section 25.
///
/// This exists mainly because of what it does to PEOPLE. A day in Colombo and a day in Manchester
/// ask completely different things of a fast bowler's body, and a fatigue model that ignores that
/// is modelling a treadmill rather than cricket. Heat and humidity are the two that matter most:
/// a humid 34-degree afternoon empties a bowler in four overs, while cool cloud cover lets the same
/// man bowl all day.
///
/// It also does the obvious cricketing things - cloud helps the ball swing, wind helps it drift,
/// and a hot dry day bakes the surface - but the physiological side is the reason it is modelled
/// per-match rather than folded into the pitch.
/// </summary>
public sealed record MatchWeather
{
    /// <summary>Degrees Celsius.</summary>
    public double TemperatureC { get; init; } = 24;

    /// <summary>Relative humidity, 0-100. The single cruellest variable for a bowler.</summary>
    public double Humidity { get; init; } = 50;

    /// <summary>0-100. Overhead conditions - high cloud helps the ball move and keeps the temperature down.</summary>
    public double CloudCover { get; init; } = 40;

    /// <summary>Kilometres per hour. A stiff breeze helps a swing bowler into it and punishes the one running with it.</summary>
    public double WindSpeedKph { get; init; } = 10;

    /// <summary>
    /// Phase 15 (§19.6): the direction the wind blows TOWARD, as a compass bearing 0-360 (0 = it
    /// carries the ball toward the "straight" boundary behind the bowler's arm, 90 = toward
    /// point/cover). Only matters above ~18 kph. Default 0. Feeds a small boundary asymmetry -
    /// the downwind side of the ground plays shorter, the upwind side longer, net roughly zero.
    /// </summary>
    public double WindBearing { get; init; }

    /// <summary>0-100 chance of interruption. Rain handling itself is a later slice; this is the flag it will read.</summary>
    public double RainRisk { get; init; }

    public static MatchWeather Mild => new();
    public static MatchWeather HotAndHumid => new() { TemperatureC = 35, Humidity = 80, CloudCover = 15, WindSpeedKph = 6 };
    public static MatchWeather CoolAndOvercast => new() { TemperatureC = 14, Humidity = 65, CloudCover = 90, WindSpeedKph = 18 };
    public static MatchWeather HotAndDry => new() { TemperatureC = 36, Humidity = 20, CloudCover = 5, WindSpeedKph = 8 };
    public static MatchWeather Cold => new() { TemperatureC = 8, Humidity = 70, CloudCover = 70, WindSpeedKph = 20 };

    /// <summary>
    /// How much harder this weather makes physical work, as a multiplier on fatigue accumulation.
    ///
    /// Heat and humidity compound rather than add, which is why "34 and humid" is a different order
    /// of difficulty from "34 and dry" - the body cannot shed heat when the air is already
    /// saturated. Cool, overcast conditions sit below 1.0: players genuinely tire less, which is
    /// exactly the effect being asked for here.
    ///
    /// Roughly: cold overcast ~0.72x, mild ~1.0x, hot and dry ~1.35x, hot and humid ~1.75x.
    /// </summary>
    public double FatigueMultiplier
    {
        get
        {
            // Comfort sits around 18-20C. Below that the body works less; above it, progressively more.
            double heat = TemperatureC <= 20
                ? 1 - (20 - Math.Clamp(TemperatureC, -5, 20)) / 20.0 * 0.30      // down to ~0.70x in the cold
                : 1 + (Math.Clamp(TemperatureC, 20, 45) - 20) / 25.0 * 0.55;     // up to ~1.55x in real heat

            // Humidity only bites once it is warm - 80% humidity at 12 degrees is just miserable,
            // not exhausting. This interaction is the whole reason the two are separate fields.
            double humidityBite = Math.Clamp((TemperatureC - 22) / 13.0, 0, 1);
            double humidityFactor = 1 + Math.Clamp(Humidity - 45, 0, 55) / 55.0 * 0.35 * humidityBite;

            // Cloud takes the sting out of the sun even on a warm day.
            double cloudRelief = 1 - Math.Clamp(CloudCover, 0, 100) / 100.0 * 0.12;

            // A breeze helps you cool down.
            double windRelief = 1 - Math.Clamp(WindSpeedKph, 0, 40) / 40.0 * 0.08;

            return Math.Clamp(heat * humidityFactor * cloudRelief * windRelief, 0.6, 2.0);
        }
    }

    /// <summary>
    /// How quickly a player recovers when he is NOT the one doing the work - a bowler resting in
    /// the field, a batter between overs. The inverse of the fatigue picture: you get your breath
    /// back quickly in the cold and barely at all in the heat.
    /// </summary>
    public double RecoveryMultiplier => Math.Clamp(1.35 - (FatigueMultiplier - 1) * 0.75, 0.45, 1.5);

    /// <summary>
    /// Cloud and humidity help the ball swing - the oldest piece of cricket folklore that turns
    /// out to be broadly true - and so does a stiff breeze (Phase 15, §19.6): a bowler running
    /// into a real wind gets noticeably more shape, which is why a captain who is switched on
    /// bowls his swing bowler from the right end.
    /// </summary>
    public double SwingBonus =>
        Math.Clamp(CloudCover, 0, 100) / 100.0 * 22
        + Math.Clamp(Humidity - 40, 0, 60) / 60.0 * 10
        + Math.Clamp(WindSpeedKph - 12, 0, 30) / 30.0 * 6;

    /// <summary>
    /// Phase 15 (§19.5): genuinely dangerous heat - a hot dry ~40C+ afternoon, or a humid ~37C+
    /// one. Drinks breaks become frequent and a forced stoppage is possible; the fatigue toll is
    /// already carried by <see cref="FatigueMultiplier"/>, this is the flag a caller reads to add
    /// the extra breaks / commentary.
    /// </summary>
    public bool IsExtremeHeat =>
        (TemperatureC >= 40 && Humidity < 55) || (TemperatureC >= 37 && Humidity >= 55);

    /// <summary>A hot dry day bakes the surface and helps it break up, which is what turns a fourth-day pitch into a spinner's.</summary>
    public double PitchDryingRate =>
        Math.Clamp((TemperatureC - 15) / 25.0, 0, 1) * (1 - Math.Clamp(Humidity, 0, 100) / 200.0);

    public string Describe() => this switch
    {
        _ when IsExtremeHeat => "Brutal heat - the players will need their fluids and the umpires may take them off.",
        { TemperatureC: >= 30, Humidity: >= 70 } => "Hot and humid - hard work out there.",
        { TemperatureC: >= 30 } => "Hot and dry.",
        { TemperatureC: <= 12 } => "Cold, with a bite in the air.",
        { WindSpeedKph: >= 28 } => "Blustery - the swing bowler will want to run in from one particular end.",
        { CloudCover: >= 75 } => "Overcast - there should be something in the air for the bowlers.",
        _ => "Pleasant conditions for cricket."
    };
}
