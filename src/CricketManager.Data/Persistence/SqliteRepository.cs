using System.Reflection;
using System.Text.Json;
using CricketManager.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace CricketManager.Data.Persistence;

/// <summary>
/// SQLite-backed repository - the migration target JsonRepository's own doc comment and
/// CLAUDE.md tech-debt item 2 called for once real NuGet access existed.
///
/// Storage shape is a deliberate JSON-column hybrid, not a full relational mapping: one
/// table per entity type, each row just (Id, Json). This fixes the actual documented
/// problem - JsonRepository loads the WHOLE file into memory on construction and rewrites
/// the WHOLE file on every SaveChangesAsync, with no concurrency control - by making both
/// operations row-scoped and transactional, without re-mapping every nested value object
/// (BattingAttributes, Reputation, FormState, Matchups, InjuryHistory, ...) into its own
/// relational shape. That full mapping would be a much larger and much riskier undertaking
/// for very little payoff today: nothing in this codebase currently runs a SQL WHERE clause
/// against a nested field - every filter query (PlayerStatsQueryService,
/// GroundRecordsService, PartnershipRecordsService, ...) already filters in C# over
/// already-loaded candidates, exactly as it does against JsonRepository today. If a query
/// pattern ever needs server-side filtering on a nested field, that is the trigger to
/// promote this to real relational mapping - not a reason to build it pre-emptively now.
///
/// Changes are staged in memory and flushed as ONE transaction of only the changed rows on
/// SaveChangesAsync - never a full-table rewrite. GetByIdAsync/GetAllAsync both consult the
/// staged changes first, so a caller reading its own not-yet-saved writes behaves exactly
/// like JsonRepository always did (Add/Update/Delete then GetAllAsync mid-session, before
/// SaveChangesAsync, sees the pending state).
/// </summary>
public sealed class SqliteRepository<T> : IRepository<T> where T : class
{
    private readonly string _connectionString;
    private readonly string _table;

    // null value = a pending delete (tombstone); consulted before hitting the database.
    private readonly Dictionary<Guid, T?> _pending = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        IncludeFields = false
    };

    private static readonly PropertyInfo IdProperty =
        typeof(T).GetProperty("Id") ?? throw new InvalidOperationException($"{typeof(T).Name} must have an Id property.");

    public SqliteRepository(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _table = typeof(T).Name + "s";

        // Pooling disabled deliberately: Microsoft.Data.Sqlite pools native connections by
        // default, so Dispose() does not release the underlying OS file handle immediately.
        // On Windows that leaves game.db locked for a moment after every operation returns -
        // harmless in normal play, but it broke every test that deletes its temp save
        // directory right after use (Directory.Delete threw "being used by another process").
        // JsonRepository never held a file handle open between operations either, so this
        // keeps SqliteRepository's observable lifecycle the same as the repository it replaces.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // WAL mode is stored in the database file header, so setting it once here is enough
        // for every future connection this or any other repository sharing the file opens -
        // it is what lets multiple per-type repositories (separate connections, same file)
        // read and write concurrently instead of serializing on a single file handle.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        using var create = connection.CreateCommand();
        create.CommandText = $"CREATE TABLE IF NOT EXISTS \"{_table}\" (Id TEXT PRIMARY KEY, Json TEXT NOT NULL);";
        create.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public async Task<T?> GetByIdAsync(Guid id)
    {
        if (_pending.TryGetValue(id, out var pendingValue))
            return pendingValue;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Json FROM \"{_table}\" WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id.ToString());

        var result = await command.ExecuteScalarAsync();
        return result is string json ? JsonSerializer.Deserialize<T>(json, SerializerOptions) : null;
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        var results = new Dictionary<Guid, T>();

        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT Id, Json FROM \"{_table}\";";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = Guid.Parse(reader.GetString(0));
                var entity = JsonSerializer.Deserialize<T>(reader.GetString(1), SerializerOptions);
                if (entity is not null) results[id] = entity;
            }
        }

        // Staged changes override what is on disk - an Add/Update not yet saved must be
        // visible, and a pending delete must not appear, even though neither has hit the DB.
        foreach (var (id, value) in _pending)
        {
            if (value is null) results.Remove(id);
            else results[id] = value;
        }

        return results.Values.ToList();
    }

    public Task AddAsync(T entity)
    {
        var id = (Guid)IdProperty.GetValue(entity)!;
        _pending[id] = entity;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(T entity)
    {
        var id = (Guid)IdProperty.GetValue(entity)!;
        _pending[id] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        _pending[id] = null;
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync()
    {
        if (_pending.Count == 0) return;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var (id, value) in _pending)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            if (value is null)
            {
                command.CommandText = $"DELETE FROM \"{_table}\" WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", id.ToString());
            }
            else
            {
                command.CommandText =
                    $"INSERT INTO \"{_table}\" (Id, Json) VALUES (@id, @json) " +
                    "ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json;";
                command.Parameters.AddWithValue("@id", id.ToString());
                command.Parameters.AddWithValue("@json", JsonSerializer.Serialize(value, SerializerOptions));
            }

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        _pending.Clear();
    }
}
