using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>What a review produced.</summary>
public enum ReviewVerdict
{
    /// <summary>The side did not review - it accepted the on-field call.</summary>
    NotReviewed,
    /// <summary>The decision was overturned - the wicket is struck off, and the review is retained.</summary>
    Overturned,
    /// <summary>Ball-tracking / replay was inconclusive ("umpire's call") - the on-field decision stands, the review is retained.</summary>
    UmpiresCall,
    /// <summary>The decision was upheld conclusively - it stands, and the side has used a review.</summary>
    StruckDown
}

/// <summary>
/// Phase 15 (§1.7): the batting side's DRS reviews for one innings. Mutable - the simulator holds
/// one per innings and <see cref="DecisionReviewService"/> mutates it in place as reviews are
/// spent. <see cref="ReviewsLeft"/> at 0 means DRS is off entirely (no reviews were ever granted).
/// </summary>
public sealed class DrsInningsState
{
    public int ReviewsLeft { get; set; }
    public int Overturned { get; private set; }
    public int UmpiresCall { get; private set; }
    public int StruckDown { get; private set; }

    public void RecordOverturn() => Overturned++;
    public void RecordUmpiresCall() => UmpiresCall++;
    public void RecordStruckDown() { StruckDown++; ReviewsLeft = Math.Max(0, ReviewsLeft - 1); }
}

/// <summary>
/// Phase 15 (§1.7 - DRS). No third-umpire subsystem is modelled ball by ball; this is the
/// decision layer only, and it is deliberately narrow:
///
/// - it acts ONLY on a given-out LBW or caught-behind (the two calls DRS overwhelmingly turns on),
/// - it decides whether the batting side actually reviews, from the striker's and non-striker's
///   <see cref="ValueObjects.MentalAttributes.ReviewJudgement"/>, his batting position, and the
///   pressure of the situation - a set top-order batter in a tight chase reviews a close one; a
///   tail-ender rarely bothers, and a poor reviewer burns one on a hopeful shout,
/// - and it rolls the outcome against <c>UmpireOutBias</c>: a weaker or more rattled panel gave
///   more of the marginal ones out, so more of them are overturned. That is the real interaction -
///   DRS blunts a bad panel's effect on the game, which is exactly why it exists.
///
/// The fielding side's reviews of turned-down not-outs are NOT rolled here (the ball model does
/// not generate "close appeal, given not out" events) - instead <see cref="BallOutcomeModel"/>
/// carries a small, deterministic fielding-DRS clawback when the panel is weak, representing the
/// fielding side reclaiming some of what a cautious umpire would otherwise have missed.
///
/// Consumes the caller's Random, and ONLY when a review actually happens (a rare event - a few
/// LBW/caught-behind wickets an innings), so a non-DRS fixture draws no extra random numbers.
/// </summary>
public sealed class DecisionReviewService
{
    /// <summary>
    /// Considers a batting-side review of a given-out decision. Returns <see cref="ReviewVerdict.NotReviewed"/>
    /// for anything that is not an LBW / caught-behind, when DRS is off, or when the side chooses
    /// not to review. On <see cref="ReviewVerdict.Overturned"/> the caller must strike the wicket
    /// off (turn the delivery into a dot).
    /// </summary>
    public ReviewVerdict ConsiderBattingReview(
        DismissalType dismissal, Player striker, Player? nonStriker,
        int wicketsFallen, int? runsRequired, int? ballsRemaining,
        double umpireOutBias, DrsInningsState drs, Random random)
    {
        if (drs.ReviewsLeft <= 0) return ReviewVerdict.NotReviewed;
        if (dismissal is not (DismissalType.LBW or DismissalType.CaughtBehind)) return ReviewVerdict.NotReviewed;

        double judgement = ((striker.Mental.ReviewJudgement + (nonStriker?.Mental.ReviewJudgement ?? striker.Mental.ReviewJudgement)) / 2.0 - 1) / 19.0; // 0-1

        // A weak / rattled panel (UmpireOutBias above 1) gives more genuinely marginal calls out.
        double bias = Math.Clamp(umpireOutBias, 1.0, 1.30);
        double pMarginal = Math.Clamp(0.42 * bias, 0.30, 0.62);
        bool wasMarginal = random.NextDouble() < pMarginal;

        // Whether to send it up. A sharp reviewer reviews the marginal ones; a poor one reviews on
        // hope. Position and pressure both push it up.
        double positionFactor = wicketsFallen <= 3 ? 1.20 : wicketsFallen <= 6 ? 1.0 : 0.55;
        double pressureFactor = 1.0;
        if (runsRequired is { } req && ballsRemaining is { } balls && balls > 0)
            pressureFactor += Math.Clamp((6.5 - req / (balls / 6.0)) * 0.0, 0, 0) + (req <= 30 ? 0.25 : 0); // tight chase -> more inclined

        double pReviewMarginal = Math.Clamp((0.45 + judgement * 0.45) * positionFactor * pressureFactor, 0.15, 0.92);
        double pReviewHopeful = Math.Clamp((0.16 - judgement * 0.14) * positionFactor, 0.02, 0.20);

        bool reviews = wasMarginal ? random.NextDouble() < pReviewMarginal
                                   : random.NextDouble() < pReviewHopeful;
        if (!reviews) return ReviewVerdict.NotReviewed;

        if (!wasMarginal)
        {
            // A hopeful review of a plumb decision - almost always struck down.
            if (random.NextDouble() < 0.05) { drs.RecordOverturn(); return ReviewVerdict.Overturned; }
            drs.RecordStruckDown();
            return ReviewVerdict.StruckDown;
        }

        // A genuine marginal call. A weak panel got more of them wrong, so more come back.
        double pOverturn = Math.Clamp(0.30 + (bias - 1.0) * 1.6, 0.20, 0.60);
        double roll = random.NextDouble();
        if (roll < pOverturn) { drs.RecordOverturn(); return ReviewVerdict.Overturned; }
        if (roll < pOverturn + (1 - pOverturn) * 0.32) { drs.RecordUmpiresCall(); return ReviewVerdict.UmpiresCall; }
        drs.RecordStruckDown();
        return ReviewVerdict.StruckDown;
    }
}
