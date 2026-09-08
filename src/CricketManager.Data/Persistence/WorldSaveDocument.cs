using CricketManager.Domain.Entities;
using CricketManager.Domain.Services;

namespace CricketManager.Data.Persistence;

/// <summary>
/// Phase 17: the single wrapper <see cref="WorldStateStore"/> persists - the whole simulated
/// world in one JSON document, reusing <see cref="SqliteRepository{T}"/>'s existing JSON-column
/// storage rather than mapping <see cref="WorldState"/>'s ~46 collections onto individual tables.
///
/// There is always at most one of these per save directory, so <see cref="Id"/> is a fixed
/// constant (<see cref="WorldStateStore.DocumentId"/>) rather than a freshly-generated Guid - the
/// point is a stable row to overwrite, not a collection of many.
/// </summary>
public sealed class WorldSaveDocument
{
    public Guid Id { get; init; } = WorldStateStore.DocumentId;

    public required WorldState World { get; init; }
    public required GameCalendar Calendar { get; init; }

    /// <summary>
    /// A forward-compat guard, not real migration machinery - there is nothing to migrate from
    /// yet. <see cref="WorldStateStore.LoadAsync"/> refuses to load a save stamped with a version
    /// newer than the running code understands, with a clear error, rather than silently
    /// misreading it.
    /// </summary>
    public int SchemaVersion { get; init; } = WorldStateStore.CurrentSchemaVersion;

    public DateTimeOffset SavedAtUtc { get; init; }
}
