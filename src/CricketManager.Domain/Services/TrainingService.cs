using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What one year of training actually did to a player, itemised so it can be reported rather than silently applied.</summary>
public sealed record TrainingResult(
    Guid PlayerId,
    TrainingFocus Focus,
    int AttributeGrowth,
    bool RoleConverted,
    bool PotentialAbilityRose,
    /// <summary>An overtraining injury this year's programme produced, when it did. The caller applies it via WorldState.ApplyInjury - TrainingService is a pure Domain service and does not own the due-date indexing that entry point is responsible for.</summary>
    Injury? OvertrainingInjury,
    string Summary);

/// <summary>
/// Phase 5: coach-directed, VOLUNTARY player development - deliberately a separate system from
/// PlayerAgeingService's INVOLUNTARY change, per that class's own doc comment: conflating them
/// would let a coach train away a 38-year-old's lost yard of pace, which is not a thing. Training
/// is what a coach chooses to work on; ageing is what happens to a body regardless of what anyone
/// chooses.
///
/// This closes two tech-debt items named since Phase 2/3 and left open until a training system
/// existed to close them:
/// - RoleTraitDeriver staleness (its own doc comment: "Phase 8 must either call ApplyTo()
///   explicitly after every attribute-changing event, or this should become event-driven").
///   ApplyAnnualTraining calls RoleTraitDeriver.ApplyTo() after every application that actually
///   moved an attribute, closing the gap directly.
/// - The CurrentAbility-can-exceed-PotentialAbility invariant (Player's own doc comment: "a
///   player exceeding their ceiling through sustained real performance is a legitimate emergent
///   story... but that has to be a deliberate decision"). The deliberate decision made here:
///   CurrentAbility is hard-clamped to PotentialAbility on every training application EXCEPT a
///   rare, bounded "breakthrough" case for a young player on a genuinely excellent, intensive
///   programme - see TryBreakthrough.
///
/// Real per-attribute development, not a single overall number, because that is what a genuine
/// net-session focus actually looks like: "working on his death bowling" is a real, nameable
/// thing a coach or commentator would say; "improving his overall rating by three points" is not.
/// </summary>
public sealed class TrainingService
{
    private readonly RoleTraitDeriver _roles = new();

    /// <summary>Below this age, growth is meaningfully faster - the real-cricket window where technique and game sense are still being formed rather than only maintained.</summary>
    private const int PrimeDevelopmentAge = 23;

    /// <summary>
    /// Phase 8, Slice 8.1/8.2: a genuine teenager (an academy prospect) develops faster still - the
    /// years where a raw player is being built, not merely refined. Seeded players never reach this
    /// band (WorldSeeder generates from age 18 up), so this only ever accelerates youth-academy
    /// intake, which is the point.
    /// </summary>
    private const int AcademyDevelopmentAge = 17;

    // ---------------- staff suggestion (Section 6: staff suggests, the human head coach decides) ----------------

    /// <summary>
    /// What the relevant specialist coach (or, with none hired, the team's general training
    /// setup) would suggest working on: this player's weakest attribute cluster among the ones
    /// genuinely relevant to his game - a part-time off-spinner's weak leg-spin craft is not a
    /// real suggestion, because he does not bowl leg-spin. Deliberately simpler than
    /// AnalystService's full estimate-vs-truth machinery (which exists for reading an OPPOSITION
    /// under uncertainty) - this is a team looking at its own player, which real staff can do
    /// far more reliably than they can read a stranger. A weak coach still occasionally misreads
    /// it, scaled by his own effectiveness, which is the one piece of that discipline kept.
    /// </summary>
    public TrainingFocus SuggestFocus(Player player, IReadOnlyList<StaffMember> teamStaff, Random random, bool delegatedToStaff = false)
    {
        var candidates = ApplicableClusters(player);
        if (candidates.Count == 0) return TrainingFocus.None;

        var ranked = candidates.OrderBy(f => ClusterAverage(player, f)).ToList();

        // Real cricket: the batting coach flags a batting concern, the bowling coach a bowling
        // one - the suggestion, like the actual weakness, comes from whichever specialist covers
        // the player's own primary discipline.
        var suggestingCoach = FindCoach(teamStaff, player.PrimaryRole == PlayerRole.Bowler ? StaffRole.BowlingCoach : StaffRole.BattingCoach);
        double coachQuality = suggestingCoach?.EffectiveWithExperience ?? 50;
        // A strong coach reliably names the real weakness. A weak one sometimes points at
        // something else - never confidently wrong, just less sharp than a specialist should be.
        // Wave 4: when the head coach has formally delegated training-focus decisions to his
        // specialists, the specialist owns the call and is sharper at it - fewer misreads.
        double misreadChance = Math.Clamp((60 - coachQuality) / 150.0, 0, 0.35) * (delegatedToStaff ? 0.5 : 1.0);

        if (random.NextDouble() < misreadChance && ranked.Count > 1)
            return ranked[random.Next(1, Math.Min(ranked.Count, 3))];

        return ranked[0];
    }

    /// <summary>Which specialist actually covers a given focus area - see the class doc comment. RoleConversion has no single natural owner, so it falls to the assistant coach.</summary>
    private static StaffRole StaffRoleFor(TrainingFocus focus) => focus switch
    {
        TrainingFocus.BattingTechnique or TrainingFocus.BattingPower or TrainingFocus.BattingAgainstPace
            or TrainingFocus.BattingAgainstSpin or TrainingFocus.BattingFinishing or TrainingFocus.AllroundBatting
            => StaffRole.BattingCoach,
        TrainingFocus.BowlingPaceDevelopment or TrainingFocus.BowlingAccuracy or TrainingFocus.BowlingVariations
            or TrainingFocus.BowlingSwingSeam or TrainingFocus.BowlingSpinCraft or TrainingFocus.BowlingDeathCraft
            or TrainingFocus.AllroundBowling => StaffRole.BowlingCoach,
        TrainingFocus.Fielding => StaffRole.FieldingCoach,
        TrainingFocus.Physical => StaffRole.StrengthAndConditioning,
        TrainingFocus.Mental => StaffRole.MentalPerformanceCoach,
        _ => StaffRole.AssistantCoach
    };

    private static StaffMember? FindCoach(IReadOnlyList<StaffMember> staff, StaffRole role) =>
        staff.FirstOrDefault(s => s.Role == role);

    private static List<TrainingFocus> ApplicableClusters(Player player)
    {
        var list = new List<TrainingFocus> { TrainingFocus.Fielding, TrainingFocus.Physical, TrainingFocus.Mental };

        if (player.PrimaryRole != PlayerRole.Bowler)
        {
            list.Add(TrainingFocus.BattingTechnique);
            list.Add(TrainingFocus.BattingPower);
            list.Add(TrainingFocus.BattingAgainstPace);
            list.Add(TrainingFocus.BattingAgainstSpin);
            list.Add(TrainingFocus.BattingFinishing);
        }

        if (player.BowlingRole != BowlingRoleType.NotABowler)
        {
            bool spinner = BallOutcomeModel.IsSpinner(player);
            if (spinner)
            {
                list.Add(TrainingFocus.BowlingSpinCraft);
            }
            else
            {
                list.Add(TrainingFocus.BowlingPaceDevelopment);
                list.Add(TrainingFocus.BowlingSwingSeam);
            }
            list.Add(TrainingFocus.BowlingAccuracy);
            list.Add(TrainingFocus.BowlingVariations);
            list.Add(TrainingFocus.BowlingDeathCraft);
        }

        return list;
    }

    // ---------------- application ----------------

    /// <param name="teamTrainingQuality">Team.Facilities.TrainingQuality - the generic facility fallback, always available.</param>
    /// <param name="teamStaff">
    /// Every backroom staff member hired at this player's team, when known - empty/null-team
    /// callers get the facility-only fallback throughout. Resolution to the ONE specialist who
    /// actually matters happens internally, once the focus itself is known (see StaffRoleFor) -
    /// this cannot be resolved by the caller in advance, because for a player with no explicit
    /// plan the focus itself is only decided inside this call, by SuggestFocus.
    /// </param>
    /// <summary>
    /// A full year of training in one call - the original annual entry point, now a thin wrapper
    /// over the periodic one at periodFraction 1.0. Kept so every existing direct caller (and the
    /// eight Phase 5 Part 1 training tests) is unaffected: the world clock itself no longer calls
    /// this - it calls ApplyPeriodicTraining monthly instead (Wave 2) - but a caller that genuinely
    /// wants "one year, resolved now" still has it.
    /// </summary>
    public TrainingResult ApplyAnnualTraining(
        Player player, int teamTrainingQuality, IReadOnlyList<StaffMember> teamStaff, Random random, DateOnly asOf)
        => ApplyPeriodicTraining(player, teamTrainingQuality, teamStaff, random, asOf, periodFraction: 1.0, reassessFocus: true);

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 2 (point 1): training now resolves on the world
    /// clock's MONTHLY tick, in twelve small increments a year, rather than one annual lump - a
    /// mood, a weakness, a coach's plan all move at a cadence a player would actually notice.
    /// periodFraction (1/12 for the monthly tick) scales the growth roll, the breakthrough roll
    /// and the overtraining-injury roll proportionally, so a year of monthly ticks lands close to
    /// what one annual call always did.
    /// </summary>
    /// <param name="reassessFocus">
    /// Wave 2 (point 1): "coach-reassessable focus". When true and the coach has left the focus
    /// unset (Focus == None), the staff's suggested focus is re-derived EVERY period rather than
    /// once a year, so the suggestion tracks the player's changing weakest area as his own
    /// training closes old gaps and opens the next one. A focus the coach explicitly set is never
    /// touched, exactly as before.
    /// </param>
    /// <param name="focusDelegatedToStaff">Wave 4: the head coach has formally handed training-focus decisions to his specialists (Team.DelegatedResponsibilities) - the specialist reads the weakness more reliably and works it a little more effectively.</param>
    public TrainingResult ApplyPeriodicTraining(
        Player player, int teamTrainingQuality, IReadOnlyList<StaffMember> teamStaff, Random random, DateOnly asOf,
        double periodFraction, bool reassessFocus, bool focusDelegatedToStaff = false)
    {
        var plan = player.TrainingPlan;
        periodFraction = Math.Clamp(periodFraction, 0.0, 1.0);

        // Wave 2 (point 4): rehab now allows LIGHT training in an injury's final phase rather
        // than a blanket block. CareerThreatening is still fully blocked - that is long-term
        // rehabilitation, nothing else. Moderate/Serious blocks the normal programme until the
        // last ~30% of the expected layoff, then permits reduced, physical-only work with no
        // overtraining risk - which is exactly what a return-to-play protocol looks like.
        // Niggle/Minor is unchanged: individual work continues throughout.
        if (player.CurrentInjury is { } injury && injury.Severity >= InjurySeverity.Moderate)
        {
            if (injury.Severity == InjurySeverity.CareerThreatening)
                return NoOp(player, $"{player.FullName} is in long-term rehabilitation and cannot train.");

            int total = Math.Max(1, injury.ExpectedReturnDate.DayNumber - injury.StartDate.DayNumber);
            int elapsed = asOf.DayNumber - injury.StartDate.DayNumber;
            if (elapsed < total * 0.7)
                return NoOp(player, $"{player.FullName} is working through rehab and not yet back in training.");

            var rehab = ApplyFocusedTraining(player, TrainingFocus.Physical, TrainingIntensity.Light,
                Math.Clamp(teamTrainingQuality, 0, 100), random, asOf, periodFraction * 0.5, rollInjury: false);
            return rehab with { Summary = $"{player.FullName} is back in light training in the final phase of his rehab." };
        }

        var focus = plan.Focus;
        if (focus == TrainingFocus.None)
        {
            focus = SuggestFocus(player, teamStaff, random, focusDelegatedToStaff);
            plan.StaffSuggestion = focus;
            // reassessFocus only changes WHETHER this re-runs each period vs once a year - the
            // gate is plan.Focus == None either way, so a coach's explicit choice is never
            // overwritten. (The parameter is retained for call-site intent and for a future
            // "reassess even a set focus if it has clearly succeeded" behaviour.)
        }

        if (focus == TrainingFocus.None) // no relevant clusters at all - nothing to do
            return NoOp(player, $"{player.FullName} has no directed training programme.");

        var coach = FindCoach(teamStaff, StaffRoleFor(focus));
        double coachQuality = coach?.EffectiveWithExperience ?? Math.Clamp(teamTrainingQuality, 0, 100);
        if (focusDelegatedToStaff && coach is not null) coachQuality = Math.Min(100, coachQuality + 6);

        if (focus == TrainingFocus.RoleConversion)
            return ApplyRoleConversion(player, plan, coachQuality, random, asOf, periodFraction);

        return ApplyFocusedTraining(player, focus, plan.Intensity, coachQuality, random, asOf, periodFraction, rollInjury: true);
    }

    private static TrainingResult NoOp(Player player, string summary) =>
        new(player.Id, TrainingFocus.None, 0, false, false, null, summary);

    private TrainingResult ApplyFocusedTraining(
        Player player, TrainingFocus focus, TrainingIntensity intensity, double coachQuality, Random random, DateOnly asOf,
        double periodFraction = 1.0, bool rollInjury = true)
    {
        bool allround = focus is TrainingFocus.AllroundBatting or TrainingFocus.AllroundBowling;
        var underlyingFocus = focus switch
        {
            TrainingFocus.AllroundBatting => TrainingFocus.BattingTechnique,
            TrainingFocus.AllroundBowling => TrainingFocus.BowlingAccuracy,
            _ => focus
        };

        double growthChance = GrowthChance(player, underlyingFocus, intensity, coachQuality, asOf) * periodFraction;
        int delta = random.NextDouble() < growthChance ? 1 : 0;

        if (delta > 0)
        {
            if (allround)
                ApplyAllroundCapped(player, focus, delta);
            else
                ApplyToCluster(player, focus, delta);

            _roles.ApplyTo(player); // closes the RoleTraitDeriver staleness gap - see class doc comment
            player.RecalculateFormatSuitability();
        }

        bool potentialRose = ApplyAbilityDelta(player, focus, delta, intensity, asOf, random, periodFraction);
        var overtrainingInjury = rollInjury ? RollOvertrainingInjury(player, intensity, random, asOf, periodFraction) : null;

        // Follow-up pass (§6.4): a genuinely SUSTAINED spell of Intensive training - not one
        // overloaded period, which RollOvertrainingInjury above already prices in as a real risk
        // per period - permanently raises injury-proneness a little, distinct from
        // SkillRegressionService's MATCH-load burnout axis. Resets the moment the load eases, so
        // this only ever bites a genuinely relentless programme.
        if (rollInjury)
        {
            player.ConsecutiveIntensiveTrainingPeriods = intensity == TrainingIntensity.Intensive
                ? player.ConsecutiveIntensiveTrainingPeriods + 1 : 0;
            if (player.ConsecutiveIntensiveTrainingPeriods >= 6 && player.Physical.InjuryProneness < 20)
            {
                player.Physical.InjuryProneness = Math.Min(20, player.Physical.InjuryProneness + 1);
                player.ConsecutiveIntensiveTrainingPeriods = 0;
            }
        }

        return new TrainingResult(player.Id, focus, delta, false, potentialRose, overtrainingInjury,
            Describe(player, focus, delta, potentialRose, overtrainingInjury));
    }

    /// <summary>
    /// Section 5: the underlying attribute shift for a role conversion happens through training
    /// (this method); the tactical reassignment itself and the confidence that comes from
    /// actually playing the new role are deliberately NOT this method's job - see the class doc
    /// comment on SituationalPerformanceModifier's own natural-vs-actual role gap, which already
    /// penalises a player fielded out of his DERIVED natural role and closes automatically once
    /// RoleTraitDeriver.ApplyTo() re-derives that role from the attributes this method has
    /// actually moved. Training supplies the technique; match practice (already modelled
    /// elsewhere) supplies the trust.
    /// </summary>
    private TrainingResult ApplyRoleConversion(Player player, PlayerTrainingPlan plan, double coachQuality, Random random, DateOnly asOf, double periodFraction = 1.0)
    {
        if (plan.TargetBattingRole is { } targetBatting && targetBatting != player.BattingRole)
        {
            double chance = GrowthChance(player, TrainingFocus.BattingTechnique, plan.Intensity, coachQuality, asOf) * periodFraction;
            if (random.NextDouble() < chance)
            {
                NudgeTowardBattingRole(player, targetBatting);
                _roles.ApplyTo(player);
                player.RecalculateFormatSuitability();

                bool converted = player.BattingRole == targetBatting;
                return new TrainingResult(player.Id, TrainingFocus.RoleConversion, 1, converted, false,
                    RollOvertrainingInjury(player, plan.Intensity, random, asOf, periodFraction),
                    converted
                        ? $"{player.FullName} has genuinely grown into a {targetBatting} - the training has taken."
                        : $"{player.FullName} is being reshaped toward {targetBatting}, and it is showing in the nets.");
            }
        }

        if (plan.TargetBowlingRole is { } targetBowling && targetBowling != player.BowlingRole)
        {
            double chance = GrowthChance(player, TrainingFocus.BowlingAccuracy, plan.Intensity, coachQuality, asOf) * periodFraction;
            if (random.NextDouble() < chance)
            {
                NudgeTowardBowlingRole(player, targetBowling);
                _roles.ApplyTo(player);
                player.RecalculateFormatSuitability();

                bool converted = player.BowlingRole == targetBowling;
                return new TrainingResult(player.Id, TrainingFocus.RoleConversion, 1, converted, false,
                    RollOvertrainingInjury(player, plan.Intensity, random, asOf, periodFraction),
                    converted
                        ? $"{player.FullName} has genuinely grown into a {targetBowling} - the training has taken."
                        : $"{player.FullName} is being reshaped toward {targetBowling}, and it is showing in the nets.");
            }
        }

        return new TrainingResult(player.Id, TrainingFocus.RoleConversion, 0, false, false, null,
            $"{player.FullName}'s role conversion made no visible progress this year.");
    }

    /// <summary>Nudges the attribute pairing RoleTraitDeriver.DeriveBattingRole actually reads, toward whichever profile the target role needs - early-role (technique/defence) or late-role (power/finishing).</summary>
    private static void NudgeTowardBattingRole(Player p, BattingRole target)
    {
        bool earlyRole = target is BattingRole.Opener or BattingRole.TopOrder;
        if (earlyRole)
        {
            p.Batting.Technique = Clamp20(p.Batting.Technique + 1);
            p.Batting.DefensiveAbility = Clamp20(p.Batting.DefensiveAbility + 1);
            p.Batting.AgainstPace = Clamp20(p.Batting.AgainstPace + 1);
            if (target == BattingRole.Opener) p.Batting.RiskManagement = Clamp20(p.Batting.RiskManagement + 1);
        }
        else
        {
            p.Batting.PowerHitting = Clamp20(p.Batting.PowerHitting + 1);
            p.Batting.DeathOverBatting = Clamp20(p.Batting.DeathOverBatting + 1);
            p.Batting.BoundaryHitting = Clamp20(p.Batting.BoundaryHitting + 1);
        }
    }

    private static void NudgeTowardBowlingRole(Player p, BowlingRoleType target)
    {
        switch (target)
        {
            case BowlingRoleType.DeathBowler:
                p.Bowling.DeathBowling = Clamp20(p.Bowling.DeathBowling + 1);
                p.Bowling.Yorker = Clamp20(p.Bowling.Yorker + 1);
                break;
            case BowlingRoleType.OpeningBowler:
                p.Bowling.NewBallBowling = Clamp20(p.Bowling.NewBallBowling + 1);
                p.Bowling.Swing = Clamp20(p.Bowling.Swing + 1);
                break;
            case BowlingRoleType.MiddleOversSpecialist:
                p.Bowling.MiddleOverBowling = Clamp20(p.Bowling.MiddleOverBowling + 1);
                p.Bowling.Containment = Clamp20(p.Bowling.Containment + 1);
                break;
            case BowlingRoleType.FirstChange:
                p.Bowling.Accuracy = Clamp20(p.Bowling.Accuracy + 1);
                break;
        }
    }

    // ---------------- growth mechanics ----------------

    /// <summary>
    /// Probability of this year's directed programme actually producing a real attribute point -
    /// same "probability, not a guaranteed tick" discipline PlayerAgeingService.Resolve already
    /// uses, so training is lumpy and two identical players on identical programmes diverge.
    /// </summary>
    private static double GrowthChance(Player player, TrainingFocus focus, TrainingIntensity intensity, double coachQuality, DateOnly asOf)
    {
        int age = player.Age(asOf);

        // Two kinds of headroom, both real: overall (has this player got a genuine ceiling still
        // to grow into) and cluster-specific (is THIS particular skill already near where his own
        // attributes otherwise sit). A player can be far from his overall potential and still have
        // a genuinely maxed-out death-bowling attribute relative to his other skills, or vice
        // versa - working on his weakest specific area is real and faster than working on
        // something already close to what he can do.
        double overallHeadroom = Math.Clamp((player.PotentialAbility - player.CurrentAbility) / 40.0, 0, 1);
        double clusterHeadroom = Math.Clamp(1 - ClusterAverage(player, focus) / 100.0, 0.1, 1);
        double headroom = overallHeadroom * 0.6 + clusterHeadroom * 0.4;

        // Real, but genuinely slower once headroom has closed - a directed programme cannot force
        // growth a player has already reached his ceiling for, only ageing's own decline applies then.
        double baseChance = 0.10 + headroom * 0.30;

        double ageFactor = age <= AcademyDevelopmentAge ? 1.7 : age <= PrimeDevelopmentAge ? 1.3 : age <= 30 ? 1.0 : age <= 34 ? 0.55 : 0.25;

        double intensityFactor = intensity switch { TrainingIntensity.Light => 0.55, TrainingIntensity.Intensive => 1.5, _ => 1.0 };

        double coachFactor = 0.6 + Math.Clamp(coachQuality, 0, 100) / 100.0 * 0.8; // 0.6x - 1.4x

        bool fastLearner = player.Personality.HasFlag(PersonalityTrait.FastLearner);
        bool slowDeveloper = player.Personality.HasFlag(PersonalityTrait.SlowDeveloper);
        bool professional = player.Personality.HasFlag(PersonalityTrait.Professional);
        bool lazy = player.Personality.HasFlag(PersonalityTrait.Lazy);
        double personalityFactor = (fastLearner ? 1.2 : 1.0) * (slowDeveloper ? 0.8 : 1.0)
                                   * (professional ? 1.1 : 1.0) * (lazy ? 0.75 : 1.0);

        return Math.Clamp(baseChance * ageFactor * intensityFactor * coachFactor * personalityFactor, 0.01, 0.85);
    }

    private static double ClusterAverage(Player player, TrainingFocus focus) =>
        AttributesFor(player, focus).DefaultIfEmpty(10).Average(v => AbilityScale.AttributeToHundred(v));

    private static IEnumerable<int> AttributesFor(Player p, TrainingFocus focus) => focus switch
    {
        TrainingFocus.BattingTechnique or TrainingFocus.AllroundBatting =>
            new[] { p.Batting.Technique, p.Batting.Timing, p.Batting.ShotSelection, p.Batting.DefensiveAbility },
        TrainingFocus.BattingPower => new[] { p.Batting.PowerHitting, p.Batting.BoundaryHitting, p.Batting.Aggression },
        TrainingFocus.BattingAgainstPace => new[] { p.Batting.AgainstPace, p.Batting.ShortBallAbility, p.Batting.SwingHandling, p.Batting.SeamHandling },
        TrainingFocus.BattingAgainstSpin => new[] { p.Batting.AgainstSpin, p.Batting.SpinHandling },
        TrainingFocus.BattingFinishing => new[] { p.Batting.DeathOverBatting, p.Batting.StrikeRotation, p.Batting.RiskManagement },
        TrainingFocus.BowlingPaceDevelopment or TrainingFocus.AllroundBowling => new[] { p.Bowling.Pace, p.Bowling.Bouncer },
        TrainingFocus.BowlingAccuracy => new[] { p.Bowling.Accuracy, p.Bowling.Containment },
        TrainingFocus.BowlingVariations => new[] { p.Bowling.Variation, p.Bowling.SlowerBall, p.Bowling.Yorker },
        TrainingFocus.BowlingSwingSeam => new[] { p.Bowling.Swing, p.Bowling.Seam, p.Bowling.NewBallBowling },
        TrainingFocus.BowlingSpinCraft => new[] { p.Bowling.Spin, p.Bowling.Variation },
        TrainingFocus.BowlingDeathCraft => new[] { p.Bowling.DeathBowling, p.Bowling.Yorker, p.Bowling.SlowerBall },
        TrainingFocus.Fielding => new[] { p.Fielding.Catching, p.Fielding.Reflexes, p.Fielding.Throwing, p.Fielding.GroundFielding, p.Fielding.Positioning, p.Fielding.BoundaryFielding },
        TrainingFocus.Physical => new[] { p.Physical.Fitness, p.Physical.Stamina, p.Physical.Strength, p.Physical.Speed, p.Physical.Recovery },
        TrainingFocus.Mental => new[] { p.Mental.Composure, p.Mental.Concentration, p.Mental.PressureHandling, p.Mental.DecisionMaking, p.Mental.GameAwareness, p.Mental.Adaptability },
        _ => Array.Empty<int>()
    };

    private static void ApplyToCluster(Player p, TrainingFocus focus, int delta)
    {
        if (delta == 0) return;
        switch (focus)
        {
            case TrainingFocus.BattingTechnique:
                p.Batting.Technique = Clamp20(p.Batting.Technique + delta);
                p.Batting.Timing = Clamp20(p.Batting.Timing + delta);
                p.Batting.ShotSelection = Clamp20(p.Batting.ShotSelection + delta);
                p.Batting.DefensiveAbility = Clamp20(p.Batting.DefensiveAbility + delta);
                break;
            case TrainingFocus.BattingPower:
                p.Batting.PowerHitting = Clamp20(p.Batting.PowerHitting + delta);
                p.Batting.BoundaryHitting = Clamp20(p.Batting.BoundaryHitting + delta);
                p.Batting.Aggression = Clamp20(p.Batting.Aggression + delta);
                break;
            case TrainingFocus.BattingAgainstPace:
                p.Batting.AgainstPace = Clamp20(p.Batting.AgainstPace + delta);
                p.Batting.ShortBallAbility = Clamp20(p.Batting.ShortBallAbility + delta);
                p.Batting.SwingHandling = Clamp20(p.Batting.SwingHandling + delta);
                p.Batting.SeamHandling = Clamp20(p.Batting.SeamHandling + delta);
                break;
            case TrainingFocus.BattingAgainstSpin:
                p.Batting.AgainstSpin = Clamp20(p.Batting.AgainstSpin + delta);
                p.Batting.SpinHandling = Clamp20(p.Batting.SpinHandling + delta);
                break;
            case TrainingFocus.BattingFinishing:
                p.Batting.DeathOverBatting = Clamp20(p.Batting.DeathOverBatting + delta);
                p.Batting.StrikeRotation = Clamp20(p.Batting.StrikeRotation + delta);
                p.Batting.RiskManagement = Clamp20(p.Batting.RiskManagement + delta);
                break;
            case TrainingFocus.BowlingPaceDevelopment:
                p.Bowling.Pace = Clamp20(p.Bowling.Pace + delta);
                p.Bowling.Bouncer = Clamp20(p.Bowling.Bouncer + delta);
                break;
            case TrainingFocus.BowlingAccuracy:
                p.Bowling.Accuracy = Clamp20(p.Bowling.Accuracy + delta);
                p.Bowling.Containment = Clamp20(p.Bowling.Containment + delta);
                break;
            case TrainingFocus.BowlingVariations:
                p.Bowling.Variation = Clamp20(p.Bowling.Variation + delta);
                p.Bowling.SlowerBall = Clamp20(p.Bowling.SlowerBall + delta);
                p.Bowling.Yorker = Clamp20(p.Bowling.Yorker + delta);
                break;
            case TrainingFocus.BowlingSwingSeam:
                p.Bowling.Swing = Clamp20(p.Bowling.Swing + delta);
                p.Bowling.Seam = Clamp20(p.Bowling.Seam + delta);
                p.Bowling.NewBallBowling = Clamp20(p.Bowling.NewBallBowling + delta);
                break;
            case TrainingFocus.BowlingSpinCraft:
                p.Bowling.Spin = Clamp20(p.Bowling.Spin + delta);
                p.Bowling.Variation = Clamp20(p.Bowling.Variation + delta);
                break;
            case TrainingFocus.BowlingDeathCraft:
                p.Bowling.DeathBowling = Clamp20(p.Bowling.DeathBowling + delta);
                p.Bowling.Yorker = Clamp20(p.Bowling.Yorker + delta);
                p.Bowling.SlowerBall = Clamp20(p.Bowling.SlowerBall + delta);
                break;
            case TrainingFocus.Fielding:
                p.Fielding.Catching = Clamp20(p.Fielding.Catching + delta);
                p.Fielding.Reflexes = Clamp20(p.Fielding.Reflexes + delta);
                p.Fielding.Throwing = Clamp20(p.Fielding.Throwing + delta);
                p.Fielding.GroundFielding = Clamp20(p.Fielding.GroundFielding + delta);
                p.Fielding.Positioning = Clamp20(p.Fielding.Positioning + delta);
                p.Fielding.BoundaryFielding = Clamp20(p.Fielding.BoundaryFielding + delta);
                break;
            case TrainingFocus.Physical:
                p.Physical.Fitness = Clamp20(p.Physical.Fitness + delta);
                p.Physical.Stamina = Clamp20(p.Physical.Stamina + delta);
                p.Physical.Strength = Clamp20(p.Physical.Strength + delta);
                p.Physical.Speed = Clamp20(p.Physical.Speed + delta);
                p.Physical.Recovery = Clamp20(p.Physical.Recovery + delta);
                break;
            case TrainingFocus.Mental:
                p.Mental.Composure = Clamp20(p.Mental.Composure + delta);
                p.Mental.Concentration = Clamp20(p.Mental.Concentration + delta);
                p.Mental.PressureHandling = Clamp20(p.Mental.PressureHandling + delta);
                p.Mental.DecisionMaking = Clamp20(p.Mental.DecisionMaking + delta);
                p.Mental.GameAwareness = Clamp20(p.Mental.GameAwareness + delta);
                p.Mental.Adaptability = Clamp20(p.Mental.Adaptability + delta);
                break;
        }
    }

    /// <summary>
    /// Section 8's realism ceiling: training can turn a part-timer into a genuinely useful
    /// second/fifth option, never a complete frontline specialist in the OTHER discipline. A
    /// player already carrying a genuine dual-discipline role (BattingAllrounder/
    /// BowlingAllrounder) is exempt - he is SUPPOSED to be elite in both, and is capped at the
    /// same ceiling every other attribute already respects (20).
    /// </summary>
    private const int PartTimerCeiling = 13;

    private static void ApplyAllroundCapped(Player p, TrainingFocus focus, int delta)
    {
        bool genuineAllrounder = p.PrimaryRole is PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder;
        int ceiling = genuineAllrounder ? 20 : PartTimerCeiling;

        if (focus == TrainingFocus.AllroundBatting)
        {
            p.Batting.Technique = CappedClamp(p.Batting.Technique + delta, ceiling);
            p.Batting.Timing = CappedClamp(p.Batting.Timing + delta, ceiling);
            p.Batting.ShotSelection = CappedClamp(p.Batting.ShotSelection + delta, ceiling);
            p.Batting.StrikeRotation = CappedClamp(p.Batting.StrikeRotation + delta, ceiling);
        }
        else // AllroundBowling
        {
            p.Bowling.Accuracy = CappedClamp(p.Bowling.Accuracy + delta, ceiling);
            p.Bowling.Containment = CappedClamp(p.Bowling.Containment + delta, ceiling);
            p.Bowling.Variation = CappedClamp(p.Bowling.Variation + delta, ceiling);
        }
    }

    private static int CappedClamp(int value, int ceiling) => Math.Clamp(value, 1, ceiling);
    private static int Clamp20(int value) => Math.Clamp(value, 1, 20);

    // ---------------- CurrentAbility / PotentialAbility (tech-debt item 5) ----------------

    private bool ApplyAbilityDelta(Player player, TrainingFocus focus, int delta, TrainingIntensity intensity, DateOnly asOf, Random random, double periodFraction = 1.0)
    {
        if (delta == 0) return false;

        double weight = focus switch
        {
            TrainingFocus.Physical => 2.5,
            TrainingFocus.Fielding => 1.0,
            TrainingFocus.Mental => 1.5,
            TrainingFocus.AllroundBatting or TrainingFocus.AllroundBowling => 1.0,
            _ when focus.ToString().StartsWith("Batting") || focus.ToString().StartsWith("Bowling") => 3.0,
            _ => 1.5
        };

        int abilityGain = (int)Math.Round(delta * weight);
        int uncapped = player.CurrentAbility + abilityGain;

        bool breakthrough = false;
        if (uncapped > player.PotentialAbility)
        {
            // The deliberate resolution: mostly hard-clamped, per the invariant this codebase
            // already documents. Only a young player, on an Intensive programme, gets a small,
            // rare chance for the ceiling itself to move - "he's turned out better than we
            // thought", a real and legitimate story, not a silent invariant breach.
            breakthrough = player.Age(asOf) <= PrimeDevelopmentAge
                           && intensity == TrainingIntensity.Intensive
                           && random.NextDouble() < 0.04 * periodFraction;

            if (breakthrough)
                player.PotentialAbility = Math.Min(200, player.PotentialAbility + random.Next(1, 4));
            else
                uncapped = player.PotentialAbility;
        }

        player.CurrentAbility = Math.Clamp(uncapped, 1, player.PotentialAbility);
        return breakthrough;
    }

    // ---------------- overtraining risk ----------------

    /// <summary>
    /// A real, if modest, injury-risk cost to an Intensive programme - overtraining injuries are
    /// genuine in professional cricket, especially fast bowlers carrying a heavy training load on
    /// top of match workload. Scaled by the player's own InjuryProneness, the same attribute
    /// MedicalEffectivenessService already reads for match-day injury risk, so a fragile player is
    /// fragile consistently rather than only during matches.
    ///
    /// Deliberately does NOT mutate the player itself, unlike everywhere else in this class -
    /// CurrentInjury/InjuryHistory are only ever meant to be set through WorldState.ApplyInjury
    /// (the sanctioned, indexed entry point - see that class's own doc comment on why an injury
    /// applied without indexing never resolves). Returning the constructed Injury as a plain
    /// carrier of type/severity/days for the caller to apply properly is what TrainingResult's own
    /// doc comment already promises.
    /// </summary>
    private static Injury? RollOvertrainingInjury(Player player, TrainingIntensity intensity, Random random, DateOnly asOf, double periodFraction = 1.0)
    {
        if (intensity != TrainingIntensity.Intensive) return null;
        if (player.CurrentInjury is not null) return null; // already carrying one - do not stack a second

        double proneness = AbilityScale.AttributeToHundred(player.Physical.InjuryProneness) / 100.0;
        bool fastBowler = player.BowlingRole is BowlingRoleType.OpeningBowler or BowlingRoleType.DeathBowler && player.Bowling.Pace >= 13;

        double chance = (0.02 + proneness * 0.05 + (fastBowler ? 0.03 : 0)) * periodFraction;
        if (random.NextDouble() >= chance) return null;

        var severity = random.NextDouble() < 0.75 ? InjurySeverity.Niggle : InjurySeverity.Minor;
        var (min, max) = Injury.TypicalLayoff(severity);
        int days = random.Next(min, max + 1);

        // The two realistic overtraining injury types: a general muscle strain from overload, or
        // (more likely for a fast bowler carrying real pace) a stress fracture from cumulative
        // impact - the classic real-cricket overuse injury for exactly that kind of workload.
        var type = fastBowler && random.NextDouble() < 0.4 ? InjuryType.StressFracture : InjuryType.MuscleStrain;
        return new Injury(type, severity, asOf, days, recurrenceRisk: 10);
    }

    private static string Describe(Player player, TrainingFocus focus, int delta, bool potentialRose, Injury? injury)
    {
        string core = delta > 0
            ? $"{player.FullName} has made real progress in {focus} this year."
            : $"{player.FullName} worked on {focus} without a breakthrough this year.";

        if (potentialRose) core += " He has turned out better than expected - his ceiling has genuinely risen.";
        if (injury is not null) core += $" The workload has cost him a {injury.Severity.ToString().ToLowerInvariant()} injury.";

        return core;
    }
}
