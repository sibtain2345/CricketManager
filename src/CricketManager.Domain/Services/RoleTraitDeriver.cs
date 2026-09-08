using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Auto-derives natural BattingRole/BowlingRole and weighted traits from a player's
/// attributes. Call once after attributes are set (seeding, or after training changes
/// attributes materially). Results can be freely overwritten afterward by hand for a
/// specific real player - this only sets sensible defaults, it doesn't lock anything.
///
/// CLOSED (Phase 5): both attribute-changing systems now call ApplyTo() after every application
/// that actually moved something - TrainingService.ApplyAnnualTraining (voluntary, coach-directed
/// change, including role-conversion nudges) and PlayerAgeingService.ApplyAnnualAgeing
/// (involuntary change). A player's BattingRole/BowlingRole/traits can no longer silently drift
/// out of sync with what his attributes have actually become, from either source.
/// </summary>
public sealed class RoleTraitDeriver
{
    public BattingRole DeriveBattingRole(Player p)
    {
        var b = p.Batting;
        // New-ball survival skill vs power/finishing skill decides where a batter naturally fits.
        double topOrderScore = b.Technique + b.DefensiveAbility + b.AgainstPace * 0.5 + p.Mental.Concentration;
        double finisherScore = b.PowerHitting + b.DeathOverBatting + b.BoundaryHitting;
        double tailenderScore = 40 - (b.Technique + b.DefensiveAbility) * 0.7; // low overall batting -> tailender

        if (p.PrimaryRole == PlayerRole.Bowler && (b.Technique + b.DefensiveAbility) < 16)
            return BattingRole.Tailender;

        if (topOrderScore >= finisherScore && topOrderScore >= tailenderScore)
            return b.RiskManagement >= 13 ? BattingRole.Opener : BattingRole.TopOrder;

        if (finisherScore > topOrderScore)
            return BattingRole.LowerOrder;

        return BattingRole.MiddleOrder;
    }

    public BowlingRoleType DeriveBowlingRole(Player p)
    {
        if (p.PrimaryRole == PlayerRole.Batsman) return BowlingRoleType.NotABowler;

        var bw = p.Bowling;
        if (bw.Spin >= 14 && p.BowlingStyle is BowlingStyle.RightArmOffSpin or BowlingStyle.RightArmLegSpin
            or BowlingStyle.LeftArmOrthodox or BowlingStyle.LeftArmChinaman)
            return BowlingRoleType.SpecialistSpinner;

        if (bw.DeathBowling >= 14 && bw.Yorker >= 12) return BowlingRoleType.DeathBowler;
        if (bw.NewBallBowling >= 14 && (bw.Swing >= 12 || bw.Seam >= 12)) return BowlingRoleType.OpeningBowler;
        if (bw.MiddleOverBowling >= 13 || bw.Containment >= 13) return BowlingRoleType.MiddleOversSpecialist;
        if (p.PrimaryRole is PlayerRole.BattingAllrounder && bw.Accuracy < 12) return BowlingRoleType.PartTimeBowler;

        return BowlingRoleType.FirstChange;
    }

    public BattingTraits DeriveBattingTraits(Player p)
    {
        var b = p.Batting;
        var m = p.Mental;
        return new BattingTraits
        {
            Anchor = Scale(b.DefensiveAbility * 0.4 + b.RiskManagement * 0.35 + m.Concentration * 0.25),
            PowerHitter = Scale(b.PowerHitting * 0.5 + b.BoundaryHitting * 0.35 + b.Aggression * 0.15),
            Slogger = Scale(b.Aggression * 0.5 + b.PowerHitting * 0.3 - b.ShotSelection * 0.2 + 10),
            ClassicalBatter = Scale(b.Technique * 0.5 + b.ShotSelection * 0.3 + b.DefensiveAbility * 0.2),
            StrokeMaker = Scale(b.Timing * 0.4 + b.ShotSelection * 0.35 + b.BoundaryHitting * 0.25),
            Finisher = Scale(b.DeathOverBatting * 0.5 + b.PowerHitting * 0.3 + m.PressureHandling * 0.2),
            PartnershipBuilder = Scale(b.StrikeRotation * 0.4 + m.Composure * 0.3 + b.DefensiveAbility * 0.3),
            ChaseSpecialist = Scale(m.PressureHandling * 0.4 + m.DecisionMaking * 0.3 + b.StrikeRotation * 0.3),
            PressurePlayer = Scale(m.PressureHandling * 0.5 + m.Composure * 0.3 + m.Confidence * 0.2)
        };
    }

    public BowlingTraits DeriveBowlingTraits(Player p)
    {
        var bw = p.Bowling;
        return new BowlingTraits
        {
            SwingBowler = Scale(bw.Swing * 0.6 + bw.NewBallBowling * 0.4),
            SeamBowler = Scale(bw.Seam * 0.6 + bw.NewBallBowling * 0.4),
            WicketTaker = Scale(bw.AttackingAbility * 0.5 + bw.Variation * 0.3 + bw.Pace * 0.2),
            PartnershipBreaker = Scale(bw.AttackingAbility * 0.4 + bw.Variation * 0.4 + bw.MiddleOverBowling * 0.2),
            GoldenArm = p.PrimaryRole == PlayerRole.BattingAllrounder ? Scale(bw.Variation * 0.5 + 15) : Scale(bw.Variation * 0.3),
            DeathSpecialist = Scale(bw.DeathBowling * 0.5 + bw.Yorker * 0.3 + bw.SlowerBall * 0.2),
            MiddleOversSpecialist = Scale(bw.MiddleOverBowling * 0.5 + bw.Containment * 0.3 + bw.Spin * 0.2),
            ContainmentBowler = Scale(bw.Containment * 0.6 + bw.Accuracy * 0.4)
        };
    }

    /// <summary>Runs full derivation and assigns results directly onto the player (roles + traits).</summary>
    public void ApplyTo(Player p)
    {
        p.BattingRole = DeriveBattingRole(p);
        p.BowlingRole = DeriveBowlingRole(p);
        p.BattingTraits = DeriveBattingTraits(p);
        p.BowlingTraits = DeriveBowlingTraits(p);
    }

    // Attributes are 1-20; weighted sums land roughly 1-20 too, scale to 0-100 and clamp.
    private static int Scale(double weightedAttributeSum) => (int)Math.Clamp(weightedAttributeSum * 5, 0, 100);
}
