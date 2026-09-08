using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// The laws and playing conditions that constrain where fielders may stand.
///
/// These are not decoration - they are the reason limited-overs cricket has the shape it does.
/// The circle restrictions are why a powerplay is worth attacking, why the middle overs are a
/// squeeze, and why death bowling is hard. Law 28.4's leg-side cap is why a captain cannot
/// simply pack the leg side and bowl at the batter's body, which is exactly what it was written
/// after Bodyline to prevent.
/// </summary>
public sealed class FieldSettingRules
{
    /// <summary>Law 28.4: no more than two fielders behind square on the leg side at the moment of delivery.</summary>
    public const int MaxBehindSquareLegSide = 2;

    /// <summary>Law 28.4 also caps the leg side overall at five.</summary>
    public const int MaxLegSideFielders = 5;

    /// <summary>
    /// How many fielders may be outside the circle in this phase. Null means no restriction,
    /// which is the case in all first-class cricket - a Test captain may set whatever he likes.
    /// </summary>
    public int? MaxOutsideCircle(MatchFormat format, MatchPhase phase) => format switch
    {
        MatchFormat.T20 => phase switch
        {
            MatchPhase.Powerplay => 2,   // overs 1-6
            _ => 5                        // 7-20
        },
        MatchFormat.ODI => phase switch
        {
            MatchPhase.Powerplay => 2,   // overs 1-10
            MatchPhase.MiddleOvers => 4, // 11-40
            _ => 5                        // 41-50
        },
        _ => null                         // first-class: no fielding restrictions
    };

    public FieldLegalityResult Validate(FieldSetting field, MatchFormat format, MatchPhase phase)
    {
        var violations = new List<string>();

        if (field.Count != 11)
            violations.Add($"A fielding side has eleven players; this field places {field.Count}.");

        if (!field.HasKeeper)
            violations.Add("No wicketkeeper is placed.");

        if (field.Placements.Select(p => p.Position).Distinct().Count() != field.Count)
            violations.Add("Two fielders cannot occupy the same position.");

        if (field.Placements.Select(p => p.PlayerId).Distinct().Count() != field.Count)
            violations.Add("The same player cannot field in two positions.");

        if (field.BehindSquareLegSideFielders > MaxBehindSquareLegSide)
            violations.Add($"Law 28.4: no more than {MaxBehindSquareLegSide} fielders behind square on the leg side (found {field.BehindSquareLegSideFielders}).");

        if (field.LegSideFielders > MaxLegSideFielders)
            violations.Add($"Law 28.4: no more than {MaxLegSideFielders} fielders on the leg side (found {field.LegSideFielders}).");

        if (MaxOutsideCircle(format, phase) is { } maxOut && field.FieldersOutsideCircle > maxOut)
            violations.Add($"{phase} in {format} allows {maxOut} fielders outside the circle (found {field.FieldersOutsideCircle}).");

        return violations.Count == 0 ? FieldLegalityResult.Legal : new FieldLegalityResult(false, violations);
    }
}

/// <summary>
/// How suited a player is to a given fielding position, 0-100.
///
/// Specialism is real and it matters: a slip fielder needs reflexes and soft hands, a boundary
/// rider needs ground covered and an arm, a short leg needs nerve. Putting the wrong man in the
/// wrong place costs catches, and putting the right one there wins them - which is the entire
/// reason a captain thinks about who fields where rather than only about the shape of the field.
/// </summary>
public sealed class FieldingAptitudeService
{
    public double GetAptitude(Player player, FieldingPosition position)
    {
        var info = FieldingPositions.Info(position);
        var f = player.Fielding;
        double Scale(int a) => Common.AbilityScale.AttributeToHundred(a);

        double aptitude = info.SkillRequired switch
        {
            FieldingSkillType.Keeping => Scale(f.Catching) * 0.45 + Scale(f.Reflexes) * 0.40 + Scale(f.Positioning) * 0.15,
            FieldingSkillType.SlipCatching => Scale(f.Catching) * 0.45 + Scale(f.Reflexes) * 0.40 + Scale(f.Positioning) * 0.15,
            FieldingSkillType.CloseCatching => Scale(f.Reflexes) * 0.45 + Scale(f.Catching) * 0.35
                                               + Common.AbilityScale.AttributeToHundred(player.Mental.Composure) * 0.20, // nerve, standing that close
            FieldingSkillType.InnerRing => Scale(f.GroundFielding) * 0.40 + Scale(f.Throwing) * 0.30 + Scale(f.Positioning) * 0.30,
            _ => Scale(f.BoundaryFielding) * 0.40 + Scale(f.Throwing) * 0.25
                 + Scale(f.Catching) * 0.20 + Common.AbilityScale.AttributeToHundred(player.Physical.Speed) * 0.15
        };

        return Math.Clamp(aptitude, 0, 100);
    }

    /// <summary>Chance of holding a chance at this position - aptitude against the position's inherent difficulty. Never certain, and never hopeless.</summary>
    public double GetCatchProbability(Player fielder, FieldingPosition position)
    {
        var info = FieldingPositions.Info(position);
        double aptitude = GetAptitude(fielder, position) / 100.0;

        // A routine catch to a good fielder is taken most of the time; a hard chance to a poor
        // one usually is not. Neither end reaches certainty.
        //
        // Calibrated against real drop rates rather than intuition: professional sides hold
        // roughly 85-90% of the chances they get, and slip catches - the hardest routinely taken -
        // still go to hand far more often than not. An earlier curve was much harsher, which
        // suppressed caught dismissals below their real share of the dismissal mix.
        double baseChance = 1 - info.CatchDifficulty * 0.45;
        return Math.Clamp(baseChance * (0.68 + aptitude * 0.5), 0.25, 0.97);
    }

    /// <summary>The best available player for a position - what a captain does before the first ball.</summary>
    public Player? BestFor(IEnumerable<Player> available, FieldingPosition position) =>
        available.OrderByDescending(p => GetAptitude(p, position)).FirstOrDefault();
}

/// <summary>
/// Builds a legal, sensible field for the situation - the AI captain's field placement.
///
/// The shape follows what real captains actually do: attacking catchers with the new ball,
/// boundary protection once the batters are set and the restrictions lift, more protection
/// still at the death, and a spinner's field that differs from a seamer's. It then assigns the
/// best available specialist to each position rather than putting bodies in slots at random.
///
/// It always returns a LEGAL field - the rules class is the arbiter, and the builder respects
/// the circle limits for the phase rather than producing something an umpire would call.
/// </summary>
public sealed class AutoFieldSetter
{
    private readonly FieldingAptitudeService _aptitude = new();
    private readonly FieldSettingRules _rules = new();

    /// <summary>Minimum balls faced before a striker's strike rate is trusted as a genuine scoring-rate signal, rather than reacting to one fluky big first hit - same "don't trust a tiny sample" discipline GroundConditionsService already uses for historical par scores.</summary>
    public const int MinimumBallsForContainmentRead = 8;

    /// <param name="liveAttackZone">
    /// Section R: a zone the batter has genuinely attacked repeatedly THIS INNINGS
    /// (BatterMatchState.DominantAttackZone) - a live, actually-observed pattern, as opposed to
    /// batterZones' a-priori ability profile. When supplied it overrides that zone's static
    /// strength to the maximum before positions are chosen, guaranteeing a captain who has
    /// noticed the pattern commits real coverage there - closing the gap the survey flagged: field
    /// effects were "a static per-ball recalculation with no read of what has actually happened."
    /// Null (the default) leaves every existing caller's behaviour unchanged.
    /// </param>
    /// <param name="strikerBallsFaced">
    /// Section R's containment half: how many balls the striker has faced this innings, gating
    /// whether his strike rate below is trusted at all. 0 (the default) disables the containment
    /// read entirely, so every existing caller is unaffected.
    /// </param>
    /// <param name="strikerStrikeRate">
    /// The striker's live runs-per-hundred-balls this innings. Compared against a format/phase
    /// baseline - when he is genuinely dominating it (not just attacking one zone, but scoring
    /// fast full stop), the field automatically pulls toward containment: fewer slips, more
    /// boundary riders, protecting the rope rather than hunting a catch. This is the "tie the
    /// scoring rate down" ask - efficient in limited overs, and just as real in multi-day cricket
    /// when a batter is taking the bowling apart, since strike rate (not required run rate, which
    /// only exists when chasing) is the one signal both formats share.
    /// </param>
    /// <param name="strikerHand">
    /// §2.2: the batter's hand, when the captain is reading the game (null = no read, every
    /// pre-existing caller unaffected). Off-spin/left-arm-orthodox turns AWAY from a left-hander
    /// toward the slips; leg-spin/left-arm-chinaman does the mirror to a right-hander - real,
    /// well-known cricket geometry <see cref="DeliveryEffectService.ApplySpinDirection"/> already
    /// uses for the dismissal MIX. This is the field-setting side of the same fact: a captain who
    /// reads it packs the cordon a slip heavier.
    /// </param>
    public FieldSetting BuildField(
        IReadOnlyList<Player> fieldingSide,
        Player bowler,
        MatchFormat format,
        MatchPhase phase,
        bool newBall = false,
        BattingZoneStrengths? batterZones = null,
        FieldAggression? coachAggression = null,
        ShotZone? liveAttackZone = null,
        int strikerBallsFaced = 0,
        double strikerStrikeRate = 0,
        BattingHand? strikerHand = null)
    {
        int maxOutside = _rules.MaxOutsideCircle(format, phase) ?? 6;
        bool spinner = bowler.Bowling.Spin > bowler.Bowling.Pace;

        var effectiveZones = liveAttackZone is { } trap && batterZones is not null
            ? WithZoneBoosted(batterZones, trap)
            : batterZones;

        double? scoringThreatRatio = strikerBallsFaced >= MinimumBallsForContainmentRead
            ? strikerStrikeRate / BaselineStrikeRate(format, phase)
            : null;

        bool turnsAwayFromBat = spinner && strikerHand is { } hand && TurnsAwayFromBat(bowler.BowlingStyle, hand);

        var positions = ChoosePositions(format, phase, newBall, spinner, maxOutside, effectiveZones, coachAggression, scoringThreatRatio, turnsAwayFromBat);
        return AssignPlayers(positions, fieldingSide, bowler);
    }

    /// <summary>
    /// §2.2: does this spinner's turn take the ball AWAY from this batter, toward the edge? Off-
    /// spin/left-arm-orthodox against a left-hander, or leg-spin/left-arm-chinaman against a
    /// right-hander - the mirror pairing DeliveryEffectService.ApplySpinDirection already scores
    /// as a real outside-edge threat.
    /// </summary>
    private static bool TurnsAwayFromBat(BowlingStyle style, BattingHand hand)
    {
        bool offSpinFamily = style is BowlingStyle.RightArmOffSpin or BowlingStyle.LeftArmOrthodox;
        bool legSpinFamily = style is BowlingStyle.RightArmLegSpin or BowlingStyle.LeftArmChinaman;
        return (offSpinFamily && hand == BattingHand.Left) || (legSpinFamily && hand == BattingHand.Right);
    }

    /// <summary>
    /// What a competent batter's strike rate looks like here - format and phase both matter, since
    /// a death-overs 165 is unremarkable and the same number in the powerplay is exceptional. Test
    /// and first-class cricket has no such phase brackets in the same sense, so one flat baseline
    /// covers the whole innings there.
    /// </summary>
    private static double BaselineStrikeRate(MatchFormat format, MatchPhase phase) => format switch
    {
        MatchFormat.T20 => phase switch { MatchPhase.Powerplay => 135, MatchPhase.DeathOvers => 165, _ => 122 },
        MatchFormat.ODI => phase switch { MatchPhase.Powerplay => 95, MatchPhase.DeathOvers => 128, _ => 82 },
        _ => 52
    };

    /// <summary>A copy of a batter's zone profile with one zone forced to maximum - the "trap has sprung" read, guaranteed to sort first when AutoFieldSetter prioritises where to place its boundary riders.</summary>
    private static BattingZoneStrengths WithZoneBoosted(BattingZoneStrengths baseZones, ShotZone zone)
    {
        var boosted = new BattingZoneStrengths();
        foreach (var z in Enum.GetValues<ShotZone>()) boosted[z] = baseZones[z];
        boosted[zone] = 100;
        return boosted;
    }

    /// <summary>
    /// Picks the SHAPE of the field. Catchers when the ball is new and the batters are not set;
    /// sweepers and deep riders when runs are the threat. Where a boundary rider is placed is
    /// influenced by the batter's strong zone, because closing off a batter's best area is the
    /// most basic thing a captain does.
    /// </summary>
    private List<FieldingPosition> ChoosePositions(
        MatchFormat format, MatchPhase phase, bool newBall, bool spinner, int maxOutside,
        BattingZoneStrengths? batterZones, FieldAggression? coachAggression = null, double? scoringThreatRatio = null,
        bool turnsAwayFromBat = false)
    {
        var field = new List<FieldingPosition> { FieldingPosition.WicketKeeper, FieldingPosition.Bowler };

        // Attacking catchers. A red-ball new-ball field is heavy on slips; a T20 powerplay has
        // one at most, because a single boundary costs more than a half-chance is worth.
        int slips = format switch
        {
            MatchFormat.Test => newBall ? 3 : phase == MatchPhase.Powerplay ? 2 : 1,
            MatchFormat.ODI => newBall ? 2 : 0,
            _ => newBall ? 1 : 0
        };
        if (spinner) slips = Math.Min(slips, 1);
        // §2.2: a spinner turning AWAY from this batter's edge (off-spin/left-arm-orthodox to a
        // left-hander, leg-spin/left-arm-chinaman to a right-hander) is a genuine outside-edge
        // threat a captain reads and packs for - a real slip where the plain spin cap above would
        // otherwise leave none at all (a T20 middle over with no new-ball slip, say).
        if (spinner && turnsAwayFromBat) slips = Math.Max(slips, 1);

        // The coach's instruction. Attacking buys a wicket by bringing catchers in and leaving the
        // rope open; defensive does the reverse. It is a genuine trade-off and the coach is allowed
        // to get it wrong - an attacking field in the last over of a chase will leak the winning runs.
        if (coachAggression == ValueObjects.FieldAggression.Attacking) slips += 1;
        else if (coachAggression == ValueObjects.FieldAggression.Defensive) slips = Math.Max(0, slips - 1);

        // Section R's containment half: a batter genuinely dominating the strike rate pulls a slip
        // away too - a captain trying to dry up the scoring does not simultaneously leave the rope
        // open by keeping a man in the cordon he could use in the deep. Additive with, not a
        // replacement for, the coach's own instruction above.
        if (scoringThreatRatio is { } threat1 && threat1 > 1.25) slips = Math.Max(0, slips - 1);

        var slipOrder = new[] { FieldingPosition.Slip1, FieldingPosition.Slip2, FieldingPosition.Slip3 };
        for (int i = 0; i < slips && i < slipOrder.Length; i++) field.Add(slipOrder[i]);

        if (format == MatchFormat.Test && newBall && !spinner) field.Add(FieldingPosition.Gully);

        // Close catchers for spin in red-ball cricket - the classic bat-pad trap.
        if (spinner && format == MatchFormat.Test && field.Count < 9)
        {
            field.Add(FieldingPosition.ShortLeg);
            if (field.Count < 9) field.Add(FieldingPosition.SillyPoint);
        }

        // Boundary riders, capped by the phase's circle restriction. Priority goes to the
        // batter's strongest zone - a captain protects where the runs are actually coming from.
        var deepByZone = new Dictionary<ShotZone, FieldingPosition>
        {
            [ShotZone.ThirdMan] = FieldingPosition.DeepThirdMan,
            [ShotZone.Point] = FieldingPosition.DeepPoint,
            [ShotZone.Cover] = FieldingPosition.DeepCover,
            [ShotZone.MidOff] = FieldingPosition.LongOff,
            [ShotZone.MidOn] = FieldingPosition.LongOn,
            [ShotZone.MidWicket] = FieldingPosition.DeepMidWicket,
            [ShotZone.SquareLeg] = FieldingPosition.DeepSquareLeg,
            [ShotZone.FineLeg] = FieldingPosition.DeepFineLeg
        };

        var zonePriority = batterZones is null
            ? new[] { ShotZone.MidWicket, ShotZone.Cover, ShotZone.MidOn, ShotZone.Point, ShotZone.FineLeg, ShotZone.ThirdMan, ShotZone.MidOff, ShotZone.SquareLeg }
            : Enum.GetValues<ShotZone>().OrderByDescending(z => batterZones[z]).ToArray();

        int deepWanted = format == MatchFormat.Test
            ? (newBall ? 0 : 2)
            : phase switch { MatchPhase.Powerplay => 2, MatchPhase.MiddleOvers => 4, _ => 5 };

        deepWanted += coachAggression switch
        {
            ValueObjects.FieldAggression.Attacking => -2,
            ValueObjects.FieldAggression.Defensive => 2,
            _ => 0
        };

        // The automatic containment response: the further above baseline his strike rate runs, the
        // more the field commits to protecting the boundary - up to three extra riders when he is
        // genuinely running away with it. There is no free field: this costs the same catching
        // presence the manual Defensive setting already costs, additive with it rather than a
        // separate mechanism.
        if (scoringThreatRatio is { } threat2 && threat2 > 1.25)
            deepWanted += (int)Math.Round(Math.Clamp((threat2 - 1.25) * 5, 0, 3));

        // The laws still win. A coach may ask for a defensive field, but he cannot have more men
        // out than the phase allows, and asking is not the same as being granted.
        deepWanted = Math.Clamp(Math.Min(deepWanted, maxOutside), 0, maxOutside);

        foreach (var zone in zonePriority)
        {
            if (field.Count(p => !FieldingPositions.Info(p).InsideCircle) >= deepWanted) break;
            if (field.Count >= 11) break;

            var candidate = deepByZone[zone];
            if (field.Contains(candidate)) continue;

            // Respect Law 28.4 while building rather than producing an illegal field and fixing
            // it afterwards - a captain simply cannot place that man there.
            if (WouldBreachLegSideLaws(field, candidate)) continue;

            field.Add(candidate);
        }

        // Fill the ring with the standard saving positions.
        var ringOrder = new[]
        {
            FieldingPosition.MidOff, FieldingPosition.MidOn, FieldingPosition.Cover,
            FieldingPosition.MidWicket, FieldingPosition.Point, FieldingPosition.SquareLeg,
            FieldingPosition.ExtraCover, FieldingPosition.FineLeg, FieldingPosition.ThirdMan,
            FieldingPosition.BackwardPoint, FieldingPosition.CoverPoint
        };

        foreach (var position in ringOrder)
        {
            if (field.Count >= 11) break;
            if (field.Contains(position)) continue;
            if (FieldingPositions.InZone(FieldingPositions.Info(position).Zone).Any(i => !i.InsideCircle && field.Contains(i.Position))
                && FieldingPositions.Info(position).Zone is ShotZone.MidOff or ShotZone.MidOn && field.Count < 9)
            {
                // A zone with a boundary rider still usually keeps a ring fielder - fall through.
            }
            if (WouldBreachLegSideLaws(field, position)) continue;
            field.Add(position);
        }

        return field.Take(11).ToList();
    }

    private static bool WouldBreachLegSideLaws(List<FieldingPosition> field, FieldingPosition candidate)
    {
        var info = FieldingPositions.Info(candidate);
        if (!info.LegSide) return false;

        int legSide = field.Count(p => FieldingPositions.Info(p).LegSide);
        int behindSquareLeg = field.Count(p => FieldingPositions.Info(p) is { LegSide: true, BehindSquare: true });

        if (legSide + 1 > FieldSettingRules.MaxLegSideFielders) return true;
        if (info.BehindSquare && behindSquareLeg + 1 > FieldSettingRules.MaxBehindSquareLegSide) return true;

        return false;
    }

    /// <summary>
    /// Puts the right man in the right place. Positions are filled in order of how much
    /// specialism they demand - the keeper and the slips first, because a dropped slip catch
    /// costs far more than a slightly slow man at mid-on.
    /// </summary>
    private FieldSetting AssignPlayers(List<FieldingPosition> positions, IReadOnlyList<Player> fieldingSide, Player bowler)
    {
        var setting = new FieldSetting();
        var remaining = fieldingSide.Where(p => p.Id != bowler.Id).ToList();

        // The bowler fields at his own position, obviously.
        if (positions.Contains(FieldingPosition.Bowler))
            setting.Placements.Add(new FieldPlacement(FieldingPosition.Bowler, bowler.Id));

        // The keeper is a DESIGNATED specialist, not simply whoever has the best hands. Selecting
        // him purely on catching aptitude handed the gloves to the best slip fielder in the side -
        // which then left the actual cordon weaker, and is not how a team is picked: a side names
        // its keeper and everyone else fields around him.
        if (positions.Contains(FieldingPosition.WicketKeeper))
        {
            var keeper = remaining.FirstOrDefault(p => p.PrimaryRole == PlayerRole.WicketKeeper)
                         ?? _aptitude.BestFor(remaining, FieldingPosition.WicketKeeper)!;
            setting.Placements.Add(new FieldPlacement(FieldingPosition.WicketKeeper, keeper.Id));
            remaining.Remove(keeper);
        }

        var priority = positions
            .Where(p => p is not (FieldingPosition.Bowler or FieldingPosition.WicketKeeper))
            .OrderBy(p => FieldingPositions.Info(p).SkillRequired switch
            {
                FieldingSkillType.Keeping => 0,
                FieldingSkillType.SlipCatching => 1,
                FieldingSkillType.CloseCatching => 2,
                FieldingSkillType.BoundaryRiding => 3,
                _ => 4
            });

        foreach (var position in priority)
        {
            if (remaining.Count == 0) break;
            var best = _aptitude.BestFor(remaining, position)!;
            setting.Placements.Add(new FieldPlacement(position, best.Id));
            remaining.Remove(best);
        }

        return setting;
    }
}
