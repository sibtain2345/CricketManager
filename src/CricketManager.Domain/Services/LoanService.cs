using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 8, Slice 8.7 + Phase 9, Slice 9.6: the loan market.
///
/// Phase 8 built the light core - a young player buried in a strong club's squad is loaned to a
/// weaker club that has a genuine gap, gets game time, develops, and comes back. Phase 9 makes the
/// TERMS real (Player.CurrentLoan / LoanAgreement): a loan FEE and a WAGE-CONTRIBUTION SPLIT both
/// move money at loan start, and a minority of loans carry an OPTION or an OBLIGATION TO BUY that
/// resolves into a permanent transfer when the loan ends.
/// </summary>
public sealed class LoanService
{
    private readonly PlayerValuationService _valuation = new();
    private readonly PlayerContractService _contracts = new();

    /// <summary>Loans run a season-ish - long enough for the game time to matter, short enough to come back for.</summary>
    private const int MinLoanDays = 150;
    private const int MaxLoanDays = 300;

    /// <summary>At most this many players out on loan from one club at once.</summary>
    private const int MaxConcurrentLoansOut = 2;

    // ---------------- arranging loans ----------------

    /// <summary>
    /// Considers sending one or two buried young players from <paramref name="team"/> out on loan.
    /// Only fires for a genuinely strong club with real squad depth; the destination is a weaker
    /// club in the same country with a role gap and room in its squad.
    /// </summary>
    public IEnumerable<GameEvent> ConsiderLoans(WorldState world, Team team, DateOnly date, Random random)
    {
        if (team.IsNational) yield break;
        if (team.SquadPlayerIds.Count < 16) yield break; // no depth to spare

        int alreadyOut = world.Players.Count(p => p.ParentClubId == team.Id && p.LoanReturnDate is not null);
        if (alreadyOut >= MaxConcurrentLoansOut) yield break;

        var squad = team.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Select(p => p!)
            .ToList();

        // Buried youngsters: young, low in the pecking order, and not currently on loan or injured.
        var candidates = squad
            .Where(p => !p.IsRetired && p.LoanReturnDate is null && p.AcademyTeamId is null
                        && p.Age(date) <= 21
                        && p.SquadStatus is SquadStatus.DevelopmentProspect or SquadStatus.Backup or SquadStatus.Fringe or SquadStatus.LongTermProject
                        && p.CurrentInjury is null
                        && (world.MatchesThisSeason.Count == 0 || world.MatchesThisSeason.GetValueOrDefault(p.Id) <= 3))
            .OrderByDescending(p => p.PotentialAbility - p.CurrentAbility)
            .Take(MaxConcurrentLoansOut - alreadyOut)
            .ToList();
        if (candidates.Count == 0) yield break;

        var possibleDestinations = world.Teams.Values
            .Where(t => !t.IsNational && t.Id != team.Id && t.Country == team.Country && t.SquadPlayerIds.Count is > 0 and < 20)
            .OrderBy(t => t.Strength)
            .ToList();
        if (possibleDestinations.Count == 0) yield break;

        foreach (var player in candidates)
        {
            var destination = possibleDestinations
                .FirstOrDefault(t => NeedsWhatHeOffers(world, t, player) && !HasLoanFrom(world, t, team));
            if (destination is null) continue;

            int days = random.Next(MinLoanDays, MaxLoanDays + 1);
            player.ParentClubId = team.Id;
            player.LoanReturnDate = date.AddDays(days);
            player.CurrentTeamId = destination.Id;
            team.SquadPlayerIds.Remove(player.Id);
            if (!destination.SquadPlayerIds.Contains(player.Id)) destination.SquadPlayerIds.Add(player.Id);
            player.Morale.Adjust(4); // a chance to actually play

            // Slice 9.6: real terms. A modest loan fee and a wage-contribution split both move
            // money now; a minority of loans carry an option (or, rarely, an obligation) to buy.
            double value = _valuation.EstimateValue(player, PlayerContractService.ActiveDomesticContract(world, player.Id), date, world.MarketIndex);
            var contract = PlayerContractService.ActiveDomesticContract(world, player.Id);
            double annualWage = contract?.AnnualWage ?? 30_000;
            double wageShare = 0.4 + random.NextDouble() * 0.4;   // the loan club covers 40-80% of the wage
            double loanFee = Math.Round(value * (0.02 + random.NextDouble() * 0.06), 0); // 2-8% of value
            double wageLump = Math.Round(annualWage * wageShare * (days / 365.0), 0);

            destination.Finances.Budget -= loanFee + wageLump;
            team.Finances.Budget += loanFee + wageLump;

            double roll = random.NextDouble();
            double? buyFee = roll < 0.04 ? Math.Round(value * 1.05, 0)   // obligation - rare
                : roll < 0.18 ? Math.Round(value * 1.15, 0)             // option
                : null;
            bool obligation = roll < 0.04;

            player.CurrentLoan = new LoanAgreement
            {
                ParentClubId = team.Id,
                LoanClubId = destination.Id,
                LoanFee = loanFee,
                WageContributionShare = wageShare,
                BuyFee = buyFee,
                IsObligationToBuy = obligation
            };

            string terms = buyFee is null ? "" : obligation ? " with an obligation to buy" : " with an option to buy";
            yield return new GameEvent(date, GameEventType.LoanAgreed,
                $"{team.Name} loan {player.FullName} ({player.Age(date)}) to {destination.Name}{terms} (fee {loanFee:N0}).",
                player.Id, destination.Id);
        }
    }

    private static bool HasLoanFrom(WorldState world, Team destination, Team parent) =>
        world.Players.Any(p => p.CurrentTeamId == destination.Id && p.ParentClubId == parent.Id && p.LoanReturnDate is not null);

    private static bool NeedsWhatHeOffers(WorldState world, Team destination, Player player)
    {
        var destSquad = destination.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null).Select(p => p!)
            .ToList();

        if (player.BowlingRole != BowlingRoleType.NotABowler)
            return destSquad.Count(p => p.BowlingRole != BowlingRoleType.NotABowler) < 6;
        if (player.PrimaryRole == PlayerRole.WicketKeeper)
            return destSquad.Count(p => p.PrimaryRole == PlayerRole.WicketKeeper) < 2;
        return destSquad.Count(p => p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder) < 7;
    }

    // ---------------- returns ----------------

    /// <summary>
    /// Brings home every player whose loan window has closed, and credits the game time he got.
    /// Deterministic - no RNG - so it is safe to append to any tick.
    /// </summary>
    public IEnumerable<GameEvent> ProcessReturns(WorldState world, DateOnly date)
    {
        foreach (var player in world.Players.Where(p => p.LoanReturnDate is { } d && d <= date).ToList())
        {
            var parentId = player.ParentClubId;
            var loanClub = player.CurrentTeamId;

            // A season on loan is real development for a young player - a nudge toward his ceiling,
            // bigger the more he actually played, plus a genuine lift in belief and standing.
            int gamesOnLoan = world.MatchesThisSeason.GetValueOrDefault(player.Id);
            int nudge = Math.Clamp(1 + gamesOnLoan / 3, 1, 6);
            player.CurrentAbility = Math.Clamp(player.CurrentAbility + nudge, 1, player.PotentialAbility);
            player.Form.RecordPerformance(6);
            player.Morale.Adjust(5);
            if (gamesOnLoan >= 4)
                player.Reputation.Adjust(domesticDelta: 2 + gamesOnLoan * 0.3);

            var loan = player.CurrentLoan;

            // Slice 9.6: does the loan turn into a permanent transfer?
            bool completePermanent = false;
            if (loan?.BuyFee is { } fee && loanClub is { } buyerId && world.Teams.TryGetValue(buyerId, out var buyer))
            {
                if (loan.IsObligationToBuy) completePermanent = true;
                else
                {
                    // The loan club decides on its option - did he do well, is there still a need,
                    // can it afford the fee.
                    bool wants = gamesOnLoan >= 6 && SquadNeeds.CanAffordFee(buyer, fee);
                    completePermanent = wants;
                }

                if (completePermanent)
                {
                    buyer.Finances.Budget -= fee;
                    if (parentId is { } sellerId && world.Teams.TryGetValue(sellerId, out var seller))
                        seller.Finances.Budget += fee;

                    var oldContract = PlayerContractService.ActiveDomesticContract(world, player.Id);
                    if (oldContract is not null) oldContract.Status = ContractStatus.Terminated;

                    double newWage = new PlayerValuationService().MarketWage(player, world.MarketIndex);
                    _contracts.Sign(world, player, buyer, date, newWage, years: 3);
                    player.SquadStatus = SquadStatus.SecondChoice;
                    player.LoanReturnDate = null;
                    player.ParentClubId = null;
                    player.CurrentLoan = null;
                    player.Morale.Adjust(6);

                    yield return new GameEvent(date, GameEventType.PlayerTransferred,
                        $"{buyer.Name} sign {player.FullName} permanently from his loan for {fee:N0}"
                        + (loan.IsObligationToBuy ? " (obligation triggered)" : "") + ".",
                        player.Id, buyer.Id);
                    continue;
                }
            }

            // Detach from the loan club, reattach to the parent.
            if (loanClub is { } lc && world.Teams.TryGetValue(lc, out var loanTeam))
                loanTeam.SquadPlayerIds.Remove(player.Id);

            player.LoanReturnDate = null;
            player.ParentClubId = null;
            player.CurrentLoan = null;

            if (parentId is { } pc && world.Teams.TryGetValue(pc, out var parent))
            {
                player.CurrentTeamId = pc;
                if (!parent.SquadPlayerIds.Contains(player.Id)) parent.SquadPlayerIds.Add(player.Id);

                yield return new GameEvent(date, GameEventType.SquadDecisionMade,
                    gamesOnLoan >= 4
                        ? $"{player.FullName} returns to {parent.Name} from his loan a better player for {gamesOnLoan} games of senior cricket."
                        : $"{player.FullName} returns to {parent.Name} from loan.",
                    player.Id, pc);
            }
            else
            {
                // Parent club is gone (shouldn't happen in the current world) - he stays where he is.
                player.CurrentTeamId = loanClub;
            }
        }
    }
}
