using CricketManager.Domain.Entities;

namespace CricketManager.Domain.Services;

/// <summary>
/// Deliberately generic across CompetitionStructureType - this doesn't simulate matches
/// (Phase 4's job) or generate a fixture list (deferred - see CLAUDE.md), it only answers
/// "given the current standings, who qualifies / who's on top" - the part of competition
/// progression that's genuinely ready to build now, independent of whether the underlying
/// results came from a real match engine or were set directly (as in these tests).
/// </summary>
public sealed class CompetitionProgressionService
{
    /// <summary>Standard tiebreak order: points first, net run rate second - same convention real leagues use.</summary>
    private static IOrderedEnumerable<ValueObjects.CompetitionStanding> Ranked(CompetitionSeason season) =>
        season.Standings.OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate);

    /// <summary>Top N teams by the standard tiebreak order - used for LeagueWithPlayoffs and GroupStageKnockout alike.</summary>
    public IReadOnlyList<Guid> GetPlayoffQualifiers(CompetitionSeason season, int numberOfQualifiers)
    {
        var qualifiers = Ranked(season).Take(numberOfQualifiers).Select(s => s.TeamId).ToList();
        season.PlayoffQualifiedTeamIds = qualifiers;
        return qualifiers;
    }

    /// <summary>
    /// Top N teams WITHIN one group, same tiebreak order - the piece GetPlayoffQualifiers
    /// can't provide for a GroupStageKnockout season, where "top of the table" has to mean
    /// "top of this group" and never leaks in a team from a group that happened to be weaker
    /// overall. Does not touch season.PlayoffQualifiedTeamIds - a caller building the
    /// knockout stage combines several groups' results first (see
    /// FixtureGenerationService.BuildGroupCrossoverSeeding), and that combined list is what
    /// belongs there.
    /// </summary>
    public IReadOnlyList<Guid> GetGroupQualifiers(CompetitionSeason season, string groupName, int numberOfQualifiers) =>
        Ranked(season).Where(s => s.GroupName == groupName).Take(numberOfQualifiers).Select(s => s.TeamId).ToList();

    /// <summary>For a pure League structure (no playoffs), the table topper IS the winner.</summary>
    public Guid? GetTableTopper(CompetitionSeason season) => Ranked(season).Select(s => (Guid?)s.TeamId).FirstOrDefault();

    /// <summary>
    /// Board-objectives follow-up: a team's 1-based finishing position in the full table, same
    /// tiebreak order as everywhere else in this class. Null when the team never actually played
    /// in this season (not in the standings at all) - the honest answer, not a fabricated last place.
    /// </summary>
    public int? GetPosition(CompetitionSeason season, Guid teamId)
    {
        var ranked = Ranked(season).Select(s => s.TeamId).ToList();
        int index = ranked.IndexOf(teamId);
        return index < 0 ? null : index + 1;
    }

    /// <summary>
    /// Marks a season complete. Callable once the champion is known - whether that came
    /// from an actual simulated final (Phase 4+) or, for a pure League structure, directly
    /// from the table topper.
    /// </summary>
    public void CompleteSeason(CompetitionSeason season, Guid championTeamId, Guid? runnerUpTeamId = null)
    {
        season.Complete(championTeamId, runnerUpTeamId);
    }
}
