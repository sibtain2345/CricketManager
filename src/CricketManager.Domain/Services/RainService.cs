using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What a stoppage did to a limited-overs match.</summary>
public sealed record OverReduction(
    int RevisedOvers,
    int? RevisedTarget,
    double MinutesLost,
    bool MatchAbandoned,
    string Description);

/// <summary>
/// Rain: when it comes, how long it lasts, and what it does to the match.
///
/// The two formats lose completely different things to it, which is why one service handles both
/// rather than each simulator inventing its own:
///
/// - **Limited overs lose OVERS**, and the target has to be recalculated because the two sides no
///   longer had the same resources. That is what DLS is for, and getting it wrong by scaling runs
///   per over is the classic mistake.
/// - **Multi-day cricket loses TIME**, and time is the resource a result is made of. A side two
///   wickets from victory with a session washed out has drawn the match. This is the single biggest
///   reason real Test matches are drawn, and it is why the multi-day draw rate in this project was
///   honestly flagged as too low until now.
/// </summary>
public sealed class RainService
{
    /// <summary>
    /// Whether it rains during a given passage of play, and for how long.
    ///
    /// Driven by the weather's rain risk. Deliberately lumpy rather than a steady drizzle of small
    /// interruptions: real rain tends to either miss a match entirely or take a serious bite out of
    /// it, and a model that sprinkles five-minute stoppages evenly feels nothing like it.
    /// </summary>
    public RainInterruption? RollForRain(
        MatchWeather? weather, int day, TimeOnly currentTime, double minutesAtRisk, Random random)
    {
        double risk = weather?.RainRisk ?? 0;
        if (risk <= 0 || minutesAtRisk <= 0) return null;

        // Chance scales with both the forecast and how long a passage we are exposing to it.
        double chance = risk / 100.0 * (minutesAtRisk / 360.0) * 0.85;
        if (random.NextDouble() >= chance) return null;

        // How bad. Most stoppages are a shower; a minority take out a session; a few end the day.
        double severity = random.NextDouble();
        double minutesLost = severity switch
        {
            < 0.55 => 15 + random.NextDouble() * 35,     // a shower
            < 0.85 => 60 + random.NextDouble() * 60,     // a serious interruption
            _ => Math.Max(90, minutesAtRisk)             // that is the day
        };

        minutesLost = Math.Min(minutesLost, minutesAtRisk);
        bool endedDay = minutesLost >= minutesAtRisk - 15;

        // Roughly a quarter of an hour per over lost, at first-class over rates.
        int oversLost = (int)Math.Round(minutesLost / 4.0);

        string description = endedDay
            ? $"Day {day}: rain has ended play, {minutesLost:F0} minutes lost."
            : minutesLost >= 60
                ? $"Day {day}: a lengthy delay from {currentTime:HH\\:mm}, {minutesLost:F0} minutes lost."
                : $"Day {day}: a shower from {currentTime:HH\\:mm}, {minutesLost:F0} minutes lost.";

        return new RainInterruption(day, currentTime, minutesLost, oversLost, endedDay, description);
    }

    /// <summary>
    /// What a stoppage does to a limited-overs match: fewer overs, and a target recalculated on
    /// resources rather than on runs per over.
    /// </summary>
    public OverReduction ApplyToLimitedOvers(
        MatchFormat format,
        int scheduledOvers,
        int oversBowledInSecondInnings,
        int wicketsDownInSecondInnings,
        int firstInningsScore,
        int oversAvailableToFirstInnings,
        double minutesLost)
    {
        // Roughly four minutes an over in limited-overs cricket, and both sides lose the overs.
        int oversLost = (int)Math.Round(minutesLost / 4.3);
        int revisedOvers = Math.Max(0, scheduledOvers - oversLost);

        int minimum = DuckworthLewisStern.MinimumOversForResult(format);

        if (revisedOvers < minimum)
            return new OverReduction(revisedOvers, null, minutesLost, MatchAbandoned: true,
                $"Match abandoned - {revisedOvers} overs is below the {minimum} needed for a result.");

        // Resources each side had. The chasing side's are measured from where it currently stands,
        // which is what makes an interruption mid-chase change the target rather than the overs alone.
        double firstResources = DuckworthLewisStern.ResourcesRemaining(oversAvailableToFirstInnings, 10);

        double secondResources =
            DuckworthLewisStern.ResourcesRemaining(scheduledOvers - oversBowledInSecondInnings, 10 - wicketsDownInSecondInnings)
            - DuckworthLewisStern.ResourcesRemaining(revisedOvers - oversBowledInSecondInnings, 10 - wicketsDownInSecondInnings);

        // Resources actually available to the chasing side across its whole innings.
        double secondTotal = DuckworthLewisStern.ResourcesRemaining(scheduledOvers, 10) - secondResources;

        int target = DuckworthLewisStern.RevisedTarget(firstInningsScore, firstResources, secondTotal);

        return new OverReduction(revisedOvers, target, minutesLost, MatchAbandoned: false,
            $"Revised to {revisedOvers} overs, target {target}.");
    }

    /// <summary>
    /// What a stoppage does to a multi-day match: nothing to the target, everything to the time.
    ///
    /// This is the mechanism behind the most common cause of a real Test draw. A side pressing for
    /// victory does not lose its advantage when it rains - it loses the overs it needed to convert
    /// the advantage, which is a completely different and much more frustrating thing.
    /// </summary>
    public int OversLostToRain(double minutesLost, IReadOnlyList<double> minutesPerOver)
    {
        if (minutesLost <= 0) return 0;

        double averageMinutesPerOver = minutesPerOver.Count == 0 ? 4.0 : minutesPerOver.Average();
        return (int)Math.Round(minutesLost / averageMinutesPerOver);
    }

    /// <summary>
    /// Simulates a whole multi-day match's weather, day by day, and reports the overs lost. Called
    /// before the match so the simulator knows how much cricket it actually has - the same way the
    /// over-rate and light calculation already works.
    /// </summary>
    public (int OversLost, IReadOnlyList<RainInterruption> Interruptions) SimulateMatchWeather(
        MatchWeather? weather, int days, int oversPerDay, Random random)
    {
        var (lost, interruptions, _) = SimulateMatchWeatherByDay(weather, days, oversPerDay, random);
        return (lost, interruptions);
    }

    /// <summary>
    /// The same simulation, but reporting the loss DAY BY DAY rather than as a single total.
    ///
    /// The breakdown matters because recovery is a day-by-day process: a session lost on day two can
    /// be made up over days three and four, while the same session lost on the final day is simply
    /// gone. A single total cannot express that difference, and treating it as one would either
    /// recover time that could not be recovered or write off time that could.
    /// </summary>
    public (int OversLost, IReadOnlyList<RainInterruption> Interruptions, IReadOnlyList<int> OversLostByDay)
        SimulateMatchWeatherByDay(MatchWeather? weather, int days, int oversPerDay, Random random)
    {
        var interruptions = new List<RainInterruption>();
        var lostByDay = new int[days];
        int totalOversLost = 0;

        for (int day = 1; day <= days; day++)
        {
            // Two rolls a day - morning and afternoon - so a day can lose a session without losing
            // all of it, which is how rain actually behaves.
            foreach (var time in new[] { new TimeOnly(11, 30), new TimeOnly(15, 0) })
            {
                var interruption = RollForRain(weather, day, time, minutesAtRisk: 180, random);
                if (interruption is null) continue;

                interruptions.Add(interruption);

                // A day cannot lose more than its own overs, however hard it rains.
                int lossThisDay = Math.Min(interruption.OversLost, oversPerDay - lostByDay[day - 1]);
                lostByDay[day - 1] += lossThisDay;
                totalOversLost += lossThisDay;

                if (interruption.EndedPlayForDay) break;
            }
        }

        return (Math.Min(totalOversLost, days * oversPerDay), interruptions, lostByDay);
    }
}
