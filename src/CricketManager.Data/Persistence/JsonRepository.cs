using System.Reflection;
using System.Text.Json;
using CricketManager.Data.Repositories;

namespace CricketManager.Data.Persistence;

/// <summary>
/// Generic JSON-file repository. One file per entity type, e.g. players.json, coaches.json.
/// Entities must expose a `Guid Id` property.
///
/// This is an intentional MVP substitute for a real database (Section 74: MVP -> Expansion).
/// It satisfies Section 65 (external data files, no source-code changes to add data) today,
/// and gets replaced by an EF Core/SQLite-backed implementation of the same IRepository
/// contract once package access is available - no other code needs to change.
///
/// KNOWN LIMITATION, MIGRATION TRIGGER (decided, not deferred vaguely): the whole file is
/// loaded into memory on construction and fully rewritten on every SaveChangesAsync, with no
/// concurrency/locking. Fine today with 7 entity types and small squads. This stops being
/// fine once match-log volume exists - concretely: MIGRATE TO SQLITE/EF CORE AT THE START OF
/// PHASE 4 (Match Simulation Engine), before ball-by-ball/innings-by-innings match records
/// start accumulating at real volume, and before Phase 71's "thousands of players, 20-30
/// in-game years" target becomes reachable. Do not defer past that point - each additional
/// entity type added after Phase 4 (Matches, News, Auctions...) makes the migration more
/// expensive, not less. The IRepository&lt;T&gt; contract above was deliberately designed so this
/// swap requires no changes to calling code - only a new implementation of this interface.
/// </summary>
public sealed class JsonRepository<T> : IRepository<T> where T : class
{
    private readonly string _filePath;
    private readonly Dictionary<Guid, T> _cache = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = false
    };

    private static readonly PropertyInfo IdProperty =
        typeof(T).GetProperty("Id") ?? throw new InvalidOperationException($"{typeof(T).Name} must have an Id property.");

    public JsonRepository(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, $"{typeof(T).Name.ToLowerInvariant()}s.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json)) return;

        var items = JsonSerializer.Deserialize<List<T>>(json, SerializerOptions) ?? new List<T>();
        foreach (var item in items)
        {
            var id = (Guid)IdProperty.GetValue(item)!;
            _cache[id] = item;
        }
    }

    public Task<T?> GetByIdAsync(Guid id) =>
        Task.FromResult(_cache.TryGetValue(id, out var value) ? value : null);

    public Task<IReadOnlyList<T>> GetAllAsync() =>
        Task.FromResult((IReadOnlyList<T>)_cache.Values.ToList());

    public Task AddAsync(T entity)
    {
        var id = (Guid)IdProperty.GetValue(entity)!;
        _cache[id] = entity;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(T entity)
    {
        var id = (Guid)IdProperty.GetValue(entity)!;
        _cache[id] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        _cache.Remove(id);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync()
    {
        var json = JsonSerializer.Serialize(_cache.Values.ToList(), SerializerOptions);
        File.WriteAllText(_filePath, json);
        return Task.CompletedTask;
    }
}
