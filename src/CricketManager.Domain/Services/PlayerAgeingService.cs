using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>What a year of ageing did to one player, itemised by attribute group so it can be reported rather than silently applied.</summary>
public sealed record AgeingResult(Guid PlayerId, int Age, double PhysicalChange, double FieldingChange, double TechnicalChange, double MentalChange, string Summary);

/// <summary>
/// Ageing is INVOLUNTARY change over time. It is deliberately a separate system from Phase 8's
/// training, which is voluntary, coach-directed improvement - conflating them would mean a
/// coach could train away a 38-year-old's lost yard of pace, which is not a thing.
///
/// The central point, and the reason a single "decline after 30" curve would be wrong: the
/// four attribute groups move on completely different schedules.
/// - **Physical** (pace, speed, stamina, strength, recovery) peaks mid-20s and is the FIRST to
///   go. This is what ends careers.
/// - **Fielding/reflexes** peak slightly later and fall away sharply from the mid-30s, which is
///   why ageing greats end up at slip rather than in the deep.
/// - **Technical** (technique, shot selection, accuracy, variations) keeps IMPROVING into the
///   early 30s and holds - a batter's method is better at 33 than at 23 - then erodes slowly,
///   and only really breaks down past 40 when the body can no longer execute what the head knows.
/// - **Mental** (composure, concentration, game awareness, decision making, leadership) keeps
///   rising well into the late 30s and barely declines at all. This is why a 36-year-old past
///   his physical peak can still be worth picking, and it is exactly the effect the design asks for.
///
/// Fractional yearly change is resolved probabilistically rather than accumulated in a hidden
/// residue field: a +0.3/year technical trend means a real chance of +1 this year and nothing
/// next, which is how attribute progression actually looks. It also means two identical players
/// diverge over a decade, which is what makes careers feel individual.
/// </summary>
public sealed class PlayerAgeingService
{
    private readonly RoleTraitDeriver _roles = new();

    /// <summary>Applies one year of ageing. Called on season rollover by the world clock - annually, not daily, because that is the granularity at which a player's attributes meaningfully move.</summary>
    public AgeingResult ApplyAnnualAgeing(Player player, DateOnly asOf, Random random, int trainingFacilityQuality = 50)
    {
        int age = player.Age(asOf);

        // Phase 8, Slice 8.4: individual peak-timing variation. A positive PeakAgeOffset is a LATE
        // developer - his physical/technical peak and the decline that follows both arrive later, so
        // the trend curves are read at a younger EFFECTIVE age. Technical timing shifts by half the
        // offset (method matures on its own schedule, only loosely tied to the body); mental barely
        // peaks at all, so it is left on the real age. 0 (every pre-Phase-8 player) is the unchanged
        // average curve.
        int physicalAge = age - player.PeakAgeOffset;
        int technicalAge = age - player.PeakAgeOffset / 2;

        double physicalTrend = PhysicalTrend(physicalAge, player);
        double fieldingTrend = FieldingTrend(physicalAge);
        double technicalTrend = TechnicalTrend(technicalAge);
        double mentalTrend = MentalTrend(age);

        // Growth (not decline) is gated by remaining potential and helped by facilities and
        // personality; decline is NOT - no amount of training stops a 37-year-old slowing down,
        // though professionalism slows it a little.
        double headroom = Math.Clamp((player.PotentialAbility - player.CurrentAbility) / 40.0, 0, 1);
        double facilityFactor = 0.8 + Math.Clamp(trainingFacilityQuality, 0, 100) / 100.0 * 0.4;

        bool fastLearner = player.Personality.HasFlag(PersonalityTrait.FastLearner);
        bool slowDeveloper = player.Personality.HasFlag(PersonalityTrait.SlowDeveloper);
        bool professional = player.Personality.HasFlag(PersonalityTrait.Professional);
        bool lazy = player.Personality.HasFlag(PersonalityTrait.Lazy);

        double growthMultiplier = facilityFactor * (0.5 + headroom * 0.8)
            * (fastLearner ? 1.25 : 1.0) * (slowDeveloper ? 0.75 : 1.0);

        // A professional looks after himself; a lazy one ages badly. Injury history accelerates
        // physical decline, which is how an injury-hit career quietly shortens itself.
        int seriousInjuries = player.InjuryHistory.Count(i => i.Severity >= InjurySeverity.Moderate);
        double declineMultiplier = (professional ? 0.85 : 1.0) * (lazy ? 1.2 : 1.0)
            * (1 + Math.Min(seriousInjuries, 6) * 0.06);

        double physical = Resolve(physicalTrend, growthMultiplier, declineMultiplier, random);
        double fielding = Resolve(fieldingTrend, growthMultiplier, declineMultiplier, random);
        double technical = Resolve(technicalTrend, growthMultiplier, declineMultiplier, random);
        double mental = Resolve(mentalTrend, growthMultiplier, declineMultiplier, random);

        ApplyToPhysical(player, (int)physical);
        ApplyToFielding(player, (int)fielding);
        ApplyToTechnical(player, (int)technical);
        ApplyToMental(player, (int)mental);

        // CurrentAbility follows the attributes rather than drifting independently - physical
        // and technical drive it most, since that is what a composite ability rating measures.
        double abilityDelta = physical * 2.5 + technical * 3.0 + fielding * 1.0 + mental * 1.5;
        player.CurrentAbility = Math.Clamp(player.CurrentAbility + (int)Math.Round(abilityDelta), 1, 200);

        // Past the peak, potential is no longer a promise - it converges down onto what the
        // player has actually become, so an unfulfilled prospect at 33 stops being "one to watch".
        if (age >= 30 && player.PotentialAbility > player.CurrentAbility)
            player.PotentialAbility = Math.Max(player.CurrentAbility, player.PotentialAbility - 3);

        player.RecalculateFormatSuitability();

        // Phase 5: closes the RoleTraitDeriver staleness gap for ageing's OWN attribute changes
        // too, not only TrainingService's - a player whose technique keeps improving into his
        // early 30s (this class's own TechnicalTrend) or whose pace declines into his late 30s
        // should have BattingRole/BowlingRole/traits re-derived from what he has actually become,
        // the same discipline TrainingService applies to itself.
        if (physical != 0 || fielding != 0 || technical != 0 || mental != 0)
            _roles.ApplyTo(player);

        return new AgeingResult(player.Id, age, physical, fielding, technical, mental,
            DescribeAgeing(player, age, physical, technical, mental));
    }

    // ---------------- curves ----------------

    private static double PhysicalTrend(int age, Player player)
    {
        // Fast bowlers break down earlier and harder - the workload is simply more punishing,
        // and it is the single biggest reason quick bowlers have shorter careers than batters.
        bool isFastBowler = player.BowlingRole is BowlingRoleType.OpeningBowler or BowlingRoleType.DeathBowler
                            && player.Bowling.Pace >= 13;
        double penalty = isFastBowler ? 1.35 : 1.0;

        return age switch
        {
            <= 21 => 0.55,
            <= 24 => 0.35,
            <= 27 => 0.05,
            <= 29 => -0.15 * penalty,
            <= 32 => -0.35 * penalty,
            <= 35 => -0.55 * penalty,
            <= 38 => -0.8 * penalty,
            _ => -1.1 * penalty
        };
    }

    private static double FieldingTrend(int age) => age switch
    {
        <= 22 => 0.5,
        <= 26 => 0.25,
        <= 29 => 0.0,
        <= 32 => -0.2,
        <= 35 => -0.4,
        <= 38 => -0.65,
        _ => -0.9
    };

    private static double TechnicalTrend(int age) => age switch
    {
        <= 20 => 0.7,
        <= 24 => 0.55,
        <= 28 => 0.35,
        <= 31 => 0.2,     // still improving - a batter's method peaks well after his body
        <= 35 => 0.0,     // plateau
        <= 38 => -0.2,
        <= 40 => -0.4,
        _ => -0.75        // past 40 the hands can no longer execute what the head still knows
    };

    private static double MentalTrend(int age) => age switch
    {
        <= 21 => 0.5,
        <= 26 => 0.4,
        <= 32 => 0.3,
        <= 36 => 0.2,     // still rising - this is why veterans stay valuable
        <= 40 => 0.05,
        _ => -0.1         // barely declines even then
    };

    /// <summary>
    /// Turns a fractional yearly trend into a whole-number attribute change. The fractional part
    /// is a probability, so +0.35 means roughly a one-in-three chance of +1 this year - real
    /// progression is lumpy, and this also makes two identical players diverge over a career.
    /// </summary>
    private static double Resolve(double trend, double growthMultiplier, double declineMultiplier, Random random)
    {
        double adjusted = trend >= 0 ? trend * growthMultiplier : trend * declineMultiplier;

        int whole = (int)adjusted;
        double fraction = Math.Abs(adjusted - whole);

        if (random.NextDouble() < fraction)
            whole += adjusted >= 0 ? 1 : -1;

        return whole;
    }

    // ---------------- application ----------------

    private static void ApplyToPhysical(Player p, int delta)
    {
        if (delta == 0) return;
        p.Physical.Speed = Clamp(p.Physical.Speed + delta);
        p.Physical.Stamina = Clamp(p.Physical.Stamina + delta);
        p.Physical.Fitness = Clamp(p.Physical.Fitness + delta);
        p.Physical.Strength = Clamp(p.Physical.Strength + delta);
        p.Physical.Recovery = Clamp(p.Physical.Recovery + delta);

        // Bowling pace is a physical attribute in everything but name - a bowler losing a yard
        // is the most visible single effect of ageing in cricket.
        p.Bowling.Pace = Clamp(p.Bowling.Pace + delta);
    }

    private static void ApplyToFielding(Player p, int delta)
    {
        if (delta == 0) return;
        p.Fielding.Reflexes = Clamp(p.Fielding.Reflexes + delta);
        p.Fielding.GroundFielding = Clamp(p.Fielding.GroundFielding + delta);
        p.Fielding.BoundaryFielding = Clamp(p.Fielding.BoundaryFielding + delta);
        p.Fielding.Throwing = Clamp(p.Fielding.Throwing + delta);
        p.Fielding.Catching = Clamp(p.Fielding.Catching + delta);
    }

    private static void ApplyToTechnical(Player p, int delta)
    {
        if (delta == 0) return;
        p.Batting.Technique = Clamp(p.Batting.Technique + delta);
        p.Batting.ShotSelection = Clamp(p.Batting.ShotSelection + delta);
        p.Batting.DefensiveAbility = Clamp(p.Batting.DefensiveAbility + delta);
        p.Batting.RiskManagement = Clamp(p.Batting.RiskManagement + delta);
        p.Batting.Timing = Clamp(p.Batting.Timing + delta);

        p.Bowling.Accuracy = Clamp(p.Bowling.Accuracy + delta);
        p.Bowling.Variation = Clamp(p.Bowling.Variation + delta);
        p.Bowling.Swing = Clamp(p.Bowling.Swing + delta);
        p.Bowling.Seam = Clamp(p.Bowling.Seam + delta);
        p.Bowling.Spin = Clamp(p.Bowling.Spin + delta);
    }

    private static void ApplyToMental(Player p, int delta)
    {
        if (delta == 0) return;
        p.Mental.Composure = Clamp(p.Mental.Composure + delta);
        p.Mental.Concentration = Clamp(p.Mental.Concentration + delta);
        p.Mental.GameAwareness = Clamp(p.Mental.GameAwareness + delta);
        p.Mental.DecisionMaking = Clamp(p.Mental.DecisionMaking + delta);
        p.Mental.PressureHandling = Clamp(p.Mental.PressureHandling + delta);
        p.Mental.Leadership = Clamp(p.Mental.Leadership + delta);
    }

    private static int Clamp(int value) => Math.Clamp(value, 1, 20);

    private static string DescribeAgeing(Player player, int age, double physical, double technical, double mental)
    {
        if (physical < 0 && technical > 0)
            return $"{player.FullName} ({age}) has lost a little physically but is still sharpening his craft.";
        if (physical < 0 && mental > 0)
            return $"{player.FullName} ({age}) is slowing down, though his reading of the game keeps improving.";
        if (physical > 0 && technical > 0)
            return $"{player.FullName} ({age}) is still developing in every department.";
        if (physical < 0 && technical < 0)
            return $"{player.FullName} ({age}) is now declining across the board.";
        return $"{player.FullName} ({age}) is holding his level.";
    }
}
