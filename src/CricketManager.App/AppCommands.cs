using CricketManager.Data.Persistence;
using CricketManager.Data.Seeding;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.Services;

namespace CricketManager.App;

/// <summary>
/// Phase 17: the headless game loop's three commands, factored out of <c>Program.cs</c> so they
/// are directly testable in-process (matching this project's own hand-rolled <c>TestRunner</c>
/// style) rather than only reachable by spawning a subprocess. <c>Program.cs</c> stays a thin
/// argument-parsing shim over these three methods; none of them does any console I/O itself.
/// </summary>
public static class AppCommands
{
    /// <summary>The default multi-country seed a fresh game starts from when the caller names none.</summary>
    public static readonly string[] DefaultCountries = { "Pakistan", "Australia", "England", "India" };

    public sealed record NewGameResult(
        string SaveDirectory, int WorldSeed, DateOnly StartDate,
        int TeamCount, int PlayerCount, int CompetitionCount, IReadOnlyList<string> Countries);

    public sealed record AdvanceResult(
        DateOnly FromDate, DateOnly ToDate, int TotalEvents,
        IReadOnlyDictionary<GameEventType, int> EventCountsByType,
        IReadOnlyList<string> Headlines);

    public sealed record StatusResult(
        DateOnly CurrentDate, int WorldSeed, int TeamCount, int ActivePlayerCount, int RetiredPlayerCount,
        int CompetitionCount, int SeasonsCompleted, string? HumanCoachName, string? HumanTeamName);

    /// <summary>
    /// Seeds a fresh multi-country world (<see cref="WorldSeeder.GenerateInternationalWorld"/> -
    /// "the real seeded world", not the older single-country MVP path) and saves it. Refuses to
    /// silently clobber an existing save at the same directory unless <paramref name="force"/> is
    /// set - a save is not something to overwrite by accident.
    /// </summary>
    public static async Task<NewGameResult> NewGame(
        string saveDirectory,
        int? seed = null,
        DateOnly? startDate = null,
        IReadOnlyList<string>? countries = null,
        int teamsPerCountry = 4,
        int squadSize = 14,
        bool force = false)
    {
        var store = new WorldStateStore(saveDirectory);
        if (!force && await store.ExistsAsync())
        {
            throw new InvalidOperationException(
                $"A save already exists at '{saveDirectory}'. Pass --force to overwrite it.");
        }

        var effectiveStart = startDate ?? new DateOnly(2026, 1, 1);
        var effectiveSeed = seed ?? Random.Shared.Next();
        var effectiveCountries = (countries is { Count: > 0 } ? countries : DefaultCountries).ToArray();

        var seeder = new WorldSeeder(effectiveSeed, effectiveStart);
        var international = seeder.GenerateInternationalWorld(
            effectiveCountries, teamsPerCountry, squadSize, effectiveStart.Year);
        var world = WorldSeeder.AssembleWorldState(international);
        var calendar = new GameCalendar(effectiveStart, worldSeed: effectiveSeed);

        await store.SaveAsync(world, calendar);

        return new NewGameResult(
            saveDirectory, effectiveSeed, effectiveStart,
            world.Teams.Count, world.Players.Count, world.Competitions.Count, effectiveCountries);
    }

    /// <summary>
    /// Loads a save, moves the clock forward (by day count, by week count, or to a target date -
    /// exactly one of the three, day-by-day underneath either way via
    /// <see cref="WorldClockService.AdvanceTo"/>/<see cref="WorldClockService.AdvanceDays"/>/
    /// <see cref="WorldClockService.AdvanceWeeks"/> so no intervening day's events are skipped),
    /// and saves the result back. Defaults to one day when nothing is specified.
    /// </summary>
    public static async Task<AdvanceResult> Advance(
        string saveDirectory, int? days = null, int? weeks = null, DateOnly? to = null)
    {
        var store = new WorldStateStore(saveDirectory);
        var (world, calendar) = await store.LoadAsync()
            ?? throw new InvalidOperationException($"No save found at '{saveDirectory}'. Run `new` first.");

        var fromDate = calendar.CurrentDate;
        var clock = new WorldClockService();

        IReadOnlyList<GameEvent> events =
            to is { } targetDate ? clock.AdvanceTo(calendar, world, targetDate)
            : weeks is { } w ? clock.AdvanceWeeks(calendar, world, w)
            : clock.AdvanceDays(calendar, world, days ?? 1);

        await store.SaveAsync(world, calendar);

        var counts = events.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count());
        var headlines = events
            .Where(e => HeadlineEventTypes.Contains(e.Type))
            .Select(e => e.Headline)
            .Take(20)
            .ToList();

        return new AdvanceResult(fromDate, calendar.CurrentDate, events.Count, counts, headlines);
    }

    /// <summary>A curated subset of event types worth surfacing as a headline in an `advance` summary - not every one of the ~100+ GameEventType values, which would drown the console in noise.</summary>
    private static readonly HashSet<GameEventType> HeadlineEventTypes = new()
    {
        GameEventType.SeasonCompleted, GameEventType.CoachDismissed, GameEventType.NationalCoachDismissed,
        GameEventType.PlayerRetired, GameEventType.TrophyContested, GameEventType.TeamPromoted,
        GameEventType.TeamRelegated, GameEventType.HallOfFameInduction, GameEventType.ClubTakeover,
        GameEventType.BoardroomCoup
    };

    /// <summary>Reports on the current state of a save - world-level counts, and (if any) the human-controlled coach's own club, since a fresh headless game has none and that's expected.</summary>
    public static async Task<StatusResult> Status(string saveDirectory)
    {
        var store = new WorldStateStore(saveDirectory);
        var (world, calendar) = await store.LoadAsync()
            ?? throw new InvalidOperationException($"No save found at '{saveDirectory}'.");

        var humanCoach = world.Coaches.FirstOrDefault(c => c.IsHumanControlled);
        string? humanTeamName = null;
        if (humanCoach?.CurrentTeamId is { } teamId && world.Teams.TryGetValue(teamId, out var team))
            humanTeamName = team.Name;

        return new StatusResult(
            calendar.CurrentDate, calendar.WorldSeed, world.Teams.Count,
            world.Players.Count(p => !p.IsRetired), world.Players.Count(p => p.IsRetired),
            world.Competitions.Count, world.CompetitionSeasons.Count(s => s.IsCompleted),
            humanCoach?.FullName, humanTeamName);
    }
}
