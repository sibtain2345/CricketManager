using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Section 2/3: reputation is deliberately NOT a single number.
/// A coach/player can be huge domestically but unknown globally.
/// Scale: 0-100. Mutation goes through Adjust() so we never allow a raw setter
/// to silently break the "earned, not free" rule described in the spec.
/// </summary>
public sealed class Reputation
{
    // [JsonInclude]: same private-setter/JSON round-trip fix as FormState.
    [JsonInclude]
    public double Domestic { get; private set; }
    [JsonInclude]
    public double Continental { get; private set; }
    [JsonInclude]
    public double Worldwide { get; private set; }

    public Reputation(double domestic = 5, double continental = 0, double worldwide = 0)
    {
        Domestic = Clamp(domestic);
        Continental = Clamp(continental);
        Worldwide = Clamp(worldwide);
    }

    /// <summary>
    /// Applies a reputation delta. Continental/worldwide gains are intentionally
    /// damped relative to domestic - a domestic trophy shouldn't move your global
    /// standing much, but a global achievement should still nudge domestic reputation up.
    /// </summary>
    public void Adjust(double domesticDelta, double continentalDelta = 0, double worldwideDelta = 0)
    {
        Domestic = Clamp(Domestic + domesticDelta);
        Continental = Clamp(Continental + continentalDelta);
        Worldwide = Clamp(Worldwide + worldwideDelta);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);

    public override string ToString() => $"Dom:{Domestic:F1} Cont:{Continental:F1} World:{Worldwide:F1}";
}
