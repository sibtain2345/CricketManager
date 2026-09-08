using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 15 (§19.3): the home side, ahead of a multi-day match, tells the groundstaff what kind
/// of surface it wants - and it can backfire. A side with a strong spin attack and batters who
/// play spin well asks for a dry, dusty turner; a pace-heavy side on a green-tinged ground asks
/// for extra grass. But if the VISITING attack is actually the one better suited to what was
/// prepared, the home side has doctored the pitch against itself - which is exactly why real
/// captains are cautious about it.
///
/// Returns a <see cref="PitchPreparation"/> for <c>MultiDayMatchSetup.HomePitchPreparation</c>;
/// <see cref="PitchPreparationService"/> then bounds and damps the request by the ground's own
/// PitchInfrastructure quality (a well-resourced groundstaff delivers most of the ask, a poor one
/// barely a third). Neutral is the common outcome - most matches are not specially prepared.
/// </summary>
public sealed class PitchDoctoringService
{
    public PitchPreparation Decide(
        Team home, IReadOnlyList<Player> homeXi, IReadOnlyList<Player> awayXi, Ground? ground, Random random,
        double homePitchInfluence = 1.0)
    {
        if (ground is null || homeXi.Count == 0 || awayXi.Count == 0) return PitchPreparation.Neutral;

        // Corrections pass (correction 2): where the home side genuinely cannot shape the pitch -
        // an ICC event (ICC-appointed prep) or a franchise T20 league (central / local-curator
        // control, or shared venues) - it does not even try. Bilateral / domestic first-class is
        // the full-influence case where a real, deliberate swing is on the table.
        if (homePitchInfluence <= 0.05) return PitchPreparation.Neutral;

        // A cautious board / a shrewd coach doctors less. A blunt one reaches for it more. The
        // base chance the side tries anything at all scales with how much influence it actually has.
        if (random.NextDouble() > 0.15 + 0.42 * Math.Clamp(homePitchInfluence, 0, 1)) return PitchPreparation.Neutral;

        double homeSpin = AttackSpinStrength(homeXi) + BattingVsSpin(homeXi) * 0.5;
        double awaySpin = AttackSpinStrength(awayXi) + BattingVsSpin(awayXi) * 0.5;
        double homePace = AttackPaceStrength(homeXi) + BattingVsPace(homeXi) * 0.5;
        double awayPace = AttackPaceStrength(awayXi) + BattingVsPace(awayXi) * 0.5;

        double spinEdge = homeSpin - awaySpin;   // > 0: a turner suits us
        double paceEdge = homePace - awayPace;   // > 0: a green top suits us

        // Only act on a genuine edge, and prefer the bigger of the two.
        if (spinEdge > 8 && spinEdge >= paceEdge) return PitchPreparation.Dry;
        if (paceEdge > 8 && paceEdge > spinEdge) return PitchPreparation.Grassy;

        // A weak all-round side with nothing to gain from either sometimes just wants a road so
        // the game is not decided by the conditions.
        if (homeSpin + homePace < awaySpin + awayPace - 14 && random.NextDouble() < 0.4)
            return PitchPreparation.Flat;

        return PitchPreparation.Neutral;
    }

    /// <summary>
    /// Corrections pass (correction 2): the real limit on home-pitch shaping is a QUALITY floor,
    /// not a magnitude cap. A genuinely aggressive prep on a poorly-resourced square can produce a
    /// surface that is rated poor - dangerous, or so one-sided it is a farce (the ICC's own
    /// "unsatisfactory / unfit" categories). Returns true on that bad outcome. Only a real
    /// bilateral / domestic push (high influence) can go wrong this way - an ICC-controlled or
    /// franchise-league surface is prepared to a central standard.
    /// </summary>
    public static bool OverPreparedPoorly(PitchPreparation prep, Ground ground, double homePitchInfluence, Random random)
    {
        if (prep == PitchPreparation.Neutral || homePitchInfluence < 0.5) return false;
        double infra = Math.Clamp(ground.Facilities.PitchInfrastructure, 0, 100) / 100.0;
        double risk = Math.Clamp((homePitchInfluence - 0.5) * 0.5 * (1.15 - infra), 0, 0.32);
        return random.NextDouble() < risk;
    }

    private static double AttackSpinStrength(IReadOnlyList<Player> xi)
    {
        var spinners = xi.Where(BallOutcomeModel.IsSpinner).OrderByDescending(p => p.Bowling.Spin).Take(2).ToList();
        return spinners.Count == 0 ? 0 : spinners.Sum(p => AbilityScale.AttributeToHundred(p.Bowling.Spin));
    }

    private static double AttackPaceStrength(IReadOnlyList<Player> xi)
    {
        var quicks = xi.Where(p => p.BowlingRole != BowlingRoleType.NotABowler && !BallOutcomeModel.IsSpinner(p))
                       .OrderByDescending(p => p.Bowling.Pace + p.Bowling.Seam).Take(2).ToList();
        return quicks.Count == 0 ? 0 : quicks.Sum(p => AbilityScale.AttributeToHundred((p.Bowling.Pace + p.Bowling.Seam) / 2));
    }

    private static double BattingVsSpin(IReadOnlyList<Player> xi) =>
        xi.OrderByDescending(p => p.Batting.AgainstSpin).Take(6).Average(p => AbilityScale.AttributeToHundred(p.Batting.AgainstSpin));

    private static double BattingVsPace(IReadOnlyList<Player> xi) =>
        xi.OrderByDescending(p => p.Batting.AgainstPace).Take(6).Average(p => AbilityScale.AttributeToHundred(p.Batting.AgainstPace));
}
