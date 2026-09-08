using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Real-world scorecard dismissal notation - issue 3 from the external probe pass. Both
/// `BatterCard` (live) and `BattingInningsRecord` (persisted) already carry the three raw fields
/// this needs (Dismissal, DismissedByBowlerId, FielderId) but share no common interface, so this
/// takes them as primitives rather than inventing one just to call this method.
///
/// The bug this closes: an ad-hoc renderer showed "c & b {bowler}" for EVERY catch except caught
/// behind, which is wrong - "c & b" is specifically the bowler taking his own return catch, the
/// one case where the catcher and the bowler are the same man. Every other catch, including a
/// catch behind the stumps, is "c {catcher} b {bowler}" on a real scorecard. `DismissalType` keeps
/// `Caught`/`CaughtBehind` as distinct values internally (dismissal-pattern analytics, matchup
/// history both want to know an edge from a straight catch) - only the SCORECARD TEXT collapses
/// them, because that is genuinely how a real scorecard reads.
/// </summary>
public static class ScorecardFormatter
{
    private static string ShortName(Player p) => string.IsNullOrWhiteSpace(p.LastName) ? p.FullName : p.LastName;

    /// <summary>
    /// The dismissal exactly as a real scorecard would print it, given a name lookup for whichever
    /// players are involved. Falls back to "?" for a player id that isn't in the lookup rather than
    /// throwing - a report should still render something for a partial or historical dataset.
    /// </summary>
    public static string DescribeDismissal(
        DismissalType dismissal, Guid? bowlerId, Guid? fielderId, IReadOnlyDictionary<Guid, Player> players)
    {
        string Bowler() => bowlerId is { } id && players.TryGetValue(id, out var b) ? ShortName(b) : "?";
        string Fielder() => fielderId is { } id && players.TryGetValue(id, out var f) ? ShortName(f) : "?";

        return dismissal switch
        {
            DismissalType.NotOut => "not out",
            DismissalType.Bowled => $"b {Bowler()}",
            DismissalType.LBW => $"lbw b {Bowler()}",
            DismissalType.CaughtAndBowled => $"c & b {Bowler()}",
            DismissalType.Caught or DismissalType.CaughtBehind => $"c {Fielder()} b {Bowler()}",
            DismissalType.Stumped => $"st {Fielder()} b {Bowler()}",
            // Credited to whoever actually fielded it, never the bowler by default - a run-out is
            // not the bowler's wicket. FielderId already correctly holds whichever fielder,
            // including the keeper when the throw ran through to his end, effected it (see
            // BallOutcomeModel.PickFielder) - no separate "which end" tracking is needed on top of
            // that, since the credited fielder already IS the answer to "who actually did it".
            DismissalType.RunOut => $"run out ({Fielder()})",
            DismissalType.HitWicket => $"hit wicket b {Bowler()}",
            DismissalType.Retired => "retired",
            _ => dismissal.ToString()
        };
    }

    /// <summary>Convenience overload reading straight off a live BatterCard.</summary>
    public static string DescribeDismissal(BatterCard card, IReadOnlyDictionary<Guid, Player> players) =>
        DescribeDismissal(card.Dismissal, card.DismissedByBowlerId, card.FielderId, players);

    /// <summary>Convenience overload reading straight off a persisted BattingInningsRecord.</summary>
    public static string DescribeDismissal(BattingInningsRecord record, IReadOnlyDictionary<Guid, Player> players) =>
        DescribeDismissal(record.Dismissal, record.DismissedByBowlerId, record.FielderId, players);
}
