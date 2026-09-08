using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Post-Phase-6 carry-forward: a point-in-time reading of a player's attribute clusters, taken
/// each quarter, so his development is genuinely visible period-over-period rather than only
/// inferable from his current numbers. Specified in the original Post-Phase-5 training
/// rectification and never actually built.
///
/// Each cluster value is the 0-100-scaled average of that group's attributes. Small deltas
/// between two snapshots are the actual "he has improved his game against spin over the last
/// year" story a coach or a UI wants to show.
/// </summary>
public sealed record PlayerDevelopmentSnapshot(
    DateOnly Date,
    double Batting,
    double BattingVsPace,
    double BattingVsSpin,
    double Bowling,
    double Fielding,
    double Mental,
    double Physical,
    int CurrentAbility,
    int PotentialAbility)
{
    public static PlayerDevelopmentSnapshot Of(Player p, DateOnly date)
    {
        double S(int attr) => AbilityScale.AttributeToHundred(attr);
        var b = p.Batting; var bo = p.Bowling; var f = p.Fielding; var m = p.Mental; var ph = p.Physical;

        return new PlayerDevelopmentSnapshot(
            Date: date,
            Batting: (S(b.Technique) + S(b.Timing) + S(b.ShotSelection) + S(b.DefensiveAbility) + S(b.ShotSelection) + S(b.PowerHitting)) / 6.0,
            BattingVsPace: (S(b.AgainstPace) + S(b.ShortBallAbility) + S(b.SwingHandling) + S(b.SeamHandling)) / 4.0,
            BattingVsSpin: (S(b.AgainstSpin) + S(b.SpinHandling)) / 2.0,
            Bowling: (S(bo.Accuracy) + S(bo.Variation) + S(bo.Containment) + S(bo.AttackingAbility) + S(Math.Max(bo.Pace, bo.Spin)) + S(bo.DeathBowling)) / 6.0,
            Fielding: (S(f.Catching) + S(f.Reflexes) + S(f.Throwing) + S(f.GroundFielding) + S(f.Positioning) + S(f.BoundaryFielding)) / 6.0,
            Mental: (S(m.Composure) + S(m.Concentration) + S(m.Determination) + S(m.PressureHandling) + S(m.DecisionMaking) + S(m.GameAwareness)) / 6.0,
            Physical: (S(ph.Fitness) + S(ph.Stamina) + S(ph.Strength) + S(ph.Speed) + S(ph.Recovery)) / 5.0,
            CurrentAbility: p.CurrentAbility,
            PotentialAbility: p.PotentialAbility);
    }
}
