using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 14 (§18.3): personality DEVELOPS over a career. A young hothead in a strong, professional
/// dressing room with a mentor gradually sheds the <see cref="PersonalityTrait.Aggressive"/> /
/// <see cref="PersonalityTrait.Inconsistent"/> edge; a quiet player with real
/// <see cref="MentalAttributes.Leadership"/> and standing grows into <see cref="PersonalityTrait.Leader"/>.
///
/// Slow, bounded, and gated on both age and environment - it never fires for a set 30-year-old,
/// and never without a genuine reason. Runs annually. Reuses the relationship graph and the
/// dressing-room harmony that already exist.
/// </summary>
public sealed class PersonalityDevelopmentService
{
    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var teams = world.Teams;

        foreach (var p in world.Players)
        {
            if (p.IsRetired || p.AcademyTeamId is not null) continue;
            int age = p.Age(date);
            if (age is < 19 or > 30) continue; // personality is mostly settled by the early 30s

            var team = p.CurrentTeamId is { } tid && teams.TryGetValue(tid, out var t) ? t : null;
            bool goodRoom = team is not null && team.DressingRoomHarmony >= 58;
            bool hasMentor = world.PlayerRelationships.Any(r => r.Involves(p.Id)
                && r.Kind is RelationshipKind.Mentorship && r.Strength >= 50);

            // Maturing: shed a flaw.
            if ((p.Personality.HasFlag(PersonalityTrait.Aggressive) || p.Personality.HasFlag(PersonalityTrait.Inconsistent))
                && age >= 22 && (goodRoom || hasMentor)
                && p.Mental.Professionalism >= 11
                && random.NextDouble() < 0.12)
            {
                var shed = p.Personality.HasFlag(PersonalityTrait.Inconsistent) ? PersonalityTrait.Inconsistent : PersonalityTrait.Aggressive;
                p.Personality &= ~shed;
                p.Mental.Composure = Math.Min(20, p.Mental.Composure + 1);
                p.Mental.Professionalism = Math.Min(20, p.Mental.Professionalism + 1);
                events.Add(new GameEvent(date, GameEventType.PersonalityDevelopment,
                    $"{p.FullName} has matured - the {shed.ToString().ToLowerInvariant()} streak of his early years is gone.", p.Id, p.CurrentTeamId));
            }

            // Growing into a leader.
            if (!p.Personality.HasFlag(PersonalityTrait.Leader)
                && age >= 25 && p.Mental.Leadership >= 13 && p.Reputation.Domestic >= 55
                && (p.Personality.HasFlag(PersonalityTrait.Professional) || p.Personality.HasFlag(PersonalityTrait.TeamOriented))
                && random.NextDouble() < 0.14)
            {
                p.Personality |= PersonalityTrait.Leader;
                p.Mental.Leadership = Math.Min(20, p.Mental.Leadership + 1);
                events.Add(new GameEvent(date, GameEventType.PersonalityDevelopment,
                    $"{p.FullName} has grown into a genuine leader in the dressing room.", p.Id, p.CurrentTeamId));
            }
        }

        // Uses its own date-seeded local Random so it adds nothing to the shared annual stream the
        // rollover's later consumers draw from - the same discipline every new tail mechanic follows.
        events.AddRange(DevelopLeadershipPipeline(world, date, new Random(date.Year * 6151 + 907)));
        return events;
    }

    /// <summary>
    /// §18.2: leadership MATERIAL below the captaincy. A senior pro who is not the captain but is
    /// genuinely respected and carries real responsibility in the group (a SeniorPro standing, or the
    /// vice-captaincy) keeps developing his leadership - so when the armband does change hands the
    /// successor is not starting cold. Small, slow, capped; the vice-captain's own per-match residual
    /// growth (Wave 4) sits on top of this.
    /// </summary>
    private static IEnumerable<GameEvent> DevelopLeadershipPipeline(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var dressingRoom = new DressingRoomService();

        // Deterministic order - this loop consumes `random`, so it must not iterate a Guid-keyed
        // dictionary's values (their order differs between two loads of the same seed).
        foreach (var team in world.Teams.Values.OrderBy(t => t.Name).ThenBy(t => t.Id))
        {
            if (team.IsFranchise) continue;
            var squad = team.SquadPlayerIds
                .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
                .Where(p => p is { IsRetired: false }).Cast<Player>().ToList();
            if (squad.Count < 6) continue;

            var captains = team.CaptainsByFormat.Values.ToHashSet();
            var viceCaptains = team.ViceCaptainsByFormat.Values.ToHashSet();

            foreach (var p in squad)
            {
                if (captains.Contains(p.Id)) continue;
                if (p.Mental.Leadership is < 10 or >= 17) continue;
                int age = p.Age(date);
                if (age < 24) continue;

                bool inGroup = viceCaptains.Contains(p.Id)
                    || dressingRoom.RoleOf(p, date) == DressingRoomRole.SeniorPro;
                if (!inGroup) continue;

                double chance = viceCaptains.Contains(p.Id) ? 0.30 : 0.16;
                if (random.NextDouble() >= chance) continue;

                p.Mental.Leadership = Math.Min(17, p.Mental.Leadership + 1);
                if (p.Mental.DecisionMaking < 16 && random.NextDouble() < 0.5)
                    p.Mental.DecisionMaking = Math.Min(16, p.Mental.DecisionMaking + 1);
            }
        }

        return events;
    }
}
