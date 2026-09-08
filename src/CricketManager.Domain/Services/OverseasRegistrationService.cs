using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 9, Slice 9.5: enforces a competition's overseas-player limit on a matchday XI (a franchise
/// league caps non-home-nation players at ~4). Post-processes an XiSelectionResult: if the eleven
/// has more overseas players than allowed, it swaps the weakest overseas players out for the
/// best available domestic players in the squad, and the ones bumped read as
/// UnavailabilityReason.Unregistered for that fixture (the enum value that has existed since Phase 4
/// for exactly this and never been set by anything).
/// </summary>
public sealed class OverseasRegistrationService
{
    public XiSelectionResult EnforceLimit(
        XiSelectionResult xi, IReadOnlyList<Player> fullSquad, string homeNation, int limit, MatchFormat format)
    {
        if (limit <= 0) return xi;

        bool IsOverseas(Player p) => !string.Equals(p.Nationality, homeNation, StringComparison.OrdinalIgnoreCase);

        var order = xi.BattingOrder.ToList();
        int overseasInXi = order.Count(IsOverseas);
        if (overseasInXi <= limit) return xi;

        var reasoning = xi.Reasoning.ToList();
        int toRemove = overseasInXi - limit;

        // The overseas players to drop: weakest by score, keeper protected.
        var dropList = order
            .Where(p => IsOverseas(p) && p.Id != xi.Wicketkeeper?.Id)
            .OrderBy(p => SquadNeeds.ScoreFor(p, format))
            .Take(toRemove)
            .ToList();

        // If the keeper himself is an overseas surplus and there is no other option, he stays -
        // a legal XI needs a keeper more than it needs to be one under the cap. Rare.
        var replacements = fullSquad
            .Where(p => !IsOverseas(p) && !order.Contains(p) && !p.IsRetired)
            .OrderByDescending(p => SquadNeeds.ScoreFor(p, format))
            .ToList();

        int done = 0;
        foreach (var drop in dropList)
        {
            var repl = replacements.ElementAtOrDefault(done);
            if (repl is null) break;
            order.Remove(drop);
            order.Add(repl);
            drop.NonInjuryUnavailability = UnavailabilityReason.Unregistered; // for this fixture
            reasoning.Add($"{drop.FullName} left out - the {limit}-overseas limit - {repl.FullName} plays instead.");
            done++;
        }

        if (done == 0) return xi;

        order = order.OrderBy(p => p.BattingRole)
            .ThenBy(p => xi.BattingOrder.Select((x, i) => (x.Id, i)).ToDictionary(t => t.Id, t => t.i).GetValueOrDefault(p.Id, 99))
            .ToList();
        var bowlers = order.Where(p => p.BowlingRole != BowlingRoleType.NotABowler).ToList();

        return xi with { BattingOrder = order, Bowlers = bowlers.Count > 0 ? bowlers : xi.Bowlers, Reasoning = reasoning };
    }

    /// <summary>Clears the transient Unregistered flag EnforceLimit set, once the fixture is done. Only touches players it flagged (Unregistered, not injured/suspended/etc.).</summary>
    public static void ClearFixtureFlags(IEnumerable<Player> players)
    {
        foreach (var p in players.Where(p => p.NonInjuryUnavailability == UnavailabilityReason.Unregistered))
            p.NonInjuryUnavailability = UnavailabilityReason.Available;
    }
}
