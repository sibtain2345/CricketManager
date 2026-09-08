using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Attached to every innings/spell record. This is what makes contextual stats
/// filtering possible (e.g. "vs Australia at Brisbane in T20 in 2026") without
/// needing a separate query mechanism per filter type - queries just match on
/// whichever fields are supplied (see PlayerStatsQueryService).
/// </summary>
public sealed class MatchContext
{
    // MatchId ties every record produced by the same match together. Without it, "best
    // match figures" (two spells in the same game) and "did this century come in a win"
    // are unanswerable, and the same performance can't be cross-referenced between the
    // batting, bowling and team-innings sides of the database.
    public Guid MatchId { get; init; }
    public DateOnly MatchDate { get; init; }

    public Guid OpponentTeamId { get; init; }
    public string OpponentName { get; init; } = string.Empty;

    // GroundId is the reliable key; Ground (name) is kept for display and for records that
    // predate a ground entity existing. Ground-scoped queries prefer the id when it's set
    // and fall back to the name - a name alone breaks on renames and on two grounds sharing
    // a name in different countries (there are several real cases of exactly that).
    public Guid GroundId { get; init; }
    public string Ground { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty; // country the match was played in
    public HomeAwayNeutral HomeAwayNeutral { get; init; }
    public MatchFormat Format { get; init; }
    public string Tournament { get; init; } = string.Empty;
    public int Season { get; init; } // year
    public InningsRole InningsRole { get; init; } // batting first / chasing, for the player's team
    public double OppositionStrength { get; init; } = 50; // Team.Strength of the opponent at match time (0-100)

    // Section requirement: domestic formats + franchise leagues need their own identity.
    // Scope says WHERE the match sits (international/domestic-FC/domestic-ListA/domestic-T20/franchise).
    // LeagueName is only set for franchise matches (e.g. "IPL", "PSL") - empty otherwise -
    // and is what lets a specific league get its own career record on top of the broad T20 bucket.
    public CompetitionScope Scope { get; init; } = CompetitionScope.International;
    public string LeagueName { get; init; } = string.Empty;
}
