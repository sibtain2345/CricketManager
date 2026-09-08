using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Job-market follow-up: produces coach candidates for a vacant head-coach job - the same real
/// gap StaffRecruitmentService closed for backroom staff, applied to the top job. WorldSeeder
/// does not generate Coach entities at all (confirmed by grep before Part 2 of this phase, and
/// unchanged since) - this is not a duplicate of anything, it is the first time this codebase has
/// ever produced one. Deliberately reuses Coach.SeedAttributesFromHistory() rather than hand-rolling
/// a second attribute-generation formula - that method already turns a PlayingExperience/
/// CoachingLicense background into real attributes AND a starting Reputation in one call, which is
/// exactly what a freshly-generated candidate needs and is already tested, shipped code.
/// </summary>
public sealed class CoachRecruitmentService
{
    private static readonly string[] FirstNames =
    {
        "Richard", "Anthony", "Simon", "Gary", "Martin", "Stuart", "Younis", "Waqar",
        "Ehsan", "Salman", "Adnan", "Kevin", "Peter", "Ian", "Dean", "Ricky"
    };

    private static readonly string[] LastNames =
    {
        "Hayward", "Sutton", "Marshall", "Pearce", "Whitfield", "Rana", "Qureshi",
        "Latif", "Chaudhry", "Sinclair", "Bramwell", "Osei", "Naidoo", "Ferris"
    };

    /// <summary>
    /// A real shortlist for one vacancy - a genuine standout background plus a step-down tail, the
    /// same tiered construction StaffRecruitmentService already established (and for the same
    /// reason: a real shortlist is not four independent coin flips).
    /// </summary>
    public IReadOnlyList<Coach> GenerateShortlist(DateOnly asOf, Random random, int count = 4)
    {
        var candidates = new List<Coach>(count);
        for (int i = 0; i < count; i++)
            candidates.Add(GenerateCandidate(asOf, random, tier: i));

        return candidates;
    }

    private static Coach GenerateCandidate(DateOnly asOf, Random random, int tier)
    {
        var (experience, license) = tier switch
        {
            0 => (PlayingExperience.InternationalStar, CoachingLicense.InternationalElite),
            1 => (PlayingExperience.International, CoachingLicense.Advanced),
            2 => (PlayingExperience.FirstClass, CoachingLicense.Level2),
            _ => (PlayingExperience.DomesticLevel, CoachingLicense.Level1)
        };

        var philosophies = Enum.GetValues<CoachingPhilosophy>();

        var coach = new Coach
        {
            FirstName = FirstNames[random.Next(FirstNames.Length)],
            LastName = LastNames[random.Next(LastNames.Length)],
            DateOfBirth = asOf.AddYears(-(38 + random.Next(0, 20))).AddDays(-random.Next(0, 365)),
            PlayingExperience = experience,
            License = license,
            Philosophy = philosophies[random.Next(philosophies.Length)],
            // Phase 12 (§4.6): most coaches are all-format; a minority specialise.
            FormatFocus = random.Next(5) switch { 0 => FormatSpecialisation.RedBall, 1 => FormatSpecialisation.WhiteBall, _ => FormatSpecialisation.AllFormats }
        };

        coach.SeedAttributesFromHistory();
        return coach;
    }

    /// <summary>
    /// Phase 8, Slice 8.6: turns a player who has just retired into a rookie coach on the open
    /// market - a low licence, a real playing pedigree, and the tactical/technical smarts he showed
    /// as a player. This closes the ecosystem loop: the world generates its own future coaches from
    /// its own retiring greats, instead of CoachRecruitmentService conjuring every candidate from
    /// nothing. He is the SAME person - same name, same date of birth, same nationality. Returned
    /// as an unemployed free agent; the job market / an AI club hires him when a chair opens.
    /// </summary>
    /// <summary>
    /// NEW-C: 0-100 - how substantial a playing career this was, weighing international caps most,
    /// then reputation, longevity and leadership. What a former player's standing as a coach /
    /// specialist / selector / mentor is scaled from, and the gate on the mentor role (legends only).
    /// </summary>
    public static double PlayingCareerWeightOf(Player player)
    {
        double caps = Math.Clamp(player.Experience.InternationalMatches / 100.0, 0, 1) * 42
                      + Math.Clamp(player.Experience.TotalMatches / 300.0, 0, 1) * 12;
        double reputation = Math.Clamp(player.Reputation.Worldwide * 0.6 + player.Reputation.Continental * 0.4, 0, 100) * 0.30;
        double longevity = Math.Clamp(player.Experience.SeasonsPlayed / 18.0, 0, 1) * 8;
        double leadership = (player.Personality.HasFlag(PersonalityTrait.Leader) ? 4 : 0)
                            + Math.Clamp((player.Mental.Leadership - 10) / 10.0, 0, 1) * 4;
        return Math.Clamp(caps + reputation + longevity + leadership, 0, 100);
    }

    public Coach CreateFromRetiredPlayer(Player player, DateOnly retiredOn)
    {
        var experience = player.Reputation.Worldwide >= 40 || player.Experience.InternationalMatches >= 40
            ? PlayingExperience.InternationalStar
            : player.Experience.InternationalMatches >= 15
                ? PlayingExperience.International
                : player.Experience.TotalMatches >= 60
                    ? PlayingExperience.FirstClass
                    : PlayingExperience.DomesticLevel;

        // A senior international captain-type gets the FormerCaptain tier, which reads as leadership
        // pedigree - a heavy-cap player who was also a genuine leader.
        if (experience == PlayingExperience.InternationalStar
            && player.Personality.HasFlag(PersonalityTrait.Leader)
            && player.Mental.Leadership >= 15)
            experience = PlayingExperience.FormerCaptain;

        var philosophy = player.Personality switch
        {
            var p when p.HasFlag(PersonalityTrait.Aggressive) || p.HasFlag(PersonalityTrait.RiskTaker) => CoachingPhilosophy.Aggressive,
            var p when p.HasFlag(PersonalityTrait.Defensive) => CoachingPhilosophy.Defensive,
            var p when p.HasFlag(PersonalityTrait.Professional) => CoachingPhilosophy.PerformanceFocused,
            var p when p.HasFlag(PersonalityTrait.TeamOriented) => CoachingPhilosophy.LongTermDevelopment,
            _ => CoachingPhilosophy.PerformanceFocused
        };

        var coach = new Coach
        {
            FirstName = player.FirstName,
            LastName = player.LastName,
            DateOfBirth = player.DateOfBirth,
            Nationality = player.Nationality,
            PlayingExperience = experience,
            License = CoachingLicense.Basic,   // just starting out - progresses over a coaching career
            Philosophy = philosophy
        };
        coach.SeedAttributesFromHistory();

        // The cricket brain he had as a player carries straight into the dugout.
        int Bump(int v, int by) => Math.Clamp(v + by, 1, 20);
        int smarts = (int)Math.Round((player.Mental.GameAwareness + player.Mental.DecisionMaking) / 2.0 / 20.0 * 4);
        coach.Attributes.TacticalKnowledge = Bump(coach.Attributes.TacticalKnowledge, smarts);
        coach.Attributes.MatchReading = Bump(coach.Attributes.MatchReading, smarts);
        coach.Attributes.TechnicalKnowledge = Bump(coach.Attributes.TechnicalKnowledge,
            (int)Math.Round(AbilityScale.CompositeAbilityToHundred(player.CurrentAbility) / 100.0 * 3));

        coach.PlayingCareerWeight = PlayingCareerWeightOf(player);
        coach.FromPlayerId = player.Id;

        // §4.7: a former player's dream job is the club he played for.
        coach.DreamJobTeamId = player.ParentClubId ?? player.CurrentTeamId;

        // §4.6: a coach's format lens follows his own playing diet - a Test-heavy career makes a
        // red-ball coach, a T20-heavy one a white-ball coach.
        int testCaps = player.Experience.MatchesIn(MatchFormat.Test);
        int whiteCaps = player.Experience.MatchesIn(MatchFormat.ODI) + player.Experience.MatchesIn(MatchFormat.T20);
        coach.FormatFocus = testCaps > whiteCaps * 1.6 ? FormatSpecialisation.RedBall
            : whiteCaps > testCaps * 2.0 ? FormatSpecialisation.WhiteBall
            : FormatSpecialisation.AllFormats;

        return coach;
    }
}
