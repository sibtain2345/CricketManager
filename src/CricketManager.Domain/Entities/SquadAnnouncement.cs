namespace CricketManager.Domain.Entities;

/// <summary>
/// Squad-selection spec Â§2: a mid-series or reserve-list replacement, kept as its own record
/// rather than a silent mutation so the "next match only" activation rule (Â§2) can be enforced
/// by DATE rather than by trusting the caller to sequence calls correctly.
/// </summary>
public sealed record SquadReplacement(
    Guid OutPlayerId,
    Guid InPlayerId,
    string Reason,
    DateOnly RequestedDate,
    /// <summary>The first date the incoming player is actually eligible to be selected. For a
    /// tournament reserve promotion this equals RequestedDate (ICC rules just require a valid
    /// reason, not a delay); for a home-bilateral mid-series replacement this is always AFTER
    /// the match in progress when the call-up happened, per Â§2's explicit rule that a
    /// replacement can never enter the XI already being played.</summary>
    DateOnly EffectiveFromDate);

/// <summary>
/// Squad-selection spec Â§1-Â§2: the pool of players named for a series or tournament, kept
/// distinct from a team's full roster (Team.SquadPlayerIds) and from any single match's XI
/// (XiSelectionService.SelectXi). The hard rule the spec states first - a playing XI can never
/// include anyone outside the announced squad - is what this entity exists to make enforceable:
/// XiSelectionService.SelectXi takes one optionally, and filters its candidate pool through
/// IsEligibleForMatch before anything else happens.
///
/// Two real-world announcement shapes, not one generic "squad list":
/// - **Bilateral/home series**: no fixed player cap, no reserves list - the squad named ahead of
///   the series IS the pool for its whole duration, unless a mid-series replacement is
///   registered (RegisterMidSeriesReplacement, home series only - see the class doc there).
/// - **Tournament (ICC events etc.)**: a fixed PlayerCap plus a separate ReservePlayerIds list.
///   A reserve can promote into the squad at any point for a valid reason
///   (PromoteReserve) - no "next match only" delay, since real ICC tournament rules only
///   require a valid reason, not a cooling-off period.
/// </summary>
public sealed class SquadAnnouncement
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TeamId { get; init; }
    public string SeriesOrTournamentName { get; set; } = string.Empty;
    public DateOnly AnnouncedDate { get; init; }

    /// <summary>
    /// Phase 6, Slice 6.2: the competition this squad was named for, when it was named for one
    /// (AiClubManagementService announces one squad per competition window). Null for a squad
    /// named for a bilateral series with no competition entity, or any pre-Phase-6 announcement.
    /// FixturePlayService uses it to pick the right announced squad for a fixture.
    /// </summary>
    public Guid? CompetitionId { get; init; }

    /// <summary>Tournament squads carry a fixed cap and a reserves list; bilateral squads don't.</summary>
    public bool IsTournamentSquad { get; init; }

    /// <summary>Hard cap for a tournament squad (e.g. 15). Null for a bilateral series, where the real-world rule is looser.</summary>
    public int? PlayerCap { get; init; }

    /// <summary>Mid-series replacement (Â§2) is a home-bilateral-series mechanism only - never for an away tour, and never for a tournament squad (which uses reserves instead).</summary>
    public bool IsHomeSeries { get; init; }

    public IReadOnlyList<Guid> PlayerIds => _playerIds;
    public IReadOnlyList<Guid> ReservePlayerIds => _reservePlayerIds;
    public IReadOnlyList<SquadReplacement> Replacements => _replacements;

    private readonly List<Guid> _playerIds = new();
    private readonly List<Guid> _reservePlayerIds = new();
    private readonly List<SquadReplacement> _replacements = new();

    public void Announce(IEnumerable<Guid> playerIds, IEnumerable<Guid>? reserves = null)
    {
        _playerIds.Clear();
        _playerIds.AddRange(playerIds.Distinct());
        _reservePlayerIds.Clear();
        if (reserves is not null) _reservePlayerIds.AddRange(reserves.Distinct().Except(_playerIds));
    }

    /// <summary>Tournament-only: a reserve steps into the squad for a valid reason, effective immediately - real ICC replacement rules don't impose a delay, only a justification.</summary>
    public void PromoteReserve(Guid outPlayerId, Guid reservePlayerId, string reason, DateOnly date)
    {
        if (!IsTournamentSquad)
            throw new InvalidOperationException("Reserve promotion is a tournament-squad mechanism - a bilateral series has no reserves list (see RegisterMidSeriesReplacement instead).");
        if (!_reservePlayerIds.Contains(reservePlayerId))
            throw new InvalidOperationException("That player is not on this squad's reserve list.");

        _playerIds.Remove(outPlayerId);
        _reservePlayerIds.Remove(reservePlayerId);
        _playerIds.Add(reservePlayerId);
        _replacements.Add(new SquadReplacement(outPlayerId, reservePlayerId, reason, date, EffectiveFromDate: date));
    }

    /// <summary>
    /// Home-bilateral-series-only mid-series call-up (Â§2). The incoming player joins the squad
    /// record immediately (so he shows up as "in the squad"), but IsEligibleForMatch will not
    /// clear him for selection until effectiveFromDate - the caller is expected to pass the date
    /// of the NEXT fixture after the one currently being played, never that match's own date,
    /// which is what makes the "never the match in progress" rule real rather than a comment.
    /// </summary>
    public void RegisterMidSeriesReplacement(Guid outPlayerId, Guid inPlayerId, string reason, DateOnly requestedDate, DateOnly effectiveFromDate)
    {
        if (IsTournamentSquad)
            throw new InvalidOperationException("Mid-series replacement is a bilateral-series mechanism - a tournament squad uses PromoteReserve instead.");
        if (!IsHomeSeries)
            throw new InvalidOperationException("Mid-series replacement is not permitted for an away series - only a home bilateral series allows a call-up mid-series.");
        if (effectiveFromDate <= requestedDate)
            throw new ArgumentException("A replacement can never be effective on or before the day it was requested - it must start from the NEXT match, not the one already being played.", nameof(effectiveFromDate));

        _playerIds.Add(inPlayerId);
        _replacements.Add(new SquadReplacement(outPlayerId, inPlayerId, reason, requestedDate, effectiveFromDate));
    }

    /// <summary>
    /// The one method XiSelectionService actually calls: is this player allowed to be picked for
    /// a match on this date. False if he was never in the squad at all, or if he only entered via
    /// a replacement whose effective date hasn't arrived yet (the match still in progress at the
    /// moment of the call-up).
    /// </summary>
    public bool IsEligibleForMatch(Guid playerId, DateOnly matchDate)
    {
        if (!_playerIds.Contains(playerId)) return false;

        var blockingReplacement = _replacements.FirstOrDefault(r => r.InPlayerId == playerId && matchDate < r.EffectiveFromDate);
        return blockingReplacement is null;
    }
}
