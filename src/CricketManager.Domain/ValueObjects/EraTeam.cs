namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Phase 16 (§15.5): a persisted Team of the Era, so a save's history is queryable rather than
/// only ever appearing once as a news line and then being lost. AllTimeXiService appends one every
/// 5 years; the panel's own reasoning is kept alongside the picked XI.
/// </summary>
public sealed record EraTeam(
    int Year, int YearsCovered, IReadOnlyList<System.Guid> PlayerIds, string Headline,
    /// <summary>§20.4: the dominant TEAM of the period - most titles won across the era's competitions. Null if nothing was clearly ahead.</summary>
    System.Guid? DominantTeamId = null,
    /// <summary>§20.4: the era's single most notable storyline - the first (highest-intensity) entry of <see cref="NotableStorylines"/>. Kept alongside it for anything that only ever read the one line.</summary>
    string? DefiningStoryline = null,
    /// <summary>
    /// Follow-up Pass 4 (§20.4): up to three genuinely notable storylines that ran during the era,
    /// ranked by how far each one built - not just the single biggest. Empty when the world had
    /// none live during the window.
    /// </summary>
    IReadOnlyList<string>? NotableStorylines = null,
    /// <summary>
    /// Follow-up Pass 4 (§20.4): the "rivalry of the era" - the pairing whose trophy was contested
    /// or whose intensity peaked during the window, read from <see cref="Entities.Rivalry"/>'s own
    /// contest history rather than a second, separate measure of what mattered. Null when nothing
    /// in the world's rivalries was active during the era.
    /// </summary>
    string? RivalryOfTheEra = null);
