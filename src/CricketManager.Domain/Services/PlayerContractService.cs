using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>What a contract-renewal negotiation produced.</summary>
public sealed record ContractRenewalDecision(bool Renewed, string Reason, double? NewAnnualWage, DateOnly? NewEndDate);

/// <summary>
/// Phase 9, Slices 9.0 + 9.1: the player-contract lifecycle - signing, the running renewal loop,
/// and expiry into free agency (the Bosman path).
///
/// Deliberately mirrors <see cref="CoachCareerService"/> / <see cref="StaffCareerService"/>: a
/// <see cref="Sign"/> that does everything in one call (contract + registration + signing bonus)
/// so a half-signed state cannot exist, an <see cref="EvaluateRenewal"/> that weighs BOTH sides
/// (does the club want to keep him, will the player accept), and a <see cref="MakeFreeAgent"/>
/// that fully detaches on expiry. The renewal is run at a fixed window ahead of the end date by
/// <see cref="WorldClockService.ProcessContractRenewals"/>, exactly like the coach/staff renewals.
///
/// The <see cref="WageBillService"/> stub is NOT deleted - it is kept as the per-player wage
/// benchmark this service and <see cref="PlayerValuationService"/> both build on, and as the
/// fallback wage bill for a world with no contract rows (an old save, or a hand-built test world).
/// </summary>
public sealed class PlayerContractService
{
    private readonly WageBillService _wages = new();

    // ---------------- signing ----------------

    /// <summary>
    /// Signs a player to a club on a new domestic contract - creates the contract, registers him
    /// (CurrentTeamId + SquadPlayerIds), pays the signing bonus out of the club's budget, and adds
    /// the contract to the world. The one call, no way to half-do it.
    /// </summary>
    public PlayerContract Sign(
        WorldState world, Player player, Team team, DateOnly date, double annualWage, int years,
        double signingBonus = 0, double? releaseClause = null, bool isHomegrown = false,
        ContractKind kind = ContractKind.Domestic, Guid? competitionId = null)
    {
        var contract = new PlayerContract
        {
            PlayerId = player.Id,
            TeamId = team.Id,
            Kind = kind,
            CompetitionId = competitionId,
            StartDate = date,
            EndDate = date.AddYears(Math.Max(1, years)),
            AnnualWage = Math.Round(Math.Max(WageFloor, annualWage), 0),
            SigningBonus = Math.Round(Math.Max(0, signingBonus), 0),
            ReleaseClauseValue = releaseClause,
            IsHomegrown = isHomegrown,
            Status = ContractStatus.Active
        };
        world.PlayerContracts.Add(contract);

        // Meeting-driven-selection ticket (requirement C): every signing - domestic, transfer,
        // free agent, or a franchise auction - flows through here, so this is the one place the
        // player's career team history has to be kept current. A shared past is never removed.
        player.CareerTeamIds.Add(team.Id);

        if (kind == ContractKind.Domestic)
        {
            player.CurrentTeamId = team.Id;
            player.TransferListed = false;
            player.TransferRequested = false;
            player.UnsettledUntil = null;
            if (!team.SquadPlayerIds.Contains(player.Id)) team.SquadPlayerIds.Add(player.Id);
        }

        if (signingBonus > 0) team.Finances.Budget -= contract.SigningBonus;
        return contract;
    }

    private const double WageFloor = 12_000;

    /// <summary>
    /// Slice 9.0: seeds a starting contract for an already-placed player at world creation. Wage
    /// from the WageBillService per-player benchmark x the (usually 1.0 at start) market index;
    /// end date 1-4 years out; a young player is likely homegrown; a genuine star has a chance of
    /// a release clause. A SEPARATE seeder step (WorldSeeder.GeneratePlayerContracts), not part of
    /// GenerateStarterWorld's own RNG stream - the Phase 8 determinism discipline.
    /// </summary>
    public PlayerContract SeedContract(Player player, Team team, DateOnly date, Random random, double marketIndex = 1.0)
    {
        double wage = _wages.PlayerWage(player) * marketIndex;
        int years = random.Next(1, 5);
        int age = player.Age(date);

        // A player who is young, or whose only club this is, reads as homegrown.
        bool homegrown = age <= 23 && random.NextDouble() < 0.7;

        double? releaseClause = null;
        double ability = AbilityScale.CompositeAbilityToHundred(player.CurrentAbility);
        if (ability >= 72 && random.NextDouble() < 0.35)
            releaseClause = Math.Round(new PlayerValuationService().EstimateValue(player, null, date, marketIndex) * (1.4 + random.NextDouble() * 0.8), 0);

        var contract = new PlayerContract
        {
            PlayerId = player.Id,
            TeamId = team.Id,
            Kind = ContractKind.Domestic,
            StartDate = date.AddYears(-random.Next(0, years)),
            EndDate = date.AddYears(years),
            AnnualWage = Math.Round(Math.Max(WageFloor, wage), 0),
            ReleaseClauseValue = releaseClause,
            IsHomegrown = homegrown,
            Status = ContractStatus.Active
        };
        return contract;
    }

    // ---------------- renewal ----------------

    /// <summary>
    /// The renewal negotiation, run once per contract at a fixed window ahead of its end date.
    /// Both sides have to agree:
    /// - <b>The club</b> wants to keep a valued player (FirstChoice/SecondChoice, or young with a
    ///   ceiling), can afford the wage, and is not carrying a glut in his position. It lets an
    ///   ageing fringe player run down.
    /// - <b>The player</b> weighs the wage offered against his market rate, his game-time prospects
    ///   (his squad status), and his temperament (Ambitious wants a bigger stage, Loyal stays,
    ///   MoneyFocused holds out for more).
    /// A successful renewal extends the deal 2-3 years and moves the wage toward the market rate.
    /// </summary>
    public ContractRenewalDecision EvaluateRenewal(
        PlayerContract contract, Player player, Team team, DateOnly asOf, Random random, double marketIndex = 1.0,
        double agentWageDemandMultiplier = 1.0)
    {
        // Phase 13 (§9.6): a well-regarded agent pushes the wage demand up - the market rate the
        // player is measured against is higher, so a cut-price renewal is harder to land.
        double marketWage = _wages.PlayerWage(player) * marketIndex * Math.Clamp(agentWageDemandMultiplier, 1.0, 1.25);
        int age = player.Age(asOf);
        double ability = AbilityScale.CompositeAbilityToHundred(player.CurrentAbility);

        // --- club side ---
        double clubWants = player.SquadStatus switch
        {
            SquadStatus.FirstChoice => 0.85,
            SquadStatus.SecondChoice or SquadStatus.ReturningFromInjury => 0.62,
            SquadStatus.Backup => 0.42,
            SquadStatus.DevelopmentProspect or SquadStatus.LongTermProject => 0.7,
            SquadStatus.Fringe => 0.22,
            _ => 0.35
        };
        // Age tempers it - a 34-year-old fringe player is let go, a 33-year-old first-choice kept a bit longer.
        if (age >= 33) clubWants *= player.SquadStatus == SquadStatus.FirstChoice ? 0.7 : 0.35;
        if (age >= 30 && age < 33) clubWants *= 0.85;
        // Budget headroom - a stretched club renews fewer.
        double budgetHeadroom = Math.Clamp((team.Board.SeasonBudget - marketWage * 5) / Math.Max(1, marketWage * 20), -0.5, 0.5);
        clubWants += budgetHeadroom * 0.3;
        if (team.Board.UnderTransferEmbargo) clubWants -= 0.15; // an embargoed club is careful with new commitments

        // --- player side ---
        // What the club would actually offer: near the market rate for a key man, below it for a
        // fringe player it is only half-trying to keep.
        double offeredWage = marketWage * player.SquadStatus switch
        {
            SquadStatus.FirstChoice => 1.05,
            SquadStatus.SecondChoice => 0.95,
            SquadStatus.DevelopmentProspect or SquadStatus.LongTermProject => 1.0,
            SquadStatus.Backup => 0.82,
            _ => 0.7
        };
        double wageSatisfaction = Math.Clamp(offeredWage / Math.Max(1, contract.AnnualWage), 0.6, 1.6);
        double gameTime = player.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice or SquadStatus.DevelopmentProspect or SquadStatus.LongTermProject ? 0.75 : 0.35;

        double playerAccepts = 0.35 + (wageSatisfaction - 1.0) * 0.6 + (gameTime - 0.5) * 0.4;
        if (player.Personality.HasFlag(PersonalityTrait.Loyal)) playerAccepts += 0.2;
        if (player.Personality.HasFlag(PersonalityTrait.Ambitious) && player.SquadStatus is SquadStatus.Backup or SquadStatus.Fringe) playerAccepts -= 0.25;
        if (player.Personality.HasFlag(PersonalityTrait.MoneyFocused) && wageSatisfaction < 1.1) playerAccepts -= 0.2;
        playerAccepts += (player.CoachTrust - 50) / 100.0 * 0.15;

        double both = Math.Clamp(clubWants, 0, 1) * Math.Clamp(playerAccepts, 0, 1);
        // When both sides are genuinely keen a deal gets done comfortably; when either is lukewarm
        // it usually does not. The 1.3x lifts the "both moderately keen" case (a valued young
        // first-choice on a fair offer) to a realistic ~75%+ without rescuing a "neither wants it"
        // case (an ageing fringe player the club is only half-trying to keep).
        bool renew = random.NextDouble() < Math.Clamp(both * 1.3 + 0.05, 0.03, 0.95);

        if (!renew)
            return new ContractRenewalDecision(false,
                $"{player.FullName} and {team.Name} have not agreed a new deal - his contract will run to {contract.EndDate:yyyy}.",
                null, null);

        int extraYears = age >= 32 ? 1 : age >= 29 ? 2 : 3;
        var newEnd = asOf > contract.EndDate ? asOf.AddYears(extraYears) : contract.EndDate.AddYears(extraYears);
        double newWage = Math.Round(Math.Max(contract.AnnualWage, offeredWage), 0);
        contract.EndDate = newEnd;
        contract.AnnualWage = newWage;

        // Post-Phase-16 completion pass (§9.2): a renewal ends a contract holdout - the deal that
        // triggered it has been improved.
        if (player.HoldingOut)
        {
            player.HoldingOut = false;
            if (player.NonInjuryUnavailability == Enums.UnavailabilityReason.ContractHoldout)
                player.NonInjuryUnavailability = Enums.UnavailabilityReason.Available;
        }

        // A renewal at the same club keeps homegrown status.
        return new ContractRenewalDecision(true,
            $"{team.Name} have extended {player.FullName}'s contract to {newEnd:yyyy} on {newWage:N0} a year.",
            newWage, newEnd);
    }

    // ---------------- expiry / free agency ----------------

    /// <summary>
    /// Slice 9.1: a contract has reached its end date with no renewal - the player leaves for
    /// nothing (Bosman). Fully detaches: contract Expired, CurrentTeamId cleared, out of the
    /// club's squad, status dropped to Fringe. He is now a free agent - FreeAgentMarketService
    /// (Slice 9.2) picks him up.
    /// </summary>
    public void MakeFreeAgent(WorldState world, Player player, PlayerContract contract, DateOnly date)
    {
        contract.Status = ContractStatus.Expired;
        if (world.Teams.TryGetValue(contract.TeamId, out var team))
            team.SquadPlayerIds.Remove(player.Id);
        player.CurrentTeamId = null;
        player.SquadStatus = SquadStatus.Fringe;
        player.TransferListed = false;
        player.TransferRequested = false;
        player.UnsettledUntil = null;
    }

    // ---------------- helpers ----------------

    public static PlayerContract? ActiveDomesticContract(WorldState world, Guid playerId) =>
        world.PlayerContracts.FirstOrDefault(c =>
            c.PlayerId == playerId && c.Kind == ContractKind.Domestic && c.Status == ContractStatus.Active);

    /// <summary>
    /// A club's total annual player wage bill from REAL contracts. Returns null when the club has
    /// no contract rows at all, so SeasonFinanceService can fall back to the WageBillService
    /// estimate for a world that never generated contracts.
    /// </summary>
    public static double? RealWageBill(WorldState world, Team team)
    {
        var contracts = world.PlayerContracts
            .Where(c => c.TeamId == team.Id && c.Kind == ContractKind.Domestic && c.Status == ContractStatus.Active)
            .ToList();
        if (contracts.Count == 0) return null;
        return Math.Round(contracts.Sum(c => c.AnnualWage) * 1.06, 0); // 6% overhead for bonuses/support
    }

    // ---------------- Slice 9.7: contract-depth rewards for a one-club servant ----------------

    /// <summary>
    /// Annual pass: pays a LOYALTY BONUS to a homegrown one-club servant at a long-tenure milestone,
    /// and grants a TESTIMONIAL (a benefit match - revenue, reputation, a farewell) to a homegrown
    /// veteran near the end of his career. Both are paid/granted exactly once (contract flags).
    /// Closes the Phase 7 "-> Phase 9" testimonial deferral.
    /// </summary>
    public IEnumerable<GameEvent> ProcessLoyaltyRewards(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();

        foreach (var contract in world.PlayerContracts.Where(c =>
                     c.Kind == ContractKind.Domestic && c.Status == ContractStatus.Active && c.IsHomegrown).ToList())
        {
            var player = world.Players.FirstOrDefault(p => p.Id == contract.PlayerId);
            if (player is null || player.IsRetired) continue;
            if (!world.Teams.TryGetValue(contract.TeamId, out var team)) continue;

            int yearsAtClub = date.Year - contract.StartDate.Year;
            int age = player.Age(date);

            // Loyalty bonus - a lump at ~10 years' service, once.
            if (!contract.LoyaltyBonusPaid && yearsAtClub >= 10)
            {
                double bonus = Math.Round(contract.AnnualWage * 1.5, 0);
                team.Finances.Budget -= bonus;
                contract.LoyaltyBonusPaid = true;
                player.Morale.Adjust(10);
                player.CoachTrust = Math.Min(100, player.CoachTrust + 8);
                events.Add(new GameEvent(date, GameEventType.LoyaltyBonusPaid,
                    $"{team.Name} pay {player.FullName} a loyalty bonus of {bonus:N0} for a decade of service.",
                    player.Id, team.Id));
            }

            // Testimonial - a benefit match for a homegrown veteran near the end.
            if (!contract.TestimonialGranted && contract.IsHomegrown && age >= 34 && yearsAtClub >= 12)
            {
                double gate = Math.Round(350_000 + team.Reputation.Domestic * 6_000, 0);
                team.Finances.Budget += gate;
                contract.TestimonialGranted = true;
                team.Reputation.Adjust(domesticDelta: 1);
                team.Board.MoveFanSentiment(4);
                player.Reputation.Adjust(domesticDelta: 3, continentalDelta: 1);
                player.Morale.Adjust(14);
                events.Add(new GameEvent(date, GameEventType.TestimonialGranted,
                    $"{team.Name} grant {player.FullName} a testimonial - {yearsAtClub} years a one-club servant. The benefit match raises {gate:N0}.",
                    player.Id, team.Id));
            }
        }

        return events;
    }
}
