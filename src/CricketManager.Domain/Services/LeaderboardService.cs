using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 12 (§14.3/§14.4): the live leaderboards a real cricket season carries - the leading
/// run-scorer and wicket-taker, and a "team of the tournament" named when a competition closes.
///
/// Reads what the sim already accumulates: <c>WorldState.MonthForm</c> (runs/wickets this month)
/// and <c>WorldState.SeasonContributions</c> (a per-season rating per player, fed by
/// FixturePlayService). No new tracking.
/// </summary>
public sealed class LeaderboardService
{
    /// <summary>The month's leading run-scorer and wicket-taker, as news. Called before MonthForm is cleared.</summary>
    public IEnumerable<GameEvent> MonthlyLeaders(WorldState world, DateOnly date)
    {
        var tallies = world.MonthForm.Values.Where(t => t.Matches >= 2).ToList();
        if (tallies.Count < 3) yield break;

        var topBat = tallies.OrderByDescending(t => t.Runs).First();
        var topBowl = tallies.OrderByDescending(t => t.Wickets).First();

        if (topBat.Runs >= 120)
            yield return new GameEvent(date, GameEventType.Leaderboard,
                $"{topBat.PlayerName} leads the run charts this month with {topBat.Runs} runs.", topBat.PlayerId, topBat.TeamId);
        if (topBowl.Wickets >= 6)
            yield return new GameEvent(date, GameEventType.Leaderboard,
                $"{topBowl.PlayerName} is the month's leading wicket-taker with {topBowl.Wickets}.", topBowl.PlayerId, topBowl.TeamId);
    }

    /// <summary>A balanced best XI from a completed season's contributions, as a news item.</summary>
    public GameEvent? TeamOfTheTournament(WorldState world, Competition competition, CompetitionSeason season)
    {
        if (!world.SeasonContributions.TryGetValue(season.Id, out var byPlayer) || byPlayer.Count < 14)
            return null;

        var ranked = byPlayer
            .Select(kv => (Player: world.Players.FirstOrDefault(p => p.Id == kv.Key), kv.Value.Rating))
            .Where(x => x.Player is not null)
            .Select(x => (Player: x.Player!, x.Rating))
            .OrderByDescending(x => x.Rating)
            .ToList();
        if (ranked.Count < 14) return null;

        var xi = new List<Player>();
        void Take(Func<Player, bool> role, int n)
        {
            foreach (var (p, _) in ranked)
            {
                if (xi.Count >= 11 || xi.Contains(p) || !role(p)) continue;
                xi.Add(p);
                if (xi.Count(role) >= n) return;
            }
        }

        Take(p => p.PrimaryRole == PlayerRole.WicketKeeper, 1);
        Take(p => p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder, 5);
        Take(p => p.PrimaryRole == PlayerRole.Bowler && !BallOutcomeModel.IsSpinner(p), 3);
        Take(p => p.PrimaryRole == PlayerRole.Bowler && BallOutcomeModel.IsSpinner(p), 1);
        Take(p => p.PrimaryRole == PlayerRole.BowlingAllrounder, 1);
        // Fill any remaining places by pure rating.
        foreach (var (p, _) in ranked)
        {
            if (xi.Count >= 11) break;
            if (!xi.Contains(p)) xi.Add(p);
        }

        var names = string.Join(", ", xi.Take(11).Select(p => p.LastName));
        return new GameEvent(new DateOnly(season.Year, 12, 1), GameEventType.TeamOfTheTournament,
            $"Team of the {competition.Name} {season.Year}: {names}.", competition.Id);
    }
}
