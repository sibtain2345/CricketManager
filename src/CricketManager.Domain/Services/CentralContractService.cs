using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 10: the national board's central-contract system and its NOC (No Objection Certificate)
/// lever - the two mechanisms a board uses to keep international cricket ahead of the franchise
/// circuit for its best players.
///
/// - <see cref="ReviewAnnually"/>: each national board awards central contracts to its top
///   pool players (tier A / B / C by standing), reviewed once a year. A higher tier is a bigger
///   retainer and less freedom to chase franchise money.
/// - <see cref="ReviewNoc"/>: called just before a franchise auction. A tier-A / tier-B player
///   whose board is protective (high <see cref="ValueObjects.NationalBoard.Politicisation"/>,
///   low <see cref="ValueObjects.CountryProfile.PoliticalStability"/>) and who has a genuine
///   international commitment clashing with that franchise window can be denied his NOC - he is
///   out of the auction that year. This is deliberately probabilistic and bounded: a board that
///   blocks everyone would be unrealistic and would gut the auction.
///
/// International cricket ALWAYS wins a clash (the user's standing instruction): this service never
/// cancels an international series. It only decides whether a centrally-contracted player is
/// released to also play a franchise season.
/// </summary>
public sealed class CentralContractService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();

    private const int TierACount = 4;
    private const int TierBCount = 6;
    private const int TierCCount = 8;

    /// <summary>Reviews every national board's central contracts. Called from the annual rollover, right after the national pools are refreshed.</summary>
    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();

        foreach (var national in world.Teams.Values.Where(t => t.IsNational).OrderBy(t => t.Name))
        {
            var eligible = world.Players
                .Where(p => !p.IsRetired
                            && string.Equals(p.Nationality, national.Country, StringComparison.OrdinalIgnoreCase)
                            && national.SquadPlayerIds.Contains(p.Id))
                .Select(p => (Player: p, Score: BestFormatScore(p)))
                .OrderByDescending(x => x.Score)
                .ToList();

            var newTier = new Dictionary<Guid, CentralContractTier>();
            for (int i = 0; i < eligible.Count; i++)
            {
                var tier = i < TierACount ? CentralContractTier.A
                    : i < TierACount + TierBCount ? CentralContractTier.B
                    : i < TierACount + TierBCount + TierCCount ? CentralContractTier.C
                    : CentralContractTier.None;
                newTier[eligible[i].Player.Id] = tier;
            }

            foreach (var (player, _) in eligible)
            {
                var was = player.CentralContractTier;
                var now = newTier.TryGetValue(player.Id, out var t) ? t : CentralContractTier.None;
                if (was == now) continue;

                player.CentralContractTier = now;

                // Only the interesting movements are news: earning a top-tier deal, or losing one.
                if (now == CentralContractTier.A && was != CentralContractTier.A)
                    events.Add(new GameEvent(date, GameEventType.CentralContractAwarded,
                        $"{player.FullName} is handed a top-tier central contract by {national.Name}.", player.Id, national.Id));
                else if (was is CentralContractTier.A or CentralContractTier.B && now == CentralContractTier.None)
                    events.Add(new GameEvent(date, GameEventType.CentralContractAwarded,
                        $"{player.FullName} loses his {national.Name} central contract.", player.Id, national.Id));
            }

            // A player no longer in the pool at all (retired, dropped) keeps no contract.
            foreach (var p in world.Players.Where(p => p.CentralContractTier != CentralContractTier.None
                                                       && string.Equals(p.Nationality, national.Country, StringComparison.OrdinalIgnoreCase)
                                                       && !national.SquadPlayerIds.Contains(p.Id)))
                p.CentralContractTier = CentralContractTier.None;

            // §5.6 / §12.2: the retainer is a real finance line - the national board actually pays
            // for its central contracts. Tier A is the biggest cheque. Debited from the board's
            // budget once a year, scaled by the nation's economy.
            double econ = world.ProfileFor(national.Country).EconomicScale;
            int tierA = eligible.Take(TierACount).Count();
            int tierB = eligible.Skip(TierACount).Take(TierBCount).Count();
            int tierC = eligible.Skip(TierACount + TierBCount).Take(TierCCount).Count();
            double retainerBill = (tierA * 1_400_000 + tierB * 700_000 + tierC * 300_000) * Math.Clamp(econ, 0.5, 3.0);
            national.Finances.Budget -= retainerBill;
        }

        return events;
    }

    /// <summary>
    /// Just before a franchise auction: decide which centrally-contracted players their boards
    /// withhold (NOC denied) because the franchise window clashes with a real international
    /// commitment. Sets <see cref="Player.NocWithheldUntil"/> to the franchise window's end.
    /// </summary>
    public IEnumerable<GameEvent> ReviewNoc(WorldState world, Competition franchiseComp, CompetitionSeason franchiseSeason,
        DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var calendar = new CompetitionCalendarService();
        var staging = calendar.GetStaging(franchiseComp, franchiseSeason.Year);
        if (staging is null) return events;

        // Which international competitions overlap the franchise window this year?
        int clashes = world.Competitions
            .Where(c => c.Scope == CompetitionScope.International)
            .Select(c => calendar.GetStaging(c, franchiseSeason.Year))
            .Count(s => s is not null && s.StartDate <= staging.EndDate && s.EndDate >= staging.StartDate);
        if (clashes == 0) return events; // no clash - the board has no reason to withhold anyone

        // Precompute each national board's protectiveness by country (RNG-free).
        var protectiveness = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var national in world.Teams.Values.Where(t => t.IsNational))
        {
            double politics = national.NationalBoard?.Politicisation ?? 45;
            double instability = 100 - world.ProfileFor(national.Country).PoliticalStability;
            protectiveness[national.Country] = Math.Clamp((politics * 0.7 + instability * 0.3) / 100.0, 0, 1);
        }
        var nationalByCountry = world.Teams.Values.Where(t => t.IsNational)
            .GroupBy(t => t.Country, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Iterate in world.Players LIST order - the seeded order - so the RNG stream is consumed
        // identically on two loads of the same seed (never Guid order).
        foreach (var player in world.Players)
        {
            if (player.IsRetired || player.CentralContractTier is not (CentralContractTier.A or CentralContractTier.B)) continue;
            if (!protectiveness.TryGetValue(player.Nationality, out var p)) continue;

            double chance = p * (player.CentralContractTier == CentralContractTier.A ? 0.55 : 0.28);
            if (random.NextDouble() >= chance) continue;

            player.NocWithheldUntil = staging.EndDate;
            var national = nationalByCountry[player.Nationality];

            // §12.2: a player denied his NOC misses a franchise payday - the board compensates him
            // with a top-up retainer (a real board finance line), and he is looked after rather
            // than simply losing out, so the morale hit is small.
            double comp = (player.CentralContractTier == CentralContractTier.A ? 800_000 : 450_000)
                          * Math.Clamp(world.ProfileFor(player.Nationality).EconomicScale, 0.5, 3.0);
            national.Finances.Budget -= comp;
            player.Morale.Adjust(-2);

            events.Add(new GameEvent(date, GameEventType.NocDenied,
                $"{national.Name}'s board denies {player.FullName} an NOC for the {franchiseComp.Name} - international duty comes first, though he is compensated with a top-up retainer.",
                player.Id, national.Id));
        }

        return events;
    }

    private double BestFormatScore(Player p)
    {
        double test = _evaluator.Evaluate(p, MatchFormat.Test).TotalScore;
        double odi = _evaluator.Evaluate(p, MatchFormat.ODI).TotalScore;
        double t20 = _evaluator.Evaluate(p, MatchFormat.T20).TotalScore;
        return Math.Max(test, Math.Max(odi, t20));
    }
}
