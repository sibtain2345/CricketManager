using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Classifies a completed limited-overs match into a MatchResultStory, from ONE team's own
/// perspective - built entirely from data MatchResult already carries (WinMargin, WonByWickets,
/// FallOfWickets, team strength), per the planning brief's own instruction to build this from
/// information already available in the match/simulation rather than adding new tracked state.
///
/// Deliberately does NOT attempt "lost from a winning position" as its own category - that
/// needs a genuine par-score replay across the whole run chase to say honestly, which is a
/// larger, separate piece of work; a plain HeavyLoss/CloseLoss/CollapseLoss/OrdinaryLoss is what
/// this can say TRUTHFULLY today, and that is stated here rather than faked with a shortcut
/// heuristic that would sometimes be wrong. "Losing after a long winning streak" is likewise not
/// a story of the MATCH - it is read from Team.CurrentStreak by TeamMoraleService instead,
/// exactly where that context actually lives.
/// </summary>
public sealed class MatchResultContextService
{
    /// <summary>A meaningfully weaker side is one whose strength trails by at least this much - calibrated as "clearly the underdog", not any small gap.</summary>
    private const double UpsetStrengthGap = 12;

    public MatchResultStory Classify(MatchResult match, Guid teamId, double teamStrength, double opponentStrength)
    {
        if (match.WinningTeamId is null)
            return match.ResultForHome == MatchOutcome.Tie ? MatchResultStory.OrdinaryLoss : MatchResultStory.NoResult;

        bool won = match.WinningTeamId == teamId;

        if (!won)
            return ClassifyLoss(match, teamId);

        bool upset = teamStrength < opponentStrength - UpsetStrengthGap;
        if (upset) return MatchResultStory.UpsetWin;

        return ClassifyWin(match);
    }

    private static MatchResultStory ClassifyWin(MatchResult match)
    {
        if (match.WonByWickets)
        {
            int wicketsInHand = match.WinMargin ?? 5;
            int wicketsLost = 10 - wicketsInHand;

            // Won, but had to fight through real trouble along the way - a genuine escape,
            // not a comfortable chase, regardless of how it finished.
            if (wicketsLost >= 6) return MatchResultStory.ComebackWin;
            if (wicketsInHand >= 8) return MatchResultStory.DominantWin;
            if (wicketsInHand <= 2) return MatchResultStory.NarrowWin;
            return MatchResultStory.OrdinaryWin;
        }
        else
        {
            // Winner defended, so the winner batted first - their own innings total is what
            // the margin should be read as a percentage OF.
            var winnersInnings = match.FirstInnings.BattingTeamId == match.WinningTeamId ? match.FirstInnings : match.SecondInnings;
            double defendedTotal = Math.Max(1, winnersInnings.Runs);
            double marginPercent = (match.WinMargin ?? 0) / defendedTotal;

            if (marginPercent >= 0.35) return MatchResultStory.DominantWin;
            if (marginPercent <= 0.08 || (match.WinMargin ?? 0) <= 15) return MatchResultStory.NarrowWin;
            return MatchResultStory.OrdinaryWin;
        }
    }

    private static MatchResultStory ClassifyLoss(MatchResult match, Guid teamId)
    {
        var losersInnings = match.FirstInnings.BattingTeamId == teamId ? match.FirstInnings : match.SecondInnings;

        // A genuine batting collapse: the last five wickets went for next to nothing, which
        // reads completely differently to a side that fought and simply came up short.
        var falls = losersInnings.FallOfWickets;
        if (falls.Count >= 10)
        {
            int runsInLastFive = falls[9].Score - falls[4].Score;
            if (runsInLastFive < 30) return MatchResultStory.CollapseLoss;
        }

        if (match.WonByWickets)
        {
            // The loser was defending and got chased down - margin is the winning side's
            // wickets in hand, which reads the SAME direction for how comfortable it was.
            int winningWicketsInHand = match.WinMargin ?? 5;
            if (winningWicketsInHand >= 8) return MatchResultStory.HeavyLoss;
            if (winningWicketsInHand <= 2) return MatchResultStory.CloseLoss;
        }
        else
        {
            double marginPercent = (match.WinMargin ?? 0) / Math.Max(1, losersInnings.Runs + (match.WinMargin ?? 0));
            if (marginPercent >= 0.35) return MatchResultStory.HeavyLoss;
            if (marginPercent <= 0.08 || (match.WinMargin ?? 0) <= 15) return MatchResultStory.CloseLoss;
        }

        return MatchResultStory.OrdinaryLoss;
    }
}
