using System.Collections.Concurrent;

namespace CricketManager.Api;

/// <summary>
/// Registered active <see cref="GameSession"/>s, keyed by <see cref="GameSession.Id"/>. A local
/// single-player app only ever has one resident session in practice, but keying by id (rather than
/// a single static field) keeps the API honest about what it actually is - a session registry, not
/// an assumption baked into every endpoint - and costs nothing.
/// </summary>
public sealed class GameSessionStore
{
    private readonly ConcurrentDictionary<Guid, GameSession> _sessions = new();

    public void Add(GameSession session) => _sessions[session.Id] = session;

    public GameSession? Get(Guid id) => _sessions.TryGetValue(id, out var s) ? s : null;

    public GameSession Require(Guid id) =>
        Get(id) ?? throw new KeyNotFoundException($"No active game session with id '{id}'.");
}
