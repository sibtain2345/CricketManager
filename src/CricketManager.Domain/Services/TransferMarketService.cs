using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 9, Slice 9.3: the transfer market - a player moving between two contracted clubs for a
/// fee. The piece that makes the whole system a market rather than a wage ledger.
///
/// How one deal comes together, and every point at which it can fall through:
/// 1. <b>The buying club</b> identifies its weakest position (SquadNeeds) and a target at another
///    club who would upgrade it. It only shops if its board has the ambition and the budget.
/// 2. <b>The fee</b> - PlayerValuationService, driven above all by how much contract the target has
///    left. A <b>release clause</b>, when the target has one, is a hard ceiling any club can trigger,
///    bypassing step 3.
/// 3. <b>The selling club</b> decides. It will not sell a first-choice player at fair value - only
///    for a premium, or if he has handed in a transfer request. A fringe player at fair value, or
///    a glut position, it will let go.
/// 4. <b>The buying club's board</b> sanctions a marquee fee (a big fraction of the season budget) -
///    ambition-driven.
/// 5. <b>The player</b> agrees personal terms - the wage, the game-time prospect at the new club,
///    his temperament (Loyal resists, Ambitious pushes to a bigger club), and how far he must move.
///
/// Transfers only resolve in a WINDOW - the pre-season summer months and a mid-season January slot -
/// so squad planning is a real pressure. Runs on the monthly tick (window-gated), at the tail of
/// ProcessPhase9Monthly so it consumes the monthly RNG stream last.
/// </summary>
public sealed class TransferMarketService
{
    private readonly PlayerContractService _contracts = new();
    private readonly PlayerValuationService _valuation = new();
    private readonly RoleFitService _fit = new();
    private readonly PlayerRelationshipService _relationships = new(); // Phase 14: "he'll sign if his mate is there"

    /// <summary>
    /// Transfers resolve only in a window. Section C: the window is keyed to the club's HEMISPHERE -
    /// a northern-hemisphere season (Apr-Sept) has a pre-season and a mid-season window in
    /// {Jan, Jun, Jul, Aug}; a southern-hemisphere season (Oct-Mar) is shifted six months to
    /// {Jul, Dec, Jan, Feb}. The parameterless overload keeps the old northern default for callers
    /// that do not know a hemisphere (pre-rectification tests).
    /// </summary>
    public static bool IsWindowOpen(DateOnly date) => date.Month is 1 or 6 or 7 or 8;

    public static bool IsWindowOpen(DateOnly date, ValueObjects.Hemisphere hemisphere) => hemisphere == ValueObjects.Hemisphere.Southern
        ? date.Month is 7 or 12 or 1 or 2
        : date.Month is 1 or 6 or 7 or 8;

    public IEnumerable<GameEvent> RunWindow(WorldState world, DateOnly date, Random random)
    {
        // Any hemisphere's window open this month? (A per-buyer check below narrows it.)
        if (!IsWindowOpen(date) && !IsWindowOpen(date, ValueObjects.Hemisphere.Southern)) yield break;

        var domesticClubs = world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise).OrderBy(t => t.Name).ToList();

        // Clubs list a genuine surplus (a glut in one position) - it keeps the market flowing.
        foreach (var club in domesticClubs)
            foreach (var ev in ListSurplus(world, club, date))
                yield return ev;

        // Phase 13 (§9.3): DEADLINE DAY - the last week of a window runs hotter. More clubs go
        // shopping, and a late panic buy carries a premium.
        bool deadline = date.Day >= 25;
        int deals = 0;

        foreach (var buyer in domesticClubs)
        {
            if (!IsWindowOpen(date, world.ProfileFor(buyer.Country).Hemisphere)) continue;
            var format = PrimaryFormat(world, buyer);
            var buyerSquad = SquadOf(world, buyer);

            var need = SquadNeeds.WeakestGroup(buyerSquad, format);
            if (need is null && buyerSquad.Count >= 18) continue;

            // Only an ambitious, solvent club goes shopping hard.
            double shoppingAppetite = buyer.Board.Ambition / 100.0 + (deadline ? 0.2 : 0);
            if (buyer.Board.SeasonBudget <= 0) shoppingAppetite *= 0.4;
            if (random.NextDouble() > Math.Clamp(shoppingAppetite + 0.15, 0.1, 0.92)) continue;

            var target = FindTarget(world, buyer, need, format);
            if (target is null) continue;

            var seller = world.Teams[target.CurrentTeamId!.Value];
            var contract = PlayerContractService.ActiveDomesticContract(world, target.Id);
            // Section C: a fee/wage is denominated against the BUYING league's cricket economy - a
            // move into a big-money nation costs more (and pays more). Neutral when no profile seeded.
            double marketIndex = world.EffectiveMarketIndex(buyer.Country);
            double fee = _valuation.EstimateValue(target, contract, date, marketIndex);
            if (deadline) fee = Math.Round(fee * 1.14, 0); // a deadline-day panic premium

            bool releaseClauseTriggered = contract?.ReleaseClauseValue is { } clause
                && clause <= fee * 1.05 && SquadNeeds.CanAffordFee(buyer, clause);
            if (releaseClauseTriggered) fee = contract!.ReleaseClauseValue!.Value;

            if (!SquadNeeds.CanAffordFee(buyer, fee))
            {
                yield return Rejected(date, buyer, seller, target, "the fee was beyond them");
                continue;
            }

            // §9.8: a salary cap. The wage the buyer would pay has to keep the club under it.
            double capWage = _valuation.MarketWage(target, world.EffectiveMarketIndex(buyer.Country)) * 1.15;
            if (!SquadNeeds.WithinSalaryCap(world, buyer, capWage))
            {
                yield return Rejected(date, buyer, seller, target, "it would have breached the salary cap");
                continue;
            }

            if (!releaseClauseTriggered && !SellerAgrees(world, seller, target, fee, format, date, random))
            {
                yield return Rejected(date, buyer, seller, target, $"{seller.Name} turned down the {fee:N0} bid");
                continue;
            }

            // Board sanction for a marquee outlay.
            if (fee > buyer.Board.SeasonBudget * 0.4 && random.NextDouble() > BoardSanctionChance(buyer))
            {
                yield return new GameEvent(date, GameEventType.TransferBidRejected,
                    $"{buyer.Name}'s board declined to sanction a {fee:N0} move for {target.FullName}.", target.Id, buyer.Id);
                continue;
            }

            double newWage = NewWageFor(target, buyer, marketIndex);
            if (!PlayerAgreesToMove(target, seller, buyer, newWage, marketIndex, random, _relationships.HasFriendAt(world, target, buyer)))
            {
                yield return Rejected(date, buyer, seller, target, $"{target.FullName} did not want the move");
                continue;
            }

            // --- the deal goes through ---
            var sellOnEvent = CompleteTransfer(world, buyer, seller, target, contract, fee, newWage, date);
            string clauseNote = releaseClauseTriggered ? " (release clause triggered)" : "";
            yield return new GameEvent(date, GameEventType.PlayerTransferred,
                $"{buyer.Name} sign {target.FullName} from {seller.Name} for {fee:N0}{clauseNote}.",
                target.Id, buyer.Id);
            if (sellOnEvent is not null) yield return sellOnEvent;
            deals++;
        }

        if (deadline && deals >= 2)
            yield return new GameEvent(date, GameEventType.TransferDeadlineDay,
                $"A busy deadline day across the domestic game - {deals} deals done as the window closes.", null);
    }

    // ---------------- agents: a real bidding contest ----------------

    /// <summary>
    /// Follow-up: a player's AGENT does more than push for a move - he takes the client to several
    /// clubs at once and engineers a bidding contest, then takes a cut of the fee. Run on the
    /// quarterly tick for transfer-listed / genuinely unsettled players with real market value
    /// (the ones worth an agent's time). The winning club is the highest bidder the player accepts
    /// AND the selling club agrees to; the agent's cut (~6%) comes off the selling club's proceeds.
    /// </summary>
    public IEnumerable<GameEvent> RunAgentBiddingWars(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        const double DefaultAgentCut = 0.06;

        var clients = world.Players
            .Where(p => !p.IsRetired && p.AcademyTeamId is null && p.LoanReturnDate is null
                        && p.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var t) && !t.IsNational && !t.IsFranchise)
            .Where(p => (p.TransferListed || p.TransferRequested || (p.UnsettledUntil is { } u && u > date))
                        && Math.Max(p.Reputation.Domestic, p.Reputation.Continental) >= 40)
            .OrderByDescending(SquadNeeds.OverallScore)
            .Take(8)
            .ToList();

        foreach (var player in clients)
        {
            var seller = world.Teams[player.CurrentTeamId!.Value];
            var contract = PlayerContractService.ActiveDomesticContract(world, player.Id);
            var suitors = world.Teams.Values
                .Where(b => !b.IsNational && !b.IsFranchise && b.Id != seller.Id)
                .Where(b => IsWindowOpen(date, world.ProfileFor(b.Country).Hemisphere))
                .Select(b => (Team: b, Fit: SquadNeeds.WeakestGroup(SquadOf(world, b), PrimaryFormat(world, b))))
                .Where(x => x.Fit is { } f && f.Group.Matches(player))
                .Select(x => x.Team)
                .Where(b => SquadNeeds.CanAffordFee(b, _valuation.EstimateValue(player, contract, date, world.EffectiveMarketIndex(b.Country))))
                .OrderBy(b => b.Name)
                .Take(4)
                .ToList();
            if (suitors.Count < 2) continue; // no contest without at least two bidders

            var bids = suitors
                .Select(b =>
                {
                    double idx = world.EffectiveMarketIndex(b.Country);
                    double baseFee = _valuation.EstimateValue(player, contract, date, idx);
                    double keenness = 1.0 + b.Board.Ambition / 100.0 * 0.25 + random.NextDouble() * 0.12;
                    return (Team: b, Fee: Math.Round(baseFee * keenness, 0), Idx: idx);
                })
                .OrderByDescending(x => x.Fee)
                .ToList();

            var winner = bids[0];
            double newWage = NewWageFor(player, winner.Team, winner.Idx);
            if (!SquadNeeds.CanAffordFee(winner.Team, winner.Fee)) continue;
            if (!SellerAgrees(world, seller, player, winner.Fee, PrimaryFormat(world, seller), date, random)) continue;
            if (!PlayerAgreesToMove(player, seller, winner.Team, newWage, winner.Idx, random)) continue;

            var sellOn = CompleteTransfer(world, winner.Team, seller, player, contract, winner.Fee, newWage, date);
            if (sellOn is not null) events.Add(sellOn);

            // Phase 13 (§9.6): the actual agent, named, takes his own commission and banks it.
            var agent = AgentService.AgentFor(world, player);
            double agentFee = Math.Round(winner.Fee * (agent?.CommissionRate ?? DefaultAgentCut), 0);
            seller.Finances.Budget -= agentFee; // the agent's cut comes off the seller's proceeds
            if (agent is not null) agent.CareerEarnings += agentFee;
            string agentName = agent?.Name ?? "his agent";

            events.Add(new GameEvent(date, GameEventType.AgentBiddingWar,
                $"{agentName} turns interest in {player.FullName} from {bids.Count} clubs into a contest - {winner.Team.Name} win the race, "
                + $"paying {seller.Name} {winner.Fee:N0} (the agent takes {agentFee:N0}). The next-highest bid was {bids[1].Fee:N0}.",
                player.Id, winner.Team.Id));
        }

        return events;
    }

    // ---------------- the moving parts ----------------

    private IEnumerable<GameEvent> ListSurplus(WorldState world, Team club, DateOnly date)
    {
        var format = PrimaryFormat(world, club);
        var squad = SquadOf(world, club);
        foreach (var g in SquadNeeds.Groups())
        {
            var members = squad.Where(p => g.Matches(p) && !p.TransferListed).ToList();
            if (members.Count <= g.TargetDepth + 2) continue; // a genuine glut only
            var weakest = members.OrderBy(p => SquadNeeds.ScoreFor(p, format)).First();
            if (weakest.SquadStatus is SquadStatus.FirstChoice) continue;
            weakest.TransferListed = true;
            yield return new GameEvent(date, GameEventType.TransferBidRejected, // reuse - a "made available" note
                $"{club.Name} have made {weakest.FullName} available for transfer.", weakest.Id, club.Id);
        }
    }

    private Player? FindTarget(WorldState world, Team buyer, (SquadNeeds.RoleGroup Group, double WeakestScore)? need, MatchFormat format)
    {
        var candidates = world.Players
            .Where(p => !p.IsRetired && p.AcademyTeamId is null && p.LoanReturnDate is null
                        && p.CurrentTeamId is { } tid && tid != buyer.Id
                        && world.Teams.TryGetValue(tid, out var t) && !t.IsNational && !t.IsFranchise)
            .Where(p => need is null || need.Value.Group.Matches(p))
            .Select(p => (Player: p, Score: SquadNeeds.ScoreFor(p, format)))
            .Where(x => need is null || x.Score > need.Value.WeakestScore + 3) // a genuine upgrade
            .OrderByDescending(x => (x.Player.TransferRequested ? 6 : 0) + (x.Player.TransferListed ? 3 : 0) + x.Score)
            .Select(x => x.Player)
            .Take(4)
            .ToList();
        return candidates.FirstOrDefault();
    }

    private bool SellerAgrees(WorldState world, Team seller, Player player, double fee, MatchFormat format, DateOnly date, Random random)
    {
        if (player.TransferRequested) return true; // he has asked to go - the club will do business
        if (player.TransferListed) return random.NextDouble() < 0.8;

        double fairValue = _valuation.EstimateValue(player, PlayerContractService.ActiveDomesticContract(world, player.Id), date, world.MarketIndex);
        double premium = fairValue <= 0 ? 1 : fee / fairValue;

        double willSell = player.SquadStatus switch
        {
            SquadStatus.FirstChoice => premium >= 1.4 ? 0.6 : 0.05,
            SquadStatus.SecondChoice or SquadStatus.ReturningFromInjury => premium >= 1.15 ? 0.7 : 0.3,
            SquadStatus.Backup => 0.7,
            SquadStatus.Fringe => 0.85,
            SquadStatus.DevelopmentProspect or SquadStatus.LongTermProject => premium >= 1.6 ? 0.5 : 0.15,
            _ => 0.5
        };
        // Squad depth in his role - a club with cover sells more readily.
        int depth = SquadOf(world, seller).Count(p => p.PrimaryRole == player.PrimaryRole && !p.IsRetired);
        if (depth >= 5) willSell += 0.15;
        // A stretched club is more willing to cash in.
        if (seller.Finances.Budget < 0) willSell += 0.2;

        return random.NextDouble() < Math.Clamp(willSell, 0.02, 0.95);
    }

    private static double BoardSanctionChance(Team buyer) =>
        Math.Clamp(0.25 + buyer.Board.Ambition / 100.0 * 0.6 + buyer.Board.Wealth / 100.0 * 0.2, 0.1, 0.95);

    private double NewWageFor(Player player, Team buyer, double marketIndex)
    {
        double market = _valuation.MarketWage(player, marketIndex);
        double bump = buyer.Reputation.Domestic >= 60 ? 1.2 : 1.1; // a bigger club pays a premium to get a deal done
        return Math.Round(market * bump, 0);
    }

    private bool PlayerAgreesToMove(Player player, Team seller, Team buyer, double wageOffered, double marketIndex, Random random, bool friendAtDestination = false)
    {
        double market = _valuation.MarketWage(player, marketIndex);
        double wageSatisfaction = Math.Clamp(wageOffered / Math.Max(1, market), 0.7, 1.8);
        double clubStep = (buyer.Reputation.Domestic + buyer.Strength) - (seller.Reputation.Domestic + seller.Strength);

        double accept = 0.4 + (wageSatisfaction - 1.0) * 0.5 + Math.Clamp(clubStep / 60.0, -0.5, 0.5) * 0.35;
        if (player.TransferRequested) accept += 0.35;
        if (player.Personality.HasFlag(PersonalityTrait.Loyal)) accept -= 0.3;
        if (player.Personality.HasFlag(PersonalityTrait.Ambitious)) accept += Math.Clamp(clubStep / 60.0, -0.3, 0.4);
        if (player.Personality.HasFlag(PersonalityTrait.MoneyFocused) && wageSatisfaction < 1.15) accept -= 0.25;
        // A player who is a genuine first-choice and settled is hard to move.
        if (player.SquadStatus == SquadStatus.FirstChoice && !player.TransferRequested) accept -= 0.2;

        // Phase 13 (§9.4): his DREAM CLUB. If this is it, he pushes hard and forgives a lot on money.
        if (player.DreamClubId == buyer.Id) accept += 0.45;

        // Phase 14 (§18.1): a close friend or mentor already at the destination is a real pull.
        if (friendAtDestination) accept += 0.18;

        // Phase 14 (§18.5): ambition direction colours the move.
        accept += player.AmbitionDirection switch
        {
            AmbitionDirection.OneClubLegend => -0.22,
            AmbitionDirection.Globetrotter => 0.15,
            AmbitionDirection.TrophyHunter when clubStep > 15 => 0.2,
            AmbitionDirection.TrophyHunter when clubStep < -15 => -0.15,
            _ => 0
        };

        bool differentCountry = !string.Equals(seller.Country, buyer.Country, StringComparison.OrdinalIgnoreCase);
        accept *= 0.7 + _fit.RelocationComfort(player.Mental.Adaptability, differentCountry) * 0.3;

        return random.NextDouble() < Math.Clamp(accept, 0.03, 0.95);
    }

    private GameEvent? CompleteTransfer(WorldState world, Team buyer, Team seller, Player player, PlayerContract? oldContract, double fee, double newWage, DateOnly date)
    {
        GameEvent? sellOnEvent = null;
        buyer.Finances.Budget -= fee;
        seller.Finances.Budget += fee;
        buyer.Board.SeasonBudget = Math.Max(0, buyer.Board.SeasonBudget - fee);

        // Phase 13 (§9.1): a sell-on clause on the OLD contract pays the club he was bought from.
        if (oldContract is { SellOnPercentage: > 0, SellOnBeneficiaryTeamId: { } benId }
            && world.Teams.TryGetValue(benId, out var beneficiary) && benId != seller.Id)
        {
            double sellOn = Math.Round(fee * oldContract.SellOnPercentage / 100.0, 0);
            seller.Finances.Budget -= sellOn;
            beneficiary.Finances.Budget += sellOn;
            sellOnEvent = new GameEvent(date, GameEventType.SellOnClausePaid,
                $"{beneficiary.Name} bank {sellOn:N0} - a {oldContract.SellOnPercentage:F0}% sell-on from {player.FullName}'s move to {buyer.Name}.",
                player.Id, beneficiary.Id);
        }

        // Phase 13 (§10.4): both clubs' player-trading P&L.
        seller.PlayerTradingPnL += fee;
        buyer.PlayerTradingPnL -= fee;

        if (oldContract is not null) oldContract.Status = ContractStatus.Terminated;
        seller.SquadPlayerIds.Remove(player.Id);
        if (seller.CaptainsByFormat.Values.Contains(player.Id))
            foreach (var f in seller.CaptainsByFormat.Where(kv => kv.Value == player.Id).Select(kv => kv.Key).ToList())
                seller.CaptainsByFormat.Remove(f);

        player.HoldingOut = false; // a move ends a holdout

        int years = player.Age(date) >= 31 ? 2 : 3;
        _contracts.Sign(world, player, buyer, date, newWage, years, signingBonus: fee * 0.05, isHomegrown: false);

        // Phase 13 (§9.1): a marquee buyer sometimes concedes a sell-on to the seller to get the deal.
        if (fee > 4_000_000 && (player.Reputation.Worldwide >= 45 || player.Age(date) <= 24))
        {
            var newContract = world.PlayerContracts.LastOrDefault(c => c.PlayerId == player.Id && c.TeamId == buyer.Id && c.Status == ContractStatus.Active);
            if (newContract is not null)
            {
                newContract.SellOnPercentage = 12;
                newContract.SellOnBeneficiaryTeamId = seller.Id;
            }
        }

        double buyerScore = SquadNeeds.ScoreFor(player, PrimaryFormat(world, buyer));
        player.SquadStatus = buyerScore >= 62 ? SquadStatus.FirstChoice : buyerScore >= 48 ? SquadStatus.SecondChoice : SquadStatus.Backup;
        player.Morale.Adjust(8);
        player.Form.RecordPerformance(3);
        player.CoachTrust = 50; // fresh start
        player.CaptainTrust = 50;

        // The selling club's fans mind losing a favourite.
        if (player.SquadStatus != SquadStatus.Backup && Math.Max(player.Reputation.Domestic, player.Reputation.Continental) >= 55)
            seller.Board.MoveFanSentiment(-4);
        buyer.Board.MoveFanSentiment(fee > buyer.Board.SeasonBudget * 0.3 ? 3 : 1);

        return sellOnEvent;
    }

    // ---------------- helpers ----------------

    private static List<Player> SquadOf(WorldState world, Team team) =>
        team.SquadPlayerIds.Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Select(p => p!).ToList();

    private static MatchFormat PrimaryFormat(WorldState world, Team team)
    {
        var comp = world.CompetitionSeasons
            .Where(s => s.ParticipatingTeamIds.Contains(team.Id))
            .Select(s => world.Competitions.FirstOrDefault(c => c.Id == s.CompetitionId))
            .FirstOrDefault(c => c is not null);
        return comp?.Format ?? MatchFormat.T20;
    }

    private static GameEvent Rejected(DateOnly date, Team buyer, Team seller, Player target, string why) =>
        new(date, GameEventType.TransferBidRejected,
            $"{buyer.Name}'s move for {seller.Name}'s {target.FullName} fell through - {why}.", target.Id, buyer.Id);
}
