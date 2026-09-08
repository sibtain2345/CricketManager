using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 8 (point 19): ICC-style team rankings that feed
/// reputation.
///
/// Points move on a rating-difference model after every match: beat a side rated above you and
/// you gain a lot, lose to one below you and you lose a lot; an even contest barely moves either.
/// Opposition STRENGTH is folded in on top (a stated, deliberate deviation from the real tables)
/// so the rankings converge toward genuine quality without needing decades of fixtures. A
/// climbing ranking lifts a team's reputation; a sliding one erodes it.
/// </summary>
public sealed class RankingService
{
    /// <summary>Gets the ranking row for a team+format, creating one seeded from the team's strength if it does not exist yet.</summary>
    public TeamRanking GetOrCreate(IList<TeamRanking> rankings, Team team, MatchFormat format)
    {
        var existing = rankings.FirstOrDefault(r => r.TeamId == team.Id && r.Format == format);
        if (existing is not null) return existing;

        var created = new TeamRanking
        {
            TeamId = team.Id,
            Format = format,
            Points = Math.Clamp(20 + team.Strength * 0.9, 0, 140) // seed near the team's own quality
        };
        rankings.Add(created);
        return created;
    }

    /// <summary>
    /// Applies one completed match to the two teams' rankings. `winnerId` null means a draw / tie /
    /// no result. Strengths are the two sides' Team.Strength at match time.
    /// </summary>
    public void RecordMatch(IList<TeamRanking> rankings, Team home, Team away, MatchFormat format, Guid? winnerId,
        double homeStrength, double awayStrength)
    {
        var homeR = GetOrCreate(rankings, home, format);
        var awayR = GetOrCreate(rankings, away, format);

        // Expected result from the ratings gap - a logistic curve, ~0.5 at parity.
        double ratingGap = homeR.Points - awayR.Points;
        double expectedHome = 1.0 / (1.0 + Math.Pow(10, -ratingGap / 40.0));

        double actualHome = winnerId is null ? 0.5 : winnerId == home.Id ? 1.0 : 0.0;

        // The move, scaled DOWN as a ranking settles (more matches rated) but never to nothing.
        double kHome = 8.0 * SettleFactor(homeR.MatchesRated);
        double kAway = 8.0 * SettleFactor(awayR.MatchesRated);

        // The deliberate deviation: nudge each rating toward what the strength gap alone implies,
        // so a genuinely strong side that has been unlucky still climbs over time.
        double strengthPull = Math.Clamp((homeStrength - awayStrength) / 20.0, -1, 1);

        homeR.Points = Math.Clamp(homeR.Points + kHome * ((actualHome - expectedHome) + strengthPull * 0.15), 0, 160);
        awayR.Points = Math.Clamp(awayR.Points + kAway * (((1 - actualHome) - (1 - expectedHome)) - strengthPull * 0.15), 0, 160);

        homeR.MatchesRated++;
        awayR.MatchesRated++;

        Recompute(rankings, format);
    }

    private static double SettleFactor(int matchesRated) => Math.Clamp(1.6 - matchesRated / 25.0, 0.5, 1.6);

    /// <summary>Recomputes 1-based positions within one format from current points.</summary>
    public void Recompute(IList<TeamRanking> rankings, MatchFormat format)
    {
        var ranked = rankings.Where(r => r.Format == format).OrderByDescending(r => r.Points).ThenBy(r => r.TeamId).ToList();
        for (int i = 0; i < ranked.Count; i++)
            ranked[i].Position = i + 1;
    }

    /// <summary>
    /// A team's ranking feeds its reputation over time: being genuinely near the top of a format
    /// is a standing in its own right, and a real slide erodes a fallen giant's aura. Called on
    /// the world clock's monthly tick - small per call.
    /// </summary>
    public void ApplyReputationEffect(Team team, TeamRanking ranking, int fieldSize)
    {
        if (fieldSize <= 1 || ranking.Position <= 0) return;

        // Where in the field this team sits, 1.0 (top) .. 0.0 (bottom).
        double standing = 1.0 - (ranking.Position - 1) / (double)Math.Max(1, fieldSize - 1);

        // A ranking well above the team's current reputation pulls reputation up; well below pulls it down.
        double impliedReputation = 20 + standing * 70; // top of the tree ~ 90, bottom ~ 20
        double delta = (impliedReputation - team.Reputation.Domestic) * 0.03;

        team.Reputation.Adjust(domesticDelta: delta, continentalDelta: delta * 0.4);
    }
}
