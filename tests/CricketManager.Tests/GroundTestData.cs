using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Tests;

/// <summary>
/// Builders for ground-records tests. Every field a given assertion doesn't care about gets
/// a sensible default, so each test only states the values it's actually testing - keeps the
/// intent of a test visible instead of buried in twenty lines of object initialiser.
/// </summary>
public static class GroundTestData
{
    public static TeamInningsRecord TeamInnings(
        Guid groundId,
        int runs,
        int wickets = 10,
        int inningsNumber = 1,
        MatchFormat format = MatchFormat.T20,
        Guid? matchId = null,
        MatchOutcome result = MatchOutcome.Win,
        string battingTeam = "Team A",
        string bowlingTeam = "Team B",
        int season = 2026) => new()
        {
            MatchId = matchId ?? Guid.NewGuid(),
            MatchDate = new DateOnly(season, 6, 1),
            GroundId = groundId,
            GroundName = "Test Ground",
            BattingTeamName = battingTeam,
            BowlingTeamName = bowlingTeam,
            Format = format,
            Season = season,
            InningsNumber = inningsNumber,
            Runs = runs,
            Wickets = wickets,
            Overs = format == MatchFormat.T20 ? 20 : 50,
            MatchResultForBattingTeam = result
        };

    public static BattingInningsRecord Innings(
        Guid playerId,
        Guid groundId,
        int runs,
        int balls = 60,
        bool notOut = false,
        MatchFormat format = MatchFormat.Test,
        Guid? matchId = null,
        string opponent = "Australia",
        int season = 2026) => new()
        {
            PlayerId = playerId,
            Runs = runs,
            BallsFaced = balls,
            NotOut = notOut,
            Context = new MatchContext
            {
                MatchId = matchId ?? Guid.NewGuid(),
                MatchDate = new DateOnly(season, 6, 1),
                GroundId = groundId,
                Ground = "Test Ground",
                OpponentName = opponent,
                Format = format,
                Season = season
            }
        };

    public static BowlingSpellRecord Spell(
        Guid playerId,
        Guid groundId,
        int wickets,
        int runsConceded = 50,
        double overs = 15,
        MatchFormat format = MatchFormat.Test,
        Guid? matchId = null,
        string opponent = "Australia",
        int season = 2026) => new()
        {
            PlayerId = playerId,
            Wickets = wickets,
            RunsConceded = runsConceded,
            OversBowled = overs,
            Context = new MatchContext
            {
                MatchId = matchId ?? Guid.NewGuid(),
                MatchDate = new DateOnly(season, 6, 1),
                GroundId = groundId,
                Ground = "Test Ground",
                OpponentName = opponent,
                Format = format,
                Season = season
            }
        };
}
