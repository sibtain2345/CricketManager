using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.8: the national selection panel / chairman of selectors, layered over
/// NationalPoolService.
///
/// NationalPoolService evolves a pool on genuine current form. This models the fact that a
/// national panel is NOT a pure form machine: a weak, politicised panel recalls a fading name on
/// reputation and old runs, and is slow to trust a young player in form who has not "done it at
/// the top level". A strong panel with a sharp chairman largely gets out of the way and lets the
/// form-based evolution stand.
///
/// The distortion is bounded - a panel picks the wrong player at the margins, it does not gut the
/// pool - and it is entirely gated on NationalBoard quality, so a well-run set-up sees almost none
/// of it.
/// </summary>
public sealed class SelectionPanelService
{
    /// <summary>
    /// Applies the panel's influence to a national team's pools AFTER NationalPoolService has done
    /// its form-based review. Consumes the caller's shared RNG at a fixed point. Returns
    /// SelectionPanelNote events describing any panel-driven call.
    /// </summary>
    public IReadOnlyList<GameEvent> ApplyPanelInfluence(
        Team nationalTeam, IReadOnlyList<NationalPool> pools, IReadOnlyList<Player> allPlayers, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var board = nationalTeam.NationalBoard;
        if (board is null || !nationalTeam.IsNational) return events;

        // A sharp, apolitical panel barely intervenes; a weak, political one meddles.
        double meddleChance = Math.Clamp(
            (60 - board.ChairmanOfSelectorsQuality) / 60.0 * 0.5
            + board.Politicisation / 100.0 * 0.4, 0, 0.75);

        var countrymen = allPlayers
            .Where(p => !p.IsRetired && string.Equals(p.Nationality, nationalTeam.Country, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var pool in pools.Where(p => p.NationalTeamId == nationalTeam.Id).OrderBy(p => p.Format))
        {
            if (random.NextDouble() >= meddleChance) continue;

            // --- a reputation recall: a well-known name not currently in the pool, past his best form ---
            var recall = countrymen
                .Where(p => !pool.Contains(p.Id) && !p.RetiredFormats.Contains(pool.Format)
                            && p.Reputation.Domestic >= 60 && p.Form.CurrentForm < 10 && p.Age(date) >= 30)
                .OrderByDescending(p => p.Reputation.Domestic)
                .FirstOrDefault();

            if (recall is not null && random.NextDouble() < 0.6)
            {
                pool.Add(recall.Id, date, "a panel recall on reputation and past service", watchlist: true);
                events.Add(new GameEvent(date, GameEventType.SelectionPanelNote,
                    $"The {nationalTeam.Country} selectors bring {recall.FullName} back into the {pool.Format} picture despite a thin run of form.",
                    recall.Id, nationalTeam.Id));
                continue;
            }

            // --- overlooking a young player in form the panel does not yet trust ---
            var overlooked = countrymen
                .Where(p => pool.Contains(p.Id) && pool.EntryFor(p.Id)!.Watchlist
                            && p.Age(date) <= 23 && p.Form.CurrentForm > 30)
                .OrderByDescending(p => p.Form.CurrentForm)
                .FirstOrDefault();

            if (overlooked is not null && random.NextDouble() < 0.4)
            {
                pool.Remove(overlooked.Id);
                events.Add(new GameEvent(date, GameEventType.SelectionPanelNote,
                    $"The {nationalTeam.Country} selectors leave out {overlooked.FullName} for the {pool.Format} squad - the panel wants to see more.",
                    overlooked.Id, nationalTeam.Id));
            }
        }

        return events;
    }

    /// <summary>
    /// How much a national-board objective (a WTC final, a World Cup) should weigh - the political
    /// board reaches for the coach faster on a tournament exit, so a missed target hurts more.
    /// </summary>
    public double ObjectivePressureMultiplier(Team nationalTeam) =>
        nationalTeam.NationalBoard is { } nb
            ? Math.Clamp(1 + nb.Politicisation / 100.0 * 0.6, 1, 1.6)
            : 1;
}
