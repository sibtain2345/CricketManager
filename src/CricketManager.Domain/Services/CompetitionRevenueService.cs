using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>One team's earnings from one competition season, itemised so it can be explained rather than appearing as a single unexplained number in the accounts.</summary>
public sealed record CompetitionEarnings(Guid TeamId, double ParticipationFee, double PrizeMoney, double MeritPayment, double Total, string Description);

/// <summary>
/// Section 11's "competition income", which was the missing third leg of the finance model:
/// sponsorship and matchday income existed, but the money a team earns simply by competing -
/// and by competing WELL - did not, so a title-winning season was worth exactly as much as a
/// last-place one.
///
/// Three separate streams, because they behave differently and a team's board cares about the
/// difference:
/// - Participation: guaranteed for entering. This is what keeps smaller sides solvent, and it
///   is why competing in a big league at all is valuable even for a team with no hope of winning.
/// - Prize money: concentrated at the top. Winning is worth far more than reaching the final.
/// - Merit payment: scaled by final table position, so a mid-table finish still pays more than
///   the bottom - which is what makes a dead-rubber late-season match still worth winning.
///
/// The pool is derived from the competition's prestige and reputation rather than stored as a
/// fixed figure, so a league that grows in standing automatically becomes worth more to compete
/// in - and no one has to remember to update a number when it does.
/// </summary>
public sealed class CompetitionRevenueService
{
    private const double BasePoolPerTeam = 60_000;

    /// <summary>
    /// Total money a competition distributes in a season. Scales sharply with standing - a World
    /// Cup is not "a bit richer" than a second-tier domestic cup, it is an order of magnitude
    /// richer, and a linear scale would badly understate that.
    /// </summary>
    public double CalculatePrizePool(Competition competition, int teamCount)
    {
        double standing = Math.Clamp(competition.Prestige, 0, 100) * 0.6
                        + Math.Clamp(competition.Reputation, 0, 100) * 0.4;

        // Quadratic in standing: 0 -> 1x, 50 -> ~2.5x, 100 -> ~7x per team.
        double standingMultiplier = 1 + Math.Pow(standing / 100.0, 2) * 6;

        // Franchise leagues distribute far more than domestic competitions of similar prestige -
        // they are commercial properties with broadcast deals, which is exactly why players
        // chase them and boards fight over windows.
        double scopeMultiplier = competition.Scope switch
        {
            CompetitionScope.International => 2.5,
            CompetitionScope.FranchiseLeague => 3.0,
            CompetitionScope.DomesticT20 => 1.2,
            _ => 1.0
        };

        return Math.Round(BasePoolPerTeam * Math.Max(2, teamCount) * standingMultiplier * scopeMultiplier, 0);
    }

    /// <summary>
    /// Splits a season's pool across the teams that took part, using the final standings.
    /// Returns an entry for every participant - including the team that finished last, which
    /// still collects its participation fee.
    /// </summary>
    public IReadOnlyList<CompetitionEarnings> DistributeSeasonRevenue(Competition competition, CompetitionSeason season)
    {
        var participants = season.ParticipatingTeamIds.Distinct().ToList();
        if (participants.Count == 0) return Array.Empty<CompetitionEarnings>();

        double pool = CalculatePrizePool(competition, participants.Count);

        // 40% guaranteed, 40% to the winners, 20% on merit across the table.
        double participationPot = pool * 0.40;
        double prizePot = pool * 0.40;
        double meritPot = pool * 0.20;

        double participationEach = participationPot / participants.Count;

        var ranked = season.Standings
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => s.NetRunRate)
            .Select(s => s.TeamId)
            .ToList();

        var earnings = new List<CompetitionEarnings>();

        foreach (var teamId in participants)
        {
            double prize = 0;
            var descriptionParts = new List<string>();

            if (season.IsCompleted)
            {
                if (season.ChampionTeamId == teamId)
                {
                    prize = prizePot * 0.55;
                    descriptionParts.Add("champions");
                }
                else if (season.RunnerUpTeamId == teamId)
                {
                    prize = prizePot * 0.25;
                    descriptionParts.Add("runners-up");
                }
                else if (season.PlayoffQualifiedTeamIds.Contains(teamId))
                {
                    // The remaining 20% is shared by the other qualifiers.
                    int otherQualifiers = Math.Max(1, season.PlayoffQualifiedTeamIds.Count(id => id != season.ChampionTeamId && id != season.RunnerUpTeamId));
                    prize = prizePot * 0.20 / otherQualifiers;
                    descriptionParts.Add("playoff qualification");
                }
            }

            // Merit: linear by finishing position, so every place gained is worth money.
            double merit = 0;
            int position = ranked.IndexOf(teamId);
            if (position >= 0 && ranked.Count > 1)
            {
                double positionShare = (ranked.Count - position) / (double)Enumerable.Range(1, ranked.Count).Sum();
                merit = meritPot * positionShare;
                descriptionParts.Add($"finished {position + 1}{Ordinal(position + 1)}");
            }

            double total = participationEach + prize + merit;

            earnings.Add(new CompetitionEarnings(
                teamId,
                Math.Round(participationEach, 0),
                Math.Round(prize, 0),
                Math.Round(merit, 0),
                Math.Round(total, 0),
                descriptionParts.Count == 0
                    ? $"{competition.Name} participation"
                    : $"{competition.Name}: {string.Join(", ", descriptionParts)}"));
        }

        return earnings;
    }

    /// <summary>Convenience for one team, since the common question is "what did WE earn".</summary>
    public CompetitionEarnings? GetTeamEarnings(Competition competition, CompetitionSeason season, Guid teamId) =>
        DistributeSeasonRevenue(competition, season).FirstOrDefault(e => e.TeamId == teamId);

    private static string Ordinal(int n) => (n % 100 is >= 11 and <= 13) ? "th" : (n % 10) switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th"
    };
}
