using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-5 rectification pass, Wave 5: mentoring groups (the Training brief's Section 7,
/// deferred until the squad-culture foundation existed - it now does).
///
/// A senior pro working with a junior does three things, all modest and all real:
/// - accelerates the junior's development in an area the senior is genuinely good at (a channel
///   on top of Wave 2's training and match experience, feeding the same RecentDevelopmentByPlayer
///   accumulator so Wave 4's staff signal sees it);
/// - slowly steadies the junior's temperament - a Professional mentor's habits rub off, a Lazy
///   or Inconsistent streak can be worn down - but only for a genuinely compatible, long-running
///   pairing, never a quick fix;
/// - lifts both their morale a little, the mentee more.
///
/// Pairings are DYNAMIC: ReviewGroups dissolves them as juniors graduate or seniors leave, and
/// forms new ones. Compatibility is a real gate - a clash of personalities barely helps and the
/// pairing is simply not made.
/// </summary>
public sealed class MentoringService
{
    private readonly DressingRoomService _room = new();

    /// <summary>Above this compatibility, a pairing is worth making. Below it, the junior is left unmentored rather than forced into a partnership that grates.</summary>
    public const double MinimumCompatibility = 45;

    /// <summary>How well a mentor and mentee would actually work together, 0-100.</summary>
    public double Compatibility(Player mentor, Player mentee, DateOnly asOf)
    {
        // The mentor has to be a genuine senior with something to teach.
        double seniority = _room.Standing(mentor, asOf);
        double gap = seniority - _room.Standing(mentee, asOf);
        if (gap < 12) return 0; // not enough of a gap for a mentoring relationship to mean anything

        double mentorQuality = seniority * 0.4
            + AbilityScale.AttributeToHundred(mentor.Mental.Leadership) * 0.3
            + (mentor.Personality.HasFlag(PersonalityTrait.Professional) ? 12 : 0)
            + (mentor.Personality.HasFlag(PersonalityTrait.TeamOriented) ? 10 : 0)
            + (mentor.Personality.HasFlag(PersonalityTrait.Leader) ? 8 : 0)
            - (mentor.Personality.HasFlag(PersonalityTrait.Lazy) ? 15 : 0);

        double menteeFit = 50
            + (mentee.Personality.HasFlag(PersonalityTrait.FastLearner) ? 12 : 0)
            + (mentee.Personality.HasFlag(PersonalityTrait.Professional) ? 8 : 0)
            + (mentee.Personality.HasFlag(PersonalityTrait.TeamOriented) ? 6 : 0)
            - (mentee.Personality.HasFlag(PersonalityTrait.Lazy) ? 14 : 0)
            - (mentee.Personality.HasFlag(PersonalityTrait.MoneyFocused) ? 6 : 0);

        // A shared discipline helps - a batter learns most from a batter.
        bool sharedDiscipline = SameDiscipline(mentor, mentee);

        return Math.Clamp(mentorQuality * 0.55 + menteeFit * 0.45 + (sharedDiscipline ? 6 : -4), 0, 100);
    }

    private static bool SameDiscipline(Player a, Player b)
    {
        bool aBat = a.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper;
        bool bBat = b.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper;
        return aBat == bBat;
    }

    /// <summary>
    /// Reviews one team's mentoring groups: dissolves any where the mentor or mentee has left the
    /// team, retired, or where the mentee has grown out of a junior role; then pairs each still-
    /// unmentored junior with the best compatible senior who is not already stretched across two
    /// mentees. Mutates `allGroups` in place (adds/removes) and returns the groups now active for
    /// this team.
    /// </summary>
    /// <param name="clubMentorBonus">
    /// Post-Phase-6 (section C): a hired club Mentor (a senior figure attached specifically to
    /// mentor young players) adds this to a new pairing's strength AND lowers the compatibility
    /// gate, so a promising junior with only a weak senior team-mate still gets developed. 0 when
    /// there is no club mentor.
    /// </param>
    public IReadOnlyList<MentoringGroup> ReviewGroups(Team team, IReadOnlyList<Player> squad, IList<MentoringGroup> allGroups, DateOnly date, double clubMentorBonus = 0)
    {
        var byId = squad.Where(p => !p.IsRetired).ToDictionary(p => p.Id);

        // dissolve stale
        var teamGroups = allGroups.Where(g => g.TeamId == team.Id).ToList();
        foreach (var g in teamGroups)
        {
            bool mentorGone = !byId.TryGetValue(g.MentorId, out var m) || m.CurrentTeamId != team.Id;
            bool menteeGone = !byId.TryGetValue(g.MenteeId, out var e) || e.CurrentTeamId != team.Id;
            bool graduated = !menteeGone && _room.RoleOf(byId[g.MenteeId], date) is DressingRoomRole.Established or DressingRoomRole.SeniorPro;
            if (mentorGone || menteeGone || graduated)
                allGroups.Remove(g);
        }

        var active = allGroups.Where(g => g.TeamId == team.Id).ToList();
        var mentored = active.Select(g => g.MenteeId).ToHashSet();
        var mentorLoad = active.GroupBy(g => g.MentorId).ToDictionary(gr => gr.Key, gr => gr.Count());

        var juniors = squad.Where(p => !p.IsRetired && p.CurrentTeamId == team.Id
            && _room.RoleOf(p, date) is DressingRoomRole.Junior or DressingRoomRole.SquadPlayer
            && !mentored.Contains(p.Id)).ToList();

        var seniors = squad.Where(p => !p.IsRetired && p.CurrentTeamId == team.Id
            && _room.RoleOf(p, date) is DressingRoomRole.Established or DressingRoomRole.SeniorPro).ToList();

        double gate = Math.Max(20, MinimumCompatibility - clubMentorBonus * 0.6); // section C: a club mentor lowers the bar
        foreach (var junior in juniors.OrderByDescending(j => j.PotentialAbility)) // develop the best prospects first
        {
            Player? best = null;
            double bestCompat = gate;
            foreach (var senior in seniors)
            {
                if (mentorLoad.TryGetValue(senior.Id, out var load) && load >= 2) continue;
                double c = Compatibility(senior, junior, date);
                if (c > bestCompat) { bestCompat = c; best = senior; }
            }

            if (best is null) continue;

            allGroups.Add(new MentoringGroup
            {
                TeamId = team.Id,
                MentorId = best.Id,
                MenteeId = junior.Id,
                FormedDate = date,
                Strength = Math.Clamp(bestCompat + clubMentorBonus, 0, 100)
            });
            mentorLoad[best.Id] = mentorLoad.TryGetValue(best.Id, out var l) ? l + 1 : 1;
        }

        return allGroups.Where(g => g.TeamId == team.Id).ToList();
    }

    /// <summary>
    /// One period of a mentoring pairing's effect. periodFraction (1/12 monthly) scales the
    /// development roll; the personality drift and morale lift are gated separately on a genuinely
    /// strong, established pairing.
    /// </summary>
    /// <param name="world">Optional - feeds the development into RecentDevelopmentByPlayer for Wave 4's staff signal.</param>
    public void ApplyMentoring(MentoringGroup group, Player mentor, Player mentee, Random random, double periodFraction, DateOnly date, WorldState? world = null)
    {
        if (mentor.IsRetired || mentee.IsRetired) return;
        periodFraction = Math.Clamp(periodFraction, 0, 1);

        double strength = Math.Clamp(group.Strength, 0, 100) / 100.0;

        // --- development acceleration ---
        // Calibrated for the monthly tick (periodFraction 1/12) - `step` keeps it proportional at
        // any other cadence. A strong pairing with a young, high-headroom mentee is a real,
        // visible boost over a season; a weak one is barely anything.
        double step = periodFraction * 12;
        double headroom = Math.Clamp((mentee.PotentialAbility - mentee.CurrentAbility) / 40.0, 0, 1);
        double devChance = 0.045 * strength * (0.25 + headroom) * step;
        if (headroom > 0 && random.NextDouble() < devChance)
        {
            NudgeMenteeTowardMentorStrength(mentor, mentee, random);
            mentee.CurrentAbility = Math.Clamp(mentee.CurrentAbility + 2, 1, mentee.PotentialAbility);
            mentee.RecalculateFormatSuitability();
            if (world is not null)
                world.RecentDevelopmentByPlayer[mentee.Id] =
                    world.RecentDevelopmentByPlayer.TryGetValue(mentee.Id, out var acc) ? acc + 1 : 1;
        }

        // --- temperament drift (rare, only for a strong long-running pairing) ---
        int monthsTogether = (date.DayNumber - group.FormedDate.DayNumber) / 30;
        if (strength > 0.65 && monthsTogether >= 12 && random.NextDouble() < 0.02 * periodFraction * 12)
        {
            if (mentor.Personality.HasFlag(PersonalityTrait.Professional) && !mentee.Personality.HasFlag(PersonalityTrait.Professional))
                mentee.Personality |= PersonalityTrait.Professional;
            else if (mentee.Personality.HasFlag(PersonalityTrait.Lazy))
                mentee.Personality &= ~PersonalityTrait.Lazy;
            else if (mentee.Personality.HasFlag(PersonalityTrait.Inconsistent) && mentor.Personality.HasFlag(PersonalityTrait.Consistent))
                mentee.Personality &= ~PersonalityTrait.Inconsistent;
        }

        // --- morale ---
        mentee.Morale.Adjust(strength * 1.5 * periodFraction);
        mentor.Morale.Adjust(strength * 0.5 * periodFraction);
    }

    private static void NudgeMenteeTowardMentorStrength(Player mentor, Player mentee, Random random)
    {
        // Pick the mentor's own strongest area within the mentee's discipline and nudge the
        // corresponding mentee attribute.
        bool bat = mentee.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper;
        if (bat)
        {
            var options = new (int MentorVal, Action Apply)[]
            {
                (mentor.Batting.Technique, () => mentee.Batting.Technique = Clamp(mentee.Batting.Technique + 1)),
                (mentor.Batting.ShotSelection, () => mentee.Batting.ShotSelection = Clamp(mentee.Batting.ShotSelection + 1)),
                (mentor.Batting.AgainstSpin, () => mentee.Batting.AgainstSpin = Clamp(mentee.Batting.AgainstSpin + 1)),
                (mentor.Mental.Concentration, () => mentee.Mental.Concentration = Clamp(mentee.Mental.Concentration + 1)),
                (mentor.Mental.GameAwareness, () => mentee.Mental.GameAwareness = Clamp(mentee.Mental.GameAwareness + 1)),
            };
            options.OrderByDescending(o => o.MentorVal).First().Apply();
        }
        else
        {
            var options = new (int MentorVal, Action Apply)[]
            {
                (mentor.Bowling.Accuracy, () => mentee.Bowling.Accuracy = Clamp(mentee.Bowling.Accuracy + 1)),
                (mentor.Bowling.Variation, () => mentee.Bowling.Variation = Clamp(mentee.Bowling.Variation + 1)),
                (mentor.Bowling.Containment, () => mentee.Bowling.Containment = Clamp(mentee.Bowling.Containment + 1)),
                (mentor.Mental.Composure, () => mentee.Mental.Composure = Clamp(mentee.Mental.Composure + 1)),
                (mentor.Mental.GameAwareness, () => mentee.Mental.GameAwareness = Clamp(mentee.Mental.GameAwareness + 1)),
            };
            options.OrderByDescending(o => o.MentorVal).First().Apply();
        }
    }

    private static int Clamp(int v) => Math.Clamp(v, 1, 20);
}
