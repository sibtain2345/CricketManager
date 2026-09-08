using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.1: a DELIBERATE STUB wage bill, so the season finance loop can produce a
/// realistic budget movement instead of pretending player wages are zero.
///
/// This is NOT a contract model - Phase 9 (Auction / Contracts / Market) owns real wages,
/// negotiations, bonuses and buy-outs, and will replace this outright. What this does is give
/// every club a plausible annual wage cost derived from the squad it actually carries: a
/// star-studded 25-man squad costs far more to run than a thin one of journeymen, which is the
/// single fact the finance loop needs to be honest.
///
/// The numbers are calibrated so a mid-table club's wage bill is the largest single line in
/// TeamFinanceService.ApplySeasonFinances (as it is in real cricket) without instantly
/// bankrupting anyone - a starting budget of ~1,000,000 and sponsorship/matchday income in the
/// same order of magnitude.
/// </summary>
public sealed class WageBillService
{
    /// <summary>The lowest a fringe professional is paid in a year - a floor every squad player clears.</summary>
    private const double BaseWage = 12_000;

    /// <summary>How steeply wage rises with quality. A world-class player (ability ~180, big reputation) lands around 20-30x a fringe player, which is roughly right for domestic cricket.</summary>
    private const double QualityWageSlope = 2_600;

    /// <summary>Estimated annual wage for one player, before any squad-level scaling.</summary>
    public double PlayerWage(Player player)
    {
        double ability = AbilityScale.CompositeAbilityToHundred(player.CurrentAbility);           // 0-100
        double reputation = (player.Reputation.Domestic + player.Reputation.Continental * 0.5
                             + player.Reputation.Worldwide * 0.3);                                // 0-180ish

        // Quality drives the bulk of it; reputation is a commercial premium on top.
        double wage = BaseWage
                      + Math.Pow(Math.Clamp(ability, 0, 100) / 100.0, 1.6) * QualityWageSlope * 20
                      + reputation * 900;

        // A player past his best on a legacy contract still costs money; a kid on a rookie deal is cheap.
        return Math.Round(Math.Max(BaseWage, wage), 0);
    }

    /// <summary>
    /// This team's total annual wage bill. Sums the squad's individual wages, then scales by a
    /// small overhead factor (staff wages are handled separately via StaffContract.AnnualSalary,
    /// but a club also carries academy players, bonuses and support costs this stub folds in).
    /// </summary>
    public double EstimateAnnualWageBill(Team team, IEnumerable<Player> squadPlayers, IEnumerable<StaffContract>? staffContracts = null)
    {
        var squad = squadPlayers.Where(p => !p.IsRetired && team.SquadPlayerIds.Contains(p.Id)).ToList();
        double playerWages = squad.Sum(PlayerWage);

        double staffWages = staffContracts?
            .Where(c => c.Status == ContractStatus.Active && c.TeamId == team.Id)
            .Sum(c => c.AnnualSalary) ?? 0;

        // 12% overhead for academy/bonuses/support the stub does not itemise.
        return Math.Round(playerWages * 1.12 + staffWages, 0);
    }
}
