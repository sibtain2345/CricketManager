using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 14 (§6.3): a technical FLAW as a remediation target. A small share of players carry a
/// specific fault - a big trigger movement, a weakness outside off, a stress-fracture action. A
/// scout spots it (and it becomes something an opponent can plan for); a good batting or bowling
/// coach works it out over a season or two; and <see cref="TechnicalFlaw.StressFractureAction"/>
/// carries a real injury cost while it stands.
///
/// Runs annually. RNG only at the seeder's own stream position (the annual rollover tail).
/// </summary>
public sealed class FlawRemediationService
{
    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        foreach (var p in world.Players)
        {
            if (p.IsRetired || p.AcademyTeamId is not null) continue;
            int age = p.Age(date);

            // --- seed a flaw (rare, young players only) ---
            if (p.TechnicalFlaw is null && age is >= 18 and <= 26 && random.NextDouble() < 0.035)
            {
                var flaws = Enum.GetValues<TechnicalFlaw>();
                var flaw = flaws[random.Next(flaws.Length)];
                // A pure bowler cannot have a batting flaw and vice versa.
                bool bowlingFlaw = flaw == TechnicalFlaw.StressFractureAction;
                if (bowlingFlaw && p.BowlingRole == BowlingRoleType.NotABowler) continue;
                if (!bowlingFlaw && p.PrimaryRole == PlayerRole.Bowler) continue;

                p.TechnicalFlaw = flaw;
                if (flaw == TechnicalFlaw.StressFractureAction)
                    p.Physical.InjuryProneness = Math.Min(20, p.Physical.InjuryProneness + 3);
                events.Add(new GameEvent(date, GameEventType.TechnicalFlaw,
                    $"The analysts have flagged a {Describe(flaw)} in {p.FullName}'s game.", p.Id, p.CurrentTeamId));
                continue;
            }

            // --- remediate an existing flaw ---
            if (p.TechnicalFlaw is { } existing)
            {
                var team = p.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var t) ? t : null;
                if (team is null) continue;

                bool bowlingFlaw = existing == TechnicalFlaw.StressFractureAction;
                var staff = world.Staff.Where(s => team.StaffIds.Contains(s.Id)).ToList();
                double coachQuality = bowlingFlaw
                    ? staff.Where(s => s.Role is StaffRole.BowlingCoach).Select(s => (double)s.TechnicalKnowledge).DefaultIfEmpty(0).Max()
                    : staff.Where(s => s.Role is StaffRole.BattingCoach).Select(s => (double)s.TechnicalKnowledge).DefaultIfEmpty(0).Max();
                coachQuality = Math.Max(coachQuality, team.Facilities.TrainingQuality / 20.0);

                double fixChance = Math.Clamp(coachQuality / 20.0 * 0.35 + p.Mental.Adaptability / 20.0 * 0.15, 0, 0.5);
                if (random.NextDouble() < fixChance)
                {
                    p.TechnicalFlaw = null;
                    if (bowlingFlaw)
                        p.Physical.InjuryProneness = Math.Max(1, p.Physical.InjuryProneness - 3);
                    else
                    {
                        p.Batting.Technique = Math.Min(20, p.Batting.Technique + 1);
                        p.RecalculateFormatSuitability();
                    }
                    events.Add(new GameEvent(date, GameEventType.TechnicalFlaw,
                        $"{p.FullName} has ironed out the {Describe(existing)} that dogged his early career.", p.Id, p.CurrentTeamId));
                }
            }
        }

        return events;
    }

    private static string Describe(TechnicalFlaw flaw) => flaw switch
    {
        TechnicalFlaw.TriggerMovementFault => "big trigger movement across his stumps",
        TechnicalFlaw.WeakOutsideOff => "tendency to chase the ball outside off",
        TechnicalFlaw.FrontFootLbwProne => "habit of playing around his front pad",
        TechnicalFlaw.StressFractureAction => "front-on, high-stress bowling action",
        _ => "gap in his strike rotation square of the wicket"
    };
}
