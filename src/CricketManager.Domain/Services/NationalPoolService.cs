using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-6, section E: designs and evolves a national team's per-format player pool.
///
/// The pool is NOT built by naive position-counting. It is coverage-driven: every specialist
/// batting slot with multiple genuine backups, a real wicketkeeper battle (3-4 contenders), and
/// bowling VARIETY - a sensible spread across pace tiers and spin types rather than one archetype
/// repeated. Roughly 35-40 players per format.
///
/// It evolves continuously (ReviewPool): strong ongoing performance pulls a player in, a decline
/// or a younger option pushes one out - inclusion is never permanent. An extraordinary in-form
/// player from outside can be added, but always to the POOL first (AddException) - never straight
/// into a squad. And a squad for actual selection is drawn FROM the pool (DrawSquad), so the
/// pool is the real thing, not a parallel list.
/// </summary>
public sealed class NationalPoolService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();

    /// <summary>Target pool size per format - deep enough for real backup at every position.</summary>
    public const int TargetSize = 38;

    private sealed record CoverageSlot(string Label, int Target, Func<Player, bool> Matches);

    /// <summary>
    /// The coverage the pool has to have. The targets sum to a little over TargetSize on purpose -
    /// a player counts against every slot he fits (an allrounder against both), so the distinct
    /// count lands around the target.
    /// </summary>
    private static IReadOnlyList<CoverageSlot> Coverage(MatchFormat format) => new List<CoverageSlot>
    {
        new("openers", 4, p => p.BattingRole is BattingRole.Opener),
        new("top order", 5, p => p.BattingRole is BattingRole.Opener or BattingRole.TopOrder),
        new("middle order", 7, p => p.BattingRole is BattingRole.TopOrder or BattingRole.MiddleOrder
                                    && p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper),
        new("wicketkeepers", 4, p => p.PrimaryRole == PlayerRole.WicketKeeper),
        new("allrounders", 5, p => p.PrimaryRole is PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder),
        new("out-and-out pace", 4, p => IsPace(p) && p.Bowling.Pace >= 14),
        new("fast-medium / seam", 5, p => IsPace(p) && p.Bowling.Pace is >= 8 and < 16),
        new("off-spin", 2, p => p.BowlingStyle is BowlingStyle.RightArmOffSpin),
        new("leg-spin / wrist-spin", 2, p => p.BowlingStyle is BowlingStyle.RightArmLegSpin or BowlingStyle.LeftArmChinaman),
        new("left-arm spin", 2, p => p.BowlingStyle is BowlingStyle.LeftArmOrthodox),
        new("any front-line spin", 4, p => IsSpin(p)),
    };

    private static bool IsPace(Player p) => p.BowlingRole != BowlingRoleType.NotABowler && p.BowlingStyle is
        BowlingStyle.RightArmFast or BowlingStyle.RightArmFastMedium or BowlingStyle.RightArmMediumFast or BowlingStyle.RightArmMedium
        or BowlingStyle.LeftArmFast or BowlingStyle.LeftArmFastMedium or BowlingStyle.LeftArmMediumFast or BowlingStyle.LeftArmMedium;

    private static bool IsSpin(Player p) => p.BowlingRole != BowlingRoleType.NotABowler && p.BowlingStyle is
        BowlingStyle.RightArmOffSpin or BowlingStyle.RightArmLegSpin or BowlingStyle.LeftArmOrthodox or BowlingStyle.LeftArmChinaman;

    private double Score(Player p, MatchFormat format)
    {
        double bat = _evaluator.Evaluate(p, format).TotalScore;
        double bowl = p.BowlingRole != BowlingRoleType.NotABowler ? _evaluator.Evaluate(p, format, forBowling: true).TotalScore : 0;
        // A little weight on ceiling so genuine young prospects get pool consideration.
        double ceiling = AbilityScale.CompositeAbilityToHundred(p.PotentialAbility);
        return Math.Max(bat, bowl) * 0.88 + ceiling * 0.12;
    }

    /// <summary>
    /// Builds a format pool from scratch: fills every coverage slot with the best available
    /// eligible players of the country, then tops up to TargetSize with the next best overall.
    /// This is the Head Coach's strategic design - his philosophy nudges the ceiling weighting
    /// (a youth-focused coach carries a couple more prospects).
    /// </summary>
    public NationalPool BuildPool(Team nationalTeam, IEnumerable<Player> allPlayers, MatchFormat format, DateOnly asOf, Coach? coach)
    {
        var pool = new NationalPool { NationalTeamId = nationalTeam.Id, Format = format };

        var eligible = allPlayers
            .Where(p => !p.IsRetired && !p.RetiredFormats.Contains(format)
                        && p.AcademyTeamId is null // Phase 8: an academy prospect is not a national option
                        && string.Equals(p.Nationality, nationalTeam.Country, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => Score(p, format))
            .ToList();
        if (eligible.Count == 0) return pool;

        bool youthLean = coach?.Philosophy is CoachingPhilosophy.YouthDevelopment or CoachingPhilosophy.LongTermDevelopment;
        var chosen = new List<Player>();

        foreach (var slot in Coverage(format))
        {
            int have = chosen.Count(slot.Matches);
            foreach (var p in eligible.Where(slot.Matches))
            {
                if (have >= slot.Target) break;
                if (chosen.Contains(p)) { continue; }
                chosen.Add(p);
                have++;
            }
        }

        // Top up to the target with the next best players regardless of slot.
        int target = youthLean ? TargetSize + 2 : TargetSize;
        foreach (var p in eligible)
        {
            if (chosen.Count >= target) break;
            if (!chosen.Contains(p)) chosen.Add(p);
        }

        foreach (var p in chosen)
            pool.Add(p.Id, asOf, PoolReasoning(p, format, coach));

        // Keep the captaincy pointing at someone in the pool.
        return pool;
    }

    /// <summary>
    /// One review cycle. Drops a player who has retired, moved out of contention (a sustained
    /// decline while better options exist), or graduated off the watchlist without earning it;
    /// adds an eligible player whose recent form now genuinely beats the weakest current pool
    /// member at his position. Deliberately conservative - the pool should turn over, not churn.
    /// </summary>
    public IReadOnlyList<string> ReviewPool(NationalPool pool, Team nationalTeam, IEnumerable<Player> allPlayers, DateOnly asOf, Coach? coach)
    {
        var notes = new List<string>();
        var byId = allPlayers.ToDictionary(p => p.Id);

        var eligible = allPlayers
            .Where(p => !p.IsRetired && !p.RetiredFormats.Contains(pool.Format)
                        && p.AcademyTeamId is null // Phase 8: an academy prospect is not a national option
                        && string.Equals(p.Nationality, nationalTeam.Country, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // --- removals ---
        foreach (var entry in pool.Entries.ToList())
        {
            if (!byId.TryGetValue(entry.PlayerId, out var p) || p.IsRetired || p.RetiredFormats.Contains(pool.Format))
            {
                pool.Remove(entry.PlayerId);
                notes.Add($"{(p?.FullName ?? "A player")} drops out of the {pool.Format} pool (retired / unavailable).");
                continue;
            }

            // A watchlist entry who has not backed up his form in ~18 months is quietly moved on.
            if (entry.Watchlist && (asOf.DayNumber - entry.AddedDate.DayNumber) > 540
                && p.Form.CurrentForm < 5)
            {
                pool.Remove(entry.PlayerId);
                notes.Add($"{p.FullName} comes off the {pool.Format} watchlist - the form never came.");
                continue;
            }

            // An established member in a genuine, prolonged decline, with a clearly better option
            // available at his position, loses his place.
            if (!entry.Watchlist && p.Form.ConsecutivePoorPerformances(-20) >= 8)
            {
                var betterAtPosition = eligible.FirstOrDefault(o => !pool.Contains(o.Id)
                    && SamePosition(o, p) && Score(o, pool.Format) > Score(p, pool.Format) + 6);
                if (betterAtPosition is not null)
                {
                    pool.Remove(entry.PlayerId);
                    pool.Add(betterAtPosition.Id, asOf, $"comes into the {pool.Format} pool ahead of a struggling {DescribePosition(p)}");
                    notes.Add($"{betterAtPosition.FullName} replaces {p.FullName} in the {pool.Format} pool.");
                }
            }
        }

        // --- additions: a player in genuine form who now beats the weakest at his position ---
        var contenders = eligible
            .Where(p => !pool.Contains(p.Id) && p.Form.CurrentForm > 25)
            .OrderByDescending(p => Score(p, pool.Format))
            .Take(4);

        foreach (var c in contenders)
        {
            if (pool.Entries.Count >= TargetSize + 4) break;
            var weakestAtPosition = pool.Entries
                .Select(e => byId.GetValueOrDefault(e.PlayerId))
                .Where(p => p is not null && SamePosition(p!, c))
                .OrderBy(p => Score(p!, pool.Format))
                .FirstOrDefault();
            if (weakestAtPosition is null || Score(c, pool.Format) > Score(weakestAtPosition, pool.Format) - 2)
            {
                pool.Add(c.Id, asOf, $"forces his way into the {pool.Format} pool on the back of a run of form");
                notes.Add($"{c.FullName} is added to the {pool.Format} pool - {(int)c.Form.CurrentForm} form and rising.");
            }
        }

        return notes;
    }

    /// <summary>
    /// The "occasional pick from outside" the brief allows - rare, and it MUST go through the pool
    /// first. Adds an extraordinary in-form player as a watchlist entry (or a full entry for a
    /// truly exceptional case), with real reasoning. He is then eligible for selection like any
    /// other pool member - never a direct jump into a squad.
    /// </summary>
    public bool AddException(NationalPool pool, Player player, DateOnly asOf, string reasoning, bool exceptional = false)
    {
        if (pool.Contains(player.Id)) return false;
        pool.Add(player.Id, asOf, reasoning, watchlist: !exceptional);
        return true;
    }

    /// <summary>
    /// Draws a squad of the given size FROM the pool for actual selection - best available pool
    /// members for the format, keeping a keeper and a legal bowling spread, watchlist entries only
    /// once the established options run out.
    /// </summary>
    public IReadOnlyList<Guid> DrawSquad(NationalPool pool, IEnumerable<Player> allPlayers, MatchFormat format, DateOnly asOf, int size)
    {
        var byId = allPlayers.ToDictionary(p => p.Id);
        var members = pool.Entries
            .Select(e => (Entry: e, Player: byId.GetValueOrDefault(e.PlayerId)))
            .Where(x => x.Player is not null && !x.Player!.IsRetired && !x.Player.RetiredFormats.Contains(format))
            .Select(x => (x.Entry, Player: x.Player!, Score: Score(x.Player!, format)))
            .OrderBy(x => x.Entry.Watchlist ? 1 : 0)
            .ThenByDescending(x => x.Score)
            .ToList();

        var chosen = new List<Guid>();
        var keeper = members.FirstOrDefault(m => m.Player.PrimaryRole == PlayerRole.WicketKeeper);
        if (keeper.Player is not null) chosen.Add(keeper.Player.Id);

        foreach (var m in members)
        {
            if (chosen.Count >= size) break;
            if (!chosen.Contains(m.Player.Id)) chosen.Add(m.Player.Id);
        }
        return chosen;
    }

    private static bool SamePosition(Player a, Player b)
    {
        bool aBowl = a.BowlingRole != BowlingRoleType.NotABowler && a.PrimaryRole is PlayerRole.Bowler;
        bool bBowl = b.BowlingRole != BowlingRoleType.NotABowler && b.PrimaryRole is PlayerRole.Bowler;
        if (aBowl && bBowl) return IsSpin(a) == IsSpin(b);
        if (aBowl || bBowl) return false;
        if (a.PrimaryRole == PlayerRole.WicketKeeper || b.PrimaryRole == PlayerRole.WicketKeeper)
            return a.PrimaryRole == b.PrimaryRole;
        return a.BattingRole == b.BattingRole;
    }

    private static string DescribePosition(Player p) => p.PrimaryRole switch
    {
        PlayerRole.WicketKeeper => "keeper",
        PlayerRole.Bowler => IsSpin(p) ? "spinner" : "seamer",
        PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder => "allrounder",
        _ => $"{p.BattingRole.ToString().ToLowerInvariant()} batter"
    };

    private static string PoolReasoning(Player p, MatchFormat format, Coach? coach) => p.PrimaryRole switch
    {
        PlayerRole.WicketKeeper => $"one of the country's genuine {format} wicketkeeping options",
        PlayerRole.Bowler when IsSpin(p) => $"provides {p.BowlingStyle} in the {format} attack",
        PlayerRole.Bowler => $"pace/seam depth for {format}",
        PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder => $"the balance an allrounder gives the {format} side",
        _ => $"a {DescribePosition(p)} in the {format} plans"
    };
}
