namespace CricketManager.Domain.Entities;

/// <summary>
/// Phase 13 (§9.6): a player agent as a PERSISTENT entity, not just a phrase in a news line. An
/// agent has a name, a reputation, a stable of clients and a commission he takes on their deals.
/// A well-regarded agent (high <see cref="Reputation"/>) genuinely gets his clients better wages
/// and bigger moves - and takes a bigger cut for it. Agents pick up players as their reputation
/// rises and let go of ones who retire.
///
/// The existing anonymous agent behaviour (TransferMarketService.RunAgentBiddingWars,
/// TransferRequestService's agent-driven holdout) now names the real agent and scales with his
/// reputation. AgentService manages the rosters.
/// </summary>
public sealed class PlayerAgent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>0-100. How good he is at his job - the wage/fee uplift he wins, and how many top players want him.</summary>
    public double Reputation { get; set; } = 45;

    /// <summary>His commission on a client's transfer fee / signing bonus. A bigger name charges more (~4-9%).</summary>
    public double CommissionRate { get; set; } = 0.05;

    public List<Guid> ClientPlayerIds { get; set; } = new();

    /// <summary>Total commission earned - a simple career ledger.</summary>
    public double CareerEarnings { get; set; }

    /// <summary>The wage-demand multiplier this agent pushes for at a renewal - 1.0 (no agent) up to ~1.2 for a top operator.</summary>
    public double WageDemandMultiplier => 1.0 + Math.Clamp(Reputation, 0, 100) / 100.0 * 0.20;
}
