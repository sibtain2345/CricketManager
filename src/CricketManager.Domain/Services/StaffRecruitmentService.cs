using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 5 Part 2: produces candidates for a vacant backroom role - a real gap, not a small one:
/// StaffMember/StaffRole have existed since Phase 4's analyst work, but nothing in this codebase
/// has ever generated one. WorldSeeder itself does not generate Coaches either (confirmed by
/// grep before writing this) - there is no existing "conjure a plausible named person" pattern
/// for a backroom appointment to follow, unlike a player, which WorldSeeder already knows how to
/// produce in depth. Kept deliberately small and self-contained here in the Domain layer (a
/// modest name pool, not WorldSeeder's own) rather than reaching into the Data-layer seeder,
/// which the Domain project has zero dependency on by design - generating a NEW named entity
/// has, until now, only ever been WorldSeeder's job; this is a first, narrow exception scoped
/// specifically to hiring a shortlist, not a second parallel world-seeding system.
/// </summary>
public sealed class StaffRecruitmentService
{
    private static readonly string[] FirstNames =
    {
        "James", "Michael", "David", "Andrew", "Chris", "Steven", "Mark", "Paul", "Grant",
        "Trevor", "Neil", "Craig", "Shane", "Wasim", "Imran", "Rashid", "Farhan", "Bilal"
    };

    private static readonly string[] LastNames =
    {
        "Wright", "Harper", "Bishop", "Carter", "Foster", "Reid", "Mahmood", "Farooq",
        "Iqbal", "Sharma", "Clarke", "Sullivan", "Hendricks", "Malik", "Osman", "Grey"
    };

    /// <summary>
    /// A shortlist for one vacant role. Deliberately varied rather than uniformly randomised: a
    /// real shortlist has a genuine standout, a couple of solid options, and at least one who is
    /// simply cheaper/less proven, which is what actually makes a hiring decision a decision.
    /// </summary>
    public IReadOnlyList<StaffMember> GenerateShortlist(StaffRole role, DateOnly asOf, Random random, int count = 4)
    {
        var candidates = new List<StaffMember>(count);
        for (int i = 0; i < count; i++)
            candidates.Add(GenerateCandidate(role, asOf, random, tier: i));

        return candidates;
    }

    /// <summary>
    /// NEW-C: a retired player takes up a specialist / scout / selector / mentor role. His playing
    /// technical understanding and standing carry across; a batter makes a natural batting coach, a
    /// bowler a bowling coach. His caps and retirement date are recorded so a national selection
    /// panel (NEW-B) can check the ICC-style eligibility threshold.
    /// </summary>
    public static StaffMember CreateFromRetiredPlayer(Player player, StaffRole role, DateOnly retiredOn)
    {
        double weight = CoachRecruitmentService.PlayingCareerWeightOf(player);
        int band = (int)Math.Clamp(6 + weight / 12.0, 6, 17);
        int tech = (int)Math.Clamp(band + (player.Mental.GameAwareness - 10) / 4.0, 3, 20);
        int comms = (int)Math.Clamp(band + (player.Personality.HasFlag(PersonalityTrait.Leader) ? 3 : 0), 3, 20);

        return new StaffMember
        {
            FirstName = player.FirstName,
            LastName = player.LastName,
            Role = role,
            DateOfBirth = player.DateOfBirth,
            Nationality = player.Nationality,
            Analysis = (int)Math.Clamp(band + (player.Mental.DecisionMaking - 10) / 5.0, 3, 20),
            Statistics = Math.Clamp(band - 1, 3, 20),
            TechnicalKnowledge = tech,
            Communication = comms,
            Diligence = (int)Math.Clamp(band + (player.Mental.Professionalism - 10) / 5.0, 3, 20),
            YearsExperience = 0,
            Reputation = Math.Clamp(weight * 0.6 + 15, 15, 90),
            PlayingCareerWeight = weight,
            FromPlayerId = player.Id,
            RetiredAsPlayerOn = retiredOn,
            Adaptability = player.Personality.HasFlag(PersonalityTrait.Professional) ? 13 : 10,
            Ambition = player.Personality.HasFlag(PersonalityTrait.Ambitious) ? 15 : 9,
            Philosophy = player.Personality.HasFlag(PersonalityTrait.Aggressive) ? CoachingPhilosophy.Aggressive
                : player.Personality.HasFlag(PersonalityTrait.TeamOriented) ? CoachingPhilosophy.LongTermDevelopment
                : CoachingPhilosophy.PerformanceFocused,
        };
    }

    private static StaffMember GenerateCandidate(StaffRole role, DateOnly asOf, Random random, int tier)
    {
        // Tier 0 is the standout, the rest step down - a real shortlist is not four coin flips.
        int qualityBand = tier switch { 0 => 15, 1 => 12, 2 => 9, _ => 6 };
        int spread = 3;

        int Roll(int centre) => Math.Clamp(centre + random.Next(-spread, spread + 1), 1, 20);

        int years = Math.Clamp(qualityBand - 4 + random.Next(-2, 5), 0, 25);

        var philosophies = Enum.GetValues<CoachingPhilosophy>();

        return new StaffMember
        {
            FirstName = FirstNames[random.Next(FirstNames.Length)],
            LastName = LastNames[random.Next(LastNames.Length)],
            Role = role,
            DateOfBirth = asOf.AddYears(-(28 + years)).AddDays(-random.Next(0, 365)),
            Analysis = Roll(qualityBand),
            Statistics = Roll(qualityBand),
            TechnicalKnowledge = Roll(qualityBand),
            Communication = Roll(qualityBand),
            Diligence = Roll(qualityBand),
            YearsExperience = years,
            // Post-Phase-6 (section A/G): a working philosophy, and the traits the job market reads.
            Philosophy = philosophies[random.Next(philosophies.Length)],
            Adaptability = Roll(qualityBand),
            Ambition = Math.Clamp(qualityBand - 2 + random.Next(-3, 6), 1, 20)
        };
    }
}
