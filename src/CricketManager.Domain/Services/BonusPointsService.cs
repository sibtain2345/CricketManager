using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Bonus points earned from a single innings performance - batting points from a team's own
/// runs, bowling points from wickets taken bowling at the opposition, and a limited-overs margin
/// bonus for a big win. Tech-debt item 9 in the known-debt log - deferred since Phase 3 for
/// exactly the reason it can be built now: it needs real per-innings run/wicket data, which only
/// exists once the match engine does.
///
/// **Modelled after the Ranji Trophy first-class bonus-point system** - a real, well-known one,
/// not a claim that every first-class competition uses these exact thresholds. Same honesty this
/// codebase already applies to DuckworthLewisStern: reproduces a real shape, not a universal one.
///
/// **Both bonus types are wired in, as of Slice 13.** The limited-overs margin bonus is wired
/// into `MatchRecorder.UpdateStandings`, gated by `CompetitionPointsSystem.MarginBonusEnabled`
/// (off by default). The FC batting/bowling bonus is wired into
/// `MultiDayMatchRecorder.UpdateStandings`, gated by `CompetitionPointsSystem.
/// BattingBowlingBonusEnabled` (on by default for `CompetitionPointsSystem.FirstClass`, since
/// real FC competitions almost universally use it, unlike the rarer limited-overs margin bonus).
/// `MultiDayMatchRecorder` itself didn't exist when this class was first written - see its own
/// doc comment for what closed that gap.
/// </summary>
public sealed class BonusPointsService
{
    private const int FirstClassBonusOversLimit = 100; // 600 legal balls - the standard FC window

    /// <summary>
    /// Batting bonus points from a team's own first-class first innings, based on runs scored in
    /// the first 100 overs. If the innings finished (all out, or fewer than 100 overs bowled)
    /// before the window closed, the final score is used - the whole innings fit inside it.
    /// </summary>
    public int BattingBonusPoints(InningsState firstInnings)
    {
        int runsInWindow = RunsThroughOver(firstInnings, FirstClassBonusOversLimit);
        return runsInWindow switch
        {
            >= 400 => 4,
            >= 350 => 3,
            >= 300 => 2,
            >= 250 => 1,
            _ => 0
        };
    }

    /// <summary>Bowling bonus points from wickets taken in the first 100 overs of the OPPONENT's first innings - i.e. this team bowling.</summary>
    public int BowlingBonusPoints(InningsState opponentFirstInnings)
    {
        int wicketsInWindow = WicketsThroughOver(opponentFirstInnings, FirstClassBonusOversLimit);
        return wicketsInWindow switch
        {
            >= 9 => 4,
            >= 7 => 3,
            >= 5 => 2,
            >= 3 => 1,
            _ => 0
        };
    }

    /// <summary>
    /// A simple limited-overs margin bonus some T20/List A competitions use: an extra point for a
    /// big win. One illustrative version (8+ wickets or 40+ runs), not a claim to match any
    /// specific league's exact margin - see the class doc for how a caller opts in.
    /// </summary>
    public int LimitedOversMarginBonus(MatchResult match, Guid teamId)
    {
        if (match.WinningTeamId != teamId) return 0;

        if (match.WonByWickets)
            return match.WinMargin is { } wicketMargin && wicketMargin >= 8 ? 1 : 0;

        return match.WinMargin is { } runMargin && runMargin >= 40 ? 1 : 0;
    }

    /// <summary>
    /// Runs scored through the end of the given over. Replays the delivery log rather than
    /// reading InningsState.Runs directly, because that field is the FINAL total - this needs the
    /// total AS OF a specific point earlier in the innings, the same replay CommentaryService and
    /// PostMatchAnalysisService already do for their own per-ball reconstructions.
    /// </summary>
    private static int RunsThroughOver(InningsState innings, int overLimit)
    {
        int maxLegalBalls = overLimit * 6;
        int legalBallsSoFar = 0;
        int runs = 0;

        foreach (var delivery in innings.Deliveries)
        {
            if (legalBallsSoFar >= maxLegalBalls) break;
            runs = delivery.ScoreAfter;
            if (delivery.Outcome.IsLegalDelivery) legalBallsSoFar++;
        }

        return runs;
    }

    /// <summary>Wickets down through the end of the given over. Same replay approach as RunsThroughOver, for the same reason.</summary>
    private static int WicketsThroughOver(InningsState innings, int overLimit)
    {
        int maxLegalBalls = overLimit * 6;
        int legalBallsSoFar = 0;
        int wickets = 0;

        foreach (var delivery in innings.Deliveries)
        {
            if (legalBallsSoFar >= maxLegalBalls) break;
            wickets = delivery.WicketsAfter;
            if (delivery.Outcome.IsLegalDelivery) legalBallsSoFar++;
        }

        return wickets;
    }
}
