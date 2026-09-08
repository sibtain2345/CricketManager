using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-9 wiring pass (closes tech-debt item 4): the match SITUATION a performance happened
/// in, so `Rate*` can weigh it - a hundred that carries a run chase in a final is not a hundred in
/// a dead rubber. Built by the match recorders from data they already hold (`InningsState` +
/// `MatchResult`); null (every direct-call test, and any caller without match context) falls back
/// to the blunt runs/balls/wickets approximation this item was tracking.
/// </summary>
public sealed record MatchSituation(
    double BaseImportance = 50,
    bool WasChase = false,
    int TeamTotal = 0,
    int TeamWicketsInHand = 10,
    int WicketsTakenByTeam = 0,
    bool? TeamWon = null,
    bool MatchDecided = true,
    double MomentumForTeam = 0); // -100..100 over this innings, from this player's team's perspective

/// <summary>What one recorded performance actually did to the player, returned so news/media/UI can explain it rather than the change happening invisibly.</summary>
public sealed record PerformanceRecordingResult(
    double RawRating,
    double ValuedRating,
    double FormBefore,
    double FormAfter,
    double DomesticReputationGain,
    double ContinentalReputationGain,
    double WorldwideReputationGain);

/// <summary>
/// The missing wiring between "a performance happened" and "the world reacts to it".
///
/// Every piece of this existed already - PerformanceValuationService knew how to weigh a
/// performance by opposition and competition prestige, FormState knew how to absorb a
/// rating, MatchupConfidenceService knew how to build confidence, PlayerCareerStats knew how
/// to total things up - but NOTHING called them together, so each caller would have had to
/// remember all four and get the order right. Phase 4 would have wired this ad hoc while
/// also building a match engine, which is how inconsistencies get baked in.
///
/// Order matters and is deliberate: rate the raw performance, value it for context, then let
/// form/matchups/reputation all consume the SAME valued number. A century against a weak
/// attack in a dead domestic rubber and a century in a World Cup final must not move a
/// career by the same amount, and that principle only holds if one valuation feeds everything.
/// </summary>
public sealed class PerformanceRecordingService
{
    private readonly PerformanceValuationService _valuation = new();
    private readonly MatchupConfidenceService _matchups = new();
    private readonly TeamMoraleService _teamMorale = new();

    /// <summary>
    /// Rates a batting innings on the -100..100 scale the rest of the system speaks.
    /// Format-aware, because 40 off 30 balls is a good T20 innings and a poor Test one.
    /// Deliberately blunt: the match engine (Phase 4) will have far richer inputs (match
    /// situation, pressure, chase context), and this exists so form/reputation aren't
    /// waiting on it - it is explicitly a first approximation, not the final model.
    /// </summary>
    /// <remarks>
    /// Scaled so an outstanding innings lands around 60, not 100. That headroom is deliberate:
    /// opposition strength and competition prestige multiply this rating afterwards, and if the
    /// raw number already sat at the +/-100 clamp those multipliers would be silently clipped
    /// away - a hundred against Australia in a World Cup would score identically to one against
    /// a weak domestic attack, defeating the entire point of contextual valuation.
    /// </remarks>
    public double RateBattingInnings(BattingInningsRecord innings, MatchFormat format, MatchSituation? situation = null)
    {
        double runsComponent = format switch
        {
            MatchFormat.Test => Math.Clamp(innings.Runs / 100.0, 0, 1.5) * 45,
            MatchFormat.ODI => Math.Clamp(innings.Runs / 80.0, 0, 1.5) * 45,
            _ => Math.Clamp(innings.Runs / 50.0, 0, 1.5) * 45
        };

        // Strike rate only matters where it matters. In Tests it's close to irrelevant;
        // in T20 a slow innings is actively damaging even when the runs total looks fine.
        double strikeRateComponent = 0;
        if (innings.BallsFaced > 0)
        {
            double parStrikeRate = format switch { MatchFormat.Test => 55, MatchFormat.ODI => 85, _ => 130 };
            double weight = format switch { MatchFormat.Test => 0.1, MatchFormat.ODI => 0.3, _ => 0.5 };
            strikeRateComponent = Math.Clamp((innings.StrikeRate - parStrikeRate) / parStrikeRate, -1, 1) * 30 * weight;
        }

        // A failure is a failure - a duck should read clearly negative, not merely "low".
        double baseline = innings.Runs < 10 && !innings.NotOut ? -35 : -15;

        double core = baseline + runsComponent + strikeRateComponent;
        return Math.Clamp(ApplySituation(core, situation, batting: true, innings.Runs, innings.NotOut), -100, 100);
    }

    /// <summary>Rates a bowling spell. Wickets dominate, economy modifies - and in T20 an expensive four-wicket haul is genuinely worth less than in a Test.</summary>
    public double RateBowlingSpell(BowlingSpellRecord spell, MatchFormat format, MatchSituation? situation = null)
    {
        double wicketComponent = Math.Clamp(spell.Wickets / 5.0, 0, 1.6) * 45;

        double economyComponent = 0;
        if (spell.OversBowled > 0)
        {
            double parEconomy = format switch { MatchFormat.Test => 3.2, MatchFormat.ODI => 5.4, _ => 8.2 };
            double weight = format switch { MatchFormat.Test => 0.3, MatchFormat.ODI => 0.4, _ => 0.6 };
            economyComponent = Math.Clamp((parEconomy - spell.Economy) / parEconomy, -1, 1) * 35 * weight;
        }

        double core = -20 + wicketComponent + economyComponent;
        return Math.Clamp(ApplySituation(core, situation, batting: false, spell.Wickets, notOut: false), -100, 100);
    }

    /// <summary>
    /// Tech-debt item 4: the situational layer the match engine can now feed. Weighs a performance
    /// by how much it MATTERED - the occasion (`BaseImportance`), whether it carried a run chase or
    /// a low-total defence, the player's share of the team effort, whether the game swung his team's
    /// way while he was out there, and the result. All bounded and centred so an ordinary innings
    /// in an ordinary game is untouched.
    /// </summary>
    private static double ApplySituation(double core, MatchSituation? s, bool batting, int contribution, bool notOut)
    {
        if (s is null) return core;

        // The occasion multiplies the POSITIVE part only (a match-winning knock in a final counts
        // for more; a failure in a final is already its own punishment via the pressure system).
        double occasion = 1.0 + Math.Clamp((s.BaseImportance - 50) / 50.0, 0, 1) * 0.25;
        double adjusted = core > 0 ? core * occasion : core;

        // Carrying a chase / defending a low total.
        double clutch = 0;
        double share = batting
            ? contribution / Math.Max(1.0, s.TeamTotal)
            : contribution / Math.Max(1.0, (double)s.WicketsTakenByTeam);
        if (s.WasChase)
        {
            if (batting && contribution >= 30) clutch += 6 + Math.Clamp(share - 0.25, 0, 0.3) * 40; // a real hand in a chase
            if (batting && contribution < 15 && !notOut && s.TeamWon == false) clutch -= 10;        // out cheaply in a lost chase
            if (!batting && s.TeamWon == true && contribution >= 2) clutch += 8;                     // wickets that won a defence
        }
        else
        {
            if (batting && share >= 0.35) clutch += 8;  // carried the innings
            if (!batting && share >= 0.5 && s.WicketsTakenByTeam >= 4) clutch += 6; // ran through the card
        }

        // The game swung his team's way while he was out there (momentum, from his team's view).
        double gameImpact = Math.Clamp(s.MomentumForTeam / 100.0, -1, 1) * 10;

        // Result nudge - a positive contribution in a win, a poor one in a loss.
        double resultNudge = 0;
        if (s.MatchDecided && s.TeamWon is { } won)
        {
            if (won && core > 0) resultNudge += 3;
            else if (!won && core < -10) resultNudge -= 3;
        }

        return adjusted + clutch + gameImpact + resultNudge;
    }

    /// <summary>
    /// Records a batting innings everywhere it should land. competitionPrestige defaults to
    /// the neutral 50 so a caller without competition context is unaffected, consistent with
    /// how PerformanceValuationService already behaves.
    /// </summary>
    public PerformanceRecordingResult RecordBattingPerformance(
        Player player,
        BattingInningsRecord innings,
        PlayerCareerStats? careerStats = null,
        double competitionPrestige = 50,
        Team? battingTeam = null,
        MatchSituation? situation = null)
    {
        double raw = RateBattingInnings(innings, innings.Context.Format, situation);
        double valued = _valuation.ValuePerformance(raw, innings.Context.OppositionStrength,
            player.Form.RecentPerformanceRatings.Count, competitionPrestige);

        // A happy dressing room makes a performance count for very slightly more toward form/
        // reputation growth, an unhappy one very slightly less - real, but capped well short of
        // dominating the player's own ability and form, per TeamMoraleService's own doc comment.
        valued *= _teamMorale.PerformanceMultiplier(battingTeam);

        double formBefore = player.Form.CurrentForm;
        player.Form.RecordPerformance(valued);

        careerStats?.RecordBattingInnings(innings.Runs, innings.BallsFaced, innings.NotOut, innings.Fours, innings.Sixes);

        RecordMatchups(player, innings.Context, valued);
        var repGain = ApplyReputationChange(player, valued, innings.Context, competitionPrestige);

        // Format suitability blends in current form, so it has to be refreshed after form moves -
        // otherwise a player's format fit silently reflects the form they had last month.
        player.RecalculateFormatSuitability();

        return new PerformanceRecordingResult(Math.Round(raw, 1), Math.Round(valued, 1),
            Math.Round(formBefore, 1), Math.Round(player.Form.CurrentForm, 1), repGain.Domestic, repGain.Continental, repGain.Worldwide);
    }

    public PerformanceRecordingResult RecordBowlingPerformance(
        Player player,
        BowlingSpellRecord spell,
        PlayerCareerStats? careerStats = null,
        double competitionPrestige = 50,
        Team? bowlingTeam = null,
        MatchSituation? situation = null)
    {
        double raw = RateBowlingSpell(spell, spell.Context.Format, situation);
        double valued = _valuation.ValuePerformance(raw, spell.Context.OppositionStrength,
            player.Form.RecentPerformanceRatings.Count, competitionPrestige);
        valued *= _teamMorale.PerformanceMultiplier(bowlingTeam);

        double formBefore = player.Form.CurrentForm;
        player.Form.RecordPerformance(valued);

        careerStats?.RecordBowlingInnings(spell.OversBowled, spell.RunsConceded, spell.Wickets);

        RecordMatchups(player, spell.Context, valued);
        var repGain = ApplyReputationChange(player, valued, spell.Context, competitionPrestige);
        player.RecalculateFormatSuitability();

        return new PerformanceRecordingResult(Math.Round(raw, 1), Math.Round(valued, 1),
            Math.Round(formBefore, 1), Math.Round(player.Form.CurrentForm, 1), repGain.Domestic, repGain.Continental, repGain.Worldwide);
    }

    /// <summary>Fielding contributions total up but deliberately don't move form or reputation on their own - nobody's stock rises on two catches, and pretending otherwise would make form noisy.</summary>
    public void RecordFieldingPerformance(FieldingRecord fielding, PlayerCareerStats? careerStats = null)
    {
        if (careerStats is null) return;
        careerStats.Catches += fielding.Catches;
        careerStats.RunOuts += fielding.RunOuts;
        careerStats.Stumpings += fielding.Stumpings;
    }

    private void RecordMatchups(Player player, ValueObjects.MatchContext context, double valuedRating)
    {
        if (context.OpponentTeamId != Guid.Empty)
            _matchups.RecordOutcome(player, MatchupKey.ForOpponent(context.OpponentTeamId), valuedRating);

        if (context.GroundId != Guid.Empty)
            _matchups.RecordOutcome(player, MatchupKey.ForGround(context.GroundId), valuedRating);
        else if (!string.IsNullOrWhiteSpace(context.Ground))
            _matchups.RecordOutcome(player, MatchupKey.ForGroundName(context.Ground), valuedRating);
    }

    /// <summary>
    /// Section 2/11: reputation is EARNED and moves slowly. A single good performance nudges
    /// it; only sustained excellence in prestigious, high-visibility cricket builds a global
    /// name. Three rules make that work:
    /// - only clearly above-average performances move it upward at all (a scratchy 30 doesn't
    ///   make you famous), while poor performances erode it far more gently than good ones
    ///   build it, because reputations decay slowly in reality. Note the thresholds are read
    ///   against the VALUED rating, which sample-size confidence damps heavily early in a
    ///   career - so a newcomer's first failure barely registers, which is correct;
    /// - continental and worldwide gains are a fraction of domestic, so a domestic career
    ///   alone cannot make someone a global name;
    /// - international and high-prestige cricket carries far more of that fraction, which is
    ///   why a World Cup performance is career-changing and a low-tier domestic one isn't.
    /// </summary>
    private (double Domestic, double Continental, double Worldwide) ApplyReputationChange(
        Player player, double valuedRating, ValueObjects.MatchContext context, double competitionPrestige)
    {
        double domestic;
        // Thresholds sit at +/-15 because they are read against the VALUED rating, which
        // sample-size confidence damps to as little as 30% early in a career. A raw 55 innings
        // by a debutant values out around 25; the same innings from an established player
        // values far higher. That is the intended behaviour - a newcomer has to do it more
        // than once before the cricket world revises its opinion of him.
        if (valuedRating >= 15) domestic = (valuedRating - 15) / 60.0 * 2.0;       // up to +2.0 for a standout display
        else if (valuedRating <= -15) domestic = (valuedRating + 15) / 60.0 * 0.6; // gentle erosion, max -0.6
        else return (0, 0, 0);

        // Visibility: how far beyond the domestic scene this performance was seen.
        double visibility = context.Scope switch
        {
            CompetitionScope.International => 1.0,
            CompetitionScope.FranchiseLeague => 0.6,
            CompetitionScope.DomesticFirstClass => 0.25,
            _ => 0.2
        } * (0.5 + Math.Clamp(competitionPrestige, 0, 100) / 100.0);

        double continental = domestic * 0.45 * visibility;
        double worldwide = domestic * 0.2 * visibility;

        player.Reputation.Adjust(domestic, continental, worldwide);
        return (Math.Round(domestic, 3), Math.Round(continental, 3), Math.Round(worldwide, 3));
    }
}
