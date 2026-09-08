using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using System.Linq;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// How a batter is going RIGHT NOW - the difference between a man who has just walked in and one
/// who, as the phrase goes, is seeing it like a football.
///
/// Three things, deliberately separate, because they move independently:
///
/// - **Set-ness** is about the eyes and the feet. It builds with balls faced and is the single
///   biggest in-innings variable there is. But it is NOT invincibility: a set batter is much
///   harder to dismiss, not impossible, and he can still be bowled by a part-timer. Cricket has a
///   phrase for that too - some deliveries have a batter's name on them.
/// - **Confidence** is about the head, and it moves on outcomes: boundaries and surviving chances
///   lift it, playing and missing and being tied down grind it away. This is why a batter can make
///   a hundred on a difficult pitch and never once feel set.
/// - **Fatigue** is about the body. Running between the wickets is real work, and a long innings
///   in the heat costs a batter his sharpness. Good fitness delays that a long way; poor fitness
///   means a man is a different player after ninety minutes.
///
/// Set-ness partly survives a break and partly does not, which matches how players describe it:
/// after a session interval you have to get your eye in again, but nothing like as much as at the
/// start of an innings.
/// </summary>
public sealed class BatterMatchState
{
    public Guid PlayerId { get; init; }

    public int BallsFaced { get; private set; }
    public int RunsScored { get; private set; }

    /// <summary>0-100. Playing himself in, then in, then completely set.</summary>
    public double Setness { get; private set; }

    /// <summary>0-100, starting at 50. Moves on what actually happens to him.</summary>
    public double Confidence { get; private set; } = 50;

    /// <summary>0-100 accumulated tiredness. Driven by balls faced and running, mitigated by fitness.</summary>
    public double Fatigue { get; private set; }

    /// <summary>Times he has been dropped or otherwise survived a chance. A batter given a life very often cashes it in - and it lifts him.</summary>
    public int LivesGiven { get; private set; }

    /// <summary>Consecutive dot balls faced. Being tied down is its own pressure, separate from the match situation.</summary>
    public int ConsecutiveDots { get; private set; }

    /// <summary>
    /// Section R (fielding bait-and-trap): the zones this batter has actually hit a FOUR to, most
    /// recent last, capped to a short rolling window (sixes deliberately do not carry a resolved
    /// zone - see DeliveryOutcome.Zone). This is a LIVE signal - what has actually happened this
    /// innings - deliberately separate from BattingZoneStrengths, which is the batter's a-priori
    /// ability profile and does not move. AutoFieldSetter reads DominantAttackZone to react to a
    /// pattern that has actually emerged rather than only ever reading a static rating.
    /// </summary>
    public List<ShotZone> RecentAttackZones { get; } = new();

    /// <summary>Records that a four went to this zone. A leaked single says much less about where a batter is deliberately attacking.</summary>
    public void RecordZoneAttack(ShotZone zone)
    {
        RecentAttackZones.Add(zone);
        if (RecentAttackZones.Count > 6) RecentAttackZones.RemoveAt(0);
    }

    /// <summary>
    /// The zone he has attacked often enough, this innings, that a captain watching would genuinely
    /// notice - three or more of his last six fours. Null when there is no established pattern yet,
    /// which is most of an innings: a single big shot into a gap is not a trend.
    /// </summary>
    public ShotZone? DominantAttackZone
    {
        get
        {
            if (RecentAttackZones.Count < 3) return null;
            var best = RecentAttackZones.GroupBy(z => z).OrderByDescending(g => g.Count()).First();
            return best.Count() >= 3 ? best.Key : null;
        }
    }

    /// <summary>
    /// Issue 11: "the batsman's eye gets set on that bowler... this should genuinely depend on how
    /// the batsman is actually playing that bowler, not build up regardless of outcome." A batter's
    /// own read on a SPECIFIC bowler within this match, 0-100 starting at a neutral 30 - deliberately
    /// separate from MatchupConfidenceService (career-level, cross-match history) and from the
    /// general Confidence/ConsecutiveDots above (whole-innings, bowler-agnostic). Rises only when he
    /// is genuinely coping - survives without being beaten, or scores - and falls when he is not,
    /// which is exactly the outcome-gating the brief asks for: a bowler who keeps beating the bat
    /// must not somehow look easier for having bowled a long spell.
    /// </summary>
    public Dictionary<Guid, double> BowlerFamiliarity { get; } = new();

    /// <param name="mysterious">
    /// A genuine mystery spinner (BallOutcomeModel.IsMysterySpinner) is deliberately harder and
    /// slower to build real familiarity against - the entire point of a wide, well-disguised
    /// repertoire is that facing more of it does not teach a batter as much as facing an honest
    /// bowler's would. This is what makes a longer format a real advantage against one WITHOUT any
    /// format-specific code: a Test innings simply gives a batter far more balls to chip away at a
    /// slower rate than a T20 innings does.
    /// </param>
    public void RecordBowlerFamiliarity(Guid bowlerId, int runsOffBat, bool beatenOrThreatened, bool mysterious = false)
    {
        double current = BowlerFamiliarity.TryGetValue(bowlerId, out var v) ? v : 30;

        double delta = beatenOrThreatened ? -3.5
            : runsOffBat >= 4 ? 2.5
            : runsOffBat > 0 ? 0.8
            : 0.3; // survived a straight dot - a small, genuine gain, not nothing

        if (mysterious) delta *= delta > 0 ? 0.45 : 1.15; // slower to earn ground, a little quicker to lose it

        BowlerFamiliarity[bowlerId] = Math.Clamp(current + delta, 0, 100);
    }

    /// <summary>
    /// How much easier this specific bowler now looks, as a multiplier on the batter's effective
    /// skill - modest on purpose (this is "got his eye in over a spell", not a transformation).
    /// Neutral at the starting 30, rising a little above 1.0 as real familiarity builds, and
    /// genuinely below 1.0 for a batter who has been visibly struggling against this bowler.
    /// </summary>
    /// <param name="mysterious">Widens the low end - genuine confusion against an unread mystery bowler is a bigger real effect than merely being tied down by an honest one.</param>
    public double GetBowlerFamiliarityMultiplier(Guid bowlerId, bool mysterious = false)
    {
        double familiarity = BowlerFamiliarity.TryGetValue(bowlerId, out var v) ? v : 30;
        double floor = mysterious ? 0.85 : 0.94;
        return Math.Clamp(floor + familiarity / 100.0 * (1.08 - floor), floor, 1.08);
    }

    /// <summary>
    /// How quickly this batter settles, in balls. Concentration and technique shorten it; a
    /// difficult pitch lengthens it a long way, which is what stops a good batter ever feeling in.
    /// </summary>
    public static int BallsToSettle(Player player, double pitchDifficulty)
    {
        double concentration = AbilityScale.AttributeToHundred(player.Mental.Concentration);
        double technique = AbilityScale.AttributeToHundred(player.Batting.Technique);

        double baseline = 26 - (concentration * 0.6 + technique * 0.4) / 100.0 * 13; // 13-26 balls

        // Difficulty is centred on 50, so a NEUTRAL pitch leaves the baseline alone. Scaling
        // straight off the raw value made every ordinary surface behave like a difficult one and
        // pushed wickets per innings well above anything real.
        double surface = 1 + (Math.Clamp(pitchDifficulty, 0, 100) - 50) / 100.0 * 1.2; // 0.4x - 1.6x
        return (int)Math.Round(Math.Clamp(baseline * surface, 8, 70));
    }

    public void RecordBall(Player player, int runsOffBat, bool boundary, bool playedAndMissed, double pitchDifficulty, MatchFormat format,
        MatchWeather? weather = null)
    {
        BallsFaced++;
        RunsScored += runsOffBat;

        // --- set-ness ---
        int ballsToSettle = BallsToSettle(player, pitchDifficulty);
        double gain = 100.0 / ballsToSettle;

        // Scoring shots settle a batter faster than surviving does - middling one out of the screws
        // tells you more about your timing than blocking six.
        // These are centred so the AVERAGE ball contributes roughly the nominal gain - otherwise
        // "balls to settle" is a lie and a batter is fully set in half the stated time.
        if (boundary) gain *= 1.6;
        else if (runsOffBat > 0) gain *= 1.05;
        else if (playedAndMissed) gain *= 0.2;  // beaten - that does not settle anybody
        else gain *= 0.85;                       // a dot settles you a little, not much

        Setness = Math.Clamp(Setness + gain, 0, 100);

        // --- confidence ---
        if (boundary) Confidence += 3.0;
        else if (runsOffBat > 0) Confidence += 0.7;
        else if (playedAndMissed) Confidence -= 2.2;
        else Confidence -= 0.35;

        ConsecutiveDots = runsOffBat == 0 ? ConsecutiveDots + 1 : 0;
        if (ConsecutiveDots >= 6) Confidence -= 1.5; // tied down

        Confidence = Math.Clamp(Confidence, 0, 100);

        // --- fatigue ---
        // Running is the work. A fit batter barely notices; an unfit one is a different player
        // after an hour, and in a long format it decides how long a big innings can last.
        double fitness = AbilityScale.AttributeToHundred(player.Physical.Stamina) * 0.6
                         + AbilityScale.AttributeToHundred(player.Physical.Fitness) * 0.4;

        double perBall = format == MatchFormat.Test ? 0.14 : 0.20;
        double runningCost = runsOffBat * 0.09;
        double resistance = 0.35 + (100 - fitness) / 100.0 * 1.3;

        // The weather. A humid 34-degree afternoon empties a batter running twos; the same innings
        // under cloud in fourteen degrees costs him a fraction of it.
        double conditions = weather?.FatigueMultiplier ?? 1.0;

        Fatigue = Math.Clamp(Fatigue + (perBall + runningCost) * resistance * conditions, 0, 100);
    }

    /// <summary>
    /// Recovery between deliveries and overs - the walk back, the drinks, the time at the
    /// non-striker's end. Small per over, but it is why a fit batter can bat all day and it means
    /// fatigue is not a one-way ratchet that only unwinds at intervals.
    /// </summary>
    public void RecoverBetweenOvers(Player player, MatchWeather? weather = null)
    {
        double fitness = AbilityScale.AttributeToHundred(player.Physical.Fitness) * 0.5
                         + AbilityScale.AttributeToHundred(player.Physical.Recovery) * 0.5;

        double recovery = (0.15 + fitness / 100.0 * 0.35) * (weather?.RecoveryMultiplier ?? 1.0);
        Fatigue = Math.Clamp(Fatigue - recovery, 0, 100);
    }

    /// <summary>A dropped catch or other let-off. The batter knows, and it lifts him - which is why sides talk about catches winning matches.</summary>
    public void RecordLife()
    {
        LivesGiven++;
        Confidence = Math.Clamp(Confidence + 6, 0, 100);
    }

    /// <summary>
    /// A session or drinks break. The body recovers a good deal; the eye does not fully survive it.
    /// This is exactly what players describe in long-format cricket - you have to start again after
    /// an interval, but nothing like from scratch.
    /// </summary>
    public void TakeBreak(BreakLength length)
    {
        var (setnessKept, fatigueCleared) = length switch
        {
            BreakLength.Drinks => (0.92, 0.25),
            BreakLength.Session => (0.78, 0.55),
            BreakLength.Innings => (0.60, 0.85),   // overnight or between innings
            _ => (0.45, 1.0)                        // a full day's break
        };

        Setness *= setnessKept;
        Fatigue *= 1 - fatigueCleared;
    }

    /// <summary>
    /// The multiplier on this batter's effective skill. Set-ness dominates, confidence and fatigue
    /// modify. The ceiling is deliberately finite: a fully set, confident, fresh batter is a much
    /// better player than he was on arrival, and still not an unbeatable one.
    /// </summary>
    public double GetEffectivenessMultiplier()
    {
        double set = 0.80 + Setness / 100.0 * 0.32;               // 0.80x -> 1.12x
        double confidence = 0.94 + Confidence / 100.0 * 0.12;      // 0.94x -> 1.06x
        double fatigue = 1 - Fatigue / 100.0 * 0.18;               // down to 0.82x
        return Math.Clamp(set * confidence * fatigue, 0.7, 1.20);
    }

    /// <summary>
    /// The multiplier on his chance of being dismissed. A set batter is far harder to get out - but
    /// never safe, which is why the floor sits well above zero. Fatigue brings the loose shot back.
    /// </summary>
    public double GetDismissalMultiplier()
    {
        double set = 1.45 - Setness / 100.0 * 0.60;                // 1.45x on arrival -> 0.85x set
        double confidence = 1.08 - Confidence / 100.0 * 0.14;
        double fatigue = 1 + Fatigue / 100.0 * 0.28;               // tired batters get out
        return Math.Clamp(set * confidence * fatigue, 0.72, 1.75);
    }

    public bool IsSet => Setness >= 70;
}

/// <summary>How long a break in play is. Longer breaks clear more fatigue but cost more of a batter's eye.</summary>
public enum BreakLength
{
    Drinks,
    Session,
    Innings,
    Day
}

/// <summary>
/// Post-Phase-5 rectification pass, Wave 6 (point 13): in-match momentum - a genuinely new
/// simulation-level mechanic, distinct from a batter's set-ness (one player, one innings) and
/// from team-form momentum (Team.FormMomentum, across matches).
///
/// Value is from the BATTING side's perspective: -100 (bowling side utterly on top, a collapse
/// in progress) .. +100 (batting side flying, the bowlers have nothing). It shifts ball by ball,
/// decays toward neutral every over because momentum is fleeting, swings on a big or a wicket-
/// laden over, and on a BREAK is personality-modulated: a composed side holds what it has built,
/// a fragile one hands it straight back. Works within a Test innings across session breaks, not
/// only in white-ball cricket.
///
/// The feedback into ball outcomes is deliberately SMALL and GATED (only a genuine swing does
/// anything at all), secondary to set-ness / form / morale - the planning brief's own warning
/// about a mechanic that makes a side unbeatable applies here as much as to a winning streak.
/// </summary>
public sealed class MatchMomentum
{
    [System.Text.Json.Serialization.JsonInclude]
    public double Value { get; private set; }

    /// <summary>Only a genuine swing past this magnitude feeds back into ball outcomes at all.</summary>
    public const double EffectGate = 25;

    public void Boundary(bool six) => Shift(six ? 9.0 : 5.5);
    public void Wicket() => Shift(-16.0);
    public void Dot() => Shift(-0.9);
    public void ScoringShot(int runs) => Shift(0.3 * Math.Clamp(runs, 1, 3));

    /// <summary>End of an over: momentum decays toward neutral, and a very expensive or wicket-laden over is itself a swing.</summary>
    public void EndOver(int runsInOver, int wicketsInOver)
    {
        Value *= 0.82;
        if (wicketsInOver >= 2) Shift(-11);
        else if (runsInOver >= 15) Shift(10);
        else if (runsInOver <= 1 && wicketsInOver == 0) Shift(-3);
    }

    /// <summary>
    /// A session / drinks / innings break. Composure is each side's key men, 0-1. A composed
    /// batting side keeps most of what it built; a fragile one, or a break that lets the fielding
    /// side regroup, hands it back - and a big swing can revert past neutral.
    /// </summary>
    public void OnBreak(double battingSideComposure, double bowlingSideComposure, BreakLength length)
    {
        double lengthKeep = length switch { BreakLength.Drinks => 0.9, BreakLength.Session => 0.68, BreakLength.Innings => 0.4, _ => 0.25 };
        double net = Math.Clamp(battingSideComposure - bowlingSideComposure, -1, 1); // + = batting side steadier
        double keep = Math.Clamp(lengthKeep + net * 0.25, 0.1, 1.0);
        Value *= keep;
    }

    private void Shift(double d) => Value = Math.Clamp(Value + d, -100, 100);

    /// <summary>Multiplier on the batting side's effective skill - 1.0 unless there is a genuine swing, then a small lift/drag.</summary>
    public double BattingMultiplier() => BattingMultiplierFor(Value);

    /// <summary>Multiplier on the bowling side's effective skill - the mirror.</summary>
    public double BowlingMultiplier() => BowlingMultiplierFor(Value);

    /// <summary>The batting-side multiplier for a given momentum value - gated at EffectGate, capped small.</summary>
    public static double BattingMultiplierFor(double value) =>
        Math.Abs(value) < EffectGate ? 1.0 : Math.Clamp(1.0 + value / 100.0 * 0.04, 0.96, 1.04);

    /// <summary>The bowling-side multiplier - the mirror.</summary>
    public static double BowlingMultiplierFor(double value) =>
        Math.Abs(value) < EffectGate ? 1.0 : Math.Clamp(1.0 - value / 100.0 * 0.04, 0.96, 1.04);
}

/// <summary>
/// How a bowler is going. The mirror of the batter's state, and just as real: a bowler who has
/// found his rhythm and his spot is a different proposition from one who has just been thrown the
/// ball, and a tiring bowler in his fourth over of a spell is another again.
/// </summary>
public sealed class BowlerMatchState
{
    public Guid PlayerId { get; init; }

    public int BallsBowledInSpell { get; private set; }
    public int BallsBowledInMatch { get; private set; }

    /// <summary>0-100. Landing it where he wants, repeatably. Builds through a spell and is lost when he is taken off.</summary>
    public double Rhythm { get; private set; } = 25;

    /// <summary>0-100, starting at 50. Wickets lift it, being carted grinds it down.</summary>
    public double Confidence { get; private set; } = 50;

    public double Fatigue { get; private set; }

    public void RecordBall(Player bowler, int runsConceded, bool wicket, bool beatTheBat, MatchFormat format,
        MatchWeather? weather = null)
    {
        BallsBowledInSpell++;
        BallsBowledInMatch++;

        // --- rhythm ---
        // A bowler finds his groove over the first couple of overs. Concentration and accuracy make
        // it come faster and hold better.
        double control = AbilityScale.AttributeToHundred(bowler.Bowling.Accuracy) * 0.6
                         + AbilityScale.AttributeToHundred(bowler.Mental.Concentration) * 0.4;

        double gain = (100 - Rhythm) * (0.02 + control / 100.0 * 0.03);
        if (runsConceded >= 4) gain -= 4;      // being hit disrupts it
        if (beatTheBat) gain += 2;
        Rhythm = Math.Clamp(Rhythm + gain, 0, 100);

        // --- confidence ---
        if (wicket) Confidence += 7;
        else if (beatTheBat) Confidence += 1.2;
        else if (runsConceded >= 6) Confidence -= 3.5;
        else if (runsConceded >= 4) Confidence -= 2.0;
        else if (runsConceded == 0) Confidence += 0.5;
        Confidence = Math.Clamp(Confidence, 0, 100);

        // --- fatigue ---
        // Bowling is far harder work than batting, and pace bowling hardest of all.
        double stamina = AbilityScale.AttributeToHundred(bowler.Physical.Stamina) * 0.7
                         + AbilityScale.AttributeToHundred(bowler.Physical.Fitness) * 0.3;

        bool quick = bowler.Bowling.Pace >= 13 && bowler.Bowling.Pace > bowler.Bowling.Spin;
        double perBall = (quick ? 0.55 : 0.30) * (format == MatchFormat.Test ? 0.85 : 1.0);
        double resistance = 0.4 + (100 - stamina) / 100.0 * 1.4;

        // Bowling in the heat is the hardest work in cricket, and the weather multiplier is where
        // that shows: the same four-over spell costs roughly two and a half times as much in
        // Colombo in April as it does in Manchester in May.
        double conditions = weather?.FatigueMultiplier ?? 1.0;

        Fatigue = Math.Clamp(Fatigue + perBall * resistance * conditions, 0, 100);
    }

    /// <summary>
    /// Recovery while fielding. A bowler taken off does NOT sit still until the interval - he
    /// stands at fine leg and gets his breath back, which is exactly why captains rotate their
    /// quicks in short spells and bring them back rather than bowling them out.
    ///
    /// Recovery is real but slow: a couple of overs off take the edge off, four or five overs make
    /// a genuine difference, and a full session restores him properly. Fitness and the weather both
    /// decide how much - you get your breath back in the cold and barely at all in the humidity.
    /// </summary>
    public void RecoverInTheField(Player bowler, int oversOff, MatchWeather? weather = null)
    {
        if (oversOff <= 0) return;

        double fitness = AbilityScale.AttributeToHundred(bowler.Physical.Recovery) * 0.55
                         + AbilityScale.AttributeToHundred(bowler.Physical.Fitness) * 0.45;

        // Per over off: roughly 0.5 points for a poor recoverer, 1.8 for an excellent one, before
        // the weather has its say. Deliberately slow - a long spell in the heat needs a long time in
        // the field to come back from, and an interval must still be worth more than standing at
        // fine leg. A faster rate let eight overs off completely undo a six-over spell, which would
        // make spell management free.
        double perOver = (0.5 + fitness / 100.0 * 1.3) * (weather?.RecoveryMultiplier ?? 1.0);

        Fatigue = Math.Clamp(Fatigue - perOver * oversOff, 0, 100);
    }

    /// <summary>Taken off. He recovers, but he has to find his rhythm again when he comes back - which is the real cost of a short spell.</summary>
    public void EndSpell()
    {
        BallsBowledInSpell = 0;
        Rhythm = Math.Max(20, Rhythm * 0.55);
    }

    /// <summary>The over in which this bowler last bowled, so time spent in the field can be credited to him.</summary>
    public int LastOverBowled { get; set; } = -1;

    /// <summary>
    /// Section Q/W follow-up: the other bowler this one shared the new-ball phase with THIS
    /// innings, when one has been identified - set once by InningsSimulator, read by
    /// BallOutcomeModel.GetBowlerEffectiveSkill via BowlingPairSynergyService. Null means no
    /// pairing has been identified yet (or this bowler did not open the bowling), which degrades
    /// to the model's previous behaviour - no synergy effect at all.
    /// </summary>
    public Guid? NewBallPartnerId { get; set; }

    public void Rest(BreakLength length)
    {
        double cleared = length switch
        {
            BreakLength.Drinks => 0.30,
            BreakLength.Session => 0.65,
            BreakLength.Innings => 0.90,
            _ => 1.0
        };
        Fatigue *= 1 - cleared;
    }

    /// <summary>Multiplier on this bowler's threat. Rhythm and confidence lift it, fatigue takes it away.</summary>
    public double GetEffectivenessMultiplier()
    {
        double rhythm = 0.88 + Rhythm / 100.0 * 0.20;      // 0.88x -> 1.08x
        double confidence = 0.94 + Confidence / 100.0 * 0.12;
        double fatigue = 1 - Fatigue / 100.0 * 0.22;
        return Math.Clamp(rhythm * confidence * fatigue, 0.68, 1.20);
    }

    /// <summary>Multiplier on his control. A tired bowler out of rhythm sprays it, which is where extras and boundary balls come from.</summary>
    public double GetControlMultiplier()
    {
        double rhythm = 0.85 + Rhythm / 100.0 * 0.25;
        double fatigue = 1 - Fatigue / 100.0 * 0.30;
        return Math.Clamp(rhythm * fatigue, 0.6, 1.12);
    }

    public bool HasFoundHisRhythm => Rhythm >= 65;
}
