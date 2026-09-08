namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// When a competition is staged: which years it happens in, and which part of those years it
/// occupies.
///
/// This is what ties competitions to the world clock. Cricket competitions are not
/// interchangeable annual events - a T20 World Cup runs on a two-year cycle, a 50-over World
/// Cup on four, domestic seasons sit in a country-specific window (an English season is
/// April-September; an Australian one runs October-March and crosses the new year), and
/// franchise leagues occupy a defended slot that everything else has to schedule around.
/// Without this, every competition would implicitly be "annual, all year", which is the
/// assumption that makes a career game's calendar feel fake.
///
/// This describes the SHAPE of the calendar, not the fixtures. Actual match dates come from
/// the scheduler (Phase 4), which will fill these windows from the ICC Future Tours Programme
/// where real data exists and generate realistic patterns where it doesn't.
/// </summary>
public sealed class CompetitionWindow
{
    /// <summary>1 = every year, 2 = every second year (T20 World Cup), 4 = every fourth (50-over World Cup).</summary>
    public int RecurrenceYears { get; set; } = 1;

    /// <summary>A year the competition is known to have been staged. Every staging is this year plus a whole number of cycles, so the phase of the cycle is anchored to something real rather than to year zero.</summary>
    public int AnchorYear { get; set; } = 2026;

    public int StartMonth { get; set; } = 1;
    public int StartDay { get; set; } = 1;
    public int EndMonth { get; set; } = 12;
    public int EndDay { get; set; } = 31;

    /// <summary>True when the window runs past 31 December into the next calendar year - normal for southern-hemisphere seasons and not an edge case to be papered over.</summary>
    public bool CrossesYearBoundary => EndMonth < StartMonth || (EndMonth == StartMonth && EndDay < StartDay);

    /// <summary>The set of calendar months (1-12) this window touches, unrolled across a year boundary if needed.</summary>
    public IEnumerable<int> MonthsTouched()
    {
        int m = StartMonth;
        while (true)
        {
            yield return m;
            if (m == EndMonth) yield break;
            m = m % 12 + 1;
        }
    }

    /// <summary>
    /// Post-Phase-7/8/9 rectification (Section H): do two competition windows overlap in the
    /// calendar? Used to decide whether a coach's year-round job clashes with a franchise campaign.
    /// A month-granularity check - enough for scheduling decisions, and it treats a shared boundary
    /// month as a clash (conservative, which is the safe side for a coach's availability).
    /// </summary>
    public bool OverlapsMonths(CompetitionWindow other)
    {
        var mine = MonthsTouched().ToHashSet();
        return other.MonthsTouched().Any(mine.Contains);
    }

    public static CompetitionWindow Annual(int startMonth, int startDay, int endMonth, int endDay) =>
        new() { RecurrenceYears = 1, StartMonth = startMonth, StartDay = startDay, EndMonth = endMonth, EndDay = endDay };

    /// <summary>A global tournament on a multi-year cycle, e.g. Cycle(2, 2026, 2, 6, 3, 8) for a T20 World Cup staged in even years across February-March.</summary>
    public static CompetitionWindow Cycle(int recurrenceYears, int anchorYear, int startMonth, int startDay, int endMonth, int endDay) =>
        new()
        {
            RecurrenceYears = recurrenceYears,
            AnchorYear = anchorYear,
            StartMonth = startMonth,
            StartDay = startDay,
            EndMonth = endMonth,
            EndDay = endDay
        };
}
