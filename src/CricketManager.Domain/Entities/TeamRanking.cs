using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Entities;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 8 (point 19): a team's standing in the ICC-style
/// ranking table for one format.
///
/// One row per (team, format). Points move after every match on a rating-difference model -
/// beating a side rated well above you is worth far more than beating one below you, and losing
/// to a minnow costs you. Deliberately opposition-STRENGTH-adjusted rather than purely
/// results-and-ratings the way the real ICC tables are - a stated deviation: it lets the
/// rankings converge toward genuine team quality faster in a simulation that does not have
/// decades of real fixtures to settle them.
/// </summary>
public sealed class TeamRanking
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TeamId { get; init; }
    public MatchFormat Format { get; init; }

    /// <summary>Rating points, roughly 0-140 like the real tables. Seeded from team strength, then moved by RankingService.</summary>
    public double Points { get; set; } = 50;

    /// <summary>1-based table position, recomputed by RankingService.Recompute. 0 = not yet placed.</summary>
    public int Position { get; set; }

    /// <summary>Matches this ranking has absorbed - low counts move faster (they are still settling).</summary>
    public int MatchesRated { get; set; }
}
