using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 13 (§8.12): mid-cycle player trades between franchises. Between mega auctions a franchise
/// squad carries over, so the only way to reshape it is a trade - a surplus player (plus a little
/// purse to balance the books) for another franchise's surplus at a position you actually need.
///
/// Run quarterly, outside any franchise window. Deterministic pairing (by name), a light
/// value-balance check both sides have to pass. A real, distinctive part of the franchise game.
/// </summary>
public sealed class FranchiseTradeService
{
    private readonly PlayerValuationService _valuation = new();
    private readonly PlayerContractService _contracts = new();

    public IEnumerable<GameEvent> RunQuarterly(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var calendar = new CompetitionCalendarService();

        foreach (var league in world.Competitions.Where(c => c.IsFranchiseAuctionLeague).OrderBy(c => c.Name))
        {
            // Only outside the window, and not right before a mega auction (a mega resets the squad).
            var staging = calendar.GetStaging(league, date.Year) ?? calendar.GetStaging(league, date.Year + 1);
            if (staging is not null && staging.StartDate <= date.AddDays(60) && staging.EndDate >= date.AddDays(-30)) continue;
            bool megaNext = league.LastMegaAuctionYear == 0 || (date.Year + 1) - league.LastMegaAuctionYear >= 3;
            if (megaNext) continue;

            var season = world.CompetitionSeasons.Where(s => s.CompetitionId == league.Id).OrderByDescending(s => s.Year).FirstOrDefault();
            if (season is null) continue;
            var franchises = world.Teams.Values.Where(t => season.ParticipatingTeamIds.Contains(t.Id) && t.SquadPlayerIds.Count >= 12)
                .OrderBy(t => t.Name).ToList();
            if (franchises.Count < 2) continue;

            // Each franchise's biggest surplus and biggest need.
            var profile = franchises.ToDictionary(f => f.Id, f =>
            {
                var squad = SquadOf(world, f);
                SquadNeeds.RoleGroup? need = SquadNeeds.WeakestGroup(squad, MatchFormat.T20, minShortfall: -1) is { } w ? w.Group : null;
                var surplus = SquadNeeds.Groups()
                    .Select(g => (Group: g, Members: squad.Where(p => g.Matches(p)).OrderByDescending(SquadNeeds.OverallScore).ToList()))
                    .Where(x => x.Members.Count >= 3)
                    .OrderByDescending(x => x.Members.Count)
                    .FirstOrDefault();
                Player? surplusPlayer = surplus.Members is { Count: >= 3 } ? surplus.Members[surplus.Members.Count - 1] : null;
                return (Need: need, SurplusPlayer: surplusPlayer);
            });

            // Find a complementary pair (a's surplus fits b's need, and vice versa).
            for (int i = 0; i < franchises.Count; i++)
                for (int j = i + 1; j < franchises.Count; j++)
                {
                    var a = franchises[i]; var b = franchises[j];
                    var pa = profile[a.Id]; var pb = profile[b.Id];
                    if (pa.SurplusPlayer is null || pb.SurplusPlayer is null) continue;
                    if (pb.Need is not { } bNeed || !bNeed.Matches(pa.SurplusPlayer)) continue;
                    if (pa.Need is not { } aNeed || !aNeed.Matches(pb.SurplusPlayer)) continue;

                    double va = _valuation.EstimateValue(pa.SurplusPlayer, null, date, world.MarketIndex);
                    double vb = _valuation.EstimateValue(pb.SurplusPlayer, null, date, world.MarketIndex);
                    if (Math.Abs(va - vb) > Math.Max(va, vb) * 0.45) continue; // too lopsided even with a cash sweetener

                    // The higher-valued player's team receives the difference in cash.
                    double cash = Math.Round(Math.Abs(va - vb) * 0.6, 0);
                    var (rich, poor) = va > vb ? (a, b) : (b, a);
                    poor.Finances.Budget -= cash; rich.Finances.Budget += cash;

                    SwapPlayer(world, a, b, pa.SurplusPlayer, date, league.Id);
                    SwapPlayer(world, b, a, pb.SurplusPlayer, date, league.Id);

                    events.Add(new GameEvent(date, GameEventType.FranchiseTrade,
                        $"{a.Name} and {b.Name} agree a trade: {pa.SurplusPlayer.FullName} for {pb.SurplusPlayer.FullName}" +
                        (cash > 0 ? $" plus {cash:N0} to {rich.Name}." : "."),
                        pa.SurplusPlayer.Id, b.Id));
                    goto nextLeague; // one trade per league per quarter
                }
            nextLeague: ;
        }

        return events;
    }

    private void SwapPlayer(WorldState world, Team from, Team to, Player player, DateOnly date, Guid competitionId)
    {
        var contract = world.PlayerContracts.FirstOrDefault(c => c.PlayerId == player.Id && c.Kind == ContractKind.Franchise
            && c.CompetitionId == competitionId && c.Status == ContractStatus.Active);
        double wage = contract?.AnnualWage ?? 0;
        if (contract is not null) contract.Status = ContractStatus.Terminated;
        from.SquadPlayerIds.Remove(player.Id);
        to.SquadPlayerIds.Add(player.Id);
        _contracts.Sign(world, player, to, date, wage, years: 1, kind: ContractKind.Franchise, competitionId: competitionId);
    }

    private static List<Player> SquadOf(WorldState world, Team t) =>
        t.SquadPlayerIds.Select(id => world.Players.FirstOrDefault(p => p.Id == id)).Where(p => p is not null).Select(p => p!).ToList();
}
