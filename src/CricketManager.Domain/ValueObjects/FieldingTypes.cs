using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>Everything static about a fielding position: where it is, what it demands, and what it can do.</summary>
public sealed record FieldingPositionInfo(
    FieldingPosition Position,
    ShotZone Zone,
    bool InsideCircle,
    bool IsCatchingPosition,
    bool BehindSquare,
    bool LegSide,
    FieldingSkillType SkillRequired,
    /// <summary>0-1. How hard a catch at this position typically is - a slip catch flies, a deep catch gives you time but you have to cover ground.</summary>
    double CatchDifficulty);

/// <summary>
/// The static properties of every fielding position.
///
/// This is what turns a field setting from a list of names into something that changes the game:
/// a position's ZONE decides which shots it intercepts, INSIDE/OUTSIDE the circle decides whether
/// it saves singles or boundaries, and BEHIND SQUARE + LEG SIDE decide whether the setting is
/// even legal (Law 28.4, the law written after Bodyline).
/// </summary>
public static class FieldingPositions
{
    private static readonly Dictionary<FieldingPosition, FieldingPositionInfo> Catalog = new()
    {
        [FieldingPosition.WicketKeeper] = new(FieldingPosition.WicketKeeper, ShotZone.ThirdMan, true, true, true, false, FieldingSkillType.Keeping, 0.55),
        [FieldingPosition.Slip1] = new(FieldingPosition.Slip1, ShotZone.ThirdMan, true, true, true, false, FieldingSkillType.SlipCatching, 0.55),
        [FieldingPosition.Slip2] = new(FieldingPosition.Slip2, ShotZone.ThirdMan, true, true, true, false, FieldingSkillType.SlipCatching, 0.60),
        [FieldingPosition.Slip3] = new(FieldingPosition.Slip3, ShotZone.ThirdMan, true, true, true, false, FieldingSkillType.SlipCatching, 0.65),
        [FieldingPosition.Slip4] = new(FieldingPosition.Slip4, ShotZone.ThirdMan, true, true, true, false, FieldingSkillType.SlipCatching, 0.70),
        [FieldingPosition.Gully] = new(FieldingPosition.Gully, ShotZone.Point, true, true, true, false, FieldingSkillType.SlipCatching, 0.62),
        [FieldingPosition.LegSlip] = new(FieldingPosition.LegSlip, ShotZone.FineLeg, true, true, true, true, FieldingSkillType.SlipCatching, 0.62),
        [FieldingPosition.ShortLeg] = new(FieldingPosition.ShortLeg, ShotZone.SquareLeg, true, true, false, true, FieldingSkillType.CloseCatching, 0.70),
        [FieldingPosition.SillyPoint] = new(FieldingPosition.SillyPoint, ShotZone.Point, true, true, false, false, FieldingSkillType.CloseCatching, 0.70),

        [FieldingPosition.Point] = new(FieldingPosition.Point, ShotZone.Point, true, true, false, false, FieldingSkillType.InnerRing, 0.40),
        [FieldingPosition.BackwardPoint] = new(FieldingPosition.BackwardPoint, ShotZone.Point, true, true, true, false, FieldingSkillType.InnerRing, 0.42),
        [FieldingPosition.CoverPoint] = new(FieldingPosition.CoverPoint, ShotZone.Cover, true, true, false, false, FieldingSkillType.InnerRing, 0.38),
        [FieldingPosition.Cover] = new(FieldingPosition.Cover, ShotZone.Cover, true, true, false, false, FieldingSkillType.InnerRing, 0.38),
        [FieldingPosition.ExtraCover] = new(FieldingPosition.ExtraCover, ShotZone.Cover, true, true, false, false, FieldingSkillType.InnerRing, 0.38),
        [FieldingPosition.MidOff] = new(FieldingPosition.MidOff, ShotZone.MidOff, true, true, false, false, FieldingSkillType.InnerRing, 0.35),
        [FieldingPosition.MidOn] = new(FieldingPosition.MidOn, ShotZone.MidOn, true, true, false, true, FieldingSkillType.InnerRing, 0.35),
        [FieldingPosition.MidWicket] = new(FieldingPosition.MidWicket, ShotZone.MidWicket, true, true, false, true, FieldingSkillType.InnerRing, 0.38),
        [FieldingPosition.SquareLeg] = new(FieldingPosition.SquareLeg, ShotZone.SquareLeg, true, true, false, true, FieldingSkillType.InnerRing, 0.40),
        [FieldingPosition.FineLeg] = new(FieldingPosition.FineLeg, ShotZone.FineLeg, true, false, true, true, FieldingSkillType.InnerRing, 0.45),
        [FieldingPosition.ThirdMan] = new(FieldingPosition.ThirdMan, ShotZone.ThirdMan, true, false, true, false, FieldingSkillType.InnerRing, 0.45),
        [FieldingPosition.Bowler] = new(FieldingPosition.Bowler, ShotZone.MidOff, true, true, false, false, FieldingSkillType.InnerRing, 0.50),

        [FieldingPosition.DeepThirdMan] = new(FieldingPosition.DeepThirdMan, ShotZone.ThirdMan, false, true, true, false, FieldingSkillType.BoundaryRiding, 0.45),
        [FieldingPosition.DeepPoint] = new(FieldingPosition.DeepPoint, ShotZone.Point, false, true, true, false, FieldingSkillType.BoundaryRiding, 0.45),
        [FieldingPosition.DeepCover] = new(FieldingPosition.DeepCover, ShotZone.Cover, false, true, false, false, FieldingSkillType.BoundaryRiding, 0.45),
        [FieldingPosition.LongOff] = new(FieldingPosition.LongOff, ShotZone.MidOff, false, true, false, false, FieldingSkillType.BoundaryRiding, 0.45),
        [FieldingPosition.LongOn] = new(FieldingPosition.LongOn, ShotZone.MidOn, false, true, false, true, FieldingSkillType.BoundaryRiding, 0.45),
        [FieldingPosition.DeepMidWicket] = new(FieldingPosition.DeepMidWicket, ShotZone.MidWicket, false, true, false, true, FieldingSkillType.BoundaryRiding, 0.45),
        [FieldingPosition.DeepSquareLeg] = new(FieldingPosition.DeepSquareLeg, ShotZone.SquareLeg, false, true, true, true, FieldingSkillType.BoundaryRiding, 0.45),
        [FieldingPosition.DeepFineLeg] = new(FieldingPosition.DeepFineLeg, ShotZone.FineLeg, false, true, true, true, FieldingSkillType.BoundaryRiding, 0.48)
    };

    public static FieldingPositionInfo Info(FieldingPosition position) => Catalog[position];

    public static IEnumerable<FieldingPositionInfo> All => Catalog.Values;

    /// <summary>Positions in a given zone - which shots this part of the field actually covers.</summary>
    public static IEnumerable<FieldingPositionInfo> InZone(ShotZone zone) => Catalog.Values.Where(p => p.Zone == zone);
}

/// <summary>One fielder in one position.</summary>
public sealed record FieldPlacement(FieldingPosition Position, Guid PlayerId);

/// <summary>Why a field setting is illegal, in the words a scorer or umpire would use.</summary>
public sealed record FieldLegalityResult(bool IsLegal, IReadOnlyList<string> Violations)
{
    public static FieldLegalityResult Legal => new(true, Array.Empty<string>());
}

/// <summary>
/// A complete field: eleven players in eleven positions.
///
/// This is what makes a captain's decisions matter ball by ball. Two slips and a gully will hold
/// some edges and let others through the gaps for four; a sweeper on the cover boundary turns a
/// four into a single; an empty midwicket invites a batter who is strong there to help himself.
/// None of that is expressible without knowing WHERE the fielders actually are.
/// </summary>
public sealed class FieldSetting
{
    public List<FieldPlacement> Placements { get; init; } = new();

    public int Count => Placements.Count;

    public IEnumerable<FieldingPositionInfo> Infos => Placements.Select(p => FieldingPositions.Info(p.Position));

    public int FieldersOutsideCircle => Infos.Count(i => !i.InsideCircle);
    public int FieldersInsideCircle => Infos.Count(i => i.InsideCircle);

    /// <summary>Catchers close to the bat, excluding the keeper - the cordon that decides whether an edge is a wicket or a boundary.</summary>
    public int CatchersBehindWicket => Infos.Count(i =>
        i.SkillRequired == FieldingSkillType.SlipCatching && i.Position != FieldingPosition.WicketKeeper);

    public int LegSideFielders => Infos.Count(i => i.LegSide);

    /// <summary>The count Law 28.4 caps at two. Written into the laws after the Bodyline series, and still the reason a captain cannot simply pack the leg side behind square.</summary>
    public int BehindSquareLegSideFielders => Infos.Count(i => i.LegSide && i.BehindSquare);

    public bool HasKeeper => Placements.Any(p => p.Position == FieldingPosition.WicketKeeper);

    public int FieldersInZone(ShotZone zone, bool? insideCircle = null) =>
        Infos.Count(i => i.Zone == zone && (insideCircle is null || i.InsideCircle == insideCircle));

    public Guid? PlayerAt(FieldingPosition position) =>
        Placements.FirstOrDefault(p => p.Position == position)?.PlayerId;

    /// <summary>Fielders who could take a catch in this zone, nearest-first by catching difficulty.</summary>
    public IEnumerable<FieldPlacement> CatchersInZone(ShotZone zone) =>
        Placements.Where(p => FieldingPositions.Info(p.Position) is { IsCatchingPosition: true } info && info.Zone == zone);
}

/// <summary>
/// Where a batter scores, as a 0-100 strength per zone.
///
/// Real batters have strong areas and the whole point of a field setting is to close them off -
/// a captain who leaves midwicket open to a strong leg-side player is giving away runs, and a
/// batter forced to score in his weak zones scores slower and takes more risk doing it.
///
/// Derived from attributes by default so every player has a plausible profile without anyone
/// hand-authoring one, but settable so real-world data can carry actual wagon-wheel splits.
/// </summary>
public sealed class BattingZoneStrengths
{
    private readonly Dictionary<ShotZone, double> _strengths = new();

    public double this[ShotZone zone]
    {
        get => _strengths.TryGetValue(zone, out var v) ? v : 50;
        set => _strengths[zone] = Math.Clamp(value, 0, 100);
    }

    public IReadOnlyDictionary<ShotZone, double> All => _strengths;

    /// <summary>
    /// A plausible profile from a player's batting attributes. Strong drivers score through
    /// cover and straight; strong pullers and flickers score square and behind on the leg side;
    /// power hitters clear the ropes down the ground. Deliberately asymmetric - a batter with an
    /// identical profile in all eight zones does not exist.
    /// </summary>
    public static BattingZoneStrengths FromPlayer(Player player)
    {
        var b = player.Batting;
        double Scale(int a) => Common.AbilityScale.AttributeToHundred(a);

        var zones = new BattingZoneStrengths
        {
            [ShotZone.Cover] = Scale(b.Technique) * 0.5 + Scale(b.Timing) * 0.5,
            [ShotZone.MidOff] = Scale(b.Technique) * 0.4 + Scale(b.PowerHitting) * 0.6,
            [ShotZone.MidOn] = Scale(b.PowerHitting) * 0.55 + Scale(b.Timing) * 0.45,
            [ShotZone.MidWicket] = Scale(b.AgainstSpin) * 0.35 + Scale(b.PowerHitting) * 0.4 + Scale(b.Aggression) * 0.25,
            [ShotZone.SquareLeg] = Scale(b.ShortBallAbility) * 0.5 + Scale(b.Aggression) * 0.5,
            [ShotZone.FineLeg] = Scale(b.ShortBallAbility) * 0.4 + Scale(b.StrikeRotation) * 0.6,
            [ShotZone.Point] = Scale(b.AgainstPace) * 0.45 + Scale(b.Timing) * 0.55,
            [ShotZone.ThirdMan] = Scale(b.StrikeRotation) * 0.5 + Scale(b.Technique) * 0.5
        };

        return zones;
    }

    /// <summary>The zone this batter scores best in - what an opposition analyst would flag first.</summary>
    public ShotZone StrongestZone => Enum.GetValues<ShotZone>().OrderByDescending(z => this[z]).First();
}
