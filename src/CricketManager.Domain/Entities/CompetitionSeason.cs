using System.Text.Json.Serialization;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Entities;

/// <summary>
/// One specific year's run of a Competition (e.g. "PSL 2026"). Multi-season history is
/// just querying all CompetitionSeason rows for a given CompetitionId across years - see
/// CompetitionHistoryService.
///
/// Champion/runner-up/IsCompleted are settable only through Complete(), which enforces the
/// invariant "a champion exists if and only if the season is finished". Previously they
/// were three independent public setters, so a season could carry a champion while still
/// reporting IsCompleted == false - and CompetitionHistoryService.CountTitles would then
/// count a title for a season that had not actually concluded.
/// </summary>
public sealed class CompetitionSeason
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompetitionId { get; init; }
    public int Year { get; set; }

    public List<Guid> ParticipatingTeamIds { get; set; } = new();
    public List<CompetitionStanding> Standings { get; set; } = new();
    public List<Guid> PlayoffQualifiedTeamIds { get; set; } = new();

    [JsonInclude] public Guid? ChampionTeamId { get; private set; }
    [JsonInclude] public Guid? RunnerUpTeamId { get; private set; }
    [JsonInclude] public bool IsCompleted { get; private set; }

    /// <summary>Concludes the season. Normally called via CompetitionProgressionService.CompleteSeason.</summary>
    public void Complete(Guid championTeamId, Guid? runnerUpTeamId = null)
    {
        ChampionTeamId = championTeamId;
        RunnerUpTeamId = runnerUpTeamId;
        IsCompleted = true;
    }

    /// <summary>groupName is only meaningful for a GroupStageKnockout season - passing it always (re-)assigns the standing's group, so the first caller to mention a group is enough to tag it, without a separate "was this already created" branch.</summary>
    public CompetitionStanding GetOrCreateStanding(Guid teamId, string groupName = "")
    {
        var existing = Standings.FirstOrDefault(s => s.TeamId == teamId);
        var standing = existing ?? new CompetitionStanding { TeamId = teamId };
        if (existing is null) Standings.Add(standing);
        if (!string.IsNullOrEmpty(groupName)) standing.AssignGroup(groupName);
        return standing;
    }
}
