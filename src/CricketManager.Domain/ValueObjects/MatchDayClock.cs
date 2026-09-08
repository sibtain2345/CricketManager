using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>The three sessions of a day's play, plus the extra time that can be claimed at the end of it.</summary>
public enum SessionType
{
    Morning,
    Afternoon,
    Evening,
    ExtraHalfHour
}

/// <summary>Whether play can continue, and with whom.</summary>
public enum LightVerdict
{
    /// <summary>Fine. Anybody can bowl.</summary>
    Clear,

    /// <summary>Gloomy. The umpires will allow play to continue, but only with spin - the ball a batter can pick up. Continuing is a real tactical choice.</summary>
    SpinOnly,

    /// <summary>Too dark. Play is off whoever is bowling.</summary>
    Offered
}

/// <summary>
/// The clock for one day's play.
///
/// The project had a date calendar but no TIME - which meant a day was a flat ninety overs and
/// nothing about a day's play could depend on when it was happening. That removes several things
/// that matter in real cricket: a session is two hours and a captain plans in sessions; a slow
/// over rate genuinely costs a side overs; the light goes in the evening and takes the quicks out
/// of the attack; and the extra half hour exists precisely to claw back overs that were lost.
///
/// Times are local and follow the standard first-class shape: 11:00 start, forty minutes for lunch,
/// twenty for tea, scheduled close at 18:00, with up to half an hour extra if the day is short of
/// overs and the light holds.
/// </summary>
public sealed class MatchDayClock
{
    public int Day { get; init; } = 1;

    public TimeOnly StartOfPlay { get; init; } = new(11, 0);
    public TimeOnly Lunch { get; init; } = new(13, 0);
    public TimeOnly AfterLunch { get; init; } = new(13, 40);
    public TimeOnly Tea { get; init; } = new(15, 40);
    public TimeOnly AfterTea { get; init; } = new(16, 0);
    public TimeOnly ScheduledClose { get; init; } = new(18, 0);

    /// <summary>The extra half hour, claimable when the day is short of its scheduled overs and the light allows.</summary>
    public TimeOnly LatestPossibleClose { get; init; } = new(18, 30);

    public int ScheduledOvers { get; init; } = 90;

    public TimeOnly CurrentTime { get; private set; }
    public int OversBowledToday { get; private set; }
    public bool ExtraHalfHourClaimed { get; private set; }
    public bool PlayAbandonedForDay { get; private set; }

    public MatchDayClock() => CurrentTime = StartOfPlay;

    public MatchDayClock(int day, int scheduledOvers = 90)
    {
        Day = day;
        ScheduledOvers = scheduledOvers;
        CurrentTime = StartOfPlay;
    }

    public SessionType CurrentSession =>
        CurrentTime < Lunch ? SessionType.Morning
        : CurrentTime < Tea ? SessionType.Afternoon
        : CurrentTime < ScheduledClose ? SessionType.Evening
        : SessionType.ExtraHalfHour;

    /// <summary>Minutes of playing time left today, allowing for the intervals still to come.</summary>
    public double MinutesRemaining
    {
        get
        {
            if (PlayAbandonedForDay) return 0;

            var close = ExtraHalfHourClaimed ? LatestPossibleClose : ScheduledClose;
            if (CurrentTime >= close) return 0;

            double minutes = (close - CurrentTime).TotalMinutes;

            // Subtract intervals that have not yet been taken.
            if (CurrentTime < Lunch) minutes -= (AfterLunch - Lunch).TotalMinutes;
            if (CurrentTime < Tea) minutes -= (AfterTea - Tea).TotalMinutes;

            return Math.Max(0, minutes);
        }
    }

    /// <summary>
    /// Advances the clock by one over, taking however long that over actually took, and steps over
    /// any interval that falls in the middle of it.
    /// </summary>
    public void BowlOver(double minutes)
    {
        CurrentTime = CurrentTime.Add(TimeSpan.FromMinutes(minutes));
        OversBowledToday++;

        // Walk through an interval if we have reached it.
        if (CurrentTime >= Lunch && CurrentTime < AfterLunch) CurrentTime = AfterLunch;
        if (CurrentTime >= Tea && CurrentTime < AfterTea) CurrentTime = AfterTea;
    }

    /// <summary>
    /// Claims the extra half hour. Only available when the day is genuinely short of its scheduled
    /// overs - it exists to make up lost time, not to extend a day that got through its work.
    /// </summary>
    public bool TryClaimExtraHalfHour()
    {
        if (ExtraHalfHourClaimed || OversBowledToday >= ScheduledOvers) return false;
        ExtraHalfHourClaimed = true;
        return true;
    }

    public void AbandonForDay() => PlayAbandonedForDay = true;

    public bool DayIsOver =>
        PlayAbandonedForDay
        || CurrentTime >= (ExtraHalfHourClaimed ? LatestPossibleClose : ScheduledClose)
        || (OversBowledToday >= ScheduledOvers && CurrentTime >= ScheduledClose);

    /// <summary>Whether the day is short enough to be worth claiming the extra half hour for.</summary>
    public bool IsBehindOnOvers => OversBowledToday < ScheduledOvers;

    /// <summary>Overs short of the scheduled quota - the figure a side gets fined for.</summary>
    public int OversShortfall => Math.Max(0, ScheduledOvers - OversBowledToday);
}

/// <summary>
/// Issues 1/2/5/6/7 (external probe pass): the running clock for an ENTIRE multi-day match, not
/// just one day of it. MatchDayClock and OverRateService's light/over-rate machinery both already
/// existed, fully built, but had exactly one caller between them (EstimateMatchBalls, a one-time
/// PRE-MATCH estimate of the whole match's total ball budget) - nothing ever actually TICKED a
/// clock while the match was being played, which is why every multi-day key moment printed
/// "[Day ?]": InningsState had no per-delivery notion of which day or session a ball belonged to,
/// bad light never actually stopped a day's play, and TakeBreak/Rest(BreakLength.Session) were
/// dead code with no caller at all.
///
/// One MatchTimeline is created ONCE per match and threaded through every innings, because an
/// innings can genuinely span more than one real day - day numbering has to stay continuous
/// across that boundary rather than being recomputed per innings-call from a naive
/// balls-bowled-so-far division (the old DayFor helper), which assumed every day bowled exactly
/// its scheduled quota and would silently disagree with reality the moment a day was cut short.
/// </summary>
public sealed class MatchTimeline
{
    private readonly int _totalDays;
    private readonly int _scheduledOversPerDay;
    private readonly OverRateService _overRates;
    private readonly MatchWeather? _weather;
    private readonly int _month;

    public MatchDayClock Clock { get; private set; }
    public int Day => Clock.Day;
    public SessionType CurrentSession => Clock.CurrentSession;
    public TimeOnly CurrentTime => Clock.CurrentTime;

    public MatchTimeline(int startingDay, int totalDays, int scheduledOversPerDay, OverRateService overRates,
        MatchWeather? weather, int month)
    {
        _totalDays = totalDays;
        _scheduledOversPerDay = scheduledOversPerDay;
        _overRates = overRates;
        _weather = weather;
        _month = month;
        Clock = new MatchDayClock(startingDay, scheduledOversPerDay);
    }

    /// <summary>
    /// Records one over's playing time and, if the day has fallen behind its scheduled quota and
    /// the light still allows it, claims the extra half hour automatically - real umpires grant it
    /// for exactly this reason and it is not a separate decision a captain makes. Returns whether
    /// the day is now over (schedule reached, extra time exhausted, or the day was already
    /// abandoned for bad light).
    /// </summary>
    public bool BowlOver(Player bowler)
    {
        Clock.BowlOver(_overRates.MinutesPerOver(bowler));

        if (!Clock.DayIsOver && !Clock.ExtraHalfHourClaimed && Clock.IsBehindOnOvers
            && Clock.CurrentTime >= Clock.ScheduledClose
            && _overRates.LightLevel(Clock.CurrentTime, _weather, _month) >= 22)
        {
            Clock.TryClaimExtraHalfHour();
        }

        return Clock.DayIsOver;
    }

    /// <summary>What the umpires would rule right now, for whoever is about to bowl.</summary>
    public LightVerdict AssessLight(bool nextBowlerIsSpinner) =>
        _overRates.AssessLight(_overRates.LightLevel(Clock.CurrentTime, _weather, _month), nextBowlerIsSpinner);

    /// <summary>Bad light has stopped play for the day - a real, earlier-than-scheduled close, not merely reaching the clock's own end time.</summary>
    public void EndDayForBadLight() => Clock.AbandonForDay();

    /// <summary>True once the match has used up its last scheduled day - there is nowhere left to advance to.</summary>
    public bool MatchIsOver => Clock.Day >= _totalDays && Clock.DayIsOver;

    /// <summary>Moves to the next scheduled day, fresh at its start-of-play time. False (no-op) if the match has no day left to advance to.</summary>
    public bool TryAdvanceDay()
    {
        if (Clock.Day >= _totalDays) return false;
        Clock = new MatchDayClock(Clock.Day + 1, _scheduledOversPerDay);
        return true;
    }
}

/// <summary>How long an over takes and what the light is doing - the two things that decide how much cricket a day actually contains.</summary>
public sealed class OverRateService
{
    /// <summary>
    /// Minutes for one over from this bowler.
    ///
    /// This is why a day of ninety overs is a target rather than a fact. A genuine quick with a long
    /// run-up takes well over four minutes an over; a spinner gets through in under three. A side
    /// with four seamers simply cannot bowl ninety overs in a day without being fined for it, which
    /// is exactly the real-world pressure that pushes captains towards playing a spinner.
    /// </summary>
    public double MinutesPerOver(Player bowler)
    {
        bool spinner = bowler.Bowling.Spin > bowler.Bowling.Pace;
        if (spinner) return 2.6;

        // Run-up length tracks pace. Express bowlers are the slowest through their overs.
        double pace = AbilityScale.AttributeToHundred(bowler.Bowling.Pace);

        return pace switch
        {
            >= 85 => 4.6,   // genuine express
            >= 70 => 4.2,   // fast
            >= 55 => 3.8,   // fast-medium
            _ => 3.4        // medium
        };
    }

    /// <summary>
    /// How many overs a day is likely to yield from this attack. Used for planning - the real figure
    /// comes from the clock as the overs are actually bowled.
    /// </summary>
    public int EstimateOversInDay(IReadOnlyList<Player> attack, double playingMinutes = 360)
    {
        if (attack.Count == 0) return 90;

        // Weighted by how much each bowler would realistically bowl - the frontline men bowl most.
        double averageMinutes = attack.Average(MinutesPerOver);
        return (int)Math.Round(playingMinutes / averageMinutes);
    }

    /// <summary>
    /// Light level, 0-100. Falls away through the evening session, and cloud takes it down further -
    /// which is why an overcast evening in England ends play and a clear one in Dubai does not.
    /// </summary>
    public double LightLevel(TimeOnly time, MatchWeather? weather, int month = 6)
    {
        // Daylight is deepest in midsummer and shortest at the ends of a season.
        double seasonalFactor = 1 - Math.Abs(month - 6.5) / 6.0 * 0.35;

        // Full light until late afternoon, then a steep fall.
        //
        // Note the explicit comparison: TimeOnly subtraction WRAPS, so 14:00 minus 17:00 is
        // twenty-one hours rather than minus three, and Math.Max on the result does nothing. That
        // made mid-afternoon read as darker than late evening.
        var lightStartsFading = new TimeOnly(17, 0);
        double hoursAfterFive = time <= lightStartsFading ? 0 : (time - lightStartsFading).TotalHours;
        double daylight = 100 - hoursAfterFive * 38 / seasonalFactor;

        double cloudLoss = (weather?.CloudCover ?? 30) / 100.0 * 30;

        return Math.Clamp(daylight - cloudLoss, 0, 100);
    }

    /// <summary>
    /// What the umpires will allow. Between the two thresholds only spin may be bowled - the ball a
    /// batter can actually pick up - which turns bad light into a tactical decision rather than a
    /// simple stoppage: a captain who wants to keep bowling has to bowl his spinners.
    /// </summary>
    public LightVerdict AssessLight(double lightLevel, bool currentBowlerIsSpinner) => lightLevel switch
    {
        >= 45 => LightVerdict.Clear,
        >= 22 => currentBowlerIsSpinner ? LightVerdict.SpinOnly : LightVerdict.Offered,
        _ => LightVerdict.Offered
    };

    /// <summary>
    /// Whether the fielding side wants to keep going in gloom by turning to spin.
    ///
    /// A genuine decision with two sides to it: a side pressing for a win wants every over it can
    /// get, but bowling spin because the light demands it means taking your quicks out of the attack
    /// at the moment they might have been most dangerous. A side under pressure is delighted to go off.
    /// </summary>
    public bool WantsToContinueInGloom(bool pressingForWin, bool hasCapableSpinner, double captainJudgement, Random random)
    {
        if (!hasCapableSpinner) return false;

        double appetite = pressingForWin ? 70 : 25;
        appetite += (captainJudgement - 50) * 0.2;

        return random.NextDouble() < Math.Clamp(appetite / 100.0, 0.05, 0.95);
    }
}
