using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>One stoppage: when it started, how long play was lost, and whether it ended the day.</summary>
public sealed record RainInterruption(
    int Day,
    TimeOnly StartedAt,
    double MinutesLost,
    int OversLost,
    bool EndedPlayForDay,
    string Description);

/// <summary>
/// The Duckworth-Lewis-Stern par calculation, in the form the game needs it.
///
/// The real method uses a published resource table of several hundred entries; reproducing that
/// table is neither possible from memory nor necessary. What matters for a simulation is that the
/// SHAPE is right, and the shape is what makes DLS counter-intuitive to people: resources are a
/// function of BOTH overs remaining and wickets in hand, and the two interact - a side with ten
/// wickets and twenty overs has far more left than one with two wickets and the same twenty.
///
/// The curve below reproduces the SHAPE of the published table rather than its exact values, and
/// is documented as such rather than presented as the real thing. It is monotonic in both inputs,
/// falls to zero when either resource runs out, and produces the property that catches people out:
/// when the side batting SECOND has more resources than the side batting first had, the target goes
/// UP, not down.
///
/// Measured against the published table it runs a few points generous in the middle - 40 overs with
/// ten wickets reads 93% here against about 89% in the real table, 20 overs with ten reads 66%
/// against about 62%. Close enough for a simulation, and wrong enough that it must not be described
/// as DLS proper. Replacing this with the real resource table is a data-layer job, not a code one,
/// and is exactly the sort of thing the external data layer exists for.
/// </summary>
public static class DuckworthLewisStern
{
    /// <summary>
    /// Resources remaining as a percentage, given overs left and wickets in hand.
    ///
    /// Two properties the real table has, and this reproduces: resources fall to zero when the overs
    /// run out OR the wickets do, and losing early wickets costs far more than losing late ones,
    /// because a side with wickets in hand can use its remaining overs and one without cannot.
    /// </summary>
    public static double ResourcesRemaining(double oversRemaining, int wicketsInHand)
    {
        if (oversRemaining <= 0 || wicketsInHand <= 0) return 0;

        // The overs component saturates: the first twenty overs of a fifty-over innings are worth
        // far less than the label suggests, because a side cannot use them all at once.
        double overs = Math.Min(oversRemaining, 50);
        double overResource = 1 - Math.Exp(-overs / 22.5);

        // The wickets component. Losing the first wicket costs little; losing the ninth costs
        // almost everything that is left.
        double wicketFactor = Math.Pow(wicketsInHand / 10.0, 0.72);

        // The interaction term is what makes DLS what it is: overs are only worth something if you
        // have the wickets to bat them out.
        double usableOvers = 1 - Math.Exp(-overs / (22.5 * Math.Max(0.35, wicketsInHand / 10.0)));

        double resources = (overResource * 0.35 + usableOvers * 0.65) * wicketFactor;

        // Normalised so a full fifty-over innings with ten wickets reads as 100%.
        const double FullInnings = 0.8909;
        return Math.Clamp(resources / FullInnings * 100, 0, 100);
    }

    /// <summary>
    /// The revised target for a side batting second whose innings has been shortened.
    ///
    /// The standard formula: scale the first innings by the ratio of the two sides' resources, and
    /// add one. When the chasing side has MORE resources than the side batting first had - which
    /// happens when the first innings was the one interrupted - the target rises rather than falls,
    /// which is the part people find surprising and which a naive "runs per over" calculation gets
    /// completely wrong.
    /// </summary>
    public static int RevisedTarget(int firstInningsScore, double firstInningsResources, double secondInningsResources)
    {
        if (firstInningsResources <= 0) return firstInningsScore + 1;

        double ratio = secondInningsResources / firstInningsResources;
        return (int)Math.Round(firstInningsScore * ratio) + 1;
    }

    /// <summary>
    /// The par score for a side mid-chase - what it should be on right now to be level. This is the
    /// figure a scoreboard shows during an interrupted chase, and the one a coach is actually
    /// managing against.
    /// </summary>
    public static int ParScore(int target, double resourcesUsedSoFar, double totalResourcesAvailable)
    {
        if (totalResourcesAvailable <= 0) return 0;
        return (int)Math.Round((target - 1) * (resourcesUsedSoFar / totalResourcesAvailable));
    }

    /// <summary>
    /// Whether a limited-overs match has reached the minimum for a DLS result at all. Twenty overs
    /// a side is the standard threshold in fifty-over cricket, five in a T20 - below that there is
    /// no result, however far ahead somebody is.
    /// </summary>
    public static int MinimumOversForResult(MatchFormat format) => format switch
    {
        MatchFormat.T20 => 5,
        MatchFormat.ODI => 20,
        _ => 0
    };
}
