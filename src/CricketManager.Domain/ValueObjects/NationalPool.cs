using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>One player's place in a national format pool - when he came in and why. Reasoning is
/// mandatory: an inclusion has to solve a problem for the team, not just be a name.</summary>
public sealed record NationalPoolEntry(
    Guid PlayerId,
    DateOnly AddedDate,
    string Reasoning)
{
    /// <summary>
    /// A watchlist entry sits in the pool on the strength of form in a DIFFERENT context (white-ball
    /// runs in a franchise league earning a T20I pool slot, say) - a real contender, not yet a
    /// settled one. Non-watchlist entries are the established pool.
    /// </summary>
    public bool Watchlist { get; init; }
}

/// <summary>
/// Post-Phase-6, section E: a national team's player pool FOR ONE FORMAT. Roughly 35-40 players,
/// built by the Head Coach for genuine role and condition coverage with real backup depth - not
/// by naive position-counting - and evolving continuously off performance monitoring. Squads for
/// actual selection are always drawn FROM this, never built independently of it.
///
/// A player can appear in more than one format's pool at once; each format keeps its own pool.
/// </summary>
public sealed class NationalPool
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid NationalTeamId { get; init; }
    public required MatchFormat Format { get; init; }

    [JsonInclude] private readonly List<NationalPoolEntry> _entries = new();
    public IReadOnlyList<NationalPoolEntry> Entries => _entries;

    public bool Contains(Guid playerId) => _entries.Any(e => e.PlayerId == playerId);

    public NationalPoolEntry? EntryFor(Guid playerId) => _entries.FirstOrDefault(e => e.PlayerId == playerId);

    public void Add(Guid playerId, DateOnly date, string reasoning, bool watchlist = false)
    {
        if (Contains(playerId)) return;
        _entries.Add(new NationalPoolEntry(playerId, date, reasoning) { Watchlist = watchlist });
    }

    public bool Remove(Guid playerId) => _entries.RemoveAll(e => e.PlayerId == playerId) > 0;

    /// <summary>Promote a watchlist entry to the established pool once he has genuinely earned it.</summary>
    public void Confirm(Guid playerId, DateOnly date, string reasoning)
    {
        int i = _entries.FindIndex(e => e.PlayerId == playerId);
        if (i >= 0 && _entries[i].Watchlist)
            _entries[i] = _entries[i] with { Watchlist = false, Reasoning = reasoning, AddedDate = _entries[i].AddedDate };
    }

    public void ReplaceAll(IEnumerable<NationalPoolEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
    }
}
