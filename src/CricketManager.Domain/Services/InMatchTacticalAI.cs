using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 6, Slice 6.4: the in-innings tactical AI that the "Deferred feature: AI in-innings
/// tactical decisions" note in CLAUDE.md always pointed at - promoting a pinch-hitter when quick
/// runs are needed, holding a specialist back, reacting to the state of the game rather than
/// batting a fixed card top to bottom.
///
/// This slice builds the batting-order half: when a wicket falls, the side may send in a
/// big-hitter from further down instead of the next man on the card - if the situation genuinely
/// calls for it (the death overs of a limited-overs innings, or a chase drifting behind the
/// required rate) and there is a genuinely better hitter available to promote. The nightwatchman
/// (the other established order-change) already lives in InningsSimulator and is unchanged.
///
/// How good the call is depends on who is making it. An AI-coached side reads the game through
/// its captain's TacticalJudgement; a poor captain misses the moment. A human coach who has set
/// his own order keeps it - the AI never silently overrides a batting order the coach chose
/// (TacticalPlan.DecisionAuthority for InMatchDecision.BattingApproach gates it).
/// </summary>
public sealed class InMatchTacticalAI
{
    private readonly MatchupConfidenceService _matchups = new();

    /// <summary>
    /// Follow-up Pass 4 (§3.6): "hiding the bunny" - a second, genuinely different batting-order
    /// shuffle from the pinch-hitter above. When a wicket falls and the next man on the card has a
    /// real, evidenced weak record against the bowler CURRENTLY operating, a genuinely
    /// better-matched not-yet-batted player is sent in ahead of him instead - the protected batter
    /// keeps his place in the order and comes in later, once the bowler's spell has moved on or
    /// someone else falls, exactly the "he simply comes in later" shape the nightwatchman already
    /// uses.
    ///
    /// Deliberately reads real matchup history via <see cref="MatchupConfidenceService"/> - the
    /// same sample-gated, bowler-keyed confidence the live ball model itself reads before letting
    /// a matchup move an outcome - rather than a guess at who "looks like" a bunny. Format-agnostic
    /// (this is a genuinely real tactic in a Test match's last session as much as a T20 chase),
    /// unlike the pinch-hitter above which is deliberately limited-overs only.
    /// </summary>
    public int? ChooseBunnyProtection(
        InningsState state, IReadOnlyList<Player> battingOrder, int nextBatterIndex,
        MatchLeadership? leadership, TacticalPlan plan, Random random)
    {
        if (nextBatterIndex >= battingOrder.Count - 1) return null;   // nobody left to protect him with
        if (state.CurrentBowlerId is not { } bowlerId) return null;   // no bowler on yet to be a bunny against

        // The coach's own order stands unless he has delegated the call - identical authority gate
        // to the pinch-hitter, since both are the same underlying decision (who bats next).
        var authority = plan.ResolveAuthority(InMatchDecision.BattingApproach);
        if (authority == DecisionAuthority.CoachHasFinalSay) return null;

        string key = MatchupKey.ForBowler(bowlerId);
        var nextMan = battingOrder[nextBatterIndex];
        double nextManMultiplier = _matchups.GetMatchupMultiplier(nextMan, key);
        if (nextManMultiplier >= 0.95) return null;                   // no real bunny relationship to hide from

        // Best not-yet-batted alternative against this SAME bowler.
        int bestIndex = -1;
        double bestMultiplier = nextManMultiplier;
        for (int i = nextBatterIndex + 1; i < battingOrder.Count; i++)
        {
            if (HasBatted(state, battingOrder[i].Id)) continue;
            double m = _matchups.GetMatchupMultiplier(battingOrder[i], key);
            if (m > bestMultiplier)
            {
                bestMultiplier = m;
                bestIndex = i;
            }
        }
        if (bestIndex < 0) return null;

        // A genuinely meaningful gap, not noise - half of the multiplier's full possible swing.
        double margin = bestMultiplier - nextManMultiplier;
        if (margin < 0.05) return null;

        double captainRead = leadership is null
            ? 0.4                                                     // an unled side rarely thinks to shuffle for a single matchup
            : Math.Clamp(leadership.Profile.TacticalJudgement(leadership.Captain) / 100.0, 0.15, 0.95);

        // Deliberately a smaller, gentler chance than the pinch-hitter - protecting a bunny for one
        // bowler's spell is a subtler, less certain call than sending in a hitter at the death.
        double protectChance = Math.Clamp(captainRead * 0.55 * (margin / 0.10), 0, 0.65);
        return random.NextDouble() < protectChance ? bestIndex : null;
    }

    /// <summary>
    /// Given a wicket has just fallen, returns the index in <paramref name="battingOrder"/> of a
    /// batter to promote ahead of <paramref name="nextBatterIndex"/>, or null to send the next man
    /// on the card. Only ever promotes someone who has not yet batted and sits below the next man.
    /// </summary>
    public int? ChoosePinchHitter(
        InningsState state, IReadOnlyList<Player> battingOrder, int nextBatterIndex,
        MatchFormat format, MatchLeadership? leadership, TacticalPlan plan, Random random)
    {
        if (format == MatchFormat.Test) return null;                       // long-format order changes are the nightwatchman, handled elsewhere
        if (nextBatterIndex >= battingOrder.Count - 1) return null;        // nobody meaningful left to promote
        if (state.Wickets >= 6) return null;                               // this deep, you send your best remaining bat, not a slog merchant

        // The coach's own order stands unless he has delegated the call.
        var authority = plan.ResolveAuthority(InMatchDecision.BattingApproach);
        if (authority == DecisionAuthority.CoachHasFinalSay) return null;

        double urgency = QuickRunsUrgency(state, format);
        if (urgency < 0.4) return null;

        // Whoever is next on the card - the bar a promotion has to clear.
        var nextMan = battingOrder[nextBatterIndex];
        double nextManHitting = HittingScore(nextMan);

        // Best not-yet-batted hitter below him.
        int bestIndex = -1;
        double bestHitting = nextManHitting;
        for (int i = nextBatterIndex + 1; i < battingOrder.Count; i++)
        {
            if (HasBatted(state, battingOrder[i].Id)) continue;
            double hitting = HittingScore(battingOrder[i]);
            if (hitting > bestHitting)
            {
                bestHitting = hitting;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return null;

        // The promotion has to be a real upgrade in hitting, weighted by how urgent it is - and
        // then filtered through how well the captain is reading the game.
        double margin = bestHitting - nextManHitting;
        if (margin < 8) return null;

        double captainRead = leadership is null
            ? 0.55                                                          // an unled side still shuffles the order sometimes
            : Math.Clamp(leadership.Profile.TacticalJudgement(leadership.Captain) / 100.0, 0.15, 0.95);

        double promoteChance = Math.Clamp(captainRead * (0.4 + urgency * 0.6) * (0.5 + margin / 40.0), 0, 0.95);
        return random.NextDouble() < promoteChance ? bestIndex : null;
    }

    /// <summary>0 = no need for quick runs, 1 = maximum - the death of a T20 innings, or a chase well behind the rate.</summary>
    private static double QuickRunsUrgency(InningsState state, MatchFormat format)
    {
        double parRate = format == MatchFormat.ODI ? 6.0 : 8.5;

        // Chasing: how far behind the required rate the side is.
        if (state.RequiredRunRate is { } rrr)
        {
            double gap = rrr - Math.Max(state.RunRate, parRate * 0.6);
            double chasePressure = Math.Clamp(gap / 5.0, 0, 1);
            // Also more urgent the fewer balls are left.
            double timePressure = state.BallsRemaining is { } br ? Math.Clamp(1 - br / 60.0, 0, 1) : 0;
            return Math.Clamp(Math.Max(chasePressure, timePressure * 0.8), 0, 1);
        }

        // Setting a total: it is the death overs that call for a hitter.
        if (state.BallsRemaining is { } balls)
        {
            int deathThreshold = format == MatchFormat.ODI ? 60 : 30;
            if (balls <= deathThreshold) return Math.Clamp(1.15 - balls / (double)deathThreshold, 0.25, 1);
        }

        return 0;
    }

    private static double HittingScore(Player p) =>
        p.Batting.PowerHitting * 0.4
        + p.Batting.DeathOverBatting * 0.3
        + p.Batting.BoundaryHitting * 0.15
        + Math.Max(p.BattingTraits.PowerHitter, p.BattingTraits.Finisher) / 100.0 * 20 * 0.15;

    private static bool HasBatted(InningsState state, Guid playerId)
    {
        foreach (var card in state.BatterCards)
            if (card.PlayerId == playerId && card.HasBatted) return true;
        return false;
    }
}
