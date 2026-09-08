using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-rectification follow-up: the franchise-league economy, deliberately built so <b>no
/// franchise ever runs a loss</b> (the user's explicit constraint).
///
/// A real franchise league is a central commercial property: the league sells one broadcast deal
/// and one set of title/associate sponsorships, and distributes the money EQUALLY to the
/// franchises (Section K - no league-position wealth gap). That central pool is sized here to
/// exceed the auction purse plus operating costs by construction, so a franchise that spends its
/// entire purse still closes the season in profit. On top of that:
/// - <b>Prize money</b> by league finishing position (paid by CompetitionRevenueService in
///   CompetitionSeasonRunner.FinishSeason, same as every other competition - not double-counted here).
/// - <b>Local revenue</b> - gate, local sponsorship, merchandise - scaled by the franchise's own
///   fan sentiment and city draw.
/// - <b>Operating cost</b> - staff, travel, ground hire - modest and fixed.
/// - A hard <b>reserve floor</b>: whatever the season did, a franchise's budget never closes below
///   a healthy positive reserve. This is the guarantee, and it is stated rather than implied.
///
/// FinancialFairPlayService and SeasonFinanceService both skip franchise teams entirely, so an
/// intra-season negative budget (from the auction spend) carries no penalty - it is squared away
/// here at season end.
/// </summary>
public sealed class FranchiseFinanceService
{
    /// <summary>The healthy positive reserve a franchise's budget is floored to at season end - the loss-proof guarantee.</summary>
    public const double ReserveFloor = 3_000_000;

    public sealed record FranchiseSettlement(
        Guid FranchiseId, string Name, double CentralPool, double PrizeMoney, double LocalRevenue,
        double PurseSpend, double OperatingCost, double Net, double ClosingBudget);

    /// <summary>
    /// Basis for the auction purse AND the central pool - one figure so they move together: a
    /// bigger market means a bigger purse and a bigger TV deal to fund it.
    /// </summary>
    public static double PurseBasis(WorldState world, Competition competition) =>
        Math.Round(14_000_000 * Math.Clamp(world.MarketIndex, 0.5, 6.0) * world.ProfileFor(competition.Country).EconomicScale, 0);

    public IReadOnlyList<(FranchiseSettlement Settlement, GameEvent Event)> SettleSeason(
        WorldState world, Competition competition, CompetitionSeason season, DateOnly date)
    {
        var results = new List<(FranchiseSettlement, GameEvent)>();
        var franchiseIds = season.ParticipatingTeamIds.ToHashSet();
        var franchises = world.Teams.Values.Where(t => franchiseIds.Contains(t.Id)).OrderBy(t => t.Name).ToList();
        if (franchises.Count == 0) return results;

        double basis = PurseBasis(world, competition);

        // The central pool per franchise: sized to cover a FULL purse spend (basis) plus operating
        // costs plus a guaranteed margin. Lifted a little by the league's standing (a bigger TV deal).
        double standing = (Math.Clamp(competition.Prestige, 0, 100) * 0.6 + Math.Clamp(competition.Reputation, 0, 100) * 0.4) / 100.0;
        double centralPerFranchise = Math.Round(basis * (1.30 + standing * 0.35), 0);

        // Ranked standings for the finishing-position note (prize money itself is applied by
        // CompetitionRevenueService in FinishSeason; this only labels the settlement).
        var ranked = season.Standings
            .OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate)
            .Select(s => s.TeamId).ToList();

        foreach (var f in franchises)
        {
            double purseSpend = world.PlayerContracts
                .Where(c => c.Kind == ContractKind.Franchise && c.CompetitionId == competition.Id
                            && c.StartDate.Year == season.Year)
                .Where(c => c.TeamId == f.Id)
                .Sum(c => c.AnnualWage);

            double operating = Math.Round(basis * 0.14, 0);

            // Local revenue scaled by fan mood and city draw (ground capacity / reputation).
            double fanFactor = 0.7 + f.Board.FanSentiment / 100.0 * 0.6;
            var ground = f.HomeGroundId is { } gid && world.Grounds.TryGetValue(gid, out var g) ? g : null;
            double cityDraw = ground is null ? 1.0 : Math.Clamp(0.6 + ground.Capacity / 60_000.0 + ground.Reputation / 200.0, 0.6, 1.8);
            double localRevenue = Math.Round(basis * 0.22 * fanFactor * cityDraw, 0);

            // Prize money is the amount CompetitionRevenueService will have already paid in for the
            // finishing position - recomputed here only for the settlement label.
            int pos = ranked.IndexOf(f.Id);
            string finishNote = pos == 0 ? "league winners" : pos == 1 ? "runners-up" : pos < 4 ? "a playoff finish" : $"{Ordinal(pos + 1)}";
            double prizeLabel = pos < 0 ? 0 : Math.Round(new CompetitionRevenueService().CalculatePrizePool(competition, franchises.Count) * 0.40 * PositionShare(pos, franchises.Count), 0);

            double net = centralPerFranchise + localRevenue - purseSpend - operating;

            // Loss-proof by construction: the central pool alone exceeds a full purse + operating,
            // so net is already positive - but floor the CLOSING budget to a healthy reserve
            // regardless, as the explicit guarantee.
            f.Finances.Budget += centralPerFranchise + localRevenue - operating;
            if (f.Finances.Budget < ReserveFloor)
                f.Finances.Budget = ReserveFloor;

            // Follow-up: the city's fans react to the campaign - a title lifts the fan base, a
            // bottom-of-the-table season deflates it. Feeds next year's local revenue and the
            // campaign-pressure the board applies to the coach.
            double fanMove = pos == 0 ? 8 : pos == 1 ? 4 : pos < franchises.Count / 2 ? 1 : pos == franchises.Count - 1 ? -6 : -2;
            f.Board.MoveFanSentiment(fanMove);

            var settlement = new FranchiseSettlement(f.Id, f.Name, centralPerFranchise, prizeLabel, localRevenue,
                purseSpend, operating, net, Math.Round(f.Finances.Budget, 0));

            var ev = new GameEvent(date, GameEventType.FranchiseFinancesSettled,
                $"{f.Name} close the {competition.Name} {season.Year} season as {finishNote} - "
                + $"central distribution {centralPerFranchise:N0}, local revenue {localRevenue:N0}, "
                + $"against an auction spend of {purseSpend:N0}: a surplus of {Math.Max(0, net):N0}.",
                f.Id, competition.Id);

            results.Add((settlement, ev));
        }

        return results;
    }

    private static double PositionShare(int pos, int n) => pos switch
    {
        0 => 0.55, 1 => 0.25, 2 => 0.12, 3 => 0.08, _ => 0.0
    };

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th"
    };
}
