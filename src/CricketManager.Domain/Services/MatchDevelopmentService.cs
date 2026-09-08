using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What one match performance did to a player's development, itemised so the recorder can report a genuinely notable one rather than applying it invisibly.</summary>
public sealed record MatchDevelopmentResult(bool Developed, bool Redeemed, bool FrozeAgain, string? Summary);

/// <summary>
/// Post-Phase-5 rectification pass, Wave 2 (points 2 and 3): the SECOND development channel the
/// source document asks for - a player develops by PLAYING, not only by training, and the two run
/// alongside each other rather than one instead of the other.
///
/// Two distinct effects, both driven off a single per-performance call from
/// MatchRecorder/MultiDayMatchRecorder:
///
/// 1. **Learning by doing, format-aware.** A young player who actually bats a Test innings gets a
///    real, small chance of sharpening the attributes that innings tested - technique and
///    concentration for a red-ball knock, power and death-hitting for a T20 one. Struggle still
///    teaches (a failure is a real learning input, per the brief) - it just teaches less than a
///    success does. Bounded by the same PotentialAbility clamp TrainingService respects, gated
///    hard on youth and remaining headroom so a 33-year-old journeyman does not "develop" his way
///    to a new career off one good night.
///
/// 2. **The pressure moment.** A big public failure in a high-importance match sets an unresolved
///    PressureMoment on the player (see that record's doc comment - the Stokes/Brathwaite case
///    named directly in the source). The next time he plays a high-pressure match, how he responds
///    is heavily personality-modulated: a Professional/BigMatchPlayer redeems himself and comes
///    out of it genuinely tougher; an Inconsistent/Lazy one freezes again and it compounds. A
///    handful of ordinary big matches with neither outcome and the moment simply fades - he has
///    moved on.
///
/// Deliberately a separate service from TrainingService (voluntary, coach-directed) and
/// PlayerAgeingService (involuntary, time-driven) - this is development that happens BECAUSE he
/// played, which is a third thing again, and the source document treats it as one.
/// </summary>
public sealed class MatchDevelopmentService
{
    private readonly RoleTraitDeriver _roles = new();

    /// <summary>A match at or above this BaseImportance is a "pressure moment" - a knockout, a final, a genuine occasion. Below it, only the learning-by-doing channel applies.</summary>
    public const double HighPressureThreshold = 70;

    /// <summary>A performance rating (the contextually valued -100..100 number) at or below this is a genuine big-match failure - the kind that hangs over a player.</summary>
    private const double BigFailureRating = -25;

    /// <summary>A performance rating at or above this, in a pressure match, clears an unresolved failure - redemption.</summary>
    private const double RedemptionRating = 15;

    /// <summary>Past this many further pressure matches with no resolution either way, the moment fades - he has moved on.</summary>
    private const int MomentFadesAfterAttempts = 5;

    /// <param name="valuedRating">The contextually valued -100..100 rating from PerformanceRecordingService - the same number Form and morale already consume.</param>
    /// <param name="matchImportance">MatchSetup.BaseImportance (0-100) - what makes a knockout a pressure moment and a dead rubber not.</param>
    /// <param name="world">Optional - when supplied, the learning-by-doing growth is accumulated into WorldState.RecentDevelopmentByPlayer for Wave 4's staff-development signal. Null everywhere it is not needed, exactly like the recorders' own optional world parameter.</param>
    public MatchDevelopmentResult ApplyMatchExperience(
        Player player, MatchFormat format, bool bowling, double valuedRating, double matchImportance,
        Random random, DateOnly asOf, WorldState? world = null)
    {
        bool developed = LearnByDoing(player, format, bowling, valuedRating, random, asOf, world);

        var pressure = ApplyPressureMoment(player, valuedRating, matchImportance, random, asOf);

        if (developed || pressure.Redeemed || pressure.FrozeAgain)
        {
            player.RecalculateFormatSuitability();
            _roles.ApplyTo(player);
        }

        string? summary = pressure.Summary
            ?? (developed ? $"{player.FullName} has learned something from that {format} outing." : null);

        return new MatchDevelopmentResult(developed, pressure.Redeemed, pressure.FrozeAgain, summary);
    }

    // ---------------- learning by doing ----------------

    private bool LearnByDoing(Player player, MatchFormat format, bool bowling, double rating, Random random, DateOnly asOf, WorldState? world)
    {
        int age = player.Age(asOf);
        double youthFactor = age <= 22 ? 1.0 : age <= 24 ? 0.75 : age <= 27 ? 0.45 : age <= 30 ? 0.2 : age <= 33 ? 0.07 : 0.015;

        double headroom = Math.Clamp((player.PotentialAbility - player.CurrentAbility) / 40.0, 0, 1);

        // Struggle teaches too - a rating of -40 still gives 0.4x, a strong +60 gives ~1.6x. What
        // it never does is teach NOTHING, which is the point the brief makes about pressure being
        // a learning input rather than only a cost.
        double performanceFactor = 0.4 + Math.Clamp((rating + 40) / 100.0, 0, 1) * 1.2;

        double chance = 0.08 * youthFactor * (0.3 + headroom) * performanceFactor;
        if (random.NextDouble() >= chance) return false;

        var attrs = FormatCluster(player, format, bowling);
        if (attrs.Count == 0) return false;

        // One attribute, +1, chosen from the format-relevant cluster - the same "a real, nameable
        // thing improved" discipline TrainingService's cluster model uses, not a blanket bump.
        var setter = attrs[random.Next(attrs.Count)];
        setter();

        // CurrentAbility follows, clamped to potential exactly as TrainingService does - match
        // experience can bring a young player TO his ceiling faster, never through it.
        player.CurrentAbility = Math.Clamp(player.CurrentAbility + 2, 1, player.PotentialAbility);

        if (world is not null)
            world.RecentDevelopmentByPlayer[player.Id] =
                world.RecentDevelopmentByPlayer.TryGetValue(player.Id, out var acc) ? acc + 1 : 1;

        return true;
    }

    private static List<Action> FormatCluster(Player p, MatchFormat format, bool bowling)
    {
        List<Action> list = new();
        void B(Func<int> get, Action<int> set) => list.Add(() => set(Math.Clamp(get() + 1, 1, 20)));

        if (!bowling)
        {
            switch (format)
            {
                case MatchFormat.Test:
                    B(() => p.Batting.Technique, v => p.Batting.Technique = v);
                    B(() => p.Batting.DefensiveAbility, v => p.Batting.DefensiveAbility = v);
                    B(() => p.Mental.Concentration, v => p.Mental.Concentration = v);
                    B(() => p.Batting.AgainstSpin, v => p.Batting.AgainstSpin = v);
                    break;
                case MatchFormat.ODI:
                    B(() => p.Batting.StrikeRotation, v => p.Batting.StrikeRotation = v);
                    B(() => p.Mental.Adaptability, v => p.Mental.Adaptability = v);
                    B(() => p.Batting.AgainstPace, v => p.Batting.AgainstPace = v);
                    B(() => p.Batting.RiskManagement, v => p.Batting.RiskManagement = v);
                    break;
                default: // T20
                    B(() => p.Batting.PowerHitting, v => p.Batting.PowerHitting = v);
                    B(() => p.Batting.DeathOverBatting, v => p.Batting.DeathOverBatting = v);
                    B(() => p.Batting.BoundaryHitting, v => p.Batting.BoundaryHitting = v);
                    break;
            }
        }
        else
        {
            switch (format)
            {
                case MatchFormat.Test:
                    B(() => p.Bowling.Accuracy, v => p.Bowling.Accuracy = v);
                    B(() => p.Bowling.Seam, v => p.Bowling.Seam = v);
                    B(() => p.Mental.Concentration, v => p.Mental.Concentration = v);
                    break;
                case MatchFormat.ODI:
                    B(() => p.Bowling.Containment, v => p.Bowling.Containment = v);
                    B(() => p.Bowling.MiddleOverBowling, v => p.Bowling.MiddleOverBowling = v);
                    B(() => p.Bowling.Variation, v => p.Bowling.Variation = v);
                    break;
                default: // T20
                    B(() => p.Bowling.Yorker, v => p.Bowling.Yorker = v);
                    B(() => p.Bowling.SlowerBall, v => p.Bowling.SlowerBall = v);
                    B(() => p.Bowling.DeathBowling, v => p.Bowling.DeathBowling = v);
                    break;
            }
        }

        return list;
    }

    // ---------------- the pressure moment ----------------

    private (bool Redeemed, bool FrozeAgain, string? Summary) ApplyPressureMoment(
        Player player, double rating, double matchImportance, Random random, DateOnly asOf)
    {
        if (matchImportance < HighPressureThreshold) return (false, false, null);

        var moment = player.PendingPressureMoment;

        if (moment is null)
        {
            if (rating <= BigFailureRating)
            {
                player.PendingPressureMoment = new PressureMoment(asOf, Math.Clamp(-rating, 0, 100));
                player.Form.AdjustConfidence(-6); // a public failure dents him now, before any arc
                player.Morale.Adjust(-4);
                return (false, false, $"{player.FullName} will have that big-match failure to put right.");
            }
            return (false, false, null);
        }

        double resilience = PersonalityResilience(player);
        double severityScale = 0.5 + moment.Severity / 100.0; // a bigger original failure means a bigger swing either way

        if (rating >= RedemptionRating)
        {
            // Redemption. The tougher the temperament, the more likely he has genuinely LEARNED
            // from it (a real PressureHandling point), not merely survived it once.
            double growthChance = Math.Clamp(0.25 + resilience * 0.4, 0.05, 0.75) * severityScale;
            if (random.NextDouble() < growthChance)
                player.Mental.PressureHandling = Math.Min(20, player.Mental.PressureHandling + 1);

            double lift = (8 + resilience * 8) * severityScale;
            player.Form.AdjustConfidence(lift);
            player.Morale.Adjust(lift * 0.7);

            player.PendingPressureMoment = null;
            return (true, false, $"{player.FullName} has answered the questions in the biggest moment - that will stay with him.");
        }

        if (rating <= BigFailureRating)
        {
            // Froze again. A fragile player compounds it; a tough one is bruised but the arc is
            // still open.
            double hit = (5 + (1 - resilience) * 7) * severityScale;
            player.Form.AdjustConfidence(-hit);
            player.Morale.Adjust(-hit * 0.7);

            int attempts = moment.SubsequentAttempts + 1;
            player.PendingPressureMoment = moment with
            {
                Severity = Math.Max(moment.Severity, Math.Clamp(-rating, 0, 100)),
                SubsequentAttempts = attempts
            };

            // If it keeps happening to someone who cannot handle it, it starts to cost him for real.
            if (attempts >= 3 && resilience < 0.1 && random.NextDouble() < 0.3)
                player.Mental.PressureHandling = Math.Max(1, player.Mental.PressureHandling - 1);

            return (false, true, $"{player.FullName} has frozen in the big moment again.");
        }

        // Neither redeemed nor failed - an ordinary big-match outing. A few of these and the moment
        // has simply passed.
        int newAttempts = moment.SubsequentAttempts + 1;
        if (newAttempts >= MomentFadesAfterAttempts)
        {
            player.PendingPressureMoment = null;
            return (false, false, null);
        }
        player.PendingPressureMoment = moment with { SubsequentAttempts = newAttempts };
        return (false, false, null);
    }

    /// <summary>
    /// How well this player's temperament equips him to come back from a big-match failure -
    /// roughly -0.6 (crumbles) to +1.0 (built for it). Reads the same personality flags
    /// SituationalPerformanceModifier.PressureAdjustment and RetirementService already lean on,
    /// so a player who is "a big-match player" everywhere is one here too.
    /// </summary>
    public static double PersonalityResilience(Player player)
    {
        double r = 0;
        if (player.Personality.HasFlag(PersonalityTrait.BigMatchPlayer)) r += 0.4;
        if (player.Personality.HasFlag(PersonalityTrait.PressurePlayer)) r += 0.3;
        if (player.Personality.HasFlag(PersonalityTrait.Professional)) r += 0.25;
        if (player.Personality.HasFlag(PersonalityTrait.Ambitious)) r += 0.2;
        if (player.Personality.HasFlag(PersonalityTrait.Consistent)) r += 0.15;
        if (player.Personality.HasFlag(PersonalityTrait.Inconsistent)) r -= 0.3;
        if (player.Personality.HasFlag(PersonalityTrait.Lazy)) r -= 0.2;

        // Someone who is temperamentally strong on the attribute, not just the flags, gets some
        // credit too - the flags are the sharp cases, the attribute is the gradient.
        r += (AbilityScale.AttributeToHundred(player.Mental.PressureHandling) - 50) / 100.0 * 0.4;

        return Math.Clamp(r, -0.6, 1.0);
    }
}
