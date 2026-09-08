using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 14 (§18.1): the player-to-player relationship graph. Friendships form from shared time at
/// a club and personality fit; feuds form from a run-out or an on-field clash; mentorships form
/// between a strong senior and a promising junior.
///
/// The graph is not just flavour:
/// - it feeds <see cref="Team.DressingRoomHarmony"/> (friendships lift it, feuds drag it),
/// - a feuding pair batting together carries a higher run-out risk (read by the ball model via
///   <see cref="PartnershipChemistryService"/>'s existing matchup key),
/// - "he'll only sign if his mate is there" - the free-agent and transfer markets weigh a friend
///   already at the target club.
///
/// Reviewed quarterly. Conservative - a relationship needs a real, repeated signal, and it decays
/// on its own.
/// </summary>
public sealed class PlayerRelationshipService
{
    private const int MaxNewPerQuarter = 6;

    public IEnumerable<GameEvent> ReviewQuarterly(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        // --- decay + prune ---
        foreach (var rel in world.PlayerRelationships.ToList())
        {
            double decay = rel.Kind switch { RelationshipKind.Feud => 1.5, RelationshipKind.Rivalry => 2.5, _ => 1.0 };
            rel.Adjust(-decay, date);
            if (rel.Strength <= 3) world.PlayerRelationships.Remove(rel);
        }

        // --- form new / reinforce from shared clubs ---
        int made = 0;
        // world.Players LIST order is the seeded order - stable between two loads of the same seed,
        // unlike p.Id (a Guid the seeder does not seed). RNG below is consumed per pair, so the
        // pairing order must be deterministic.
        var playerIndex = new Dictionary<Guid, int>();
        for (int i = 0; i < world.Players.Count; i++) playerIndex[world.Players[i].Id] = i;

        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise && !t.IsNational).OrderBy(t => t.Name))
        {
            var squad = team.SquadPlayerIds
                .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Where(p => p is { IsRetired: false } && p.AcademyTeamId is null)
                .Select(p => p!)
                .OrderBy(p => playerIndex.GetValueOrDefault(p.Id, int.MaxValue))
                .ToList();
            if (squad.Count < 4) continue;

            // A deterministic pairing pass: adjacent players in the id-ordered squad.
            for (int i = 0; i + 1 < squad.Count && made < MaxNewPerQuarter; i += 2)
            {
                var a = squad[i]; var b = squad[i + 1];
                var existing = world.PlayerRelationships.FirstOrDefault(r => r.Match(a.Id, b.Id));

                double compat = Compatibility(a, b);
                if (existing is not null)
                {
                    existing.Adjust(compat > 0 ? 3.5 : -1.5, date);
                    continue;
                }

                if (compat >= 0.2 && random.NextDouble() < 0.28)
                {
                    var kind = SeniorGap(a, b, date) ? RelationshipKind.Mentorship : RelationshipKind.Friendship;
                    var rel = new PlayerRelationship { PlayerAId = a.Id, PlayerBId = b.Id, Kind = kind, Formed = date, LastReinforced = date };
                    rel.SetStrength(kind == RelationshipKind.Mentorship ? 45 : 50);
                    world.PlayerRelationships.Add(rel);
                    made++;
                    events.Add(new GameEvent(date, GameEventType.PlayerRelationship,
                        kind == RelationshipKind.Mentorship
                            ? $"{a.FullName} has taken {b.FullName} under his wing at {team.Name}."
                            : $"{a.FullName} and {b.FullName} have become close at {team.Name}.",
                        a.Id, b.Id));
                }
                else if (compat <= -0.35 && random.NextDouble() < 0.16)
                {
                    var rel = new PlayerRelationship { PlayerAId = a.Id, PlayerBId = b.Id, Kind = RelationshipKind.Feud, Formed = date, LastReinforced = date };
                    rel.SetStrength(45);
                    world.PlayerRelationships.Add(rel);
                    made++;
                    events.Add(new GameEvent(date, GameEventType.PlayerRelationship,
                        $"There is friction between {a.FullName} and {b.FullName} in the {team.Name} dressing room.", a.Id, b.Id));
                }
            }

            // The graph's net effect on the room.
            double net = world.PlayerRelationships
                .Where(r => squad.Any(p => p.Id == r.PlayerAId) && squad.Any(p => p.Id == r.PlayerBId))
                .Sum(r => r.Kind switch
                {
                    RelationshipKind.Friendship => r.Strength / 100.0 * 0.6,
                    RelationshipKind.Mentorship => r.Strength / 100.0 * 0.4,
                    RelationshipKind.Feud => -r.Strength / 100.0 * 1.1,
                    _ => 0
                });
            if (Math.Abs(net) > 0.4)
                team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony + Math.Clamp(net, -3, 3), 0, 100);
        }

        return events;
    }

    /// <summary>-1..+1: how well two players get on, from personality flags and the shared-attribute of professionalism.</summary>
    private static double Compatibility(Player a, Player b)
    {
        double s = 0;
        double BothOrEither(PersonalityTrait t, double both, double either)
            => (a.Personality.HasFlag(t) && b.Personality.HasFlag(t)) ? both
             : (a.Personality.HasFlag(t) || b.Personality.HasFlag(t)) ? either : 0.0;

        // A shared dressing room is a bonding baseline - most team-mates get along fine.
        s += 0.15;
        s += BothOrEither(PersonalityTrait.TeamOriented, 0.5, 0.15);
        s += BothOrEither(PersonalityTrait.Professional, 0.35, 0.1);
        s -= BothOrEither(PersonalityTrait.Aggressive, 0.55, 0.15);
        s -= BothOrEither(PersonalityTrait.MoneyFocused, 0.25, 0.08);
        if (a.Personality.HasFlag(PersonalityTrait.Lazy) != b.Personality.HasFlag(PersonalityTrait.Lazy)) s -= 0.35;
        s += (a.Mental.Professionalism + b.Mental.Professionalism - 20) / 40.0 * 0.3;
        return Math.Clamp(s, -1, 1);
    }

    private static bool SeniorGap(Player a, Player b, DateOnly date)
    {
        int ageGap = Math.Abs(a.Age(date) - b.Age(date));
        return ageGap >= 7 && (Math.Max(a.Reputation.Domestic, b.Reputation.Domestic) >= 60);
    }

    /// <summary>Phase 14: does the target club have a friend of this player at it? Used by the markets.</summary>
    public bool HasFriendAt(WorldState world, Player player, Team club)
    {
        return world.PlayerRelationships
            .Where(r => r.Involves(player.Id) && r.Kind is RelationshipKind.Friendship or RelationshipKind.Mentorship && r.Strength >= 45)
            .Select(r => r.Other(player.Id))
            .Any(otherId => club.SquadPlayerIds.Contains(otherId));
    }
}
