using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 6, Slice 6.7: national-team selection. A national side is not a club with a fixed roster
/// - it is a POOL of the eligible players of a country, from which a squad is named for each
/// tournament (by the same AiClubManagementService / XiSelectionService path a club uses, since a
/// national Team with its pool in SquadPlayerIds looks like any other team to those services).
///
/// This service owns the one national-specific job: keeping that pool current. It is refreshed on
/// the annual rollover so a player who has broken through gets into contention and a retired one
/// drops out.
/// </summary>
public sealed class NationalSelectionService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();

    /// <summary>How deep the national pool goes - enough for a tournament squad plus genuine cover and fringe contenders.</summary>
    public const int PoolSize = 24;

    private readonly NationalPoolService _poolService = new();

    /// <summary>
    /// Rebuilds/evolves a national team's PER-FORMAT pools (section E) and sets Team.SquadPlayerIds
    /// to the union of the three, so every service that reads SquadPlayerIds still works. A pool
    /// player's CurrentTeamId is left ALONE - national selection is a parallel concept, not a
    /// transfer. Called on the annual rollover.
    /// </summary>
    public IReadOnlyList<string> RefreshPool(Team nationalTeam, IEnumerable<Player> allPlayers, DateOnly asOf,
        IList<NationalPool>? pools = null, Coach? coach = null)
    {
        if (!nationalTeam.IsNational) return Array.Empty<string>();
        var players = allPlayers.ToList();
        var notes = new List<string>();

        foreach (var format in new[] { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 })
        {
            NationalPool pool;
            if (pools is not null && pools.FirstOrDefault(p => p.NationalTeamId == nationalTeam.Id && p.Format == format) is { } existing)
            {
                pool = existing;
                notes.AddRange(_poolService.ReviewPool(pool, nationalTeam, players, asOf, coach));
            }
            else
            {
                pool = _poolService.BuildPool(nationalTeam, players, format, asOf, coach);
                pools?.Add(pool);
            }
        }

        // SquadPlayerIds = union of the format pools, so AiClubManagementService / FixturePlayService
        // (which read SquadPlayerIds) see the whole national player base.
        var union = (pools ?? Array.Empty<NationalPool>())
            .Where(p => p.NationalTeamId == nationalTeam.Id)
            .SelectMany(p => p.Entries.Select(e => e.PlayerId))
            .Distinct()
            .ToList();

        if (union.Count == 0)
        {
            // No pools supplied (a caller on the old 2-arg path) - fall back to the flat best-N list.
            union = players
                .Where(p => !p.IsRetired && string.Equals(p.Nationality, nationalTeam.Country, StringComparison.OrdinalIgnoreCase))
                .Select(p => (Player: p, Score: BestFormatScore(p)))
                .OrderByDescending(x => x.Score)
                .Take(PoolSize)
                .Select(x => x.Player.Id)
                .ToList();
        }

        nationalTeam.SquadPlayerIds = union;

        // Meeting-driven-selection ticket (requirement C): a spell in the national pool is genuine
        // shared time - it is where a domestic captain builds a first-hand read on his countrymen
        // that a foreign franchise coach never gets. Recorded permanently on the player's career
        // team history.
        foreach (var pid in union)
            if (players.FirstOrDefault(p => p.Id == pid) is { } poolPlayer)
                poolPlayer.CareerTeamIds.Add(nationalTeam.Id);

        // Keep the national captaincy pointed at someone in the pool (CaptaincyAppointmentService
        // owns the real appointment; this is just a safety net so a stale id never lingers).
        foreach (var format in new[] { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 })
        {
            var current = nationalTeam.GetCaptain(format);
            if (current is { } id && union.Contains(id)) continue;

            var newCaptain = players
                .Where(p => union.Contains(p.Id) && !p.RetiredFormats.Contains(format) && !p.IsRetired)
                .OrderByDescending(p => p.Mental.Leadership * 5 + p.Reputation.Domestic)
                .FirstOrDefault();
            if (newCaptain is not null) nationalTeam.SetCaptain(format, newCaptain.Id);
        }

        return notes;
    }

    /// <summary>The pool for a national team and format, if one has been built.</summary>
    public static NationalPool? PoolFor(IEnumerable<NationalPool> pools, Guid nationalTeamId, MatchFormat format) =>
        pools.FirstOrDefault(p => p.NationalTeamId == nationalTeamId && p.Format == format);

    private double BestFormatScore(Player p)
    {
        double test = _evaluator.Evaluate(p, MatchFormat.Test).TotalScore;
        double odi = _evaluator.Evaluate(p, MatchFormat.ODI).TotalScore;
        double t20 = _evaluator.Evaluate(p, MatchFormat.T20).TotalScore;
        return Math.Max(test, Math.Max(odi, t20));
    }
}
