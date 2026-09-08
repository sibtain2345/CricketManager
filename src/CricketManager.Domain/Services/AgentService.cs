using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 13 (§9.6): manages the persistent <see cref="PlayerAgent"/> rosters. Runs quarterly:
/// - a player whose reputation has risen and who has no agent is signed by one (the better the
///   player, the better the agent who lands him);
/// - a retired client is dropped;
/// - an agent's own reputation drifts on how his clients are doing.
///
/// The negotiation EFFECT lives where the deals are done (PlayerContractService.EvaluateRenewal
/// reads the agent's WageDemandMultiplier; TransferMarketService names the agent and pays his
/// commission). This service is just the book-keeping. RNG-free.
/// </summary>
public sealed class AgentService
{
    public IEnumerable<GameEvent> ReviewQuarterly(WorldState world, DateOnly date)
    {
        if (world.Agents.Count == 0) yield break;

        var agentsByRep = world.Agents.OrderByDescending(a => a.Reputation).ToList();

        // Drop retired clients.
        foreach (var agent in world.Agents)
            agent.ClientPlayerIds.RemoveAll(id =>
                world.Players.FirstOrDefault(p => p.Id == id) is not { IsRetired: false });

        // Sign up unrepresented players who have grown into needing an agent.
        foreach (var player in world.Players.Where(p => !p.IsRetired && p.AgentId is null && p.AcademyTeamId is null))
        {
            double profile = player.Reputation.Domestic + player.Reputation.Continental * 0.4 + player.Reputation.Worldwide * 0.3;
            if (profile < 42) continue;

            // A bigger player attracts a better agent - but only one with room on his books.
            var agent = agentsByRep.FirstOrDefault(a => a.ClientPlayerIds.Count < 14
                                                        && a.Reputation >= profile - 25)
                        ?? agentsByRep.LastOrDefault(a => a.ClientPlayerIds.Count < 18);
            if (agent is null) continue;

            agent.ClientPlayerIds.Add(player.Id);
            player.AgentId = agent.Id;
            if (profile >= 75)
                yield return new GameEvent(date, GameEventType.PlayerSigned,
                    $"{player.FullName} signs with the agent {agent.Name}.", player.Id);
        }

        // Agent reputation drifts on the standing of his stable.
        foreach (var agent in world.Agents)
        {
            if (agent.ClientPlayerIds.Count == 0) { agent.Reputation = Math.Max(20, agent.Reputation - 0.3); continue; }
            double avgClientProfile = agent.ClientPlayerIds
                .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Where(p => p is not null)
                .Average(p => p!.Reputation.Domestic + p.Reputation.Continental * 0.3 + p.Reputation.Worldwide * 0.4);
            // The best clients pull an agent's standing up; drift is slow.
            agent.Reputation += (Math.Max(agent.Reputation, avgClientProfile) - agent.Reputation) * 0.03
                                - (avgClientProfile < agent.Reputation - 20 ? 0.5 : 0);
            agent.Reputation = Math.Clamp(agent.Reputation, 20, 95);
            agent.CommissionRate = Math.Round(0.04 + agent.Reputation / 100.0 * 0.05, 3);
        }
    }

    /// <summary>The agent representing this player, or null.</summary>
    public static PlayerAgent? AgentFor(WorldState world, Player player) =>
        player.AgentId is { } aid ? world.Agents.FirstOrDefault(a => a.Id == aid) : null;
}
