using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.5: end-of-period awards, decided from real accumulated output.
///
/// Reads WorldState.YearForm / MonthForm - the running per-player tallies FixturePlayService
/// feeds from each match's combined rating plus its runs and wickets - so an award goes to the
/// player who was genuinely the standout over the period, not whoever had one big innings.
/// Breakthrough player is age-gated; team of the season is the top ranked side.
/// </summary>
public sealed class AwardsService
{
    private const int MinMatchesForAnnualAward = 4;
    private const int MinMatchesForMonthlyAward = 2;

    /// <summary>Player of the month, from MonthForm. Null when nobody has done enough to merit it. The caller clears MonthForm afterwards.</summary>
    public AwardResult? PlayerOfTheMonth(IReadOnlyDictionary<Guid, PlayerFormTally> monthForm, int year, int month)
    {
        var best = monthForm.Values
            .Where(t => t.Matches >= MinMatchesForMonthlyAward)
            .OrderByDescending(t => t.PeriodScore)
            .ThenBy(t => t.PlayerName)
            .FirstOrDefault();
        if (best is null) return null;

        return new AwardResult(AwardType.PlayerOfTheMonth, year, month, best.PlayerId, best.TeamId, best.PlayerName,
            $"{best.PlayerName} is the player of the month - {best.Runs} runs and {best.Wickets} wickets in {best.Matches} matches.");
    }

    /// <summary>
    /// The annual awards: player of the year, batter of the year, bowler of the year, breakthrough
    /// player, and team of the season. From YearForm plus the ranking table. The caller clears
    /// YearForm afterwards.
    /// </summary>
    public IReadOnlyList<AwardResult> AnnualAwards(
        IReadOnlyDictionary<Guid, PlayerFormTally> yearForm, IReadOnlyList<TeamRanking> rankings,
        IDictionary<Guid, Team> teams, int year)
    {
        var awards = new List<AwardResult>();
        var eligible = yearForm.Values.Where(t => t.Matches >= MinMatchesForAnnualAward).ToList();

        if (eligible.Count > 0)
        {
            var poty = eligible.OrderByDescending(t => t.PeriodScore).ThenBy(t => t.PlayerName).First();
            awards.Add(new AwardResult(AwardType.PlayerOfTheYear, year, null, poty.PlayerId, poty.TeamId, poty.PlayerName,
                $"{poty.PlayerName} is the {year} player of the year - {poty.Runs} runs, {poty.Wickets} wickets across {poty.Matches} matches."));

            var batter = eligible.Where(t => t.Runs >= 200)
                .OrderByDescending(t => t.Runs + t.AverageRating * 3).ThenBy(t => t.PlayerName).FirstOrDefault();
            if (batter is not null)
                awards.Add(new AwardResult(AwardType.BatterOfTheYear, year, null, batter.PlayerId, batter.TeamId, batter.PlayerName,
                    $"{batter.PlayerName} - {batter.Runs} runs in {year} - takes the batting award."));

            var bowler = eligible.Where(t => t.Wickets >= 10)
                .OrderByDescending(t => t.Wickets * 2 + t.AverageRating * 3).ThenBy(t => t.PlayerName).FirstOrDefault();
            if (bowler is not null)
                awards.Add(new AwardResult(AwardType.BowlerOfTheYear, year, null, bowler.PlayerId, bowler.TeamId, bowler.PlayerName,
                    $"{bowler.PlayerName} - {bowler.Wickets} wickets in {year} - is the bowler of the year."));

            var breakthrough = eligible.Where(t => t.Age <= 23)
                .OrderByDescending(t => t.PeriodScore).ThenBy(t => t.PlayerName).FirstOrDefault();
            if (breakthrough is not null)
                awards.Add(new AwardResult(AwardType.BreakthroughPlayer, year, null, breakthrough.PlayerId, breakthrough.TeamId, breakthrough.PlayerName,
                    $"{breakthrough.PlayerName} ({breakthrough.Age}) is the breakthrough player of {year}."));
        }

        // Team of the season - the side that tops the composite ranking across formats.
        var topTeam = rankings
            .GroupBy(r => r.TeamId)
            .Select(g => (TeamId: g.Key, Points: g.Sum(r => r.Points)))
            .OrderByDescending(x => x.Points)
            .FirstOrDefault();
        if (topTeam.TeamId != Guid.Empty && teams.TryGetValue(topTeam.TeamId, out var t))
            awards.Add(new AwardResult(AwardType.TeamOfTheSeason, year, null, null, t.Id, t.Name,
                $"{t.Name} are the team of the season for {year}."));

        return awards;
    }
}
