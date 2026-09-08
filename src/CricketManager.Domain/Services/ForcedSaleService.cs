using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 13 (§10.8): when a club's finances are in genuine distress, the board does not just take
/// a points deduction - it forces a fire sale. The most valuable saleable asset goes to whichever
/// club can afford him, at a discount that reflects the seller's weak hand.
///
/// Deterministic (no RNG): the seller has no leverage, so there is no negotiation - the board
/// picks the asset and the fee is the valuation, discounted.
/// </summary>
public sealed class ForcedSaleService
{
    private readonly PlayerValuationService _valuation = new();
    private readonly PlayerContractService _contracts = new();

    /// <summary>Force one sale for a distressed club, or null if there is nothing to sell / nobody to sell to.</summary>
    public GameEvent? ForceSale(WorldState world, Team distressed, DateOnly date)
    {
        var squad = distressed.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is { IsRetired: false } && p.AcademyTeamId is null)
            .Select(p => p!)
            .OrderByDescending(p => _valuation.EstimateValue(p, ContractFor(world, p), date, world.MarketIndex))
            .ToList();
        if (squad.Count < 14) return null; // selling below a fieldable squad is not on the table

        var asset = squad.First();
        var fee = Math.Round(_valuation.EstimateValue(asset, ContractFor(world, asset), date, world.MarketIndex) * 0.78, 0);

        // The buyer: the richest club that is not the seller, can afford it, and is not itself
        // under an embargo. Deterministic - by budget then name.
        var buyer = world.Teams.Values
            .Where(t => !t.IsNational && !t.IsFranchise && t.Id != distressed.Id && !t.Board.UnderTransferEmbargo)
            .Where(t => t.Finances.Budget >= fee && t.SquadPlayerIds.Count < 26)
            .OrderByDescending(t => t.Finances.Budget)
            .ThenBy(t => t.Name)
            .FirstOrDefault();
        if (buyer is null) return null;

        // Move the money and the registration.
        var oldContract = ContractFor(world, asset);
        buyer.Finances.Budget -= fee;
        distressed.Finances.Budget += fee;
        distressed.PlayerTradingPnL += fee;
        buyer.PlayerTradingPnL -= fee;

        if (oldContract is { SellOnPercentage: > 0, SellOnBeneficiaryTeamId: { } benId }
            && world.Teams.TryGetValue(benId, out var beneficiary) && benId != distressed.Id)
        {
            double sellOn = Math.Round(fee * oldContract.SellOnPercentage / 100.0, 0);
            distressed.Finances.Budget -= sellOn;
            beneficiary.Finances.Budget += sellOn;
        }

        if (oldContract is not null) oldContract.Status = ContractStatus.Terminated;
        distressed.SquadPlayerIds.Remove(asset.Id);
        foreach (var f in distressed.CaptainsByFormat.Where(kv => kv.Value == asset.Id).Select(kv => kv.Key).ToList())
            distressed.CaptainsByFormat.Remove(f);

        int years = asset.Age(date) >= 31 ? 2 : 3;
        _contracts.Sign(world, asset, buyer, date, Math.Round(_valuation.MarketWage(asset, world.MarketIndex), 0), years,
            signingBonus: fee * 0.05, isHomegrown: false);
        asset.SquadStatus = SquadStatus.SecondChoice;
        asset.Morale.Adjust(-4);
        distressed.Board.MoveFanSentiment(-6);

        return new GameEvent(date, GameEventType.ForcedSale,
            $"{distressed.Name} are forced to sell {asset.FullName} to {buyer.Name} for {fee:N0} to balance the books.",
            asset.Id, distressed.Id);
    }

    private static PlayerContract? ContractFor(WorldState world, Player p) =>
        world.PlayerContracts.FirstOrDefault(c => c.PlayerId == p.Id && c.Kind == ContractKind.Domestic && c.Status == ContractStatus.Active);
}
