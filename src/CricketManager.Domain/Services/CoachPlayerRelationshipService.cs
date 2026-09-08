using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 12 (§4.8): the coach-to-player working relationship - separate from the captain-only
/// <see cref="CoachCaptainRelationship"/>. Every head coach has a rapport with each of his senior
/// players. It moves on how the player is being treated (picked and backed, or on the fringe),
/// on personality fit, and on whether the coach's programme is actually working for him.
///
/// Consequences, all bounded:
/// - a strong bond lifts the player's <see cref="Player.CoachTrust"/> and gives his development a
///   small nudge (a player who trusts the coach works harder at what the coach asks);
/// - a poor bond drags CoachTrust down and, past a threshold, has the player agitate to leave
///   (<see cref="Player.TransferRequested"/> pressure via a dressing-room-harmony hit);
/// - the net picture across a squad pulls <see cref="Team.DressingRoomHarmony"/>.
///
/// Runs on the monthly tick with its own date-seeded local RNG, so it never perturbs the shared
/// monthly stream. Bonds are stored on <see cref="WorldState.CoachPlayerRelationships"/>.
/// </summary>
public sealed class CoachPlayerRelationshipService
{
    public IEnumerable<GameEvent> ReviewMonthly(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();
        var rng = new Random(date.Year * 12 + date.Month);

        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise).OrderBy(t => t.Name))
        {
            if (team.CurrentCoachId is not { } coachId) continue;
            var coach = world.Coaches.FirstOrDefault(c => c.Id == coachId);
            if (coach is null) continue;

            var squad = team.SquadPlayerIds
                .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Where(p => p is { IsRetired: false, AcademyTeamId: null }).Cast<Player>().ToList();
            if (squad.Count == 0) continue;

            double harmonyPull = 0;
            int counted = 0;

            foreach (var player in squad)
            {
                var bond = world.CoachPlayerRelationships.FirstOrDefault(b => b.CoachId == coachId && b.PlayerId == player.Id);
                if (bond is null)
                {
                    bond = new CoachPlayerBond { CoachId = coachId, PlayerId = player.Id, LastMoved = date };
                    world.CoachPlayerRelationships.Add(bond);
                }

                // Treatment: a first-choice / regular warms to the coach; a fringe player who never
                // gets a look cools on him.
                double delta = player.SquadStatus switch
                {
                    SquadStatus.FirstChoice => 1.4,
                    SquadStatus.SecondChoice => 0.6,
                    SquadStatus.Backup or SquadStatus.ReturningFromInjury => -0.1,
                    SquadStatus.Fringe or SquadStatus.EmergencyReplacement => -1.1,
                    _ => 0.2
                };

                // Personality fit: a demanding, results-first setup clicks with a professional
                // player and grates on an aggressive / inconsistent one; a development-minded coach
                // has more time for a rough diamond.
                if (coach.Philosophy is CoachingPhilosophy.PerformanceFocused or CoachingPhilosophy.ShortTermResults or CoachingPhilosophy.Aggressive)
                {
                    if (player.Personality.HasFlag(PersonalityTrait.Professional)) delta += 0.4;
                    if (player.Personality.HasFlag(PersonalityTrait.Aggressive) || player.Personality.HasFlag(PersonalityTrait.Inconsistent)) delta -= 0.4;
                }
                if (coach.Philosophy is CoachingPhilosophy.YouthDevelopment or CoachingPhilosophy.LongTermDevelopment or CoachingPhilosophy.ExperienceFocused
                    && player.Mental.Professionalism < 10) delta += 0.3;

                // The programme working: recent development in the player's favour.
                if (world.RecentDevelopmentByPlayer.TryGetValue(player.Id, out var dev) && dev > 0.4) delta += 0.5;

                // A poor man-manager can't hold a fragile relationship together.
                delta += (coach.Attributes.ManManagement - 11) * 0.05;

                bond.Adjust(delta);
                bond.DecayTowardNeutral(0.4);
                bond.LastMoved = date;

                // Effect on the player.
                double rapport = bond.Rapport;
                player.CoachTrust = Math.Clamp(player.CoachTrust + (rapport - 50) * 0.03, 0, 100);
                if (rapport >= 68 && world.RecentDevelopmentByPlayer.ContainsKey(player.Id))
                    world.RecentDevelopmentByPlayer[player.Id] += 0.15;

                // A genuinely broken relationship - a regular who has lost the coach - has him
                // agitate. Rare, and only for someone with the standing to do it.
                if (rapport <= 26 && player.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice
                    && player.Reputation.Domestic >= 45 && !player.TransferRequested && rng.NextDouble() < 0.22)
                {
                    player.TransferRequested = true;
                    player.TransferListed = true;
                    team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony - 3, 0, 100);
                    events.Add(new GameEvent(date, GameEventType.TransferRequestFiled,
                        $"{player.FullName} has fallen out with {coach.FullName} and wants to leave {team.Name}.", player.Id, team.Id));
                }

                harmonyPull += rapport - 50;
                counted++;
            }

            if (counted > 0)
            {
                double target = 50 + harmonyPull / counted * 0.4;
                team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony + (target - team.DressingRoomHarmony) * 0.05, 0, 100);
            }
        }

        return events;
    }
}
