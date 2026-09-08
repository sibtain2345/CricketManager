using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 10 (§17.5): a domestic competition's SHAPE is not fixed for the life of a save. A league
/// that spends several years as a genuine draw (high reputation) expands - it earns an extra
/// promotion place, so the division below sends up two and the top flight grows by one. A league
/// that spends several years in the doldrums (low reputation) contracts - an extra relegation with
/// no matching promotion, shrinking it.
///
/// Bounded and slow: it takes THREE consecutive notable years to move, the top flight never grows
/// past ~8 or shrinks below ~4, and the change is applied through the same
/// CompetitionSeasonRunner promotion/relegation machinery (Competition.PendingExpansion) rather
/// than a parallel roster rewrite. RNG-free - it reads reputation and standings only.
///
/// Runs once a year, from ProcessAnnualRollover, over the linked domestic pyramids.
/// </summary>
public sealed class CompetitionLifecycleService
{
    private const int StreakToAct = 3;
    private const double HighBand = 70;
    private const double LowBand = 26;
    private const int MaxTopFlightSize = 8;
    private const int MinDivisionSize = 4;

    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date, int yearJustFinished)
    {
        foreach (var comp in world.Competitions
                     .Where(c => c.Scope is CompetitionScope.DomesticT20 or CompetitionScope.DomesticFirstClass or CompetitionScope.DomesticListA)
                     .OrderBy(c => c.Name))
        {
            // Advance the reputation streak.
            if (comp.Reputation >= HighBand)
                comp.ReputationStreakYears = comp.ReputationStreakYears >= 0 ? comp.ReputationStreakYears + 1 : 1;
            else if (comp.Reputation <= LowBand)
                comp.ReputationStreakYears = comp.ReputationStreakYears <= 0 ? comp.ReputationStreakYears - 1 : -1;
            else
                comp.ReputationStreakYears = 0;

            var bottom = comp.SecondTierCompetitionId is { } bid
                ? world.Competitions.FirstOrDefault(c => c.Id == bid)
                : null;
            if (bottom is null || comp.PromotionRelegationCount <= 0) continue;

            var topSeason = LatestCompleted(world, comp.Id, yearJustFinished);
            var bottomSeason = LatestCompleted(world, bottom.Id, yearJustFinished);
            if (topSeason is null || bottomSeason is null) continue;

            if (comp.ReputationStreakYears >= StreakToAct
                && topSeason.ParticipatingTeamIds.Count < MaxTopFlightSize
                && bottomSeason.ParticipatingTeamIds.Count > MinDivisionSize)
            {
                comp.PendingExpansion = 1;
                comp.ReputationStreakYears = 0;
                yield return new GameEvent(date, GameEventType.CompetitionStageAdvanced,
                    $"The {comp.Name} expands for {yearJustFinished + 1} - a sustained run of strong crowds and competitiveness earns it an extra place, promoted from the {bottom.Name}.",
                    comp.Id);
            }
            else if (comp.ReputationStreakYears <= -StreakToAct
                     && topSeason.ParticipatingTeamIds.Count > MinDivisionSize)
            {
                comp.PendingExpansion = -1;
                comp.ReputationStreakYears = 0;
                comp.Prestige = Math.Max(10, comp.Prestige - 4);
                yield return new GameEvent(date, GameEventType.CompetitionStageAdvanced,
                    $"The {comp.Name} contracts for {yearJustFinished + 1} - years of dwindling interest cost it a place, with an extra club relegated to the {bottom.Name}.",
                    comp.Id);
            }
        }
    }

    private static CompetitionSeason? LatestCompleted(WorldState world, System.Guid compId, int year) =>
        world.CompetitionSeasons
            .Where(s => s.CompetitionId == compId && s.IsCompleted && s.Year <= year)
            .OrderByDescending(s => s.Year)
            .FirstOrDefault();
}
