using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

public sealed record BattingSituation(
    int OversRemainingInInnings,
    int WicketsInHand,
    MatchPhase Phase,
    bool IsChasing,
    /// <summary>0-100. 50 is an ordinary league game; 90+ is a World Cup knockout. Defaults to neutral so existing callers are unaffected.</summary>
    double PressureLevel = 50);

/// <summary>
/// This is the piece that makes roles/traits actually matter in simulation instead of
/// being descriptive-only. Produces a multiplier (typically 0.75-1.25) applied to a
/// player's effective batting ability for one innings, based on:
///  - whether they're batting in their natural position (role mismatch penalty)
///  - whether their traits suit the current match situation (trait fit bonus/penalty)
///
/// Deliberately soft-capped (no trait/role combination can push below ~0.7 or above
/// ~1.3) so nothing becomes a hard restriction - a natural opener at No.7 is worse off,
/// not incapable, matching the "don't create unrealistic hard restrictions" requirement.
/// </summary>
public sealed class SituationalPerformanceModifier
{
    public double GetBattingMultiplier(Player player, BattingRole positionBattingAt, BattingSituation situation)
    {
        double multiplier = 1.0;

        multiplier += RoleMismatchAdjustment(player.BattingRole, positionBattingAt, situation);
        multiplier += TraitSituationAdjustment(player.BattingTraits, situation);
        multiplier += PressureAdjustment(player, situation.PressureLevel);
        multiplier += RoleClarityAdjustment(player.AssignedRole, positionBattingAt);

        return Math.Clamp(multiplier, 0.7, 1.3);
    }

    /// <summary>
    /// The effect the design specifically asked for: a young player with excellent ability who
    /// has not been there before struggles when it matters, while a 35-year-old past his physical
    /// peak delivers because he has.
    ///
    /// Two inputs, deliberately both: EXPERIENCE (has he been here?) and PRESSURE HANDLING (is he
    /// built for it?). They are different things - some players are temperamentally unflappable
    /// on debut, others never settle after 100 caps - so neither alone is enough. Experience is
    /// weighted slightly higher because it is the one that changes over a career.
    ///
    /// At neutral pressure (50) this returns ~0, so ordinary cricket is unaffected. The swing only
    /// opens up as the occasion gets bigger, which is exactly where it should.
    /// </summary>
    public double PressureAdjustment(Player player, double pressureLevel)
    {
        double pressure = (Math.Clamp(pressureLevel, 0, 100) - 50) / 50.0; // -1 .. +1
        if (Math.Abs(pressure) < 0.01) return 0;

        double experience = (player.Experience.Level - 50) / 50.0;                        // -1 .. +1
        double temperament = (Common.AbilityScale.AttributeToHundred(player.Mental.PressureHandling) - 50) / 50.0;

        double composure = experience * 0.6 + temperament * 0.4;

        // Personality traits that exist precisely for this moment.
        if (player.Personality.HasFlag(PersonalityTrait.BigMatchPlayer)) composure += 0.35;
        if (player.Personality.HasFlag(PersonalityTrait.PressurePlayer)) composure += 0.25;
        if (player.Personality.HasFlag(PersonalityTrait.Inconsistent)) composure -= 0.2;

        // Post-Phase-5 rectification pass, Wave 2 (point 3): an unresolved big-match failure is a
        // real weight - the player walks out with something to prove until he clears it. How
        // heavily it sits on him is exactly the personality question the rest of this method is
        // about, so it is folded into `composure` rather than added as a separate term: a
        // temperamentally strong player barely feels it, a fragile one is dragged down further
        // every time he is put back in that seat.
        if (pressure > 0 && player.PendingPressureMoment is { } moment)
        {
            double weight = -0.25 - moment.Severity / 100.0 * 0.25 - Math.Min(moment.SubsequentAttempts, 4) * 0.05;
            composure += weight * (1 - Math.Clamp(composure, 0, 1)); // a strong composure absorbs most of it
        }

        // Max +/-0.15 at maximum pressure - meaningful, never decisive on its own.
        return Math.Clamp(composure * pressure * 0.15, -0.15, 0.15);
    }

    /// <summary>
    /// Follow-up pass (§5.13): a small, distinct signal from RoleMismatchAdjustment above -
    /// RoleMismatchAdjustment is about the player's own NATURAL/derived role clashing with the
    /// situation; this is about whether he is playing to the role his COACH has explicitly told
    /// him is his. A player genuinely knowing his job (clarity) is worth a little; being asked to
    /// do something else entirely, even competently, carries a small cost - real, secondary,
    /// never decisive on its own. No-op when nothing has been assigned.
    /// </summary>
    private static double RoleClarityAdjustment(BattingRole? assignedRole, BattingRole actualPosition)
    {
        if (assignedRole is not { } assigned) return 0;
        return assigned == actualPosition ? 0.04 : -0.03;
    }

    private static double RoleMismatchAdjustment(BattingRole naturalRole, BattingRole actualPosition, BattingSituation situation)
    {
        if (naturalRole == actualPosition) return 0;

        // A top-order/opener natural batter shoved into a late, overs-limited situation
        // struggles more than a middle-order player would - they haven't "arrived" the way they're used to.
        bool isLateLimitedSituation = situation.OversRemainingInInnings <= 5;
        bool naturalIsEarlyRole = naturalRole is BattingRole.Opener or BattingRole.TopOrder;

        if (isLateLimitedSituation && naturalIsEarlyRole)
            return -0.15; // meaningful but not crippling

        // A natural finisher/lower-order batter promoted up the order without their usual license to attack.
        bool naturalIsLateRole = naturalRole is BattingRole.LowerOrder or BattingRole.Tailender;
        bool earlySituation = situation.OversRemainingInInnings >= 15;
        if (earlySituation && naturalIsLateRole)
            return -0.10;

        return -0.05; // generic mild mismatch penalty for any other role displacement
    }

    private static double TraitSituationAdjustment(ValueObjects.BattingTraits traits, BattingSituation situation)
    {
        double adj = 0;

        // Finisher/PowerHitter thrive in death overs / limited-overs-remaining situations.
        if (situation.Phase == MatchPhase.DeathOvers || situation.OversRemainingInInnings <= 5)
        {
            adj += (traits.Finisher - 50) / 500.0;      // trait 100 -> +0.10, trait 0 -> -0.10
            adj += (traits.PowerHitter - 50) / 600.0;
            // Anchors are built for time, not for hitting from ball one - real but soft penalty.
            adj -= Math.Max(0, traits.Anchor - 60) / 500.0;
        }

        // Anchor/PartnershipBuilder do better when they have overs to work with.
        if (situation.OversRemainingInInnings >= 15)
        {
            adj += (traits.Anchor - 50) / 600.0;
            adj += (traits.PartnershipBuilder - 50) / 700.0;
        }

        if (situation.IsChasing)
            adj += (traits.ChaseSpecialist - 50) / 600.0;

        return adj;
    }

    public double GetBowlingMultiplier(Player player, MatchPhase phase, bool newBall)
    {
        double multiplier = 1.0;
        var t = player.BowlingTraits;

        if (newBall)
        {
            multiplier += (t.SwingBowler - 50) / 500.0;
            multiplier += (t.SeamBowler - 50) / 600.0;
        }

        if (phase == MatchPhase.DeathOvers)
        {
            multiplier += (t.DeathSpecialist - 50) / 400.0;
            // A part-timer ("golden arm") shouldn't perform like a death-overs elite specialist by default.
            if (player.BowlingRole == BowlingRoleType.PartTimeBowler)
                multiplier -= 0.08;
        }

        if (phase == MatchPhase.MiddleOvers)
            multiplier += (t.MiddleOversSpecialist - 50) / 500.0;

        // Golden Arm: part-time bowlers get a small surprise-wicket variance bump rather than
        // raw ability - modeled here as a mild containment/wicket nudge, not full specialist power.
        if (player.BowlingRole == BowlingRoleType.PartTimeBowler && t.GoldenArm > 60)
            multiplier += 0.04;

        return Math.Clamp(multiplier, 0.75, 1.25);
    }
}
