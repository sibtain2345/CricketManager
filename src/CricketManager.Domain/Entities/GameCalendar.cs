using System.Text.Json.Serialization;

namespace CricketManager.Domain.Entities;

/// <summary>
/// The world's clock. There is exactly one of these per save, and it is the single source of
/// truth for "what day is it".
///
/// Everything with a duration already existed - injuries have layoffs, infrastructure has
/// build times, contracts have end dates, players have birthdays - but NOTHING advanced time,
/// so every one of those durations was inert. Each service took an `asOf` date as a parameter
/// and trusted the caller to supply a sensible one, which means two callers could disagree
/// about what day it was and a save could sit forever at the moment it was created.
///
/// This is deliberately an entity rather than a static/ambient clock: it has to be saved and
/// reloaded with the world, and a game where the date comes from DateTime.Today would advance
/// while the player wasn't playing.
/// </summary>
public sealed class GameCalendar
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The day the career began. Kept so elapsed career length is answerable without a separate counter.</summary>
    public DateOnly StartDate { get; init; }

    [JsonInclude] public DateOnly CurrentDate { get; private set; }

    /// <summary>
    /// The world's master RNG seed, fixed when the career is created and saved with it.
    ///
    /// Everything the clock does stochastically - ageing, retirement, and anything later systems
    /// add to the annual rollover - derives its stream from this plus the year, so advancing the
    /// same save twice produces the SAME world. The rollover previously seeded itself with
    /// HashCode.Combine(...), and .NET randomises string/struct hash codes per process, so the
    /// seed silently differed between runs: a save advanced on Tuesday aged and retired different
    /// players than the same save advanced on Wednesday. That makes a career simulation
    /// impossible to debug and a save file not really a save.
    ///
    /// <para>
    /// **The default is <see cref="Random.Shared"/>.Next() - deliberately, and ONLY correct for a
    /// genuinely new, one-off career.** Any caller that needs a REPRODUCIBLE calendar - a test, a
    /// benchmark, anything comparing two runs - MUST pass an explicit <c>worldSeed</c>, or the two
    /// runs will silently diverge. This has bitten the test suite before (tech-debt item 12). If
    /// you are creating a calendar and do not have a strong reason to want an unpredictable seed,
    /// you almost certainly want to pass one. <see cref="NewRandomCareer"/> is the named,
    /// clearly-intentional way to get the unpredictable-seed behaviour on purpose.
    /// </para>
    /// </summary>
    public int WorldSeed { get; init; } = Random.Shared.Next();

    public GameCalendar() { }

    /// <summary>
    /// Creates a calendar with an EXPLICIT seed. This is the encouraged path everywhere except a
    /// brand-new player-facing career - passing the seed means "two runs of this will match",
    /// which is what a test, a comparison or a debug session needs. Pass <c>worldSeed: null</c>
    /// only if you specifically want the unpredictable default (and prefer <see cref="NewRandomCareer"/>
    /// for that, so the intent is unmistakable at the call site).
    /// </summary>
    public GameCalendar(DateOnly startDate, int? worldSeed = null)
    {
        StartDate = startDate;
        CurrentDate = startDate;
        if (worldSeed is { } seed) WorldSeed = seed;
    }

    /// <summary>
    /// A calendar for a genuinely new one-off career, with an unpredictable master seed. The one
    /// place the <c>Random.Shared.Next()</c> default is actually what you want - named so a call
    /// site that wants it says so, and an accidental omission of an explicit seed elsewhere stands
    /// out rather than blending in.
    /// </summary>
    public static GameCalendar NewRandomCareer(DateOnly startDate) => new(startDate, worldSeed: null);

    /// <summary>A deterministic RNG stream for a given year's processing. Same save, same year, same stream - always.</summary>
    public Random RandomForYear(int year) => new(unchecked(WorldSeed * 397 + year));

    /// <summary>
    /// The same idea, keyed by day rather than year - job-market follow-up (contract renewal),
    /// the first DAILY-tick mechanism in this codebase to need a probability roll (every prior
    /// daily-processing step - injury recovery, infrastructure completion, competition windows -
    /// only ever checks whether a fixed, already-known due date has arrived). Reusing
    /// RandomForYear(date.Year) here would have been a real, subtle bug: it re-seeds an IDENTICAL
    /// stream on every call within the same year, so two different renewal decisions falling on
    /// two different days of the same year would draw CORRELATED, not independent, rolls -
    /// whichever one happened to be "the first roll of the day" on its own date would get the
    /// exact same outcome as any other contract that was also first-roll-of-the-day. Keying on
    /// DayNumber (a stable, unique integer per calendar date) avoids that entirely.
    /// </summary>
    public Random RandomForDay(DateOnly date) => new(unchecked(WorldSeed * 397 + date.DayNumber));

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 1: the same idea as RandomForDay, but for the new
    /// weekly tick cadence - deliberately a DIFFERENT multiplier from RandomForDay rather than
    /// reusing it, so a weekly-cadence roll and a daily-cadence roll that happen to land on the
    /// same calendar date never draw from the identical stream.
    /// </summary>
    public Random RandomForWeek(DateOnly date) => new(unchecked(WorldSeed * 613 + date.DayNumber));

    /// <summary>
    /// The monthly counterpart. Keyed on an ABSOLUTE, monotonically increasing month index
    /// (year * 12 + month), not just the 1-12 month-of-year alone - the same correlation hazard
    /// RandomForDay was built to avoid for daily rolls applies here too: March 2026 and March
    /// 2027 must not draw from the same stream just because they share a calendar month number.
    /// </summary>
    public Random RandomForMonth(int year, int month) => new(unchecked(WorldSeed * 991 + year * 12 + month));

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: the quarterly-tick stream. A DIFFERENT multiplier
    /// again, for the same reason RandomForDay and RandomForWeek each have their own: a
    /// quarter-boundary month is also a month boundary, so a quarterly roll and a monthly roll
    /// on the same date must not draw from the identical RandomForMonth stream. Keyed on an
    /// absolute quarter index (year*4 + quarter, quarter 0-3).
    /// </summary>
    public Random RandomForQuarter(int year, int quarter) => new(unchecked(WorldSeed * 1409 + year * 4 + quarter));

    /// <summary>
    /// Phase 6, Slice 6.1: a per-fixture stream so each match played on a given day gets its own
    /// deterministic randomness, independent of every other fixture that day and of the daily/
    /// monthly/etc. streams. `index` is the fixture's position in the day's deterministically
    /// sorted list of due fixtures - NOT anything derived from a Guid, per this project's
    /// determinism history. Its own multiplier for the same reason every other stream has one.
    /// </summary>
    public Random RandomForFixture(DateOnly date, int index) => new(unchecked(WorldSeed * 3011 + date.DayNumber * 131 + index));

    /// <summary>
    /// Phase 9, Slice 9.5: a stream for one franchise-league auction, keyed on the year and a small
    /// integer competition index - its own multiplier so the auction never draws from the same
    /// stream as the daily competition-window processing that triggers it (which already uses
    /// RandomForDay, and a second RandomForDay(date) call in the same tick would be correlated).
    /// </summary>
    public Random RandomForAuction(int year, int competitionIndex) => new(unchecked(WorldSeed * 5077 + year * 37 + competitionIndex));

    /// <summary>Calendar year. Cricket seasons don't align to it everywhere (an Australian or English season straddles or sits inside one differently), which is why season windows are a competition-level concept rather than something the clock decides.</summary>
    [JsonIgnore] public int Year => CurrentDate.Year;

    [JsonIgnore] public int DaysElapsed => CurrentDate.DayNumber - StartDate.DayNumber;
    [JsonIgnore] public int SeasonsElapsed => DaysElapsed / 365;

    /// <summary>
    /// Moves the clock. Only WorldClockService should call this - advancing the date without
    /// running the day's processing would skip injury returns, completed building work and
    /// everything else that is supposed to happen on the way, which is precisely the bug this
    /// whole design exists to prevent.
    /// </summary>
    internal void SetCurrentDate(DateOnly date)
    {
        if (date < CurrentDate)
            throw new InvalidOperationException($"Time does not run backwards: cannot move the calendar from {CurrentDate} to {date}.");
        CurrentDate = date;
    }

    /// <summary>Escape hatch for loading a save or setting up a scenario, where the date is being restored rather than advanced.</summary>
    public static GameCalendar Restore(DateOnly startDate, DateOnly currentDate, int? worldSeed = null) =>
        new(startDate, worldSeed) { CurrentDate = currentDate };
}
