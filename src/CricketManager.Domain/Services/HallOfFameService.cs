using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.7: Hall of Fame induction on retirement, for genuine greats only.
///
/// The bar is deliberately high - a long, solid career is not enough; this is for the players a
/// generation remembers. The score blends career volume (runs, wickets, catches across all
/// formats), standing (worldwide/continental reputation - a global name), and honours (individual
/// awards, player-of-the-series wins, records held). Called at the retirement point in the annual
/// rollover.
/// </summary>
public sealed class HallOfFameService
{
    /// <summary>The career score a player needs to be inducted. Calibrated so only a handful per generation clear it.</summary>
    public const double InductionBar = 62;

    public GameEvent? ConsiderInduction(Player player, WorldState world, DateOnly date)
    {
        if (player.IsHallOfFamer) return null;

        var careerStats = world.CareerStats.Values.Where(s => s.PlayerId == player.Id).ToList();
        int runs = careerStats.Sum(s => s.Runs);
        int wickets = careerStats.Sum(s => s.Wickets);
        int catches = careerStats.Sum(s => s.Catches);
        int hundreds = careerStats.Sum(s => s.Hundreds);
        int fiveFors = careerStats.Sum(s => s.FiveWicketHauls);

        int awards = world.Awards.Count(a => a.PlayerId == player.Id);
        int seriesWins = world.NewsArchive.Count(n => n.SubjectId == player.Id && n.Category == NewsCategory.Milestones && n.Headline.Contains("player of the"));
        int recordsHeld = world.RecordBook.Values.Count(r => r.HolderPlayerId == player.Id);

        double volume = runs / 220.0 + wickets * 0.28 + catches * 0.10 + hundreds * 1.2 + fiveFors * 1.0;
        double standing = player.Reputation.Worldwide * 0.35 + player.Reputation.Continental * 0.12;
        double honours = awards * 6 + seriesWins * 4 + recordsHeld * 8;

        double score = Math.Round(volume + standing + honours, 1);
        if (score < InductionBar) return null;

        player.IsHallOfFamer = true;
        string citation = BuildCitation(player, runs, wickets, hundreds, fiveFors, awards);
        world.HallOfFame.Add(new HallOfFameInductee(player.Id, player.FullName, player.Nationality, date, score, citation));

        return new GameEvent(date, GameEventType.HallOfFameInduction,
            $"{player.FullName} is inducted into the Hall of Fame. {citation}", player.Id);
    }

    private static string BuildCitation(Player player, int runs, int wickets, int hundreds, int fiveFors, int awards)
    {
        var bits = new List<string>();
        if (runs >= 5_000) bits.Add($"{runs:N0} career runs");
        if (hundreds >= 10) bits.Add($"{hundreds} centuries");
        if (wickets >= 200) bits.Add($"{wickets} wickets");
        if (fiveFors >= 10) bits.Add($"{fiveFors} five-fors");
        if (awards >= 2) bits.Add($"{awards} major individual awards");
        return bits.Count == 0
            ? "A career that defined an era."
            : string.Join(", ", bits) + ".";
    }
}
