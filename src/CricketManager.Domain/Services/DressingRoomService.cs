using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 5 (point 10): the dressing-room hierarchy.
///
/// A squad is not eleven equal voices. A senior pro's read of the coach - is he backing him, is
/// the environment right - carries far more weight than a debutant's, and the juniors take their
/// temperature from the seniors rather than forming an independent opinion. This service:
///
/// - ranks each player's standing in the room (RoleOf), from reputation, experience, leadership
///   and age - the same "derive it, never trust a bare default" discipline the rest of this
///   codebase uses;
/// - reads the SENIOR consensus on the coach (a standing-weighted average of the senior players'
///   CoachTrust), which is what actually governs whether the room is with him;
/// - each period, pulls the juniors' own CoachTrust toward that consensus (the room follows its
///   leaders), and moves Team.DressingRoomHarmony toward a target set by how high AND how united
///   the senior trust is, plus results.
///
/// Deliberately works off Player.CoachTrust (already "trust in the current coaching setup") and
/// Team.CurrentStreak (already maintained by TeamMoraleService) rather than inventing new state.
/// </summary>
public sealed class DressingRoomService
{
    /// <summary>A player's standing in the room, 0-100.</summary>
    public double Standing(Player player, DateOnly asOf)
    {
        double reputation = Math.Max(player.Reputation.Domestic, player.Reputation.Continental * 0.9);
        double experience = player.Experience.Level;
        double leadership = AbilityScale.AttributeToHundred(player.Mental.Leadership);

        // Influence peaks in the early 30s - old enough to have seen everything, not yet on the
        // way out. A 23-year-old star still has less pull in the room than a 32-year-old journeyman.
        int age = player.Age(asOf);
        double ageFactor = age <= 24 ? 0.6 : age <= 28 ? 0.85 : age <= 35 ? 1.0 : 0.9;

        double core = reputation * 0.38 + experience * 0.34 + leadership * 0.28;

        // A designated captain always carries a senior voice regardless of the rest.
        if (player.Personality.HasFlag(PersonalityTrait.Leader)) core += 8;

        return Math.Clamp(core * ageFactor, 0, 100);
    }

    public DressingRoomRole RoleOf(Player player, DateOnly asOf) => Standing(player, asOf) switch
    {
        >= 68 => DressingRoomRole.SeniorPro,
        >= 50 => DressingRoomRole.Established,
        >= 32 => DressingRoomRole.SquadPlayer,
        _ => DressingRoomRole.Junior
    };

    /// <summary>
    /// The senior players' standing-weighted consensus on the coach, 0-100, or null when a squad
    /// has no genuine seniors yet (a brand-new franchise, an academy side) - null means "no
    /// established view", not "they dislike him".
    /// </summary>
    public double? SeniorConsensusCoachTrust(IReadOnlyList<Player> squad, DateOnly asOf)
    {
        var seniors = squad
            .Where(p => !p.IsRetired && RoleOf(p, asOf) is DressingRoomRole.Established or DressingRoomRole.SeniorPro)
            .Select(p => (Trust: p.CoachTrust, Weight: Standing(p, asOf)))
            .ToList();

        if (seniors.Count == 0) return null;
        double totalWeight = seniors.Sum(s => s.Weight);
        if (totalWeight <= 0) return null;
        return seniors.Sum(s => s.Trust * s.Weight) / totalWeight;
    }

    /// <summary>
    /// One period of room dynamics. Juniors' CoachTrust converges toward the senior consensus;
    /// harmony moves toward a target from the senior trust level, its spread, and results.
    /// </summary>
    public void ApplyRoomDynamics(Team team, IReadOnlyList<Player> squad, DateOnly asOf, double periodFraction)
    {
        periodFraction = Math.Clamp(periodFraction, 0, 1);
        var active = squad.Where(p => !p.IsRetired).ToList();
        if (active.Count == 0) return;

        // The rates below are calibrated for a MONTHLY call; `step` scales them proportionally if
        // this is ever called at a different cadence (1.0 for the standard 1/12 monthly tick).
        double step = periodFraction * 12;

        double? consensus = SeniorConsensusCoachTrust(active, asOf);

        if (consensus is { } seniorTrust)
        {
            // The room follows its leaders - juniors and squad players drift toward the senior view.
            foreach (var p in active.Where(p => RoleOf(p, asOf) is DressingRoomRole.Junior or DressingRoomRole.SquadPlayer))
            {
                double pull = (seniorTrust - p.CoachTrust) * 0.14 * step;
                p.CoachTrust = Math.Clamp(p.CoachTrust + pull, 0, 100);
            }

            // Harmony: high senior trust AND a united senior view is a tight room; a big spread
            // among the seniors is a fractured one, whatever the average.
            var seniorTrusts = active
                .Where(p => RoleOf(p, asOf) is DressingRoomRole.Established or DressingRoomRole.SeniorPro)
                .Select(p => p.CoachTrust).ToList();
            double spread = seniorTrusts.Count > 1 ? seniorTrusts.Max() - seniorTrusts.Min() : 0;

            double target = Math.Clamp(seniorTrust - spread * 0.5 + Math.Clamp(team.CurrentStreak, -3, 3) * 2.5, 0, 100);
            team.DressingRoomHarmony = Math.Clamp(
                team.DressingRoomHarmony + (target - team.DressingRoomHarmony) * 0.16 * step, 0, 100);
        }
        else
        {
            // No senior voice - harmony drifts gently toward neutral.
            team.DressingRoomHarmony = Math.Clamp(
                team.DressingRoomHarmony + (50 - team.DressingRoomHarmony) * 0.08 * step, 0, 100);
        }

        // A genuinely united or genuinely fractured room feeds the collective mood a little.
        team.Morale.Adjust((team.DressingRoomHarmony - 50) / 50.0 * 1.2 * periodFraction);
    }
}
