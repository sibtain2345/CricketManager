using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-7/8/9 rectification (Section G): retention and Right-to-Match, ahead of a MEGA
/// auction. Modelled on the current (2025/2026 cycle) IPL rules - confirmed by research, and the
/// numbers noted in CLAUDE.md so they do not silently drift:
///
/// - Up to <b>6</b> players retained per franchise, of whom at most <b>5 capped</b> and at most
///   <b>2 uncapped</b>.
/// - <b>Fixed retention slabs</b> deducted from the purse: the first three capped retentions cost
///   the equivalent of 18 / 14 / 11 crore, a 4th and 5th cost 18 / 14 again; an uncapped
///   retention costs ~4 crore. (Everything is scaled into the game's own currency via the
///   auction's crore-equivalent unit.)
/// - Every retention slot NOT used becomes a <b>Right-to-Match card</b> - a chance to reclaim a
///   released former player at the auction by matching the winning bid (with the RTM twist handled
///   in FranchiseAuctionService: the winner then gets one uncapped raise, and the original club
///   decides again).
/// - If a franchise wants to retain a genuinely important player at a lower value slab than he
///   thinks he is worth, he can force his way into the auction (PlayerContractService personality
///   machinery). A franchise that lets a real star go faces re-signing him at a much higher
///   auction price, or losing him - that tension is the point.
///
/// A MINI auction (the years between mega auctions) has NO retention step at all - the squad
/// simply carries over and only released/gap slots go under the hammer.
/// </summary>
public sealed class FranchiseRetentionService
{
    public const int MaxRetentions = 6;
    public const int MaxCappedRetentions = 5;
    public const int MaxUncappedRetentions = 2;

    /// <summary>Retention slab prices in CRORE-EQUIVALENT units (see FranchiseAuctionService.CroreEquiv). Capped: first-3 then 4th/5th; uncapped: flat.</summary>
    private static readonly double[] CappedSlabs = { 18, 14, 11, 18, 14 };
    private const double UncappedSlab = 4;

    /// <summary>Up to this many young uncapped players a franchise may keep on a development / reserve list, at a nominal fee, separate from the slab retentions and not costing an RTM card. This is how a franchise builds and keeps its own core (real leagues all allow it).</summary>
    public const int MaxReserveRetentions = 3;
    private const double ReserveFeeCrore = 0.30;

    /// <summary>The outcome for one franchise's retention window. Retained holds the slab retentions; ReserveRetained holds the development-list keepers (also stay in the squad, at a nominal fee).</summary>
    public sealed record RetentionOutcome(
        Guid FranchiseId, IReadOnlyList<Guid> Retained, double PurseSpent, int RtmCards,
        IReadOnlyList<Guid> RtmEligible, IReadOnlyList<GameEvent> Events)
    {
        public IReadOnlyList<Guid> ReserveRetained { get; init; } = Array.Empty<Guid>();
    }

    /// <summary>
    /// Runs the retention window for one franchise ahead of a mega auction. Reads the franchise's
    /// squad from LAST cycle (its current SquadPlayerIds, before the auction clears them), keeps the
    /// best few within the slab rules, and returns the RTM position for the rest. Does NOT clear
    /// the squad - the auction does that after every franchise's retentions are settled.
    /// </summary>
    public RetentionOutcome RunRetention(
        WorldState world, Competition competition, Team franchise, DateOnly date, double croreEquiv, double startingPurse, Random random)
    {
        var events = new List<GameEvent>();
        // Meeting-driven-selection ticket, Section 4: the last linear world.Players scan in the
        // franchise-auction path (flagged since Phase 9) - a dictionary lookup instead. RNG-free,
        // order-preserving, so no determinism concern.
        var byId = world.Players.ToDictionary(p => p.Id);
        var squad = franchise.SquadPlayerIds
            .Select(id => byId.GetValueOrDefault(id))
            .Where(p => p is not null && !p!.IsRetired && !p.RetiredFormats.Contains(MatchFormat.T20))
            .Select(p => p!)
            .OrderByDescending(SquadNeeds.OverallScore)
            .ToList();

        var retained = new List<Guid>();
        double spent = 0;
        int cappedRetained = 0, uncappedRetained = 0;
        var valuation = new PlayerValuationService();

        // How keen the franchise is to retain deep vs go to the auction - an ambitious, settled
        // squad retains more; one that underperformed wants a reset.
        int retentionAppetite = 3 + (int)Math.Round(franchise.Board.Ambition / 100.0 * 3);

        foreach (var player in squad)
        {
            if (retained.Count >= MaxRetentions || retained.Count >= retentionAppetite) break;

            bool capped = IsCapped(player);
            if (capped && cappedRetained >= MaxCappedRetentions) continue;
            if (!capped && uncappedRetained >= MaxUncappedRetentions) continue;

            double slab = capped
                ? CappedSlabs[Math.Min(cappedRetained, CappedSlabs.Length - 1)]
                : UncappedSlab;
            double slabCost = slab * croreEquiv;
            if (spent + slabCost > startingPurse * 0.75) continue; // never mortgage the whole purse on retentions

            // Is he worth a retention slot? Only a genuine contributor, and the slab has to be
            // fair-ish against his market value - a big name held at a low slab may agitate.
            double marketValue = valuation.EstimateValue(player, null, date, world.MarketIndex);
            double slabVsValue = slabCost <= 0 ? 1 : marketValue * 0.35 / slabCost; // franchise value ~35% of transfer value

            bool wantsToKeep = SquadNeeds.OverallScore(player) >= 55 && retained.Count < retentionAppetite;
            if (!wantsToKeep) continue;

            // The player's leverage: an underpaid star can push his way out.
            bool playerForcesOut = slabVsValue > 1.6
                && player.Personality.HasFlag(PersonalityTrait.MoneyFocused)
                && random.NextDouble() < 0.4;
            if (playerForcesOut)
            {
                // §8.5: retention is a NEGOTIATION - the franchise gets one chance to improve the
                // offer. An ambitious franchise with purse in hand bumps him to a higher slab and
                // he stays; otherwise he walks into the auction.
                double betterSlab = capped
                    ? CappedSlabs[Math.Max(0, cappedRetained - 1)] * 1.15
                    : UncappedSlab * 1.6;
                double betterCost = betterSlab * croreEquiv;
                bool franchiseImproves = franchise.Board.Ambition >= 62
                    && spent + betterCost <= startingPurse * 0.80
                    && SquadNeeds.OverallScore(player) >= 62
                    && random.NextDouble() < 0.6;
                if (franchiseImproves && betterCost / Math.Max(1, marketValue * 0.35) >= 1.1)
                {
                    retained.Add(player.Id);
                    spent += betterCost;
                    if (capped) cappedRetained++; else uncappedRetained++;
                    events.Add(new GameEvent(date, GameEventType.PlayerRetained,
                        $"{franchise.Name} improve their offer and retain {player.FullName} after a stand-off, for {betterCost:N0}.",
                        player.Id, franchise.Id));
                    continue;
                }

                events.Add(new GameEvent(date, GameEventType.TransferRequestFiled,
                    $"{player.FullName} rejects {franchise.Name}'s retention terms and will enter the {competition.Name} auction.",
                    player.Id, franchise.Id));
                continue;
            }

            retained.Add(player.Id);
            spent += slabCost;
            if (capped) cappedRetained++; else uncappedRetained++;
            events.Add(new GameEvent(date, GameEventType.PlayerRetained,
                $"{franchise.Name} retain {player.FullName} ({(capped ? "capped" : "uncapped")}) for {slabCost:N0}.",
                player.Id, franchise.Id));
        }

        // Reserve / development list: keep up to a few young uncapped players the franchise has
        // invested in, at a nominal fee. Not a slab retention, does not cost an RTM card - this is
        // how a franchise builds a lasting core rather than rebuilding from scratch every mega auction.
        var reserve = new List<Guid>();
        foreach (var player in squad.Where(p => !retained.Contains(p.Id)))
        {
            if (reserve.Count >= MaxReserveRetentions) break;
            int age = player.Age(date);
            bool young = age <= 24 && !IsCapped(player);
            bool worthKeeping = young && SquadNeeds.OverallScore(player) >= 48
                                && AbilityScale.CompositeAbilityToHundred(player.PotentialAbility) >= 55;
            if (!worthKeeping) continue;

            double fee = ReserveFeeCrore * croreEquiv;
            if (spent + fee > startingPurse * 0.80) continue;
            reserve.Add(player.Id);
            spent += fee;
            events.Add(new GameEvent(date, GameEventType.ReservePlayerRetained,
                $"{franchise.Name} keep {player.FullName} ({age}) on the reserve list for {fee:N0} - one for the future.",
                player.Id, franchise.Id));
        }

        int rtmCards = MaxRetentions - retained.Count;
        var kept = retained.Concat(reserve).ToHashSet();
        var rtmEligible = squad.Where(p => !kept.Contains(p.Id)).Select(p => p.Id).ToList();

        return new RetentionOutcome(franchise.Id, retained, Math.Round(spent, 0), rtmCards, rtmEligible, events)
        {
            ReserveRetained = reserve
        };
    }

    /// <summary>
    /// Section G: a player is "capped" if he has real international pedigree - 10+ international
    /// caps, OR has played international cricket recently. Everyone else is uncapped, and cheaper
    /// to retain and to sign.
    /// </summary>
    public static bool IsCapped(Player p) =>
        p.Experience.InternationalMatches >= 10
        || (p.Experience.InternationalMatches >= 3 && Math.Max(p.Reputation.Continental, p.Reputation.Worldwide) >= 35);
}
