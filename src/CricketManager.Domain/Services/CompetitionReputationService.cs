using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>How a competition's earned reputation moved this season, and why.</summary>
public sealed record CompetitionReputationChange(double Delta, string Reason);

/// <summary>
/// Phase 7, Slice 7.2 (closes tech-debt item 7): the thing that actually MOVES
/// Competition.Reputation. It has had real consumers since Phase 3 (SponsorshipValuationService,
/// MatchdayRevenueService, CompetitionRevenueService) but nothing ever raised or lowered it - a
/// league's media profile was frozen at whatever it was seeded at.
///
/// A season moves it on three grounded signals, all bounded and slow (a competition's standing
/// takes years to build or lose, not one summer):
/// - <b>Crowds</b>: the average fill rate across the season vs a healthy baseline. Full grounds
///   lift a competition's profile; empty ones sink it.
/// - <b>Competitiveness</b>: how close the final table was. A tight title race is compelling
///   television; a procession is not.
/// - <b>The clubs in it</b>: the average reputational pull of the participants. A league that
///   attracts and keeps big clubs grows; one the big clubs leave shrinks.
///
/// The result feeds straight back into sponsorship and the broadcast pool next season, so a
/// competition that becomes a genuine draw is worth more without anyone editing a number.
/// </summary>
public sealed class CompetitionReputationService
{
    /// <summary>A healthy average fill rate - above this a competition's crowds are a plus, below it a minus.</summary>
    private const double HealthyFillRate = 0.42;

    private const double MaxSeasonMove = 4.0;

    public CompetitionReputationChange ReviewSeason(
        Competition competition, CompetitionSeason season, IReadOnlyList<double> crowdFillRates, IDictionary<Guid, Team> teams,
        WorldState? world = null)
    {
        double delta = 0;
        var reasons = new List<string>();

        // §12.5: franchise money erodes a domestic competition's standing - if a big share of the
        // players who WOULD be its stars are off playing (and being paid by) a franchise league,
        // the domestic product is diminished. Only for a non-franchise competition, and only when
        // the world's contract data is available.
        if (world is not null && competition.Scope is not CompetitionScope.FranchiseLeague and not CompetitionScope.International)
        {
            var participantIds = season.ParticipatingTeamIds.ToHashSet();
            var relevantPlayers = world.Players.Where(p => p.CurrentTeamId is { } t && participantIds.Contains(t)).ToList();
            if (relevantPlayers.Count > 20)
            {
                int onFranchiseDeals = relevantPlayers.Count(p =>
                    world.PlayerContracts.Any(c => c.PlayerId == p.Id && c.Kind == ContractKind.Franchise && c.Status == ContractStatus.Active));
                double franchiseShare = onFranchiseDeals / (double)relevantPlayers.Count;
                if (franchiseShare > 0.15)
                {
                    double drain = -Math.Clamp((franchiseShare - 0.15) * 6, 0, 1.8);
                    delta += drain;
                    reasons.Add("the franchise circuit is pulling players away");
                }
            }
        }

        // --- crowds ---
        if (crowdFillRates.Count > 0)
        {
            double avgFill = crowdFillRates.Average();
            double crowdTerm = Math.Clamp((avgFill - HealthyFillRate) / HealthyFillRate, -1, 1) * 2.2;
            delta += crowdTerm;
            reasons.Add(crowdTerm >= 0
                ? $"grounds averaged {avgFill:P0} full"
                : $"crowds were thin ({avgFill:P0})");
        }

        // --- competitiveness: the spread of final points, normalised ---
        if (season.Standings.Count >= 3)
        {
            var pts = season.Standings.Select(s => (double)s.Points).OrderByDescending(x => x).ToList();
            double mean = pts.Average();
            if (mean > 0)
            {
                double stdev = Math.Sqrt(pts.Average(p => (p - mean) * (p - mean)));
                double cv = stdev / mean; // low = tight table, high = runaway
                double compTerm = Math.Clamp((0.45 - cv) / 0.45, -1, 1) * 1.6;
                delta += compTerm;
                reasons.Add(compTerm >= 0 ? "a tight, watchable title race" : "a one-sided season");
            }
        }

        // --- the clubs in it ---
        var participants = season.ParticipatingTeamIds
            .Select(id => teams.TryGetValue(id, out var t) ? t : null)
            .Where(t => t is not null).Select(t => t!).ToList();
        if (participants.Count > 0)
        {
            double avgRep = participants.Average(t => t.Reputation.Domestic);
            double clubTerm = Math.Clamp((avgRep - 50) / 50.0, -1, 1) * 1.2;
            delta += clubTerm;
            if (Math.Abs(clubTerm) > 0.4)
                reasons.Add(clubTerm >= 0 ? "a strong field of clubs" : "a weak field");
        }

        delta = Math.Clamp(delta, -MaxSeasonMove, MaxSeasonMove);
        competition.AdjustReputation(delta);

        string reason = reasons.Count == 0
            ? $"{competition.Name}'s standing is unchanged after {season.Year}."
            : $"{competition.Name}'s standing {(delta >= 0 ? "rises" : "slips")} after {season.Year} - {string.Join(", ", reasons)}.";
        return new CompetitionReputationChange(Math.Round(delta, 2), reason);
    }
}
