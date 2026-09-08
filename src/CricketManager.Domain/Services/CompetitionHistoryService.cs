using CricketManager.Domain.Entities;

namespace CricketManager.Domain.Services;

/// <summary>
/// Section requirement: competition/team history queryable across seasons (last 5
/// seasons, previous positions, titles, playoff appearances). Trivial once CompetitionSeason
/// exists as its own entity per year - this is just filtering/ordering, no new modeling.
/// Will matter for the Job Centre (evaluating a team's recent quality/trajectory) once that exists.
///
/// Title/playoff counts take an optional competitionId: without it these count across every
/// competition a team has ever entered, which is the right answer for "how decorated is this
/// team" but the wrong one for "how have they done in THIS league" - and the previous
/// signature made the second question look like it was being answered when it wasn't.
/// </summary>
public sealed class CompetitionHistoryService
{
    public IReadOnlyList<CompetitionSeason> GetRecentSeasons(IEnumerable<CompetitionSeason> allSeasons, Guid competitionId, int count) =>
        allSeasons.Where(s => s.CompetitionId == competitionId).OrderByDescending(s => s.Year).Take(count).ToList();

    public IReadOnlyList<CompetitionSeason> GetTeamHistory(IEnumerable<CompetitionSeason> allSeasons, Guid teamId) =>
        allSeasons.Where(s => s.ParticipatingTeamIds.Contains(teamId)).OrderByDescending(s => s.Year).ToList();

    public int CountTitles(IEnumerable<CompetitionSeason> allSeasons, Guid teamId, Guid? competitionId = null) =>
        allSeasons.Count(s => s.ChampionTeamId == teamId && (competitionId is null || s.CompetitionId == competitionId));

    public int CountPlayoffAppearances(IEnumerable<CompetitionSeason> allSeasons, Guid teamId, Guid? competitionId = null) =>
        allSeasons.Count(s => s.PlayoffQualifiedTeamIds.Contains(teamId) && (competitionId is null || s.CompetitionId == competitionId));

    /// <summary>Where the team finished in the table for a specific season - 1-indexed (1 = table topper), or null if the team didn't feature that season.</summary>
    public int? GetTablePosition(CompetitionSeason season, Guid teamId)
    {
        var ranked = season.Standings.OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate).ToList();
        int index = ranked.FindIndex(s => s.TeamId == teamId);
        return index < 0 ? null : index + 1;
    }
}
