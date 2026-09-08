using CricketManager.Domain.Entities;

namespace CricketManager.Domain.Services;

/// <summary>A competition's concrete dates for one specific staging.</summary>
public sealed record ScheduledCompetition(Competition Competition, int Year, DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Answers "what cricket is on, and when" from each competition's window.
///
/// Two things this gets right that a naive implementation does not:
/// - **Multi-year cycles are anchored to a real staging**, not to year zero. A T20 World Cup
///   staged in 2026 on a two-year cycle happens in 2028 and 2030, not "every even year"
///   by coincidence of the modulus.
/// - **Cross-year seasons are first-class.** An Australian domestic season starting in October
///   and finishing in March belongs to ONE season, and asking whether 14 January falls inside
///   it has to return true. Treating a window as a simple month range inside one calendar year
///   silently breaks every southern-hemisphere competition.
///
/// This deliberately produces WINDOWS, not fixtures. Filling a window with actual matches is
/// the scheduler's job in Phase 4.
/// </summary>
public sealed class CompetitionCalendarService
{
    /// <summary>Is this competition staged in the given year? Years before the anchor are handled properly - a tournament first staged in 2026 did not exist in 2024.</summary>
    public bool IsStagedIn(Competition competition, int year)
    {
        var window = competition.Window;
        if (window.RecurrenceYears <= 1) return year >= window.AnchorYear;
        if (year < window.AnchorYear) return false;

        return (year - window.AnchorYear) % window.RecurrenceYears == 0;
    }

    /// <summary>The concrete start/end dates for one staging, or null if it isn't staged that year.</summary>
    public ScheduledCompetition? GetStaging(Competition competition, int year)
    {
        if (!IsStagedIn(competition, year)) return null;

        var window = competition.Window;
        var start = SafeDate(year, window.StartMonth, window.StartDay);

        // A window that ends "before" it starts runs into the following calendar year.
        var end = window.CrossesYearBoundary
            ? SafeDate(year + 1, window.EndMonth, window.EndDay)
            : SafeDate(year, window.EndMonth, window.EndDay);

        return new ScheduledCompetition(competition, year, start, end);
    }

    public IReadOnlyList<ScheduledCompetition> GetStagingsForYear(IEnumerable<Competition> competitions, int year) =>
        competitions.Select(c => GetStaging(c, year)).Where(s => s is not null).Select(s => s!)
            .OrderBy(s => s.StartDate).ToList();

    /// <summary>
    /// Is this competition actually being played on this date? Checks the staging that began
    /// in the current year AND the one that began last year, because a cross-year season is
    /// still running in January under the previous year's staging.
    /// </summary>
    public bool IsInWindow(Competition competition, DateOnly date)
    {
        foreach (int year in new[] { date.Year, date.Year - 1 })
        {
            var staging = GetStaging(competition, year);
            if (staging is not null && date >= staging.StartDate && date <= staging.EndDate)
                return true;
        }
        return false;
    }

    /// <summary>Everything being played on a given date - the "what's on right now" view a schedule screen needs.</summary>
    public IReadOnlyList<Competition> GetActiveCompetitions(IEnumerable<Competition> competitions, DateOnly date) =>
        competitions.Where(c => IsInWindow(c, date)).ToList();

    /// <summary>
    /// When is this competition next staged after the given date? This is what makes long-term
    /// planning possible - a coach asking "how long until the next World Cup" is asking whether
    /// to build a squad now or in three years, which is one of the central decisions of a career.
    /// </summary>
    public ScheduledCompetition? GetNextStaging(Competition competition, DateOnly after, int searchYears = 12)
    {
        for (int year = after.Year; year <= after.Year + searchYears; year++)
        {
            var staging = GetStaging(competition, year);
            if (staging is not null && staging.StartDate > after) return staging;
        }
        return null;
    }

    /// <summary>Clamps a day to the month's real length so a 31st in a 30-day month, or 29 February in a common year, doesn't throw.</summary>
    private static DateOnly SafeDate(int year, int month, int day)
    {
        int safeMonth = Math.Clamp(month, 1, 12);
        int safeDay = Math.Clamp(day, 1, DateTime.DaysInMonth(year, safeMonth));
        return new DateOnly(year, safeMonth, safeDay);
    }
}
