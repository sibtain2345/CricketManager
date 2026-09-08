using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What a single day will attempt to do about time already lost.</summary>
public sealed record DayRecoveryPlan(
    int Day,
    /// <summary>Minutes the day starts earlier than scheduled. Only permitted when time was lost on a PREVIOUS day.</summary>
    int EarlyStartMinutes,
    /// <summary>Minutes added at the end of the day. This is the extra time proper.</summary>
    int ExtraTimeMinutes,
    /// <summary>Overs this day will attempt on top of the scheduled quota.</summary>
    int AdditionalOvers,
    int RevisedDayOvers,
    string Description);

/// <summary>The whole match's recovery plan, and how much of the lost time it actually claws back.</summary>
public sealed record LostTimeRecoveryPlan(
    IReadOnlyList<DayRecoveryPlan> Days,
    int OversLost,
    int OversRecovered,
    int OversPermanentlyLost,
    string Summary);

/// <summary>
/// Making up overs lost to rain, the way the playing conditions actually allow.
///
/// This is the piece that was missing from the rain slice. Real Test and first-class cricket does
/// not simply write off a washed-out session: the following days start early, run late, and carry a
/// larger over quota until the arrears are cleared. A match that loses a session on day two can be
/// almost entirely back on schedule by day four, which is why plenty of rain-hit Tests still produce
/// results - and modelling rain WITHOUT recovery quietly makes every wet match a draw, which is not
/// what happens.
///
/// The rules modelled, and they are the real ones:
/// - **Up to an hour of extra time a day.** The standard allowance, taken at the end of the day.
/// - **Up to half an hour of early start**, and only when time was lost on a PREVIOUS day - you
///   cannot start early to make up time you have not yet lost.
/// - **A capped daily over quota.** A day cannot absorb unlimited arrears; the practical ceiling is
///   about fifteen extra overs, because that is what an hour buys at a first-class over rate.
/// - **The final day cannot pass its arrears on.** Whatever is still owed at the start of the last
///   day, only that day's own extra time can recover, and the rest is gone for good.
/// </summary>
public sealed class LostTimeRecoveryService
{
    /// <summary>ICC standard: up to one hour of extra time per day to make up lost overs.</summary>
    public const int MaxExtraTimeMinutesPerDay = 60;

    /// <summary>And up to half an hour of early start, permitted only against arrears already incurred.</summary>
    public const int MaxEarlyStartMinutes = 30;

    /// <summary>
    /// Plans the recovery across the remaining days.
    /// </summary>
    /// <param name="oversLostByDay">Overs lost to weather on each day, indexed from day 1.</param>
    /// <param name="totalDays">Days in the match.</param>
    /// <param name="scheduledOversPerDay">Normally ninety.</param>
    /// <param name="minutesPerOver">The attacks' over rate - a slow attack recovers fewer overs from the same extra hour, which is a real and under-appreciated penalty.</param>
    public LostTimeRecoveryPlan Plan(
        IReadOnlyList<int> oversLostByDay, int totalDays, int scheduledOversPerDay = 90, double minutesPerOver = 4.0)
    {
        var plans = new List<DayRecoveryPlan>();

        // How many overs an hour of extra time actually buys. A four-seam attack getting through
        // 4.6 minutes an over recovers about thirteen; a spin-heavy one nearer twenty-three.
        int oversPerExtraHour = (int)Math.Floor(MaxExtraTimeMinutesPerDay / Math.Max(2.0, minutesPerOver));
        int oversPerEarlyStart = (int)Math.Floor(MaxEarlyStartMinutes / Math.Max(2.0, minutesPerOver));

        int arrears = 0;
        int totalLost = 0;
        int totalRecovered = 0;

        for (int day = 1; day <= totalDays; day++)
        {
            int lostToday = day - 1 < oversLostByDay.Count ? Math.Max(0, oversLostByDay[day - 1]) : 0;
            totalLost += lostToday;

            // An early start can only address arrears carried in from previous days - you cannot
            // start early to make up time you have not yet lost.
            // An early start is equally impossible on a day that never starts.
            bool washedOutDay = lostToday >= scheduledOversPerDay;
            int earlyStartOvers = washedOutDay ? 0 : Math.Min(arrears, oversPerEarlyStart);
            int earlyStartMinutes = earlyStartOvers > 0 ? MaxEarlyStartMinutes : 0;

            // Extra time at the end can address arrears AND anything lost today, because by then
            // the day's weather has already happened.
            //
            // But a day that was washed out COMPLETELY gets none of it: you cannot take an extra
            // hour at the end of a day on which no play was possible at all. Without this a lost day
            // was credited with its own extra time and a full washout came back almost entirely,
            // which is not what happens - a lost day genuinely shortens a match.
            bool completeWashout = lostToday >= scheduledOversPerDay;

            int outstandingAfterEarlyStart = arrears - earlyStartOvers + lostToday;
            int extraTimeOvers = completeWashout
                ? 0
                : Math.Min(Math.Max(0, outstandingAfterEarlyStart), oversPerExtraHour);
            int extraTimeMinutes = extraTimeOvers > 0
                ? (int)Math.Ceiling(extraTimeOvers * minutesPerOver)
                : 0;

            int additional = earlyStartOvers + extraTimeOvers;
            int revised = scheduledOversPerDay - lostToday + additional;

            // A day cannot bowl a negative number of overs, and cannot make up what the weather took
            // beyond what the extra time allows.
            revised = Math.Max(0, revised);

            arrears = Math.Max(0, arrears + lostToday - additional);
            totalRecovered += additional;

            plans.Add(new DayRecoveryPlan(day, earlyStartMinutes, extraTimeMinutes, additional, revised,
                DescribeDay(day, lostToday, earlyStartMinutes, extraTimeMinutes, additional)));
        }

        // Whatever is still owed after the final day is gone - there is no sixth day to claim it on.
        int permanentlyLost = arrears;

        string summary = totalLost == 0
            ? "No time lost."
            : permanentlyLost == 0
                ? $"All {totalLost} overs lost to the weather were made up through early starts and extra time."
                : $"{totalRecovered} of {totalLost} lost overs recovered; {permanentlyLost} gone for good.";

        return new LostTimeRecoveryPlan(plans, totalLost, totalRecovered, permanentlyLost, summary);
    }

    /// <summary>
    /// The net overs a match will actually contain, after the weather has taken its share and the
    /// playing conditions have clawed back what they can.
    /// </summary>
    public int NetMatchOvers(
        IReadOnlyList<int> oversLostByDay, int totalDays, int scheduledOversPerDay = 90, double minutesPerOver = 4.0)
    {
        var plan = Plan(oversLostByDay, totalDays, scheduledOversPerDay, minutesPerOver);
        return plan.Days.Sum(d => d.RevisedDayOvers);
    }

    /// <summary>
    /// The minimum overs that must be bowled in the final hour of the last day. A side batting to
    /// save a match knows exactly how many it has left to survive, and a fielding side cannot simply
    /// bowl slowly to run out the clock - which is precisely why the rule exists.
    /// </summary>
    public const int MinimumOversInFinalHour = 15;

    private static string DescribeDay(int day, int lost, int earlyStart, int extraTime, int additional)
    {
        if (lost == 0 && additional == 0) return $"Day {day}: full day's play scheduled.";

        var parts = new List<string>();
        if (lost > 0) parts.Add($"{lost} overs lost");
        if (earlyStart > 0) parts.Add($"{earlyStart}-minute early start");
        if (extraTime > 0) parts.Add($"{extraTime} minutes of extra time");

        return additional > 0
            ? $"Day {day}: {string.Join(", ", parts)} - {additional} overs made up."
            : $"Day {day}: {string.Join(", ", parts)}.";
    }
}
