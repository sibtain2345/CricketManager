using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 8, Slices 8.1-8.3: the youth academy - the talent PIPELINE the world never had. Without
/// an intake of new players a long career sim only ages: the Phase 7 acceptance test deliberately
/// ran just six years for exactly this reason. This is what makes a multi-decade world work.
///
/// Three jobs:
/// - **Intake** (8.1): each club generates a small annual cohort of teenagers - low current
///   ability, a wide potential spread. Cohort SIZE and QUALITY scale with the club's
///   YouthDevelopmentQuality + its home ground's YouthFacilities + reputation, plus a light
///   nation-talent skew (some countries produce more pace, some more spin). Prospects are NOT
///   senior-squad-selectable (they are not in Team.SquadPlayerIds).
/// - **Development** (8.2): prospects develop through the SAME monthly TrainingService tick every
///   player does - no parallel engine - on a youth-accelerated curve (TrainingService recognises
///   a genuine teenager). This service does not run development itself.
/// - **Promotion / release** (8.2 + 8.3): once a year a club reviews its academy. A graduate who
///   is ready moves into the senior squad; a prospect who has plateaued is released to the free
///   agent pool (inert until Phase 9's market). The decision reads a SCOUTED estimate of the
///   prospect's potential (ScoutingAccuracyService), never the true value - so a weak-scouting
///   club genuinely releases a gem and over-promotes a dud, and a strong scouting department
///   gets it right. That is the recruitment-misjudgement story, made real.
///
/// Deliberately a Domain-layer service that generates its own Player entities (a narrow, stated
/// exception to "only WorldSeeder makes entities", the same one CoachRecruitmentService /
/// StaffRecruitmentService already take) - a hiring/intake flow needs to conjure a person, and the
/// Data layer's WorldSeeder is not reachable from Domain by design.
/// </summary>
public sealed class AcademyService
{
    private static readonly string[] FirstNames =
        { "Ayaan", "Bilal", "Cameron", "Dev", "Ethan", "Faisal", "Gareth", "Haris", "Ishaan",
          "Jamie", "Kabir", "Liam", "Musa", "Noah", "Omar", "Pranav", "Rohan", "Sami", "Taj", "Yusuf" };
    private static readonly string[] LastNames =
        { "Abbas", "Bennett", "Chandra", "D'Souza", "Ellis", "Farooqi", "Gill", "Harper", "Iyer",
          "Jadhav", "Kaur", "Lowe", "Mehta", "Nair", "Owens", "Pillai", "Rahman", "Shaw", "Trott", "Vaughan" };

    /// <summary>Below this age a prospect is a genuine teenager and TrainingService's fast-growth window applies at full strength.</summary>
    public const int IntakeMinAge = 15;
    public const int IntakeMaxAge = 18;

    /// <summary>Nobody is a first-team option straight out of the academy, and nobody stays an academy player past this age - by then he is either a senior squad member or he is released.</summary>
    public const int GraduationAge = 18;
    public const int AcademyHardCapAge = 22;

    // ---------------- intake ----------------

    /// <summary>
    /// One club's annual youth intake. Sets AcademyTeamId + CurrentTeamId + adds each id to
    /// team.AcademyPlayerIds. The caller adds the returned players to the world's player list
    /// (at the END of it - academy players stay at the tail so senior-player RNG consumption in
    /// the monthly/annual ticks is unchanged, per this project's determinism discipline).
    /// </summary>
    public IReadOnlyList<Player> GenerateIntake(Team team, Ground? homeGround, DateOnly asOf, Random random, int scoutingQuality,
        CricketManager.Domain.ValueObjects.CountryProfile? country = null)
    {
        int cohortSize = CohortSize(team, homeGround, random, country);
        var cohort = new List<Player>(cohortSize);

        // A well-resourced academy identifies and attracts better raw material and misjudges the
        // ceiling less - the same scouting number that drives the promotion/release call. Phase 10:
        // a nation with high board youth investment lifts the raw material a little further.
        double resource = Math.Clamp(
            (team.Facilities.YouthDevelopmentQuality * 0.5
             + (homeGround?.Facilities.YouthFacilities ?? 30) * 0.3
             + team.Reputation.Domestic * 0.2) / 100.0
            + (country is null ? 0 : (country.BoardYouthInvestment - 50) / 100.0 * 0.12)
            // Phase 16 (§15.6): a golden generation lifts the raw material, a fallow decade thins it.
            // Deliberately a modest nudge - real over a decade, not enough to make a nation's talent
            // cycle the thing that decides a world title.
            + (EraQualityFactor(country, asOf.Year) - 1.0) * 0.25, 0.05, 1.0);

        var profile = NationTalentProfile(team.Country, country);

        for (int i = 0; i < cohortSize; i++)
        {
            var role = PickRole(profile, random);
            var prospect = GenerateProspect(role, team, asOf, random, resource, scoutingQuality, profile);
            prospect.AcademyTeamId = team.Id;
            prospect.CurrentTeamId = team.Id;
            team.AcademyPlayerIds.Add(prospect.Id);
            cohort.Add(prospect);
        }

        return cohort;
    }

    /// <summary>
    /// Phase 16 (§15.6): where a nation's talent cycle sits this year - ~0.82 (a fallow decade) to
    /// ~1.18 (a golden generation), a slow ~14-year wave offset per nation by its
    /// GenerationCyclePhase. Deterministic - no RNG - so it never perturbs the annual-rollover stream.
    /// A world with no profile is a flat 1.0.
    /// </summary>
    public static double EraQualityFactor(CricketManager.Domain.ValueObjects.CountryProfile? country, int year)
    {
        if (country is null) return 1.0;
        double t = year / 11.0 + country.GenerationCyclePhase;
        return 1.0 + Math.Sin(t * 2 * Math.PI) * 0.14;
    }

    private static int CohortSize(Team team, Ground? homeGround, Random random,
        CricketManager.Domain.ValueObjects.CountryProfile? country = null)
    {
        double quality = team.Facilities.YouthDevelopmentQuality * 0.55
                       + (homeGround?.Facilities.YouthFacilities ?? 30) * 0.3
                       + team.Reputation.Domestic * 0.15;
        int baseCount = 3 + (int)Math.Round(quality / 100.0 * 4); // 3 - 7
        // Phase 10: a nation that genuinely produces more cricketers has bigger academy cohorts.
        // TalentProduction 55 = neutral; 88 (India) adds ~1, 45 subtracts ~0.5.
        int nationBump = country is null ? 0 : (int)Math.Round((country.TalentProduction - 55) / 33.0);
        return Math.Clamp(baseCount + nationBump + random.Next(-1, 2), 2, 9);
    }

    private Player GenerateProspect(PlayerRole role, Team team, DateOnly asOf, Random random, double resource, int scoutingQuality, NationProfile profile)
    {
        int age = random.Next(IntakeMinAge, IntakeMaxAge + 1);
        var dob = asOf.AddYears(-age).AddDays(-random.Next(0, 365));

        // Raw: current ability genuinely low, potential a wide punt - narrower and higher on
        // average at a well-resourced academy, a bigger gamble at a poor one.
        int current = random.Next(28, 34) + (int)Math.Round(resource * 18) + random.Next(-6, 7);
        current = Math.Clamp(current, 22, 78);
        int ceilingSpread = (int)Math.Round(35 + resource * 45); // 35 - 80
        int potential = Math.Clamp(current + random.Next(12, ceilingSpread + 1), current, 198);

        // A rare genuine prodigy - already well ahead for his age, and a high ceiling.
        bool prodigy = random.NextDouble() < 0.04 + resource * 0.05;
        if (prodigy)
        {
            current = Math.Clamp(current + random.Next(15, 30), current, 110);
            potential = Math.Clamp(Math.Max(potential, current + random.Next(30, 60)), current, 200);
        }

        var style = role is PlayerRole.Batsman or PlayerRole.WicketKeeper
            ? BowlingStyle.None
            : PickBowlingStyle(role, profile, random);

        var player = new Player
        {
            FirstName = FirstNames[random.Next(FirstNames.Length)],
            LastName = LastNames[random.Next(LastNames.Length)],
            Nationality = team.Country,
            DateOfBirth = dob,
            BattingHand = random.NextDouble() < 0.72 ? BattingHand.Right : BattingHand.Left,
            PrimaryRole = role,
            BowlingStyle = style,
            CurrentAbility = current,
            PotentialAbility = potential,
        };

        int band = (int)Math.Clamp(6 + resource * 5 + (prodigy ? 3 : 0), 4, 15);
        player.Batting = GenerateBatting(role, band, random);
        player.Bowling = GenerateBowling(role, band, random);
        player.Fielding = GenerateFielding(band + 1, random); // young legs field well
        player.Mental = GenerateMental(band - 1, random);      // the head lags the body at 16
        player.Physical = GeneratePhysical(band, random);

        // 8.4: individual peak timing. Teenagers skew toward NEEDING time (positive offset), with
        // a real spread and a personality lean - a prodigy tends to peak earlier, a SlowDeveloper
        // later.
        int peak = random.Next(-2, 5);
        player.Personality = RollPersonality(random, prodigy);
        if (player.Personality.HasFlag(PersonalityTrait.SlowDeveloper)) peak += 2;
        if (player.Personality.HasFlag(PersonalityTrait.FastLearner) || prodigy) peak -= 2;
        player.PeakAgeOffset = Math.Clamp(peak, -4, 6);

        player.SquadStatus = potential - current >= 40 || AbilityScale.CompositeAbilityToHundred(potential) >= 68
            ? SquadStatus.LongTermProject
            : SquadStatus.DevelopmentProspect;

        player.TrainingPlan.Intensity = TrainingIntensity.Normal;

        new RoleTraitDeriver().ApplyTo(player);
        player.RecalculateFormatSuitability();

        // A tiny starting reputation - a prodigy is already a name in age-group circles.
        double rep = prodigy ? 18 + random.Next(0, 14) : 3 + random.Next(0, 6);
        player.Reputation = new Reputation(domestic: Math.Round(rep, 1), continental: Math.Round(rep * 0.15, 1), worldwide: 0);

        return player;
    }

    private static PersonalityTrait RollPersonality(Random random, bool prodigy)
    {
        var traits = PersonalityTrait.None;
        double r = random.NextDouble();
        if (prodigy && r < 0.5) traits |= PersonalityTrait.FastLearner;
        else if (r < 0.18) traits |= PersonalityTrait.FastLearner;
        else if (r < 0.34) traits |= PersonalityTrait.SlowDeveloper;

        double p = random.NextDouble();
        if (p < 0.22) traits |= PersonalityTrait.Professional;
        else if (p < 0.34) traits |= PersonalityTrait.Lazy;
        if (random.NextDouble() < 0.15) traits |= PersonalityTrait.Ambitious;
        return traits;
    }

    // ---------------- annual review: promotion + release ----------------

    /// <summary>What a club's annual academy review did (or recommends). Applied is false when a human club kept the call and this is advice only.</summary>
    public sealed record AcademyReview(IReadOnlyList<GameEvent> Events, int Promoted, int Released);

    /// <summary>
    /// One club's annual academy review. Reads a SCOUTED estimate of each prospect's potential
    /// (never the true value) to decide who graduates and who is let go. With applyChanges false,
    /// nothing is mutated and the events are recommendations only.
    /// </summary>
    public AcademyReview ReviewAcademy(
        IReadOnlyList<Player> academyPlayers, Team team, DateOnly asOf, Random random, int scoutingQuality, bool applyChanges,
        bool graduateToOpenMarket = false)
    {
        var scout = new ScoutingAccuracyService(random);
        var events = new List<GameEvent>();
        int promoted = 0, released = 0;

        // How high a graduate is judged against - a rough "senior squad depth" bar for THIS club,
        // a strong club demanding more of a teenager than a weak one. Most graduates come in as
        // squad depth, not first-team ready, so the bars sit BELOW walk-into-the-XI level.
        double squadBar = 45 + Math.Clamp((team.Strength - 50), -20, 30) * 0.6;

        foreach (var prospect in academyPlayers.Where(p => p.AcademyTeamId == team.Id && !p.IsRetired).ToList())
        {
            int age = prospect.Age(asOf);
            int scoutedPotential = scout.EstimatePotentialAbility(prospect.PotentialAbility, scoutingQuality);
            double scoutedCeiling = AbilityScale.CompositeAbilityToHundred(scoutedPotential);
            double currentH = AbilityScale.CompositeAbilityToHundred(prospect.CurrentAbility);

            // Ready now: 18+, already at genuine squad-depth level.
            bool readyOnMerit = age >= GraduationAge && currentH >= squadBar - 8;
            // Worth blooding: 19+, a scouted ceiling worth investing in, and not miles off.
            bool promisingEnoughToBlood = age >= GraduationAge + 1 && scoutedCeiling >= 50 && currentH >= squadBar - 20;
            // Now-or-never: 20+, has shown enough that keeping him in the academy is wasting him.
            bool nowOrNever = age >= GraduationAge + 2 && scoutedCeiling >= 42 && currentH >= squadBar - 26;
            bool ageForcedOut = age >= AcademyHardCapAge;

            if (readyOnMerit || promisingEnoughToBlood || nowOrNever)
            {
                if (applyChanges)
                {
                    if (graduateToOpenMarket) GraduateToOpenMarket(prospect, team, scoutedCeiling >= 76);
                    else Promote(prospect, team, scoutedCeiling >= 76);
                }
                promoted++;
                events.Add(new GameEvent(asOf, GameEventType.SquadDecisionMade,
                    graduateToOpenMarket
                        ? $"{team.Name}'s academy graduates {prospect.FullName} ({age}) into the auction pool - a franchise cannot promote its own product directly."
                        : scoutedCeiling >= 76
                            ? $"{team.Name} promote {prospect.FullName} ({age}) from the academy - the standout of his age group."
                            : $"{team.Name} promote {prospect.FullName} ({age}) from the academy into the senior squad.",
                    prospect.Id, team.Id));
                continue;
            }

            bool plateaued = age >= GraduationAge + 2 && scoutedCeiling < 42 && currentH < squadBar - 24;
            if (plateaued || ageForcedOut)
            {
                if (applyChanges) Release(prospect, team);
                released++;
                events.Add(new GameEvent(asOf, GameEventType.SquadDecisionMade,
                    $"{team.Name} release {prospect.FullName} ({age}) - the academy has taken him as far as it can.",
                    prospect.Id, team.Id));
            }
        }

        return new AcademyReview(events, promoted, released);
    }

    private static void Promote(Player prospect, Team team, bool standout)
    {
        team.AcademyPlayerIds.Remove(prospect.Id);
        if (!team.SquadPlayerIds.Contains(prospect.Id)) team.SquadPlayerIds.Add(prospect.Id);
        prospect.AcademyTeamId = null;
        prospect.CurrentTeamId = team.Id;
        prospect.SquadStatus = SquadStatus.DevelopmentProspect;
        prospect.Morale.Adjust(standout ? 14 : 9);
        prospect.CoachTrust = Math.Min(100, prospect.CoachTrust + 6);

        // 8.6: the wonderkid becomes a known name the day he is promoted - a bump on top of the
        // small age-group reputation he already carries, which is what raises the scrutiny he then
        // walks out under.
        if (standout)
            prospect.Reputation = new Reputation(
                domestic: Math.Round(Math.Max(prospect.Reputation.Domestic, 32 + team.Reputation.Domestic * 0.15), 1),
                continental: Math.Round(prospect.Reputation.Continental + 6, 1),
                worldwide: prospect.Reputation.Worldwide);
    }

    /// <summary>
    /// Section M: a FRANCHISE academy graduate is never signed by his own franchise. He enters the
    /// open market as a genuine prospect (not a discard - positive morale, a real squad status, the
    /// standout's reputation bump) and is bought through the auction like anyone else, possibly by a
    /// rival. FreeAgentMarketService / FranchiseAuctionService pick him up from CurrentTeamId == null.
    /// </summary>
    private static void GraduateToOpenMarket(Player prospect, Team team, bool standout)
    {
        team.AcademyPlayerIds.Remove(prospect.Id);
        prospect.AcademyTeamId = null;
        prospect.ParentClubId = null;
        prospect.CurrentTeamId = null;
        prospect.SquadStatus = SquadStatus.DevelopmentProspect;
        prospect.Morale.Adjust(standout ? 12 : 7);
        if (standout)
            prospect.Reputation = new Reputation(
                domestic: Math.Round(Math.Max(prospect.Reputation.Domestic, 30 + team.Reputation.Domestic * 0.15), 1),
                continental: Math.Round(prospect.Reputation.Continental + 5, 1),
                worldwide: prospect.Reputation.Worldwide);
    }

    private static void Release(Player prospect, Team team)
    {
        team.AcademyPlayerIds.Remove(prospect.Id);
        prospect.AcademyTeamId = null;
        prospect.CurrentTeamId = null; // a free agent - inert until Phase 9's market
        prospect.SquadStatus = SquadStatus.Fringe;
        prospect.Morale.Adjust(-11);
    }

    // ---------------- nation talent profile ----------------

    internal readonly record struct NationProfile(double Pace, double Spin, double Bat, double Keeper, double Allrounder);

    /// <summary>
    /// A light, deliberately rough skew on what kind of players a country's academies produce -
    /// a stopgap until Phase 10's Country entity does this properly. Weights are relative, not
    /// probabilities. Subcontinent leans spin/batting; the pace nations lean quick bowling.
    /// </summary>
    internal static NationProfile NationTalentProfile(string country, CricketManager.Domain.ValueObjects.CountryProfile? profile = null)
    {
        var basis = country?.ToLowerInvariant() switch
        {
            "india" or "pakistan" or "sri lanka" or "bangladesh" or "afghanistan"
                => new NationProfile(Pace: 0.9, Spin: 1.7, Bat: 1.3, Keeper: 0.5, Allrounder: 0.8),
            "australia" or "south africa" or "new zealand"
                => new NationProfile(Pace: 1.8, Spin: 0.7, Bat: 1.1, Keeper: 0.5, Allrounder: 0.9),
            "england"
                => new NationProfile(Pace: 1.5, Spin: 0.9, Bat: 1.2, Keeper: 0.5, Allrounder: 1.1),
            "west indies"
                => new NationProfile(Pace: 1.6, Spin: 0.9, Bat: 1.2, Keeper: 0.4, Allrounder: 0.9),
            _ => new NationProfile(Pace: 1.2, Spin: 1.1, Bat: 1.2, Keeper: 0.5, Allrounder: 1.0)
        };

        // Phase 10: when a real CountryProfile is supplied, its PaceVsSpinTalentBias overrides the
        // hardcoded name table - 50 leaves the pace/spin split as-is, >50 shifts toward pace, <50
        // toward spin. Keeps the name table as the fallback for a world with no profiles seeded.
        if (profile is null) return basis;
        double shift = (profile.PaceVsSpinTalentBias - 50) / 50.0; // -1..+1
        double pace = Math.Clamp(basis.Pace * (1 + shift * 0.7), 0.3, 3.0);
        double spin = Math.Clamp(basis.Spin * (1 - shift * 0.7), 0.3, 3.0);
        return basis with { Pace = pace, Spin = spin };
    }

    private static PlayerRole PickRole(NationProfile p, Random random)
    {
        double total = p.Pace + p.Spin + p.Bat + p.Keeper + p.Allrounder;
        double r = random.NextDouble() * total;
        if ((r -= p.Bat) < 0) return PlayerRole.Batsman;
        if ((r -= p.Keeper) < 0) return PlayerRole.WicketKeeper;
        if ((r -= p.Allrounder) < 0) return random.Next(2) == 0 ? PlayerRole.BattingAllrounder : PlayerRole.BowlingAllrounder;
        return PlayerRole.Bowler; // pace + spin both land here; PickBowlingStyle splits them
    }

    private static BowlingStyle PickBowlingStyle(PlayerRole role, NationProfile p, Random random)
    {
        bool spin = random.NextDouble() * (p.Pace + p.Spin) < p.Spin;
        if (spin)
        {
            var s = new[] { BowlingStyle.RightArmOffSpin, BowlingStyle.RightArmLegSpin, BowlingStyle.LeftArmOrthodox, BowlingStyle.LeftArmChinaman };
            return s[random.Next(s.Length)];
        }
        var pace = new[]
        {
            BowlingStyle.RightArmFast, BowlingStyle.RightArmFastMedium, BowlingStyle.RightArmMediumFast, BowlingStyle.RightArmMedium,
            BowlingStyle.LeftArmFast, BowlingStyle.LeftArmFastMedium, BowlingStyle.LeftArmMedium
        };
        return pace[random.Next(pace.Length)];
    }

    // ---------------- compact attribute generation ----------------

    private static int Rnd(int band, Random random) => Math.Clamp(band + random.Next(-3, 4), 1, 20);

    private static BattingAttributes GenerateBatting(PlayerRole role, int band, Random random)
    {
        int b = role is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper ? band + 2 : Math.Max(2, band - 4);
        return new BattingAttributes
        {
            Technique = Rnd(b, random), Timing = Rnd(b, random), ShotSelection = Rnd(b, random), DefensiveAbility = Rnd(b, random),
            Aggression = Rnd(b, random), AgainstPace = Rnd(b, random), AgainstSpin = Rnd(b, random), ShortBallAbility = Rnd(b, random),
            SwingHandling = Rnd(b, random), SeamHandling = Rnd(b, random), SpinHandling = Rnd(b, random),
            DeathOverBatting = Rnd(b, random), PowerHitting = Rnd(b, random), StrikeRotation = Rnd(b, random),
            BoundaryHitting = Rnd(b, random), RiskManagement = Rnd(b, random)
        };
    }

    private static BowlingAttributes GenerateBowling(PlayerRole role, int band, Random random)
    {
        int b = role is PlayerRole.Bowler or PlayerRole.BowlingAllrounder or PlayerRole.BattingAllrounder ? band + 2 : Math.Max(1, band - 6);
        return new BowlingAttributes
        {
            Pace = Rnd(b, random), Accuracy = Rnd(b, random), Swing = Rnd(b, random), Seam = Rnd(b, random), Spin = Rnd(b, random),
            Variation = Rnd(b, random), Yorker = Rnd(b, random), Bouncer = Rnd(b, random), SlowerBall = Rnd(b, random),
            DeathBowling = Rnd(b, random), NewBallBowling = Rnd(b, random), MiddleOverBowling = Rnd(b, random),
            Containment = Rnd(b, random), AttackingAbility = Rnd(b, random)
        };
    }

    private static FieldingAttributes GenerateFielding(int band, Random random) => new()
    {
        Catching = Rnd(band, random), Reflexes = Rnd(band, random), Throwing = Rnd(band, random),
        GroundFielding = Rnd(band, random), Positioning = Rnd(band, random), BoundaryFielding = Rnd(band, random)
    };

    private static MentalAttributes GenerateMental(int band, Random random) => new()
    {
        Composure = Rnd(band, random), Concentration = Rnd(band, random), Confidence = Rnd(band, random),
        Determination = Rnd(band, random), Leadership = Rnd(band, random), PressureHandling = Rnd(band, random),
        DecisionMaking = Rnd(band, random), Adaptability = Rnd(band, random), Professionalism = Rnd(band, random),
        Consistency = Rnd(band, random), GameAwareness = Rnd(band, random),
        RunningCalling = Math.Clamp(band, 1, 20), // Phase 11: RNG-free centre value - never perturb the annual-rollover stream
        ReviewJudgement = Math.Clamp(band, 1, 20) // Phase 15: likewise RNG-free
    };

    private static PhysicalAttributes GeneratePhysical(int band, Random random) => new()
    {
        Fitness = Rnd(band, random), Stamina = Rnd(band, random), Strength = Rnd(band, random),
        Speed = Rnd(band, random), InjuryProneness = random.Next(3, 15), Recovery = Rnd(band + 1, random)
    };
}
