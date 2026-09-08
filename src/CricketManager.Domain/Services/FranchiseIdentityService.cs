using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Meeting-driven-selection ticket, requirement B: a franchise's recruitment IDENTITY
/// (<see cref="FranchiseArchetype"/>) is no longer fixed at world creation - it evolves.
///
/// Two drivers, both slow, gated and capped like the existing coaching-philosophy drift, but
/// applied to a different field (<see cref="FranchiseArchetype"/> and <see cref="CoachingPhilosophy"/>
/// are different types):
///
/// - <b>Results and history</b> - a settled, winning group drifts toward a settled
///   <see cref="FranchiseArchetype.Balanced"/> identity; big money spent with no trophy drifts a
///   <see cref="FranchiseArchetype.StarHunter"/> toward <see cref="FranchiseArchetype.Moneyball"/>;
///   years of near-misses with a bought squad drift toward
///   <see cref="FranchiseArchetype.YouthBuilder"/>. Checked once a year, at the auction.
/// - <b>A new coach</b> - a fresh campaign coach whose own philosophy points elsewhere sits down
///   with the captain and redraws the plan, when the coach's push clearly outweighs the captain's
///   resistance.
///
/// <b>Deterministic - no RNG.</b> A shift only happens on a genuinely clear signal AND when the
/// identity has not already changed in the last <see cref="HysteresisYears"/> years
/// (<see cref="Team.LastIdentityShiftYear"/>). This is the same "slow, only on real signal" shape
/// the RNG-gated coach drift has, expressed without a dice roll - which is what keeps it from
/// perturbing the shared clock or a dedicated auction stream (the Stage 1 determinism lesson).
/// </summary>
public sealed class FranchiseIdentityService
{
    private const int HysteresisYears = 3;

    /// <summary>The recruitment identity a coach of this philosophy naturally brings.</summary>
    public static FranchiseArchetype ArchetypeForPhilosophy(CoachingPhilosophy p) => p switch
    {
        CoachingPhilosophy.Aggressive or CoachingPhilosophy.ShortTermResults or CoachingPhilosophy.ReputationFocused
            => FranchiseArchetype.StarHunter,
        CoachingPhilosophy.AnalyticsDriven or CoachingPhilosophy.PerformanceFocused or CoachingPhilosophy.TacticalFlexibility
            => FranchiseArchetype.Moneyball,
        CoachingPhilosophy.YouthDevelopment or CoachingPhilosophy.LongTermDevelopment
            => FranchiseArchetype.YouthBuilder,
        _ => FranchiseArchetype.Balanced,
    };

    private static bool InCooldown(Team franchise, int year) =>
        franchise.LastIdentityShiftYear != 0 && year - franchise.LastIdentityShiftYear < HysteresisYears;

    /// <summary>Driver 1: sustained results/history. Call once a year per franchise, at the auction.</summary>
    public GameEvent? DriftFromResults(Team franchise, double continuity, int recentTitles, DateOnly date)
    {
        if (InCooldown(franchise, date.Year)) return null;

        FranchiseArchetype? desired = null;
        string why = "";

        if (recentTitles >= 2 && continuity >= 0.7 && franchise.FranchiseArchetype != FranchiseArchetype.Balanced)
        {
            desired = FranchiseArchetype.Balanced;
            why = "a settled, winning group - the plan now is continuity";
        }
        else if (recentTitles == 0 && continuity < 0.35 && franchise.PlayerTradingPnL < -400_000
                 && franchise.FranchiseArchetype == FranchiseArchetype.StarHunter)
        {
            desired = FranchiseArchetype.Moneyball;
            why = "big money spent, no trophy - the owner wants value, not names";
        }
        else if (recentTitles == 0 && continuity < 0.30 && franchise.DynastyRating <= 28
                 && franchise.FranchiseArchetype != FranchiseArchetype.YouthBuilder)
        {
            desired = FranchiseArchetype.YouthBuilder;
            why = "years of near-misses with a bought squad - time to build one";
        }

        if (desired is not { } d || d == franchise.FranchiseArchetype) return null;

        var old = franchise.FranchiseArchetype;
        franchise.FranchiseArchetype = d;
        franchise.LastIdentityShiftYear = date.Year;
        return new GameEvent(date, GameEventType.FranchiseIdentityShift,
            $"{franchise.Name} rethink how they build a squad ({old} -> {d}) - {why}.", franchise.Id);
    }

    /// <summary>Driver 2: a new campaign coach whose philosophy points elsewhere, backed (or not) by the captain.</summary>
    public GameEvent? DriftFromNewCoach(WorldState world, Team franchise, Coach coach, DateOnly date)
    {
        if (InCooldown(franchise, date.Year)) return null;

        var coachArchetype = ArchetypeForPhilosophy(coach.Philosophy);
        if (coachArchetype == franchise.FranchiseArchetype) return null;

        double captainResist = 0.35;
        var capId = franchise.GetCaptain(MatchFormat.T20);
        if (capId is { } cid && world.Players.FirstOrDefault(p => p.Id == cid) is { } cap)
            captainResist = Math.Clamp(0.15 + cap.Mental.Leadership / 40.0, 0.15, 0.60);

        // A persuasive coach with real authority pushes a new direction through; a strong-willed
        // captain at a settled club resists it. Deterministic threshold, no roll.
        double coachPush = Math.Clamp(0.30 + coach.Attributes.ManManagement / 40.0 + (coach.Authority - 20) / 200.0, 0.10, 0.95);
        if (coachPush <= captainResist + 0.15) return null;

        var old = franchise.FranchiseArchetype;
        franchise.FranchiseArchetype = coachArchetype;
        franchise.CulturalIdentity = coach.Philosophy; // the club's stated identity follows the new direction
        franchise.LastIdentityShiftYear = date.Year;
        return new GameEvent(date, GameEventType.FranchiseIdentityShift,
            $"{franchise.Name}'s new coach {coach.FullName} sits down with the captain and redraws the plan ({old} -> {coachArchetype}).",
            coach.Id, franchise.Id);
    }
}
