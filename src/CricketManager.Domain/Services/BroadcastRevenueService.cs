using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>One club's cut of a competition's broadcast pool for a season.</summary>
public sealed record BroadcastPayout(Guid TeamId, double EqualShare, double MarketShare, double Total);

/// <summary>
/// Phase 7, Slice 7.2: broadcast / central-distribution money - the biggest real revenue lever in
/// modern cricket, and it was entirely absent. CompetitionRevenueService covers participation,
/// prize and merit money (earned by competing and by competing well); this covers the money a
/// competition earns from selling itself to broadcasters and hands to the clubs that make it worth
/// watching.
///
/// The pool scales with the competition's STANDING (prestige blended with earned reputation - the
/// same signal SponsorshipValuationService and CompetitionRevenueService already use) and with the
/// market size of the clubs in it (a league of big-reputation clubs sells for far more). The split
/// is deliberately less equal than prize money: an equal base share every club gets, plus a
/// market share weighted by each club's own reputation - because a broadcaster is paying for the
/// clubs that draw an audience.
/// </summary>
public sealed class BroadcastRevenueService
{
    private const double BasePoolPerTeam = 90_000;

    public double CalculateBroadcastPool(Competition competition, IReadOnlyList<Team> participants)
    {
        if (participants.Count < 2) return 0;

        // §10.3: a signed multi-year deal pays its fixed value regardless of this one season's
        // numbers - the whole point of a rights contract is that it locks the money in.
        if (competition.BroadcastDeal is { } deal && deal.AnnualValue > 0)
            return deal.AnnualValue;

        double standing = Math.Clamp(competition.Prestige, 0, 100) * 0.55
                        + Math.Clamp(competition.Reputation, 0, 100) * 0.45;

        // Cubic-ish in standing - a top competition's TV deal dwarfs a minor one's.
        double standingMultiplier = 1 + Math.Pow(standing / 100.0, 2.2) * 9;

        // Market size: the average reputational pull of the clubs in it, on a 0-2x scale.
        double avgReputation = participants.Average(t => t.Reputation.Domestic + t.Reputation.Continental * 0.4);
        double marketMultiplier = 0.5 + Math.Clamp(avgReputation, 0, 120) / 120.0 * 1.5;

        double scopeMultiplier = competition.Scope switch
        {
            CompetitionScope.International => 3.5,
            CompetitionScope.FranchiseLeague => 4.0,
            CompetitionScope.DomesticT20 => 1.4,
            _ => 1.0
        };

        return Math.Round(BasePoolPerTeam * participants.Count * standingMultiplier * marketMultiplier * scopeMultiplier, 0);
    }

    /// <summary>
    /// §10.3: renegotiate the broadcast deal - called at the annual rollover when the current deal
    /// has expired (or none exists yet). The new deal runs 3-4 years at the pool value the
    /// competition commands RIGHT NOW, so a competition that has grown since the last one banks the
    /// upside and one that has slipped takes the hit - lumpy, like real rights cycles.
    /// </summary>
    public GameEvent? RenegotiateDeal(Competition competition, IReadOnlyList<Team> participants, int year, Random random)
    {
        if (competition.BroadcastDeal is { } d && !d.IsExpired(year)) return null;
        if (participants.Count < 2) return null;

        double open = CalculateBroadcastPoolIgnoringDeal(competition, participants);
        int term = 3 + (year % 2);
        string[] partners = { "SportsNet", "Global Sports", "Prime Cricket", "The Sports Network", "Apex Broadcasting" };
        int nameKey = competition.Name.Aggregate(17, (h, ch) => unchecked(h * 31 + ch)); // stable (never string.GetHashCode)
        var partner = partners[Math.Abs(nameKey + year) % partners.Length];

        bool renewal = competition.BroadcastDeal is not null;
        competition.BroadcastDeal = new BroadcastDeal { Partner = partner, AnnualValue = Math.Round(open, 0), ExpiresYear = year + term };

        return new GameEvent(new DateOnly(year, 1, 2), GameEventType.BroadcastRevenuePaid,
            $"{competition.Name} {(renewal ? "renew their broadcast rights with" : "sign a new broadcast deal with")} {partner} - {open:N0} a year for {term} years.",
            competition.Id);
    }

    private double CalculateBroadcastPoolIgnoringDeal(Competition competition, IReadOnlyList<Team> participants)
    {
        var noDeal = competition.BroadcastDeal;
        competition.BroadcastDeal = null;
        try { return CalculateBroadcastPool(competition, participants); }
        finally { competition.BroadcastDeal = noDeal; }
    }

    /// <summary>Splits the pool: 60% equal, 40% by each club's share of the total reputation in the competition.</summary>
    public IReadOnlyList<BroadcastPayout> Distribute(Competition competition, IReadOnlyList<Team> participants)
    {
        if (participants.Count < 2) return Array.Empty<BroadcastPayout>();

        double pool = CalculateBroadcastPool(competition, participants);
        double equalPot = pool * 0.60;
        double marketPot = pool * 0.40;

        double equalEach = equalPot / participants.Count;
        double totalReputation = participants.Sum(t => Math.Max(1, t.Reputation.Domestic + t.Reputation.Continental * 0.4));

        return participants.Select(t =>
        {
            double market = marketPot * (Math.Max(1, t.Reputation.Domestic + t.Reputation.Continental * 0.4) / totalReputation);
            return new BroadcastPayout(t.Id, Math.Round(equalEach, 0), Math.Round(market, 0), Math.Round(equalEach + market, 0));
        }).ToList();
    }
}
