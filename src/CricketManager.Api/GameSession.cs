using CricketManager.Data.Persistence;
using CricketManager.Data.Seeding;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Services;

namespace CricketManager.Api;

/// <summary>
/// Phase 17 Part 2: a resident, stateful game session for the desktop UI - distinct from
/// <see cref="CricketManager.App.AppCommands"/>, which is correctly shaped for the CLI
/// (load -> do one thing -> save, per invocation). A UI keeps the world resident in memory across
/// many interactions (every screen navigation, every advance), so it needs its own session object -
/// but it calls the SAME underlying primitives <c>AppCommands</c> already calls
/// (<see cref="WorldStateStore"/>, <see cref="WorldSeeder"/>, <see cref="WorldClockService"/>).
/// Genuine reuse at the primitive layer, not a force-fit of the CLI's per-call shape onto a
/// resident session.
/// </summary>
public sealed class GameSession
{
    private static readonly string[] DefaultCountries = { "Pakistan", "Australia", "England", "India" };
    private static readonly WorldClockService Clock = new();

    public Guid Id { get; }
    public string SaveDirectory { get; }
    public WorldState World { get; private set; }
    public GameCalendar Calendar { get; private set; }

    private readonly WorldStateStore _store;

    private GameSession(Guid id, string saveDirectory, WorldState world, GameCalendar calendar)
    {
        Id = id;
        SaveDirectory = saveDirectory;
        World = world;
        Calendar = calendar;
        _store = new WorldStateStore(saveDirectory);
    }

    /// <summary>Seeds a fresh multi-country world and saves it - mirrors <c>AppCommands.NewGame</c>, but returns a live, resident session instead of a summary record.</summary>
    public static async Task<GameSession> CreateNewAsync(
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
            throw new InvalidOperationException($"A save already exists at '{saveDirectory}'. Pass force=true to overwrite it.");

        var effectiveStart = startDate ?? new DateOnly(2026, 1, 1);
        var effectiveSeed = seed ?? Random.Shared.Next();
        var effectiveCountries = (countries is { Count: > 0 } ? countries : DefaultCountries).ToArray();

        var seeder = new WorldSeeder(effectiveSeed, effectiveStart);
        var international = seeder.GenerateInternationalWorld(effectiveCountries, teamsPerCountry, squadSize, effectiveStart.Year);
        var world = WorldSeeder.AssembleWorldState(international);
        var calendar = new GameCalendar(effectiveStart, worldSeed: effectiveSeed);

        await store.SaveAsync(world, calendar);
        return new GameSession(Guid.NewGuid(), saveDirectory, world, calendar);
    }

    /// <summary>Loads an existing save into a resident session.</summary>
    public static async Task<GameSession?> LoadAsync(string saveDirectory)
    {
        var store = new WorldStateStore(saveDirectory);
        var loaded = await store.LoadAsync();
        if (loaded is null) return null;

        var (world, calendar) = loaded.Value;
        return new GameSession(Guid.NewGuid(), saveDirectory, world, calendar);
    }

    /// <summary>Advances the clock (day count, week count, or to a target date) and persists the result. Returns the events produced, for the caller to summarise however it needs.</summary>
    public IReadOnlyList<GameEvent> Advance(int? days = null, int? weeks = null, DateOnly? to = null)
    {
        IReadOnlyList<GameEvent> events =
            to is { } targetDate ? Clock.AdvanceTo(Calendar, World, targetDate)
            : weeks is { } w ? Clock.AdvanceWeeks(Calendar, World, w)
            : Clock.AdvanceDays(Calendar, World, days ?? 1);

        return events;
    }

    public Task SaveAsync() => _store.SaveAsync(World, Calendar);
}
