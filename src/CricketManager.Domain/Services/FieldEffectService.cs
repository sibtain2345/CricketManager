using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>The aggregate effect a field has on scoring, before any single ball is rolled.</summary>
public sealed record FieldEffect(
    double BoundaryScale,
    double SixScale,
    double SingleScale,
    double DotScale,
    double EdgeCarryToCordon,
    /// <summary>
    /// Compensates the base wicket rate for how well this field converts CHANCES into wickets.
    ///
    /// The format base rates are calibrated against real wickets-per-innings. Once edges can beat
    /// an empty cordon and catches can be dropped, a chance is no longer automatically a wicket,
    /// so leaving the base rate alone would quietly suppress wickets everywhere. Instead the base
    /// rate is treated as a CHANCE rate and rescaled against a reference field: an attacking
    /// cordon with good hands converts more than the reference and a bare field converts fewer,
    /// while the average across normal fields lands back on the real-world anchor.
    /// </summary>
    double ChanceToWicketScale);

/// <summary>
/// Turns a field setting into numbers the ball model can use.
///
/// This is the piece that makes field placement matter rather than decorate. Four distinct
/// effects, each of them something a viewer would recognise:
///
/// - **Boundary protection.** A sweeper on the cover boundary turns a well-timed drive from four
///   into one or two. Protection is weighted by where the BATTER actually scores, so putting a
///   man out at deep midwicket against a leg-side player is worth far more than putting him at
///   third man.
/// - **Ring pressure.** More fielders inside the circle means fewer easy singles and more dots,
///   which is the whole mechanism behind a middle-overs squeeze - but it leaves fewer men out,
///   so the boundary risk rises. There is no free field.
/// - **Edge carry.** The example that started this: with two slips and a gully there are gaps,
///   and some edges fly between them for four. A fourth slip closes the gap and turns those into
///   catches - at the cost of a man somewhere else. With no slips at all, an edge is simply four.
/// - **Catch conversion.** Whether a chance is actually held depends on WHO is standing there,
///   via FieldingAptitudeService. A world-class slip and a reluctant one are not the same catch.
/// </summary>
public sealed class FieldEffectService
{
    private readonly FieldingAptitudeService _aptitude = new();
    private readonly FielderPerformanceService _fielders = new();

    /// <summary>
    /// The aggregate effect, weighted by the batter's scoring zones. Deterministic, so the ball
    /// model's probability distribution stays testable - the specific zone a shot goes to is
    /// rolled separately when the ball is actually played.
    /// </summary>
    public FieldEffect Calculate(FieldSetting? field, BattingZoneStrengths? zones, MatchFormat format)
    {
        if (field is null || field.Count == 0)
            return new FieldEffect(1.0, 1.0, 1.0, 1.0, DefaultEdgeCarry(format), 1.0);

        zones ??= new BattingZoneStrengths();

        double totalWeight = 0, protectedWeight = 0, ringWeight = 0;

        foreach (var zone in Enum.GetValues<ShotZone>())
        {
            // A batter's strong zones matter more: that is where the runs would have come from,
            // so that is where protection is worth having.
            double weight = Math.Pow(zones[zone] / 50.0, 1.5);
            totalWeight += weight;

            int deep = field.FieldersInZone(zone, insideCircle: false);
            int ring = field.FieldersInZone(zone, insideCircle: true);

            // Diminishing returns - a second man in the same zone adds much less than the first.
            protectedWeight += weight * (1 - Math.Pow(0.55, deep));
            ringWeight += weight * (1 - Math.Pow(0.65, ring));
        }

        double protection = totalWeight <= 0 ? 0 : protectedWeight / totalWeight;
        double ringDensity = totalWeight <= 0 ? 0 : ringWeight / totalWeight;

        // Boundaries: a fully protected field roughly halves the four rate. Sixes are less
        // affected - a man on the rope cannot stop a shot that clears him, he can only catch it.
        double boundaryScale = 1 - protection * 0.55;
        double sixScale = 1 - protection * 0.20;

        // Singles: a packed ring cuts them off, an empty one gives them away.
        double singleScale = 1 - (ringDensity - 0.5) * 0.45;
        double dotScale = 1 + (ringDensity - 0.5) * 0.30;

        return new FieldEffect(
            Math.Clamp(boundaryScale, 0.4, 1.5),
            Math.Clamp(sixScale, 0.7, 1.3),
            Math.Clamp(singleScale, 0.6, 1.4),
            Math.Clamp(dotScale, 0.8, 1.3),
            EdgeCarry(field, format),
            ChanceToWicketScale(field, format));
    }

    /// <summary>
    /// The probability an edge is intercepted by the cordon rather than running away for runs.
    ///
    /// Each catcher behind the wicket closes part of the arc, with diminishing returns - the
    /// first slip is worth far more than the fourth. With an empty cordon, an edge simply beats
    /// the keeper and goes for four, which is exactly why a captain keeps a slip in for as long
    /// as he dares.
    /// </summary>
    public double EdgeCarry(FieldSetting field, MatchFormat format)
    {
        // The keeper takes the majority of edges that come through on his own - a side with no
        // slips still gets caught-behind dismissals all the time. An earlier calibration gave the
        // keeper far too small a share, which meant nearly three quarters of all edges ran away
        // for four whenever the cordon was empty, and wickets collapsed across the board.
        int catchers = field.CatchersBehindWicket;
        double keeperShare = field.HasKeeper ? 0.60 : 0.05;

        double cordon = 1 - Math.Pow(0.62, catchers);
        double carry = keeperShare + cordon * 0.35;

        // Red-ball cricket carries more to the cordon: the ball is harder, the keeper and slips
        // stand closer to the stumps, and edges travel.
        if (format == MatchFormat.Test) carry *= 1.12;

        return Math.Clamp(carry, 0.15, 0.92);
    }

    /// <summary>
    /// Roughly what share of catching chances this field converts. Caught and caught-behind are
    /// about half of all dismissals, so only that share is exposed to the field; bowled, lbw,
    /// stumped and run out are unaffected by where the catchers stand.
    /// </summary>
    public double ExpectedConversion(FieldSetting field, MatchFormat format)
    {
        const double CaughtShare = 0.34;        // outfield catches
        const double CaughtBehindShare = 0.16;  // edges to the cordon
        const double AverageHands = 0.77;       // MEASURED from the reach x hold model, not assumed

        double carry = EdgeCarry(field, format);

        // An outfield chance only becomes a wicket if somebody is standing in that part of the
        // field. Measured across the eight zones rather than guessed from a headcount, because a
        // field can have nine catching positions and still leave two zones completely open.
        int zonesCovered = Enum.GetValues<ShotZone>().Count(z => field.CatchersInZone(z).Any());
        double zonePresence = zonesCovered / 8.0;

        double converted = (1 - CaughtShare - CaughtBehindShare)
                           + CaughtShare * zonePresence * AverageHands
                           + CaughtBehindShare * carry * AverageHands;

        return Math.Clamp(converted, 0.4, 1.0);
    }

    /// <summary>
    /// What a normal field converts, measured from the auto-set fields this game actually
    /// produces. Anything better than this takes more wickets than the format baseline, anything
    /// worse takes fewer - so an attacking cordon is genuinely rewarded and a defensive,
    /// spread-out field genuinely costs wickets, while a typical field lands on the real-world
    /// wickets-per-innings anchor the base rates were calibrated against.
    /// </summary>
    private const double ReferenceConversion = 0.90;

    public double ChanceToWicketScale(FieldSetting field, MatchFormat format) =>
        Math.Clamp(ReferenceConversion / ExpectedConversion(field, format), 0.75, 1.45);

    /// <summary>
    /// A shot heading for the rope: is anybody out there, and do they cut it off?
    ///
    /// This is the other half of what an exceptional fielder is worth, and over an innings it is
    /// worth more than his catches - a boundary saved is three runs, every time. It only applies
    /// where a fielder is actually placed in that part of the outfield, which is precisely what
    /// makes a captain's placement a live decision rather than a statistical average.
    /// </summary>
    public FieldingOutcome? ResolveBoundary(
        FieldSetting? field, ShotZone zone, IReadOnlyDictionary<Guid, Player> players, double shotPower, Random random)
    {
        if (field is null) return null;

        // Only a fielder in the deep can cut off a boundary; a ring fielder has been beaten already.
        var deep = field.Placements
            .Where(p => FieldingPositions.Info(p.Position) is { InsideCircle: false } info && info.Zone == zone)
            .ToList();

        if (deep.Count == 0) return null; // nobody out there - it is four

        var placement = deep[random.Next(deep.Count)];
        if (!players.TryGetValue(placement.PlayerId, out var fielder)) return null;

        return _fielders.ResolveBoundaryAttempt(fielder, placement.Position, shotPower, random);
    }

    /// <summary>
    /// An ordinary ball fielded in the ring. Mostly nothing happens; occasionally a fumble gives
    /// away an extra run. No single misfield decides anything, but a sloppy side leaks fifteen or
    /// twenty runs an innings without a single memorable moment - which is the quiet difference
    /// between a good fielding side and a poor one.
    /// </summary>
    public FieldingOutcome? ResolveGroundFielding(
        FieldSetting? field, ShotZone zone, IReadOnlyDictionary<Guid, Player> players, int runsRun, Random random)
    {
        if (field is null) return null;

        var ring = field.Placements
            .Where(p => FieldingPositions.Info(p.Position) is { InsideCircle: true } info && info.Zone == zone)
            .ToList();

        if (ring.Count == 0) return null;

        var placement = ring[random.Next(ring.Count)];
        if (!players.TryGetValue(placement.PlayerId, out var fielder)) return null;

        return _fielders.ResolveGroundFielding(fielder, placement.Position, runsRun, random);
    }

    /// <summary>The zones either side of this one. The wagon wheel is circular, so third man and fine leg are neighbours behind the wicket.</summary>
    private static IEnumerable<ShotZone> AdjacentZones(ShotZone zone)
    {
        var all = Enum.GetValues<ShotZone>();
        int index = Array.IndexOf(all, zone);
        yield return all[(index + 1) % all.Length];
        yield return all[(index - 1 + all.Length) % all.Length];
    }

    private static double DefaultEdgeCarry(MatchFormat format) => format switch
    {
        MatchFormat.Test => 0.72,
        MatchFormat.ODI => 0.55,
        _ => 0.48
    };

    /// <summary>
    /// Which zone this shot went to. Weighted by the batter's strengths, so a strong leg-side
    /// player really does hit more through midwicket - which is what makes closing that zone off
    /// a meaningful decision rather than a guess.
    /// </summary>
    public ShotZone RollZone(BattingZoneStrengths? zones, Random random)
    {
        zones ??= new BattingZoneStrengths();

        var all = Enum.GetValues<ShotZone>();
        double total = all.Sum(z => Math.Pow(zones[z] / 50.0, 2));
        double roll = random.NextDouble() * total;

        double cumulative = 0;
        foreach (var zone in all)
        {
            cumulative += Math.Pow(zones[zone] / 50.0, 2);
            if (roll <= cumulative) return zone;
        }
        return ShotZone.Cover;
    }

    /// <summary>
    /// Resolves a catching chance in a zone: who is there, and do they hold it?
    /// Returns the fielder and whether the catch was taken. A dropped catch is a real outcome and
    /// the batter survives - which is why fielding attributes are worth paying for.
    /// </summary>
    public (Guid? FielderId, FieldingPosition? Position, bool Caught) ResolveCatch(
        FieldSetting? field, ShotZone zone, IReadOnlyDictionary<Guid, Player> players, Random random) =>
        ResolveCatchChance(field, zone, players, RollChanceDifficulty(random), random) is var outcome
            ? (outcome.FielderId, outcome.Position, outcome.Held)
            : default;

    /// <summary>
    /// How hard this particular chance is. Most chances offered are regulation; a minority are
    /// genuinely hard, and a few are the ones only an exceptional fielder ever reaches. The skew
    /// matters: if every chance were average, an outstanding fielder would be worth almost nothing,
    /// because his advantage lives almost entirely in the hard tail.
    /// </summary>
    public double RollChanceDifficulty(Random random)
    {
        double roll = random.NextDouble();
        return roll switch
        {
            // Calibrated so an average professional side holds roughly 85% of the chances it is
            // offered, which is what real fielding statistics show. An earlier spread was far
            // harsher and dropped the hold rate to 45%, which pushed caught dismissals to a
            // quarter of the mix when they should be over half.
            < 0.72 => random.NextDouble() * 0.22,              // regulation
            < 0.91 => 0.22 + random.NextDouble() * 0.26,       // needs a good fielder
            < 0.985 => 0.48 + random.NextDouble() * 0.27,      // hard
            _ => 0.75 + random.NextDouble() * 0.25             // the "impossible" ones
        };
    }

    /// <summary>
    /// Full resolution of a catching chance: who it went to, whether he got there, and whether he
    /// held it. Range and hands are asked separately - see FielderPerformanceService - which is
    /// what lets an exceptional fielder take chances nobody else reaches while still occasionally
    /// shelling a regulation one.
    /// </summary>
    public FieldingOutcome ResolveCatchChance(
        FieldSetting? field, ShotZone zone, IReadOnlyDictionary<Guid, Player> players, double difficulty, Random random)
    {
        if (field is null) return new FieldingOutcome(null, null, true, true, 0, false, string.Empty);

        var catchers = field.CatchersInZone(zone).ToList();

        // Nobody in that exact zone does not mean the chance is dead. The zones are a wagon-wheel
        // split, not walls: a man in the neighbouring zone will often still get across to it,
        // just as a harder chance. Treating an empty zone as an automatic four made catches far
        // too rare and pushed the dismissal mix away from caught, which dominates real cricket.
        bool stretched = false;
        if (catchers.Count == 0)
        {
            catchers = AdjacentZones(zone).SelectMany(z => field.CatchersInZone(z)).ToList();
            stretched = true;
        }

        if (catchers.Count == 0)
            return new FieldingOutcome(null, null, false, false, 4, false, "Nobody in that part of the field.");

        var placement = catchers[random.Next(catchers.Count)];
        if (!players.TryGetValue(placement.PlayerId, out var fielder))
            return new FieldingOutcome(placement.PlayerId, placement.Position, true, true, 0, false, string.Empty);

        // Reaching across from the neighbouring zone makes it a materially harder chance.
        double effective = Math.Clamp(stretched ? difficulty + 0.25 : difficulty, 0, 1);

        return _fielders.ResolveCatchChance(fielder, placement.Position, new FieldingChance(zone, effective, IsAerial: true), random);
    }
}
