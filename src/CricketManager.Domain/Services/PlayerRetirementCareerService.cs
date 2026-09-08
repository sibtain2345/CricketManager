using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Seven-suggestions pass (NEW-C): a retired player's SECOND career. Real cricketers move into a
/// spread of roles when they stop playing - head coach, a specialist coach (batting / bowling /
/// fielding), scout / talent-ID, a national selector, a mentor (the legends - Dhoni, Dravid,
/// Ponting), commentary / administration, or simply out of the game. Which one depends on his
/// playing career (a big international name has options a journeyman does not) AND on what HE
/// wants - his temperament and long-term plans.
///
/// This replaces the old flat "a pedigreed retiree enters the coaching market" block. It runs from
/// ProcessPhase8Annual, consuming the shared annual Random at the same tail position that block did.
/// </summary>
public sealed class PlayerRetirementCareerService
{
    private readonly CoachRecruitmentService _coachRecruitment = new();

    /// <summary>What a retiring player wants to do next - a preference read from his temperament and standing.</summary>
    public enum Path { LeavesGame, HeadCoach, SpecialistCoach, Scout, Selector, Mentor }

    public IEnumerable<GameEvent> PlaceRetirees(WorldState world, IReadOnlyList<Player> newlyRetired, DateOnly date, Random random)
    {
        // Stable order (never by Guid) - this consumes the shared annual Random per retiree.
        foreach (var player in newlyRetired.OrderBy(p => p.LastName, StringComparer.Ordinal).ThenBy(p => p.FirstName, StringComparer.Ordinal).ThenBy(p => p.DateOfBirth))
        {
            double weight = CoachRecruitmentService.PlayingCareerWeightOf(player);
            var pref = PreferredPath(player, weight);

            // Even a suitable candidate only takes it up some of the time - not everyone coaches.
            double takeUp = pref switch
            {
                Path.Mentor => 0.7,
                Path.HeadCoach => 0.5,
                Path.SpecialistCoach => 0.42,
                Path.Selector => 0.35,
                Path.Scout => 0.3,
                _ => 0.0
            };
            if (pref == Path.LeavesGame || random.NextDouble() >= takeUp)
            {
                if (pref == Path.LeavesGame && weight >= 30)
                    yield return new GameEvent(date, GameEventType.PlayerCareerAfterCricket,
                        $"{player.FullName} steps away from the game after retiring - no coaching or media role, at least for now.", player.Id);
                continue;
            }

            switch (pref)
            {
                case Path.HeadCoach:
                {
                    var coach = _coachRecruitment.CreateFromRetiredPlayer(player, date);
                    world.Coaches.Add(coach);
                    yield return new GameEvent(date, GameEventType.PlayerCareerAfterCricket,
                        $"{coach.FullName} moves into coaching after retiring - he wants the top job, and his playing pedigree ({weight:N0}/100) opens doors.", coach.Id);
                    break;
                }
                case Path.Mentor:
                {
                    var staff = StaffRecruitmentService.CreateFromRetiredPlayer(player, StaffRole.Mentor, date);
                    world.Staff.Add(staff);
                    yield return new GameEvent(date, GameEventType.PlayerCareerAfterCricket,
                        $"{staff.FullName} takes on a mentor role - a revered name lending his experience to the next generation.", staff.Id);
                    break;
                }
                case Path.SpecialistCoach:
                {
                    var role = SpecialistRoleFor(player);
                    var staff = StaffRecruitmentService.CreateFromRetiredPlayer(player, role, date);
                    world.Staff.Add(staff);
                    yield return new GameEvent(date, GameEventType.PlayerCareerAfterCricket,
                        $"{staff.FullName} joins the coaching ranks as a {Describe(role)} after retiring.", staff.Id);
                    break;
                }
                case Path.Selector:
                {
                    var staff = StaffRecruitmentService.CreateFromRetiredPlayer(player, StaffRole.Selector, date);
                    world.Staff.Add(staff);
                    yield return new GameEvent(date, GameEventType.PlayerCareerAfterCricket,
                        $"{staff.FullName} is earmarked for national selection - he will be eligible for a panel seat once he has been retired five years.", staff.Id);
                    break;
                }
                case Path.Scout:
                {
                    var staff = StaffRecruitmentService.CreateFromRetiredPlayer(player, StaffRole.Scout, date);
                    world.Staff.Add(staff);
                    yield return new GameEvent(date, GameEventType.PlayerCareerAfterCricket,
                        $"{staff.FullName} moves into scouting - a good eye for a player and a lot of miles on the domestic circuit.", staff.Id);
                    break;
                }
            }
        }
    }

    private static Path PreferredPath(Player p, double weight)
    {
        bool legend = p.Reputation.Worldwide >= 75 && (p.Personality.HasFlag(PersonalityTrait.Leader) || p.Mental.Leadership >= 15);
        bool ambitious = p.Personality.HasFlag(PersonalityTrait.Ambitious);
        bool professional = p.Personality.HasFlag(PersonalityTrait.Professional);
        bool teamOriented = p.Personality.HasFlag(PersonalityTrait.TeamOriented);
        bool disengaged = p.Personality.HasFlag(PersonalityTrait.Lazy)
                          || (p.Personality.HasFlag(PersonalityTrait.MoneyFocused) && !ambitious);
        int caps = p.Experience.InternationalMatches;
        bool meetsSelectorCaps = caps >= 7 || p.Experience.MatchesIn(MatchFormat.Test) >= 7
                                 || p.Experience.TotalMatches >= 30
                                 || (caps >= 10 && p.Experience.TotalMatches >= 20);

        if (disengaged && !legend) return Path.LeavesGame;
        if (legend && (ambitious || teamOriented)) return Path.Mentor;
        if (weight >= 55 && (ambitious || p.Personality.HasFlag(PersonalityTrait.Leader)) && p.Mental.GameAwareness >= 11)
            return Path.HeadCoach;
        if (weight >= 25 && (professional || teamOriented || p.Mental.GameAwareness >= 12))
            return Path.SpecialistCoach;
        if (weight >= 30 && meetsSelectorCaps && !p.Personality.HasFlag(PersonalityTrait.Lazy))
            return Path.Selector;
        if (weight >= 8)
            return Path.Scout;
        return Path.LeavesGame;
    }

    private static StaffRole SpecialistRoleFor(Player p) => p.PrimaryRole switch
    {
        PlayerRole.Bowler => StaffRole.BowlingCoach,
        PlayerRole.BowlingAllrounder => p.Bowling.Pace + p.Bowling.Spin > p.Batting.Technique ? StaffRole.BowlingCoach : StaffRole.BattingCoach,
        PlayerRole.WicketKeeper => StaffRole.FieldingCoach,
        _ => StaffRole.BattingCoach
    };

    private static string Describe(StaffRole r) => r switch
    {
        StaffRole.BattingCoach => "batting coach",
        StaffRole.BowlingCoach => "bowling coach",
        StaffRole.FieldingCoach => "fielding coach",
        _ => r.ToString()
    };
}
