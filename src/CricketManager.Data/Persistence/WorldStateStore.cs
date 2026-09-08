using CricketManager.Domain.Entities;
using CricketManager.Domain.Services;

namespace CricketManager.Data.Persistence;

/// <summary>
/// Phase 17: persists the entire simulated world - <see cref="WorldState"/> plus its driving
/// <see cref="GameCalendar"/> - as one JSON document, closing the gap flagged since Phase 4's own
/// planning notes ("a save currently loses ~19 WorldState collections"). This is the first
/// persistence path the simulation loop itself actually uses; <see cref="GameDataContext"/>'s 25
/// entity-per-table repositories remain generic, tested CRUD infrastructure with their own
/// separate purpose (see that class's own doc comments) and are left untouched by this class.
///
/// Reuses <see cref="SqliteRepository{T}"/> completely unmodified against one more table
/// (<c>WorldSaveDocuments</c>) in the SAME <c>game.db</c> file <see cref="GameDataContext"/>
/// already writes to for a given save directory - a save directory still means exactly one file,
/// and this class can coexist with a <see cref="GameDataContext"/> pointed at the same directory
/// without either one knowing the other exists.
/// </summary>
public sealed class WorldStateStore
{
    /// <summary>
    /// There is always at most one world per save directory, so every <see cref="WorldSaveDocument"/>
    /// uses this same fixed id - the point is one stable row to overwrite on every save, not a
    /// growing collection of them.
    /// </summary>
    public static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const int CurrentSchemaVersion = 1;

    private readonly SqliteRepository<WorldSaveDocument> _repository;

    public WorldStateStore(string saveDirectory)
    {
        var dbPath = Path.Combine(saveDirectory, "game.db");
        _repository = new SqliteRepository<WorldSaveDocument>(dbPath);
    }

    /// <summary>Saves (or overwrites) the world. A day-by-day <c>advance</c> loop calls this once, at the end, not per day - the repository itself is transactional per call, not per collection.</summary>
    public async Task SaveAsync(WorldState world, GameCalendar calendar)
    {
        var existing = await _repository.GetByIdAsync(DocumentId);
        var document = new WorldSaveDocument
        {
            World = world,
            Calendar = calendar,
            SavedAtUtc = DateTimeOffset.UtcNow
        };

        if (existing is null) await _repository.AddAsync(document);
        else await _repository.UpdateAsync(document);

        await _repository.SaveChangesAsync();
    }

    /// <summary>
    /// Loads the world, or null if this save directory has never been saved to. Calls
    /// <see cref="WorldState.Reindex"/> before returning - an API where a caller has to remember
    /// that step separately is exactly the kind of thing that silently breaks a world (see
    /// WorldState's own history of "welding application to indexing" for the same reason).
    /// </summary>
    public async Task<(WorldState World, GameCalendar Calendar)?> LoadAsync()
    {
        var document = await _repository.GetByIdAsync(DocumentId);
        if (document is null) return null;

        if (document.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"This save was written by a newer version of CricketManager (schema {document.SchemaVersion}); " +
                $"this build only understands up to schema {CurrentSchemaVersion}.");
        }

        document.World.Reindex();
        return (document.World, document.Calendar);
    }

    /// <summary>True if this save directory has a world saved to it already - used by `new` to refuse a silent overwrite.</summary>
    public async Task<bool> ExistsAsync() => await _repository.GetByIdAsync(DocumentId) is not null;
}
