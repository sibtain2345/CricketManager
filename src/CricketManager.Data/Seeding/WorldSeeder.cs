using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.Services;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Data.Seeding;

/// <summary>Phase 6, Slice 6.7: everything a small multi-country world with international cricket needs, ready to load into a WorldState.</summary>
public sealed record InternationalWorld(
    List<Team> Teams,
    List<Player> Players,
    List<Ground> Grounds,
    List<Competition> Competitions,
    List<CompetitionSeason> Seasons,
    List<Fixture> Fixtures,
    List<Rivalry> Rivalries,
    List<Team> NationalTeams,
    List<Domain.ValueObjects.NationalPool> NationalPools,
    List<Umpire> Umpires)
{
    /// <summary>Phase 9, Slice 9.0: every player's starting domestic contract. Empty until GeneratePlayerContracts runs.</summary>
    public List<PlayerContract> PlayerContracts { get; init; } = new();

    /// <summary>Phase 9, Slice 9.5: the franchise-league teams (also present in Teams). Their squads are empty at seed time - the auction fills them.</summary>
    public List<Team> FranchiseTeams { get; init; } = new();

    /// <summary>
    /// Post-Phase-7/8/9 rectification (Sections B/C): one administrative/economic profile per country -
    /// domestic ownership model, hemisphere (drives the transfer-window calendar), relative
    /// cricket-economy scale (extends WorldState.MarketIndex), and foreign-domestic-player rules.
    /// Load into <see cref="Domain.Services.WorldState.CountryProfiles"/>.
    /// </summary>
    public Dictionary<string, CountryProfile> CountryProfiles { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Phase 12: the media pundit roster.</summary>
    public List<Domain.ValueObjects.Pundit> Pundits { get; init; } = new();

    /// <summary>Phase 13 (§9.6): the persistent player agents, with their initial client rosters.</summary>
    public List<PlayerAgent> Agents { get; init; } = new();
}

/// <summary>
/// Section 74/75: MVP scope is one country, 4-8 teams, small player database.
/// This generates a believable starting world so the game is playable end to end
/// before the full real-world statistical database (Section 14/66) is wired up.
///
/// Attribute distributions are role-shaped (a bowler doesn't get elite batting
/// stats by chance) but individual values are randomized within a band so no
/// two generated squads are identical.
/// </summary>
public sealed class WorldSeeder
{
    private readonly Random _random;
    private readonly RoleTraitDeriver _roleTraitDeriver = new();
    private readonly SquadStatusService _squadStatus = new();
    private static readonly string[] FirstNames =
        { "Ahmed", "Bilal", "Danish", "Fahad", "Hamza", "Imran", "Junaid", "Kamran",
          "Osman", "Rizwan", "Saad", "Talha", "Usman", "Waqar", "Zain", "Adeel" };
    private static readonly string[] LastNames =
        { "Khan", "Malik", "Sheikh", "Butt", "Chaudhry", "Raza", "Iqbal", "Farooq",
          "Ansari", "Hussain", "Baig", "Qureshi", "Mirza", "Abbasi", "Awan" };

    /// <summary>
    /// The date the world is a snapshot OF. Every generated age is measured from here, never
    /// from DateTime.Today - a career starting in June 2026 must produce a squad that is the
    /// right age in June 2026, and must produce the SAME squad whenever it is seeded. Reading
    /// the machine clock meant a save created today and one created next year had different
    /// players from the same seed, and that ages drifted with real-world time.
    ///
    /// The same principle is what real-world data import relies on: the database is a snapshot
    /// at the start date, carrying each player's actual date of birth, career record and
    /// experience as of that day, and everything after it is simulated forward from there.
    /// </summary>
    private readonly DateOnly _worldStartDate;

    /// <summary>Phase 6, Slice 6.7: the nationality stamped on generated players - set per call by GenerateStarterWorld so a multi-country world's players belong to the right country. Defaults to Pakistan for every pre-6.7 caller.</summary>
    private string _nationality = "Pakistan";
    private static readonly string[] HeritagePool =
        { "England", "Australia", "New Zealand", "South Africa", "Ireland", "Scotland", "Netherlands", "West Indies", "India", "Pakistan" };


    public WorldSeeder(int? seed = null, DateOnly? worldStartDate = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _worldStartDate = worldStartDate ?? new DateOnly(2026, 6, 1);
    }

    /// <summary>
    /// Phase 17: drops an <see cref="InternationalWorld"/> - "everything in one bag ready to drop
    /// into a WorldState" per that record's own doc comment - into a real, playable
    /// <see cref="WorldState"/>. Promoted from a pattern every full-world integration test in the
    /// suite already hand-rolled slightly differently (the most complete version was a private
    /// test helper, <c>WorldFromInternational</c>); this is the one production version, so
    /// <c>CricketManager.App</c>'s <c>new</c> command and any future caller don't have to
    /// re-invent it. Franchise teams are NOT added separately - <see cref="InternationalWorld.Teams"/>
    /// already includes them (WorldSeeder's own generation appends franchise teams into the same
    /// list it returns as <c>Teams</c>; <see cref="InternationalWorld.FranchiseTeams"/> is a
    /// filtered convenience view for callers that want to look at franchises specifically).
    ///
    /// Pure and RNG-free - a straight reshaping of already-generated data, so calling it never
    /// perturbs any seeded RNG stream.
    /// </summary>
    public static WorldState AssembleWorldState(InternationalWorld world)
    {
        var state = new WorldState
        {
            Teams = world.Teams.ToDictionary(t => t.Id),
            Grounds = world.Grounds.ToDictionary(g => g.Id),
            Players = world.Players.ToList(),
            Projects = new List<InfrastructureProject>()
        };

        foreach (var c in world.Competitions) state.Competitions.Add(c);
        foreach (var s in world.Seasons) state.CompetitionSeasons.Add(s);
        foreach (var f in world.Fixtures) state.Fixtures.Add(f);
        foreach (var r in world.Rivalries) state.Rivalries.Add(r);
        foreach (var p in world.NationalPools) state.NationalPools.Add(p);
        foreach (var u in world.Umpires) state.Umpires.Add(u);
        foreach (var pc in world.PlayerContracts) state.PlayerContracts.Add(pc);
        foreach (var kv in world.CountryProfiles) state.CountryProfiles[kv.Key] = kv.Value;
        foreach (var p in world.Pundits) state.Pundits.Add(p);
        foreach (var a in world.Agents) state.Agents.Add(a);

        return state;
    }

    public (List<Team> Teams, List<Player> Players) GenerateStarterWorld(string country = "Pakistan", int teamCount = 4, int squadSize = 13)
    {
        _nationality = country;
        var teams = new List<Team>();
        var players = new List<Player>();

        var teamNames = new[] { "Lahore Lions", "Karachi Kings XI", "Peshawar Panthers", "Multan Monarchs", "Quetta Quakes", "Islamabad Icons" };

        for (int t = 0; t < teamCount; t++)
        {
            var team = new Team
            {
                Name = teamNames[t % teamNames.Length],
                Country = country,
                Strength = Math.Round(45 + _random.NextDouble() * 30, 1) // 45-75 spread so teams aren't identical
            };

            // Reputation tracks strength loosely but deliberately not exactly - a team can be
            // better known than it is currently good, and vice versa. Continental/worldwide
            // recognition for a domestic side is near zero, which is the point of the tiers.
            double domesticRep = Math.Clamp(team.Strength + (_random.NextDouble() * 24 - 12), 5, 95);
            team.Reputation = new Reputation(
                domestic: Math.Round(domesticRep, 1),
                continental: Math.Round(domesticRep * 0.25, 1),
                worldwide: Math.Round(domesticRep * 0.08, 1));

            // Phase 7, Slice 7.3: a board with a real profile, varied per club. Derived from the
            // team's own strength/reputation and its position in the list - deliberately NOT from
            // _random, so this does not shift the player-generation RNG stream a step (which would
            // change every seeded world and break determinism-sensitive tests). A little variety
            // comes from the team index alone.
            double wobble = (t * 37 % 25) - 12;                 // -12..+12, deterministic per team
            team.Board = new Domain.ValueObjects.ClubBoard
            {
                Ownership = (t % 4) switch { 0 => OwnershipModel.Association, 1 => OwnershipModel.PrivateOwner, 2 => OwnershipModel.Corporate, _ => OwnershipModel.Association },
                Wealth = Math.Round(Math.Clamp(domesticRep * 0.7 + 15 + wobble, 15, 95), 1),
                Ambition = Math.Round(Math.Clamp(domesticRep * 0.5 + 30 + wobble * 0.5, 20, 95), 1),
                Patience = Math.Round(Math.Clamp(70 - (domesticRep - 50) * 0.3 - wobble * 0.5, 20, 90), 1),
                FanSentiment = Math.Round(Math.Clamp(52 + wobble * 0.6, 25, 80), 1),
                // §10.2: a season-ticket / membership base, in thousands of members - bigger for a
                // well-supported, well-regarded club. RNG-free, from reputation + the team index.
                MembershipBase = Math.Round(Math.Clamp(domesticRep * 90 + 2_000 + (t * 41 % 100) * 30, 800, 22_000), 0),
                // Phase 12 (§13.1/§13.2): the chairman's agenda and the owner's trajectory - RNG-free,
                // from the team index, so most boards are Balanced/Stable and a few have a real slant.
                Agenda = (t % 6) switch
                {
                    1 => Domain.ValueObjects.ChairmanAgenda.WinNow,
                    2 => Domain.ValueObjects.ChairmanAgenda.YouthAndAcademy,
                    3 => Domain.ValueObjects.ChairmanAgenda.Austerity,
                    4 => Domain.ValueObjects.ChairmanAgenda.Prestige,
                    _ => Domain.ValueObjects.ChairmanAgenda.Balanced
                },
                Trajectory = (t % 5) switch
                {
                    1 => Domain.ValueObjects.OwnerTrajectory.InvestingHeavily,
                    3 => Domain.ValueObjects.OwnerTrajectory.WindingDown,
                    _ => Domain.ValueObjects.OwnerTrajectory.Stable
                },
                BoardUnity = Math.Round(Math.Clamp(72 + wobble * 0.4, 45, 92), 1),
            };
            team.Finances.Budget = Math.Round(400_000 + team.Board.Wealth * 22_000 + (t * 53 % 100) * 3_000, 0);

            var squad = GenerateSquad(squadSize, team.Strength);
            foreach (var p in squad)
            {
                p.CurrentTeamId = team.Id;
                team.SquadPlayerIds.Add(p.Id);
                players.Add(p);

                // A player's starting reputation follows their ability, damped - a good
                // domestic cricketer is known at home and nowhere else until they do something
                // beyond it. This is the baseline PerformanceRecordingService then moves.
                double playerRep = Math.Clamp(Domain.Common.AbilityScale.CompositeAbilityToHundred(p.CurrentAbility) * 0.6, 1, 70);
                p.Reputation = new Reputation(
                    domestic: Math.Round(playerRep, 1),
                    continental: Math.Round(playerRep * 0.2, 1),
                    worldwide: Math.Round(playerRep * 0.05, 1));

                // Real squad status, not the bare SecondChoice placeholder - same reasoning as
                // _roleTraitDeriver.ApplyTo() below: called here because ability, potential,
                // Reputation and Experience (from SeedExistingCareer, already run in
                // GenerateSquad) are all populated by this point.
                p.SquadStatus = _squadStatus.SuggestStatus(p, _worldStartDate);
            }

            // Captaincy: the highest-leadership player available, per format. Split captaincy
            // is normal in modern cricket, so this picks per format rather than once.
            AssignCaptains(team, squad);

            teams.Add(team);
        }

        return (teams, players);
    }

    /// <summary>
    /// Picks a captain per format. Leadership dominates, with format suitability as a
    /// secondary term so a Test-shaped player doesn't end up leading the T20 side purely on
    /// leadership - which is roughly how real selection panels weigh it.
    /// </summary>
    private static void AssignCaptains(Team team, IReadOnlyList<Player> squad)
    {
        if (squad.Count == 0) return;

        foreach (var format in new[] { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 })
        {
            var captain = squad
                .OrderByDescending(p => p.Mental.Leadership * 5
                    + (format switch
                    {
                        MatchFormat.Test => p.FormatSuitability.TestSuitability,
                        MatchFormat.ODI => p.FormatSuitability.OdiSuitability,
                        _ => p.FormatSuitability.T20Suitability
                    }) * 0.4)
                .First();

            team.SetCaptain(format, captain.Id);
        }
    }

    private static readonly string[] PakistanCities =
        { "Lahore", "Karachi", "Rawalpindi", "Multan", "Peshawar", "Faisalabad", "Quetta", "Hyderabad" };

    /// <summary>
    /// Phase 3: generates one home Ground per team (realistic city assignment, no two teams
    /// sharing a city where avoidable) plus a starter Competition + CompetitionSeason that
    /// wraps all the given teams into a round-robin-with-playoffs domestic T20 league. This
    /// is what turns the seeded teams from Phase 2 into something Phase 3's competition/
    /// standings machinery can actually operate on.
    /// </summary>
    public (List<Ground> Grounds, Competition Competition, CompetitionSeason Season) GenerateGroundsAndCompetition(
        IReadOnlyList<Team> teams, string country = "Pakistan", int seasonYear = 2026, string competitionName = "National T20 Cup")
    {
        var grounds = new List<Ground>();
        var shuffledCities = PakistanCities.OrderBy(_ => _random.Next()).ToList();

        for (int i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            var city = shuffledCities[i % shuffledCities.Count];
            var ground = new Ground
            {
                Name = $"{city} Cricket Stadium",
                City = city,
                Country = country,
                Capacity = RandInt(18000, 45000),
                Ends = new List<string> { $"{city} Pavilion End", $"{city} Members End" },
                PitchPaceRating = Math.Round(30 + _random.NextDouble() * 50, 1),
                PitchSpinRating = Math.Round(30 + _random.NextDouble() * 50, 1),
                PitchBounceRating = Math.Round(30 + _random.NextDouble() * 50, 1),
                PitchBattingFriendliness = Math.Round(35 + _random.NextDouble() * 40, 1),
                Reputation = Math.Round(30 + _random.NextDouble() * 40, 1),
                EstablishedYear = RandInt(1885, 1995),
                SquareBoundaryMetres = RandInt(58, 78),
                StraightBoundaryMetres = RandInt(64, 85),
                OutfieldSpeed = Math.Round(35 + _random.NextDouble() * 50, 1),
                AltitudeMetres = RandInt(0, 350),
                HasFloodlights = _random.NextDouble() > 0.25,
                DewTendency = Math.Round(15 + _random.NextDouble() * 55, 1)
            };

            // Facilities correlate loosely with the size/standing of the venue - a big,
            // well-regarded ground tends to have better commercial and media infrastructure -
            // but deliberately with noise, so ground quality isn't a straight function of capacity.
            int facilityBase = (int)Math.Clamp(ground.Reputation * 0.6 + ground.Capacity / 1200.0, 15, 85);
            ground.Facilities = new GroundFacilities
            {
                CorporateHospitality = ClampFacility(facilityBase + RandInt(-12, 12)),
                MediaFacilities = ClampFacility(facilityBase + RandInt(-12, 12)),
                MedicalFacilities = ClampFacility(facilityBase + RandInt(-10, 10)),
                PitchInfrastructure = ClampFacility(facilityBase + RandInt(-10, 15)),
                TrainingFacilities = ClampFacility(facilityBase + RandInt(-15, 10)),
                YouthFacilities = ClampFacility(facilityBase + RandInt(-20, 8))
            };
            ground.HomeTeamIds.Add(team.Id);
            team.HomeGroundId = ground.Id;
            grounds.Add(ground);
        }

        var competition = new Competition
        {
            Name = competitionName,
            StructureType = CompetitionStructureType.LeagueWithPlayoffs,
            Scope = CompetitionScope.DomesticT20,
            Format = MatchFormat.T20,
            Country = country,
            Prestige = Math.Round(40 + _random.NextDouble() * 20, 1), // mid-tier domestic cup by default
            UsesLimitedOversMarginBonus = true, // tech-debt item 9: this domestic T20 cup awards a big-win bonus point
            SalaryCap = 14_000_000 // §9.8: a wage cap on a participating club's domestic bill - high enough to only bite a runaway spender
        };
        // Reputation starts near prestige but not identical - a competition's earned standing
        // and its structural importance are related, not the same number.
        competition.Reputation = Math.Round(Math.Clamp(competition.Prestige + (_random.NextDouble() * 20 - 10), 0, 100), 1);

        // A domestic season occupies a slot in the year rather than running all year round.
        // Pakistan's window is the northern-hemisphere-style Sep-Mar shape; other countries
        // will differ, which is exactly why the window lives on the competition.
        // §17.4 (regional weather): the MATCH-rain half is done - CountryProfile.RainRiskMultiplier
        // feeds MatchWeatherService, so an English domestic match genuinely loses more time to rain
        // than an Australian one. Re-timing the WINDOW itself around a monsoon cascades into every
        // window-timing test and belongs in the dedicated Phase 10 domestic-calendar follow-up.
        competition.Window = CompetitionWindow.Annual(startMonth: 9, startDay: 15, endMonth: 3, endDay: 20);

        var season = new CompetitionSeason { CompetitionId = competition.Id, Year = seasonYear };
        foreach (var team in teams)
        {
            season.ParticipatingTeamIds.Add(team.Id);
            season.GetOrCreateStanding(team.Id); // zeroed row, ready for results once matches exist
        }

        return (grounds, competition, season);
    }

    /// <summary>
    /// Phase 6, Slice 6.1: turns the seeded competition/season into an actual playable calendar -
    /// a double round-robin scheduled inside the competition's window - plus the world's starting
    /// rivalries (a derby for any two teams sharing a home city, and one historical rivalry
    /// between the two strongest sides). The playoff bracket is NOT generated here: qualifiers
    /// are not known until the group stage finishes, so that is the season runner's job (Slice
    /// 6.3), exactly as PlayoffBracketService's own doc comment already implies.
    /// </summary>
    public (List<Fixture> Fixtures, List<Rivalry> Rivalries) GenerateFixturesAndRivalries(
        Competition competition, CompetitionSeason season, IReadOnlyList<Team> teams, IReadOnlyList<Ground> grounds)
    {
        var teamsById = teams.ToDictionary(t => t.Id);
        var calendar = new CompetitionCalendarService();
        var staging = calendar.GetStaging(competition, season.Year);
        var windowStart = staging?.StartDate ?? new DateOnly(season.Year, competition.Window.StartMonth, competition.Window.StartDay);
        var windowEnd = staging?.EndDate ?? windowStart.AddMonths(5);

        var scheduler = new FixtureGenerationService();
        var pairings = scheduler.GenerateRoundRobinPairings(season.ParticipatingTeamIds, doubleRoundRobin: true);
        var fixtures = scheduler.ScheduleRoundRobin(
            competition.Id, season.Id, pairings, windowStart, windowEnd, competition.Format, teamsById);

        var rivalries = GenerateRivalries(teams, grounds, season.Year);
        return (fixtures, rivalries);
    }

    /// <summary>
    /// Phase 6, Slice 6.3: a two-division domestic structure with promotion and relegation. The
    /// two competitions share a window and format; the top division links to the bottom via
    /// SecondTierCompetitionId, and PromotionRelegationCount teams swap each season. Teams are
    /// split by Strength (strongest in the top flight). Each division gets a season and a
    /// full double round-robin fixture list.
    /// </summary>
    public (List<Ground> Grounds, Competition Top, Competition Bottom, List<CompetitionSeason> Seasons, List<Fixture> Fixtures, List<Rivalry> Rivalries)
        GenerateTwoTierDomesticStructure(
            IReadOnlyList<Team> teams, string country = "Pakistan", int seasonYear = 2026,
            int promotionRelegationCount = 1, string topName = "Premier Division", string bottomName = "Championship")
    {
        var grounds = GenerateGrounds(teams, country);
        var teamsById = teams.ToDictionary(t => t.Id);

        var byStrength = teams.OrderByDescending(t => t.Strength).ToList();
        int topSize = (byStrength.Count + 1) / 2;
        var topTeams = byStrength.Take(topSize).ToList();
        var bottomTeams = byStrength.Skip(topSize).ToList();

        // A single-calendar-year window (English-style) - so a season finishes before the next
        // year's annual rollover creates the following season, which is what lets promotion and
        // relegation resolve in time to take effect.
        var window = CompetitionWindow.Annual(startMonth: 4, startDay: 10, endMonth: 8, endDay: 30);

        var bottom = new Competition
        {
            Name = bottomName, StructureType = CompetitionStructureType.League,
            Scope = CompetitionScope.DomesticT20, Format = MatchFormat.T20, Country = country,
            Prestige = 32, Reputation = 30, Window = window
        };
        var top = new Competition
        {
            Name = topName, StructureType = CompetitionStructureType.LeagueWithPlayoffs,
            Scope = CompetitionScope.DomesticT20, Format = MatchFormat.T20, Country = country,
            Prestige = 52, Reputation = 48, Window = window, PlayoffQualifierCount = 4,
            SecondTierCompetitionId = bottom.Id, PromotionRelegationCount = promotionRelegationCount
        };

        var scheduler = new FixtureGenerationService();
        var seasons = new List<CompetitionSeason>();
        var fixtures = new List<Fixture>();

        foreach (var (competition, divisionTeams) in new[] { (top, topTeams), (bottom, bottomTeams) })
        {
            var season = new CompetitionSeason { CompetitionId = competition.Id, Year = seasonYear };
            foreach (var t in divisionTeams) { season.ParticipatingTeamIds.Add(t.Id); season.GetOrCreateStanding(t.Id); }
            seasons.Add(season);

            var pairings = scheduler.GenerateRoundRobinPairings(season.ParticipatingTeamIds, doubleRoundRobin: true);
            var staging = new CompetitionCalendarService().GetStaging(competition, seasonYear);
            fixtures.AddRange(scheduler.ScheduleRoundRobin(
                competition.Id, season.Id, pairings,
                staging?.StartDate ?? new DateOnly(seasonYear, 9, 15),
                staging?.EndDate ?? new DateOnly(seasonYear + 1, 3, 20),
                competition.Format, teamsById));
        }

        var rivalries = GenerateRivalries(teams, grounds, seasonYear);
        return (grounds, top, bottom, seasons, fixtures, rivalries);
    }

    private List<Ground> GenerateGrounds(IReadOnlyList<Team> teams, string country)
    {
        var (grounds, _, _) = GenerateGroundsAndCompetition(teams, country);
        return grounds;
    }

    /// <summary>
    /// Phase 10 (§17.1/§17.3): a THREE-division domestic pyramid. Thin wrapper over the generalised
    /// <see cref="GenerateTieredDomesticStructure"/>, kept so existing callers are unchanged.
    /// </summary>
    public (List<Ground> Grounds, List<Competition> Divisions, List<CompetitionSeason> Seasons, List<Fixture> Fixtures, List<Rivalry> Rivalries)
        GenerateThreeTierDomesticStructure(
            IReadOnlyList<Team> teams, string country = "Pakistan", int seasonYear = 2026, int promotionRelegationCount = 1)
        => GenerateTieredDomesticStructure(teams, tierCount: 3, country, seasonYear, promotionRelegationCount);

    /// <summary>
    /// Phase 10 / the meeting-driven-selection ticket (I): a domestic pyramid of an arbitrary
    /// number of divisions with cascading promotion and relegation - Div1 -> Div2 -> ... -> DivN
    /// linked via <see cref="Competition.SecondTierCompetitionId"/>, which
    /// <see cref="CricketManager.Domain.Services.CompetitionSeasonRunner"/> walks end to end at
    /// season's close. Teams are split by Strength into <paramref name="tierCount"/> tiers (each at
    /// least 2, so the requested count is clamped to <c>teams.Count / 2</c>). Every division gets a
    /// season and a full double round-robin. RNG-free (a fixed split + fixture generation).
    /// </summary>
    public (List<Ground> Grounds, List<Competition> Divisions, List<CompetitionSeason> Seasons, List<Fixture> Fixtures, List<Rivalry> Rivalries)
        GenerateTieredDomesticStructure(
            IReadOnlyList<Team> teams, int tierCount, string country = "Pakistan", int seasonYear = 2026, int promotionRelegationCount = 1)
    {
        var grounds = GenerateGrounds(teams, country);
        var teamsById = teams.ToDictionary(t => t.Id);
        var byStrength = teams.OrderByDescending(t => t.Strength).ThenBy(t => t.Name).ToList();

        // At least THREE clubs a division. A 2-club division (a home-and-away pair with a
        // one-fixture "playoff") is a degenerate structure that, over a long multi-year sim,
        // exposed a latent non-determinism that was reproducible but not fully root-caused - and
        // a real lower division has more than two clubs anyway. So the requested tier count is
        // capped at teams/3, not teams/2.
        int n = Math.Clamp(tierCount, 1, Math.Max(1, byStrength.Count / 3));
        int per = byStrength.Count / n;
        var tiers = new List<List<Team>>();
        for (int i = 0; i < n; i++)
            tiers.Add(i == n - 1
                ? byStrength.Skip(per * i).ToList()          // the bottom tier mops up any remainder
                : byStrength.Skip(per * i).Take(per).ToList());

        var window = CompetitionWindow.Annual(startMonth: 4, startDay: 10, endMonth: 8, endDay: 30);
        string[] names = { "Premier Division", "First Division", "Second Division", "Third Division", "Fourth Division", "Fifth Division" };
        var divisions = new List<Competition>();
        for (int i = 0; i < n; i++)
            divisions.Add(new Competition
            {
                Name = $"{country} {(i < names.Length ? names[i] : $"Division {i + 1}")}",
                StructureType = i == 0 ? CompetitionStructureType.LeagueWithPlayoffs : CompetitionStructureType.League,
                Scope = CompetitionScope.DomesticT20, Format = MatchFormat.T20, Country = country,
                Prestige = Math.Max(8, 54 - i * 12), Reputation = Math.Max(6, 50 - i * 12), Window = window,
                PlayoffQualifierCount = i == 0 ? Math.Min(4, tiers[i].Count) : 0,
                PromotionRelegationCount = i < n - 1 ? promotionRelegationCount : 0,
            });
        for (int i = 0; i < n - 1; i++)
            divisions[i].SecondTierCompetitionId = divisions[i + 1].Id;

        var scheduler = new FixtureGenerationService();
        var seasons = new List<CompetitionSeason>();
        var fixtures = new List<Fixture>();
        for (int i = 0; i < n; i++)
        {
            var season = new CompetitionSeason { CompetitionId = divisions[i].Id, Year = seasonYear };
            foreach (var t in tiers[i]) { season.ParticipatingTeamIds.Add(t.Id); season.GetOrCreateStanding(t.Id); }
            seasons.Add(season);
            var pairings = scheduler.GenerateRoundRobinPairings(season.ParticipatingTeamIds, doubleRoundRobin: true);
            var staging = new CompetitionCalendarService().GetStaging(divisions[i], seasonYear);
            fixtures.AddRange(scheduler.ScheduleRoundRobin(divisions[i].Id, season.Id, pairings,
                staging?.StartDate ?? new DateOnly(seasonYear, 4, 10),
                staging?.EndDate ?? new DateOnly(seasonYear, 8, 30),
                MatchFormat.T20, teamsById));
        }

        var rivalries = GenerateRivalries(teams, grounds, seasonYear);
        return (grounds, divisions, seasons, fixtures, rivalries);
    }

    private static List<Rivalry> GenerateRivalries(IReadOnlyList<Team> teams, IReadOnlyList<Ground> grounds, int year)
    {
        var rivalries = new List<Rivalry>();
        var cityByTeam = new Dictionary<Guid, string>();
        foreach (var team in teams)
        {
            var ground = grounds.FirstOrDefault(g => g.Id == team.HomeGroundId);
            if (ground is not null) cityByTeam[team.Id] = ground.City;
        }

        // A derby for any two teams that share a home city.
        for (int i = 0; i < teams.Count; i++)
        for (int j = i + 1; j < teams.Count; j++)
        {
            if (cityByTeam.TryGetValue(teams[i].Id, out var cityA)
                && cityByTeam.TryGetValue(teams[j].Id, out var cityB)
                && cityA == cityB)
            {
                var derby = new Rivalry { TeamAId = teams[i].Id, TeamBId = teams[j].Id, Source = RivalrySource.Geographic };
                derby.SetIntensity(82);
                rivalries.Add(derby);
            }
        }

        // One historical rivalry between the two strongest sides, if they are not already a derby.
        var strongest = teams.OrderByDescending(t => t.Strength).Take(2).ToList();
        if (strongest.Count == 2 && !rivalries.Any(r => r.Match(strongest[0].Id, strongest[1].Id)))
        {
            var classic = new Rivalry { TeamAId = strongest[0].Id, TeamBId = strongest[1].Id, Source = RivalrySource.Historical };
            classic.SetIntensity(68);
            rivalries.Add(classic);
        }

        return rivalries;
    }

    /// <summary>
    /// Phase 6, Slice 6.7: a small multi-country world with international cricket. For each
    /// country: a domestic league (teams, players, grounds, a season and fixtures) plus a
    /// national team whose squad is the pool of that country's best players. Then one
    /// international competition across the national teams, on a two-year cycle, with its own
    /// season and fixtures. Everything is returned in one bag ready to drop into a WorldState.
    /// </summary>
    public InternationalWorld GenerateInternationalWorld(
        string[]? countries = null, int teamsPerCountry = 4, int squadSize = 14, int seasonYear = 2026)
    {
        countries ??= new[] { "Pakistan", "Australia", "England", "India" };

        var allTeams = new List<Team>();
        var allPlayers = new List<Player>();
        var allGrounds = new List<Ground>();
        var allCompetitions = new List<Competition>();
        var allSeasons = new List<CompetitionSeason>();
        var allFixtures = new List<Fixture>();
        var allRivalries = new List<Rivalry>();
        var nationalTeams = new List<Team>();
        var nationalPools = new List<Domain.ValueObjects.NationalPool>();
        var nationalSelection = new Domain.Services.NationalSelectionService();

        // Phase 10: the per-country administrative/economic/talent profile is RNG-free, so building
        // it up front (rather than at the tail, where it used to sit) does not perturb the seeding
        // stream - and it lets the domestic calendar below pick hemisphere-appropriate windows.
        var countryProfiles = GenerateCountryProfiles(countries);

        foreach (var country in countries)
        {
            var profile = countryProfiles.GetValueOrDefault(country);
            var (teams, players) = GenerateStarterWorld(country, teamsPerCountry, squadSize);
            foreach (var t in teams) t.Name = $"{country} {t.Name}";

            // Requirement I (the meeting-driven-selection ticket) asked for a real multi-division
            // domestic pyramid HERE, in the live seeded world. The generic N-tier generator
            // (GenerateTieredDomesticStructure) is built and unit-tested, and CompetitionSeasonRunner
            // already walks an arbitrary-length promotion/relegation chain - but wiring it in
            // reproducibly amplified a latent non-determinism over a long multi-year sim (a seeded
            // world with several same-window domestic competitions per country pushes some
            // Guid.NewGuid()-ordered iteration, first reachable a couple of sim-years in, into
            // flipping an outcome). It was narrowed but not root-caused. Rather than ship a
            // non-deterministic world (the project's hard discipline), the live wiring is deferred
            // to Phase 17 - see CLAUDE.md's "MEETING-DRIVEN SELECTION TICKET" and deferred register.
            // The seeded world keeps its single top-flight T20 competition per country for now.
            var (grounds, competition, season) =
                GenerateGroundsAndCompetition(teams, country, seasonYear, $"{country} T20 Cup");
            competition.Window = DomesticWindow(profile, DomesticSlot.T20);
            var (divFixtures, rivalries) = GenerateFixturesAndRivalries(competition, season, teams, grounds);

            allTeams.AddRange(teams);
            allPlayers.AddRange(players);
            allGrounds.AddRange(grounds);
            allCompetitions.Add(competition);
            allSeasons.Add(season);
            allFixtures.AddRange(divFixtures);
            allRivalries.AddRange(rivalries);

            // Phase 10 (§17.2): a genuine THREE-FORMAT domestic season - the same clubs also play a
            // First-Class Championship and a List A (50-over) cup, in their own slots in the year.
            var teamsByIdLocal = teams.ToDictionary(t => t.Id);
            foreach (var (fmt, slot, scope, name, structure, prestige) in new[]
            {
                (MatchFormat.Test, DomesticSlot.FirstClass, CompetitionScope.DomesticFirstClass, $"{country} First-Class Championship", CompetitionStructureType.FirstClassChampionship, 46.0),
                (MatchFormat.ODI, DomesticSlot.ListA, CompetitionScope.DomesticListA, $"{country} One-Day Cup", CompetitionStructureType.GroupStageKnockout, 40.0),
            })
            {
                var fc = new Competition
                {
                    Name = name, StructureType = structure, Scope = scope, Format = fmt, Country = country,
                    Prestige = prestige, Reputation = prestige - 4, Window = DomesticWindow(profile, slot),
                    PlayoffQualifierCount = structure == CompetitionStructureType.GroupStageKnockout ? Math.Min(4, teams.Count) : 0,
                };
                var fcSeason = new CompetitionSeason { CompetitionId = fc.Id, Year = seasonYear };
                foreach (var t in teams) { fcSeason.ParticipatingTeamIds.Add(t.Id); fcSeason.GetOrCreateStanding(t.Id); }
                var fcStaging = new CompetitionCalendarService().GetStaging(fc, seasonYear);
                var fcScheduler = new FixtureGenerationService();
                var fcPairings = fcScheduler.GenerateRoundRobinPairings(fcSeason.ParticipatingTeamIds, doubleRoundRobin: false);
                allFixtures.AddRange(fcScheduler.ScheduleRoundRobin(fc.Id, fcSeason.Id, fcPairings,
                    fcStaging?.StartDate ?? new DateOnly(seasonYear, fc.Window.StartMonth, fc.Window.StartDay),
                    fcStaging?.EndDate ?? new DateOnly(seasonYear, fc.Window.EndMonth, fc.Window.EndDay),
                    fmt, teamsByIdLocal));
                allCompetitions.Add(fc);
                allSeasons.Add(fcSeason);
            }

            var national = new Team
            {
                Name = country,
                Country = country,
                IsNational = true,
                Strength = Math.Round(teams.Average(t => t.Strength) + 8, 1),
                Reputation = new Reputation(domestic: 80, continental: 60, worldwide: 45),
                HomeGroundId = grounds.OrderByDescending(g => g.Capacity).First().Id,
                // Phase 7, Slice 7.8: every national side gets a board with a selection panel and a
                // chairman of selectors. Derived from the country's index, NOT _random, so it does
                // not shift the seeding stream (the same determinism reasoning as the club board).
                NationalBoard = new Domain.ValueObjects.NationalBoard
                {
                    ChairmanOfSelectorsQuality = Math.Clamp(52.0 + (Array.IndexOf(countries, country) * 41 % 40) - 12, 40, 92),
                    PanelSize = 3 + (Array.IndexOf(countries, country) % 3),
                    Politicisation = Math.Clamp(35.0 + (Array.IndexOf(countries, country) * 29 % 50) - 8, 20, 85),
                    // §11.3: board ambition - the powerhouse nations expect the trophy, the smaller
                    // ones expect to be competitive. RNG-free (from the country), like the rest.
                    Ambition = country switch
                    {
                        "India" or "Australia" or "England" => 85,
                        "Pakistan" or "South Africa" or "New Zealand" => 60,
                        _ => 42
                    },
                },
            };
            nationalSelection.RefreshPool(national, players, _worldStartDate, nationalPools);
            allTeams.Add(national);
            nationalTeams.Add(national);
        }

        // One international tournament across the national teams.
        var intl = new Competition
        {
            Name = "World T20 Championship",
            StructureType = CompetitionStructureType.LeagueWithPlayoffs,
            Scope = CompetitionScope.International,
            Format = MatchFormat.T20,
            Country = string.Empty,
            Prestige = 85,
            Reputation = 82,
            PlayoffQualifierCount = Math.Min(4, nationalTeams.Count),
            Window = CompetitionWindow.Cycle(2, seasonYear, startMonth: 6, startDay: 1, endMonth: 7, endDay: 15)
        };
        var intlSeason = new CompetitionSeason { CompetitionId = intl.Id, Year = seasonYear };
        foreach (var nt in nationalTeams) { intlSeason.ParticipatingTeamIds.Add(nt.Id); intlSeason.GetOrCreateStanding(nt.Id); }

        var scheduler = new FixtureGenerationService();
        var intlPairings = scheduler.GenerateRoundRobinPairings(intlSeason.ParticipatingTeamIds, doubleRoundRobin: false);
        var intlStaging = new CompetitionCalendarService().GetStaging(intl, seasonYear);
        var teamsById = allTeams.ToDictionary(t => t.Id);
        allFixtures.AddRange(scheduler.ScheduleRoundRobin(
            intl.Id, intlSeason.Id, intlPairings,
            intlStaging?.StartDate ?? new DateOnly(seasonYear, 6, 1),
            intlStaging?.EndDate ?? new DateOnly(seasonYear, 7, 15),
            intl.Format, teamsById));

        allCompetitions.Add(intl);
        allSeasons.Add(intlSeason);

        // Phase 10: the rest of the international calendar - a WTC-style Test Championship, an ODI
        // Championship, and a handful of named bilateral trophy series between traditional rivals.
        // All are ordinary Competitions the generic CompetitionSeasonRunner plays; the bilateral
        // ones carry a Rivalry with a permanent trophy.
        var (intlComps, intlSeasons, intlFixtures, intlRivalries) =
            GenerateInternationalCalendar(nationalTeams, seasonYear, teamsById);
        allCompetitions.AddRange(intlComps);
        allSeasons.AddRange(intlSeasons);
        allFixtures.AddRange(intlFixtures);
        allRivalries.AddRange(intlRivalries);

        var umpires = GenerateUmpirePanel(countries, count: Math.Max(16, countries.Length * 6));

        // Phase 8, Slice 8.1: seed each domestic club a youth academy so a new save starts with a
        // pipeline rather than an empty one. Appended to allPlayers AFTER everything else, so
        // academy players stay at the tail of the world's player list and senior-player RNG
        // consumption in the monthly/annual ticks is unchanged (the determinism discipline).
        var academyPlayers = GenerateAcademies(allTeams.Where(t => !t.IsNational).ToList(), allGrounds);
        allPlayers.AddRange(academyPlayers);

        // Follow-up: a small pool of ASSOCIATE-nation players (Netherlands / Nepal / Scotland / UAE),
        // unattached free agents, so domestic clubs can satisfy the "at least one associate in the
        // squad" rule (the user's "every country benefits from every other" principle). Appended at
        // the tail, RNG at the tail - the determinism discipline.
        var associatePlayers = GenerateAssociatePlayers();
        allPlayers.AddRange(associatePlayers);

        // Phase 10 (§11.7): the associate nations are REAL national teams now - each with its own
        // player pool - and they contest an ICC Associate Qualifier whose winner and runner-up earn
        // a place at the next World T20. RNG-free: teams/pools/fixtures derived from the fixed
        // associate list and the players just generated. Appended after everything else.
        var associateTeams = new List<Team>();
        var assocSelection = new Domain.Services.NationalSelectionService();
        for (int ai = 0; ai < AssociateNations.Length; ai++)
        {
            var nation = AssociateNations[ai];
            var pool = associatePlayers.Where(p => string.Equals(p.Nationality, nation, StringComparison.OrdinalIgnoreCase)).ToList();
            var at = new Team
            {
                Name = nation, Country = nation, IsNational = true,
                Strength = Math.Round(pool.DefaultIfEmpty().Average(p => p?.CurrentAbility ?? 480) / 10.0, 1),
                Reputation = new Reputation(domestic: 46, continental: 30, worldwide: 18),
                HomeGroundId = allGrounds.Count > 0 ? allGrounds[ai % allGrounds.Count].Id : Guid.Empty,
                NationalBoard = new Domain.ValueObjects.NationalBoard
                {
                    ChairmanOfSelectorsQuality = 48, PanelSize = 3, Politicisation = 40, Ambition = 42
                },
            };
            if (pool.Count > 0) assocSelection.RefreshPool(at, pool, _worldStartDate, nationalPools);
            allTeams.Add(at);
            associateTeams.Add(at);
            nationalTeams.Add(at);
        }

        if (associateTeams.Count >= 2)
        {
            var qualifier = new Competition
            {
                Name = "ICC Associate Qualifier",
                StructureType = CompetitionStructureType.LeagueWithPlayoffs,
                Scope = CompetitionScope.International, Format = MatchFormat.T20, Country = string.Empty,
                Prestige = 55, Reputation = 50,
                PlayoffQualifierCount = Math.Min(4, associateTeams.Count),
                // Runs just before the World T20 window so its qualifiers can (conceptually) feed it.
                Window = CompetitionWindow.Cycle(2, seasonYear, startMonth: 4, startDay: 5, endMonth: 5, endDay: 20),
            };
            var qSeason = new CompetitionSeason { CompetitionId = qualifier.Id, Year = seasonYear };
            foreach (var at in associateTeams) { qSeason.ParticipatingTeamIds.Add(at.Id); qSeason.GetOrCreateStanding(at.Id); }
            var qStaging = new CompetitionCalendarService().GetStaging(qualifier, seasonYear);
            var qScheduler = new FixtureGenerationService();
            var qPairings = qScheduler.GenerateRoundRobinPairings(qSeason.ParticipatingTeamIds, doubleRoundRobin: false);
            var teamsByIdAll = allTeams.ToDictionary(t => t.Id);
            allFixtures.AddRange(qScheduler.ScheduleRoundRobin(qualifier.Id, qSeason.Id, qPairings,
                qStaging?.StartDate ?? new DateOnly(seasonYear, 4, 5),
                qStaging?.EndDate ?? new DateOnly(seasonYear, 5, 20),
                MatchFormat.T20, teamsByIdAll));
            allCompetitions.Add(qualifier);
            allSeasons.Add(qSeason);
        }

        // Follow-up: FIVE franchise leagues on non-overlapping windows (IPL / PSL / CPL / SA20 / BBL),
        // all T20, all auction leagues. New teams/grounds/competition/season/fixtures per league;
        // squads empty - the auction fills them when each window opens.
        var franchiseTeams = new List<Team>();
        foreach (var (teams, fGrounds, fComp, fSeason, fFixtures) in GenerateFranchiseLeagues(countries, seasonYear))
        {
            franchiseTeams.AddRange(teams);
            allTeams.AddRange(teams);
            allGrounds.AddRange(fGrounds);
            allCompetitions.Add(fComp);
            allSeasons.Add(fSeason);
            allFixtures.AddRange(fFixtures);
        }

        // S1: multi-league ownership groups - the Reliance (MI), Sunrisers, Capitals and Royals
        // shape. RNG-FREE: each of the first 3 leagues (in the stable order they were generated)
        // contributes one franchise to each of 3 groups, so every group spans 3 leagues.
        var leagues = allCompetitions.Where(c => c.IsFranchiseAuctionLeague)
            .Select(c => allSeasons.First(s => s.CompetitionId == c.Id).ParticipatingTeamIds
                .Select(id => franchiseTeams.First(t => t.Id == id)).OrderBy(t => t.Name).ToList())
            .Where(l => l.Count >= 3)
            .Take(3).ToList();
        var groupIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        for (int g = 0; g < groupIds.Length; g++)
            for (int l = 0; l < leagues.Count; l++)
                leagues[l][g].OwnershipGroupId = groupIds[g];

        // Section B: one administrative/economic profile per country (built at the top of this
        // method now - Phase 10 - so the domestic calendar can read hemisphere/rain from it).

        // Phase 10: bake each country's home-conditions bias into its grounds' pitch ratings, so
        // touring the subcontinent really is a spin challenge and touring England a seam one. A
        // one-time seed adjustment (the pitch also deteriorates day-by-day in a match as before).
        foreach (var g in allGrounds)
            if (countryProfiles.TryGetValue(g.Country, out var gp))
            {
                g.PitchPaceRating = Math.Clamp(g.PitchPaceRating + gp.HomePitchSeamBias, 10, 95);
                g.PitchSpinRating = Math.Clamp(g.PitchSpinRating + gp.HomePitchSpinBias, 10, 95);
                // Phase 15 (§19.4): Australia and New Zealand use drop-in pitches widely - they
                // wear far more evenly across a multi-day match. RNG-free, by country.
                if (g.Country is "Australia" or "New Zealand") g.UsesDropInPitch = true;
            }
        // Section B: a board-controlled country's regional sides read as association-run, not
        // club-membership - which is what makes them un-takeover-able (BoardService already no-ops
        // a takeover for Association / MemberOwned).
        foreach (var t in allTeams.Where(t => !t.IsNational && !t.IsFranchise))
            if (countryProfiles.TryGetValue(t.Country, out var cp) && cp.DomesticStructure == DomesticStructureModel.BoardControlledRegions)
                t.Board.Ownership = OwnershipModel.Association;

        // Phase 9, Slice 9.0: give every senior domestic player a starting contract. A SEPARATE
        // step, after everything else - GenerateStarterWorld does not do this, so an exact-count
        // test on it is unaffected, and this consumes _random only at the tail.
        var contracts = GeneratePlayerContracts(
            allTeams.Where(t => !t.IsNational && !t.IsFranchise).ToList(), allPlayers);

        // Phase 12: a small pundit roster - RNG-free, derived from the strongest domestic clubs, so
        // each pundit has an old allegiance and a rival and his takes carry a conflict of interest.
        var pundits = GeneratePundits(allTeams.Where(t => !t.IsNational && !t.IsFranchise).ToList());

        // Phase 13 (§9.6): the player agents - RNG-free, a fixed name pool, the best players
        // assigned to the best agents.
        var agents = GenerateAgents(allPlayers.Where(p => !p.IsRetired && p.AcademyTeamId is null).ToList());

        // Meeting-driven-selection ticket (requirement C): every player/coach starts with his
        // current club in his career team history, so a franchise auction in year one already has
        // a real first-hand-knowledge signal to read (national-pool membership was added above by
        // RefreshPool). RNG-free.
        foreach (var p in allPlayers)
            if (p.CurrentTeamId is { } ptid) p.CareerTeamIds.Add(ptid);

        return new InternationalWorld(allTeams, allPlayers, allGrounds, allCompetitions, allSeasons, allFixtures, allRivalries, nationalTeams, nationalPools, umpires)
        {
            PlayerContracts = contracts,
            FranchiseTeams = franchiseTeams,
            CountryProfiles = countryProfiles,
            Pundits = pundits,
            Agents = agents
        };
    }

    private static readonly string[] PunditNames =
        { "Nasser Hurst", "Ravi Menon", "Ian Gale", "Sanjay Rao", "Mark Butcher-Grey", "Wasim Tariq", "Grant Elliot-Voss" };

    private static readonly string[] AgentNames =
        { "Kilbride Sports Management", "Damian Voss", "Priya Nair Agency", "The Ellison Group", "Farhan Malik",
          "Redfern & Co.", "Global Willow Partners", "Anton Beck", "Crestline Talent", "Hemi Whitaker" };

    /// <summary>
    /// Phase 13 (§9.6): the player agents. RNG-FREE - a fixed name pool, tiered reputations, and the
    /// players assigned by reputation rank (the biggest names to the biggest agents), so this does
    /// not touch the seeding stream.
    /// </summary>
    private static List<PlayerAgent> GenerateAgents(IReadOnlyList<Player> players)
    {
        var agents = new List<PlayerAgent>();
        for (int i = 0; i < AgentNames.Length; i++)
            agents.Add(new PlayerAgent
            {
                Name = AgentNames[i],
                Reputation = Math.Round(78 - i * 5.0, 1),
                CommissionRate = Math.Round(0.04 + (78 - i * 5.0) / 100.0 * 0.05, 3),
            });

        // Players with a genuine domestic profile get an agent at world creation.
        var repRanked = players
            .Select(p => (p, profile: p.Reputation.Domestic + p.Reputation.Continental * 0.4 + p.Reputation.Worldwide * 0.3))
            .Where(x => x.profile >= 25)
            .OrderByDescending(x => x.profile)
            .ThenBy(x => x.p.FullName)
            .ToList();

        for (int i = 0; i < repRanked.Count; i++)
        {
            // Round-robin the top agents for the top players, then spread the rest.
            var agent = repRanked[i].profile >= 60
                ? agents[i % Math.Min(4, agents.Count)]
                : agents[i % agents.Count];
            if (agent.ClientPlayerIds.Count >= 16) agent = agents.OrderBy(a => a.ClientPlayerIds.Count).First();
            agent.ClientPlayerIds.Add(repRanked[i].p.Id);
            repRanked[i].p.AgentId = agent.Id;
        }

        return agents;
    }

    /// <summary>Phase 12: the media pundit roster - RNG-free (top teams by strength, fixed name pool).</summary>
    private static List<Domain.ValueObjects.Pundit> GeneratePundits(IReadOnlyList<Team> clubs)
    {
        var byStrength = clubs.OrderByDescending(t => t.Strength).ThenBy(t => t.Name).ToList();
        var list = new List<Domain.ValueObjects.Pundit>();
        for (int i = 0; i < Math.Min(PunditNames.Length, Math.Max(0, byStrength.Count - 1)); i++)
        {
            var former = byStrength[i % byStrength.Count];
            var rival = byStrength[(i + byStrength.Count / 2) % byStrength.Count];
            list.Add(new Domain.ValueObjects.Pundit(
                PunditNames[i], former.Id, rival.Id == former.Id ? null : rival.Id, former.Country,
                BiasStrength: 0.35 + (i % 3) * 0.22));
        }
        return list;
    }

    /// <summary>
    /// Phase 10: the international calendar beyond the one T20 tournament. A WTC-style Test
    /// Championship and an ODI Championship (both all-nation leagues, played by the generic
    /// CompetitionSeasonRunner), plus a handful of named bilateral trophy series between traditional
    /// rivals - each a 2-team, 2-match series carrying a Rivalry with a permanent trophy that
    /// changes hands (or is retained on a drawn series). RNG-free: everything is derived from the
    /// team list and fixed cycle offsets, so this does not perturb the seeding stream.
    /// </summary>
    /// <summary>Phase 10 (§17.2/§17.4): which format slot a domestic competition occupies in the year.</summary>
    private enum DomesticSlot { FirstClass, ListA, T20 }

    /// <summary>
    /// Phase 10: a hemisphere-appropriate window for a domestic competition of a given format.
    /// Northern-hemisphere cricket is an April-September game; southern is October-March. The three
    /// formats sit in their own, non-overlapping slots within that season (first-class first, then
    /// the one-day cup, then the T20 blast at the height of summer). A brief brush with the
    /// biennial June-July World T20 is left as-is - the sim already fields weakened domestic sides
    /// while players are on international duty, which is exactly what happens in reality.
    /// </summary>
    private static CompetitionWindow DomesticWindow(CountryProfile? profile, DomesticSlot slot)
    {
        bool southern = profile?.Hemisphere == Hemisphere.Southern;
        // (startMonth, startDay, endMonth, endDay) for the northern calendar; shifted +6 months for the south.
        var (sm, sd, em, ed) = slot switch
        {
            DomesticSlot.FirstClass => (4, 10, 6, 25),
            DomesticSlot.ListA => (7, 5, 8, 5),
            _ => (8, 10, 9, 25),
        };
        if (southern) { sm = (sm + 5) % 12 + 1; em = (em + 5) % 12 + 1; }
        return CompetitionWindow.Annual(sm, sd, em, ed);
    }

    private (List<Competition> Comps, List<CompetitionSeason> Seasons, List<Fixture> Fixtures, List<Rivalry> Rivalries)
        GenerateInternationalCalendar(IReadOnlyList<Team> nationalTeams, int seasonYear, Dictionary<Guid, Team> teamsById)
    {
        var comps = new List<Competition>();
        var seasons = new List<CompetitionSeason>();
        var fixtures = new List<Fixture>();
        var rivalries = new List<Rivalry>();
        var scheduler = new FixtureGenerationService();
        var calendar = new CompetitionCalendarService();

        Competition MakeLeague(string name, MatchFormat format, double prestige, int startMonth, int endMonth)
        {
            var c = new Competition
            {
                Name = name,
                StructureType = CompetitionStructureType.League,
                Scope = CompetitionScope.International,
                Format = format,
                Country = string.Empty,
                Prestige = prestige,
                Reputation = prestige - 3,
                Window = CompetitionWindow.Annual(startMonth, 1, endMonth, 25),
            };
            var season = new CompetitionSeason { CompetitionId = c.Id, Year = seasonYear };
            foreach (var nt in nationalTeams) { season.ParticipatingTeamIds.Add(nt.Id); season.GetOrCreateStanding(nt.Id); }
            var staging = calendar.GetStaging(c, seasonYear);
            var pairings = scheduler.GenerateRoundRobinPairings(season.ParticipatingTeamIds, doubleRoundRobin: false);
            fixtures.AddRange(scheduler.ScheduleRoundRobin(c.Id, season.Id, pairings,
                staging?.StartDate ?? new DateOnly(seasonYear, startMonth, 1),
                staging?.EndDate ?? new DateOnly(seasonYear, endMonth, 25),
                format, teamsById));
            comps.Add(c);
            seasons.Add(season);
            return c;
        }

        // The two all-nation leagues, on windows clear of the June-July World T20 window.
        MakeLeague("World Test Championship", MatchFormat.Test, 90, startMonth: 1, endMonth: 4);
        MakeLeague("ODI Championship", MatchFormat.ODI, 80, startMonth: 9, endMonth: 11);

        // Named bilateral trophy series - only where both nations exist in this world.
        (string A, string B, string Trophy, MatchFormat Format, int CycleOffset)[] pairs =
        {
            ("Australia", "England", "The Ashes", MatchFormat.Test, 0),
            ("India", "Australia", "Border-Gavaskar Trophy", MatchFormat.Test, 1),
            ("England", "India", "Pataudi Trophy", MatchFormat.Test, 0),
            ("Australia", "New Zealand", "Trans-Tasman Trophy", MatchFormat.Test, 1),
            ("England", "South Africa", "Basil D'Oliveira Trophy", MatchFormat.Test, 0),
            ("India", "Pakistan", "The Subcontinent Series", MatchFormat.ODI, 1),
        };

        foreach (var (a, b, trophy, format, offset) in pairs)
        {
            var ta = nationalTeams.FirstOrDefault(t => string.Equals(t.Country, a, StringComparison.OrdinalIgnoreCase));
            var tb = nationalTeams.FirstOrDefault(t => string.Equals(t.Country, b, StringComparison.OrdinalIgnoreCase));
            if (ta is null || tb is null) continue;

            int startMonth = format == MatchFormat.Test ? 11 : 12; // late-year tour slot, clear of the leagues
            var c = new Competition
            {
                Name = $"{trophy} ({a} v {b})",
                StructureType = CompetitionStructureType.League,
                Scope = CompetitionScope.International,
                Format = format,
                Country = string.Empty,
                Prestige = 82,
                Reputation = 82,
                Window = CompetitionWindow.Cycle(2, seasonYear + offset, startMonth, 1, startMonth + 1, 28),
            };
            comps.Add(c);

            // A completed "history" season two cycles back, carrying the two participants - so
            // CompetitionSeasonRunner.CreateSeasonsForYear can carry them forward when the series
            // next stages (it reads the most recent prior season with >= 2 participants). Without
            // this a series that does not stage in the seed year would never get a participant list.
            var history = new CompetitionSeason { CompetitionId = c.Id, Year = seasonYear + offset - 2 };
            foreach (var t in new[] { ta, tb }) { history.ParticipatingTeamIds.Add(t.Id); history.GetOrCreateStanding(t.Id); }
            history.Complete(ta.Id); // completed so the season runner skips it - it exists only to carry the participants forward
            seasons.Add(history);

            // Seed the first live edition only if it stages in seasonYear (offset 0); otherwise the
            // annual rollover's CreateSeasonsForYear picks it up on its cycle year.
            if (offset == 0)
            {
                var season = new CompetitionSeason { CompetitionId = c.Id, Year = seasonYear };
                foreach (var t in new[] { ta, tb }) { season.ParticipatingTeamIds.Add(t.Id); season.GetOrCreateStanding(t.Id); }
                var staging = calendar.GetStaging(c, seasonYear);
                var pairings = scheduler.GenerateRoundRobinPairings(season.ParticipatingTeamIds, doubleRoundRobin: true); // 2 matches
                fixtures.AddRange(scheduler.ScheduleRoundRobin(c.Id, season.Id, pairings,
                    staging?.StartDate ?? new DateOnly(seasonYear, startMonth, 1),
                    staging?.EndDate ?? new DateOnly(seasonYear, startMonth + 1, 28),
                    format, teamsById));
                seasons.Add(season);
            }

            rivalries.Add(new Rivalry
            {
                TeamAId = ta.Id,
                TeamBId = tb.Id,
                Source = RivalrySource.Historical,
                TrophyName = trophy,
                TrophyHolderId = ta.Id, // arbitrary starting holder
                LastContestedYear = seasonYear - 2,
            });
            rivalries[^1].SetIntensity(78);
        }

        return (comps, seasons, fixtures, rivalries);
    }

    /// <summary>The associate nations seeded into a world - a small player pool each, no domestic league or national team of their own, their players available for cross-border domestic signing (the "min 1 associate in a domestic squad" rule).</summary>
    public static readonly string[] AssociateNations = { "Netherlands", "Nepal", "Scotland", "UAE" };

    /// <summary>
    /// Follow-up: a modest pool of associate-nation players (~12 each), unattached free agents
    /// (CurrentTeamId null), for domestic clubs to sign into their one-associate squad slot. Their
    /// standard is a shade below a full-member domestic player - a genuine gap for the good ones to
    /// close. Uses _random at the seeder's tail so it does not perturb the senior-player stream.
    /// </summary>
    public List<Player> GenerateAssociatePlayers()
    {
        var pool = new List<Player>();
        var previous = _nationality;
        foreach (var nation in AssociateNations)
        {
            _nationality = nation;
            for (int i = 0; i < 12; i++)
            {
                var role = i switch
                {
                    < 5 => PlayerRole.Batsman,
                    5 => PlayerRole.WicketKeeper,
                    < 8 => PlayerRole.BattingAllrounder,
                    _ => PlayerRole.Bowler
                };
                var p = GeneratePlayer(role, teamStrength: RandInt(38, 52)); // a notch below full-member domestic
                p.CurrentTeamId = null;
                p.SquadStatus = SquadStatus.Fringe;
                pool.Add(p);
            }
        }
        _nationality = previous;
        return pool;
    }

    /// <summary>
    /// Sections B/C: one <see cref="CountryProfile"/> per full-member country PLUS one per associate
    /// nation. The domestic structure model, economic scale and rain risk here are ILLUSTRATIVE
    /// variants for a fictional world - a real-data import would supply the actual per-board rules.
    /// Domestic foreign-player rules are the user's principle: 4 overseas in the squad, >=1 of them
    /// an associate, at most 2 in the XI (DIFFERENT from the franchise-league caps of 4-XI / 8-squad).
    /// </summary>
    public Dictionary<string, CountryProfile> GenerateCountryProfiles(string[] countries)
    {
        var southern = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Australia", "New Zealand", "South Africa", "West Indies", "Zimbabwe", "Sri Lanka" };
        var boardControlled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "India", "Australia", "Pakistan" };
        var economy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["India"] = 1.7, ["England"] = 1.2, ["Australia"] = 1.15, ["Pakistan"] = 0.8, ["South Africa"] = 0.9, ["New Zealand"] = 0.75 };
        var rain = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["England"] = 1.6, ["Pakistan"] = 1.3, ["India"] = 1.25, ["New Zealand"] = 1.3, ["Australia"] = 0.75, ["South Africa"] = 0.9 };

        // Phase 10: the talent / home-conditions layer. Illustrative for a fictional world - a
        // real-data layer would supply the actual per-nation values.
        var subcontinent = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "India", "Pakistan", "Sri Lanka", "Bangladesh" };
        var paceNations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Australia", "England", "South Africa", "New Zealand", "West Indies" };
        var talent = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["India"] = 88, ["Australia"] = 78, ["England"] = 76, ["Pakistan"] = 72, ["South Africa"] = 68, ["New Zealand"] = 58, ["Sri Lanka"] = 60, ["West Indies"] = 58 };
        // S5: a heritage / seniority proxy - the founding Test nations at the top, a recent Full
        // Member near the bottom. One of the four ICC-revenue-split weights.
        var history = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["England"] = 96, ["Australia"] = 96, ["India"] = 90, ["West Indies"] = 84, ["Pakistan"] = 80,
              ["South Africa"] = 82, ["New Zealand"] = 78, ["Sri Lanka"] = 68, ["Bangladesh"] = 42, ["Zimbabwe"] = 40,
              ["Ireland"] = 28, ["Afghanistan"] = 26 };

        var map = new Dictionary<string, CountryProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in countries)
        {
            bool sub = subcontinent.Contains(c);
            bool pace = paceNations.Contains(c);
            map[c] = new CountryProfile
            {
                Nationality = c,
                Membership = MembershipStatus.FullMember,
                DomesticStructure = boardControlled.Contains(c) ? DomesticStructureModel.BoardControlledRegions : DomesticStructureModel.ClubMembership,
                Hemisphere = southern.Contains(c) ? Hemisphere.Southern : Hemisphere.Northern,
                EconomicScale = economy.TryGetValue(c, out var e) ? e : 0.7,
                RainRiskMultiplier = rain.TryGetValue(c, out var r) ? r : 1.0,
                AllowsForeignDomesticPlayers = true,
                DomesticSquadOverseasLimit = 4,
                DomesticXiOverseasLimit = 2,
                DomesticMinAssociatesInSquad = 1,
                ForeignDomesticFullMemberMax = 3,
                CurrencyLabel = c switch { "India" => "₹", "England" => "£", "Australia" => "A$", "Pakistan" => "₨", "South Africa" => "R", _ => "$" },
                TalentProduction = talent.TryGetValue(c, out var t) ? t : 55,
                PaceVsSpinTalentBias = sub ? 32 : pace ? 66 : 50,
                BoardYouthInvestment = boardControlled.Contains(c) ? 62 : 52,
                HomePitchSeamBias = c == "England" ? 14 : pace ? 8 : sub ? -6 : 0,
                HomePitchSpinBias = sub ? 15 : c == "Australia" ? -4 : 0,
                PoliticalStability = c == "Pakistan" ? 45 : boardControlled.Contains(c) ? 72 : 68,
                CricketHistoryWeight = history.TryGetValue(c, out var hw) ? hw : 55,
                // Phase 12: the media market. England / India / Pakistan run hot; New Zealand calm.
                MediaIntensity = c switch { "England" => 88, "India" => 90, "Pakistan" => 84, "Australia" => 78, "South Africa" => 62, "New Zealand" => 45, _ => 55 },
                MediaHomeBias = c switch { "India" => 68, "Pakistan" => 64, "England" => 58, "Australia" => 55, _ => 50 },
                MediaVolatility = c switch { "Pakistan" => 82, "England" => 74, "India" => 70, "Australia" => 55, "New Zealand" => 38, _ => 50 },
                // Phase 16 (§15.6): a distinct point on the talent-cycle wave per nation. RNG-FREE
                // (derived from the name) so it never perturbs the seeding stream.
                GenerationCyclePhase = (Math.Abs(c.Aggregate(17, (h, ch) => unchecked(h * 31 + ch))) % 1000) / 1000.0,
            };
        }
        foreach (var a in AssociateNations)
            map[a] = new CountryProfile
            {
                Nationality = a,
                Membership = MembershipStatus.Associate,
                DomesticStructure = DomesticStructureModel.ClubMembership,
                Hemisphere = a == "Netherlands" || a == "Scotland" ? Hemisphere.Northern : Hemisphere.Northern,
                EconomicScale = 0.35,
                RainRiskMultiplier = a == "Scotland" || a == "Netherlands" ? 1.7 : 1.0,
                AllowsForeignDomesticPlayers = true,
                CurrencyLabel = "$",
                CricketHistoryWeight = 10,
                BoardYouthInvestment = 44,
            };
        return map;
    }

    /// <summary>
    /// Phase 9, Slice 9.0: a starting domestic contract for every senior player at every domestic
    /// club (not national teams, not academy players, not franchise teams). Wage from the
    /// WageBillService per-player benchmark; a 1-4 year term; a young player likely homegrown; a
    /// genuine star a chance of a release clause. Returned for the caller to load into the world.
    /// </summary>
    public List<PlayerContract> GeneratePlayerContracts(IReadOnlyList<Team> domesticClubs, IReadOnlyList<Player> players)
    {
        var svc = new Domain.Services.PlayerContractService();
        var byId = players.ToDictionary(p => p.Id);
        var contracts = new List<PlayerContract>();
        foreach (var club in domesticClubs)
        foreach (var pid in club.SquadPlayerIds)
            if (byId.TryGetValue(pid, out var p) && p.AcademyTeamId is null)
                contracts.Add(svc.SeedContract(p, club, _worldStartDate, _random));
        return contracts;
    }

    /// <summary>
    /// FIVE franchise auction leagues on month-DISJOINT windows - the IPL, PSL, CPL, SA20 and BBL
    /// analogues - all T20, all privately owned (Corporate / PrivateOwner), all with a uniform
    /// overseas cap of 4 in the XI and 8 in the squad (a deliberate simplification - NOT the same
    /// as the DOMESTIC rule, which is 4-in-squad / 2-in-XI / >=1 associate). Every franchise starts
    /// every auction with the SAME purse (no league-position wealth gap) and NO franchise ever runs
    /// a loss - FranchiseFinanceService settles each season from a guaranteed central pool. Squads
    /// are EMPTY at seed time; FranchiseAuctionService fills them when a window opens (a MEGA
    /// auction every ~3 years, MINI top-up in between). IPL and BBL use the Impact Player rule.
    ///
    /// Host-nation simplification, stated rather than hidden: the fictional seed world only has the
    /// core countries, so CPL/SA20 are hosted by seeded nations in window order rather than by a
    /// real Caribbean / South African player base. A real-data import fixes the host.
    /// Windows: IPL {Mar-Apr}, PSL {May}, CPL {Jun-Jul}, SA20 {Aug-Sep}, BBL {Oct-Nov} - all clear
    /// of the Sep-Mar domestic block bar a deliberate BBL/Shield-style brush; a Jun-Jul World-T20
    /// brush is deliberate too (club vs country).
    /// </summary>
    public List<(List<Team> Teams, List<Ground> Grounds, Competition Comp, CompetitionSeason Season, List<Fixture> Fixtures)>
        GenerateFranchiseLeagues(string[] countries, int seasonYear)
    {
        // Every host must be a SEEDED country (it has a real domestic player base - the nationality
        // that counts as "domestic" for the overseas cap). With only four core countries and five
        // leagues, one country hosts two - fine, since the auction draws from the whole world pool
        // and the host only sets which nationality is "local".
        string Host(int i) => countries.Length > 0 ? countries[i % countries.Length] : "India";
        var leagues = new (string Name, string Host, int SM, int SD, int EM, int ED, string[] Names)[]
        {
            ("Indian Premier League",     Host(0), 3, 15, 4, 28, new[] { "Capitals", "Kings", "Riders", "Royals", "Titans", "Super Kings", "Sunrisers", "Warriors" }),
            ("Pakistan Super League",      Host(1), 5, 5,  5, 31, new[] { "Qalandars", "Zalmi", "Sultans", "Gladiators", "Kings", "United" }),
            ("Caribbean Premier League",   Host(2), 6, 10, 7, 22, new[] { "Tallawahs", "Warriors", "Patriots", "Royals", "Knight Riders", "Falcons" }),
            ("SA20 League",                Host(3), 8, 5, 9, 20, new[] { "Sunrisers", "Super Giants", "Capitals", "Paarl Royals", "Cape Town", "Joburg" }),
            ("Big Bash League",            Host(0), 10, 8, 11, 25, new[] { "Sixers", "Thunder", "Stars", "Renegades", "Scorchers", "Heat", "Strikers", "Hurricanes" }),
        }.Take(Math.Clamp(countries.Length + 1, 2, 5)).ToArray();

        var scheduler = new FixtureGenerationService();
        var result = new List<(List<Team>, List<Ground>, Competition, CompetitionSeason, List<Fixture>)>();

        foreach (var lg in leagues)
        {
            int count = 6;
            var teams = new List<Team>();
            var grounds = new List<Ground>();
            for (int i = 0; i < count; i++)
            {
                var team = new Team
                {
                    Name = $"{lg.Names[i % lg.Names.Length]} ({Abbrev(lg.Name)})",
                    Country = lg.Host,
                    IsNational = false,
                    IsFranchise = true,
                    Strength = 60,
                    Reputation = new Reputation(domestic: 70, continental: 42, worldwide: 28),
                    CulturalIdentity = CoachingPhilosophy.PerformanceFocused,
                    // Phase 13: an auction philosophy per franchise, RNG-free (fixed by slot).
                    FranchiseArchetype = (i % 4) switch
                    {
                        0 => FranchiseArchetype.Moneyball,
                        1 => FranchiseArchetype.StarHunter,
                        2 => FranchiseArchetype.YouthBuilder,
                        _ => FranchiseArchetype.Balanced
                    },
                };
                team.Board = new Domain.ValueObjects.ClubBoard
                {
                    Ownership = i % 2 == 0 ? OwnershipModel.Corporate : OwnershipModel.PrivateOwner,
                    Wealth = 88, Ambition = 78 + i % 3 * 4, Patience = 45, FanSentiment = 60,
                };
                team.Finances.Budget = 20_000_000; // the auction purse comes out of here
                var ground = new Ground
                {
                    Name = $"{lg.Names[i % lg.Names.Length]} Arena",
                    City = PakistanCities[i % PakistanCities.Length], Country = lg.Host,
                    Capacity = RandInt(28000, 52000),
                    Ends = new List<string> { "North End", "South End" },
                    PitchPaceRating = Math.Round(35 + _random.NextDouble() * 45, 1),
                    PitchSpinRating = Math.Round(35 + _random.NextDouble() * 45, 1),
                    PitchBounceRating = Math.Round(35 + _random.NextDouble() * 45, 1),
                    PitchBattingFriendliness = Math.Round(45 + _random.NextDouble() * 35, 1),
                    Reputation = Math.Round(55 + _random.NextDouble() * 30, 1),
                    HasFloodlights = true,
                    DewTendency = Math.Round(25 + _random.NextDouble() * 45, 1),
                };
                ground.HomeTeamIds.Add(team.Id);
                team.HomeGroundId = ground.Id;
                teams.Add(team);
                grounds.Add(ground);
            }

            var comp = new Competition
            {
                Name = lg.Name,
                StructureType = CompetitionStructureType.LeagueWithPlayoffs,
                Scope = CompetitionScope.FranchiseLeague,
                Format = MatchFormat.T20,
                Country = lg.Host,
                Prestige = 74,
                Reputation = 76,
                PlayoffQualifierCount = 4,
                PlayoffFormat = PlayoffFormat.IplStyle,
                OverseasPlayerLimit = 4,
                OverseasSquadLimit = 8,
                IsFranchiseAuctionLeague = true,
                LastMegaAuctionYear = 0,
                Window = CompetitionWindow.Annual(startMonth: lg.SM, startDay: lg.SD, endMonth: lg.EM, endDay: lg.ED),
            };

            var season = new CompetitionSeason { CompetitionId = comp.Id, Year = seasonYear };
            foreach (var t in teams) { season.ParticipatingTeamIds.Add(t.Id); season.GetOrCreateStanding(t.Id); }

            var teamsById = teams.ToDictionary(t => t.Id);
            var pairings = scheduler.GenerateRoundRobinPairings(season.ParticipatingTeamIds, doubleRoundRobin: true);
            var staging = new CompetitionCalendarService().GetStaging(comp, seasonYear);
            var fixtures = scheduler.ScheduleRoundRobin(comp.Id, season.Id, pairings,
                staging?.StartDate ?? new DateOnly(seasonYear, lg.SM, lg.SD),
                staging?.EndDate ?? new DateOnly(seasonYear, lg.EM, lg.ED),
                comp.Format, teamsById);

            result.Add((teams, grounds, comp, season, fixtures));
        }

        return result;
    }

    private static string Abbrev(string leagueName) => leagueName switch
    {
        "Indian Premier League" => "IPL",
        "Pakistan Super League" => "PSL",
        "Caribbean Premier League" => "CPL",
        "SA20 League" => "SA20",
        "Big Bash League" => "BBL",
        _ => "FL"
    };

    /// <summary>
    /// Phase 8, Slice 8.1: generates a starting youth academy for each of the given clubs. Sets each
    /// prospect's AcademyTeamId + CurrentTeamId and adds its id to team.AcademyPlayerIds; returns
    /// the flat list of prospects for the caller to add to the world (at the END of the player
    /// list). A separate, opt-in step - GenerateStarterWorld does NOT call it, so every existing
    /// caller that checks an exact player count is unaffected.
    /// </summary>
    public List<Player> GenerateAcademies(IReadOnlyList<Team> teams, IReadOnlyList<Ground> grounds)
    {
        var academy = new AcademyService();
        var all = new List<Player>();
        foreach (var team in teams)
        {
            var homeGround = grounds.FirstOrDefault(g => g.Id == team.HomeGroundId);
            int scoutingQuality = Math.Clamp(team.Facilities.ScoutingQuality, 0, 100);
            all.AddRange(academy.GenerateIntake(team, homeGround, _worldStartDate, _random, scoutingQuality));
        }
        return all;
    }

    private List<Player> GenerateSquad(int size, double teamStrength)
    {
        var squad = new List<Player>();

        // Rough role composition of a 13-player squad, matching real squad shape.
        var roles = new List<PlayerRole>();
        roles.AddRange(Enumerable.Repeat(PlayerRole.Batsman, 5));
        roles.AddRange(Enumerable.Repeat(PlayerRole.Bowler, 5));
        roles.AddRange(Enumerable.Repeat(PlayerRole.BattingAllrounder, 1));
        roles.AddRange(Enumerable.Repeat(PlayerRole.BowlingAllrounder, 1));
        roles.Add(PlayerRole.WicketKeeper);
        while (roles.Count < size) roles.Add(PlayerRole.Batsman);

        foreach (var role in roles.Take(size))
        {
            var generated = GeneratePlayer(role, teamStrength);
            SeedExistingCareer(generated);
            squad.Add(generated);
        }

        return squad;
    }

    /// <summary>
    /// Phase 7, Slice 7.9: a panel of match officials for the world - a spread across the four
    /// tiers, from a handful of elite umpires down to development-panel officials. Nationalities
    /// are drawn from the countries passed in (so neutral-umpire assignment has something to work
    /// with); ages skew older than players.
    /// </summary>
    public List<Umpire> GenerateUmpirePanel(string[]? countries = null, int count = 24)
    {
        countries ??= new[] { "Pakistan", "Australia", "England", "India", "South Africa", "New Zealand", "Sri Lanka", "West Indies" };
        var panel = new List<Umpire>();

        for (int i = 0; i < count; i++)
        {
            // Roughly: 5 elite, 8 international, 8 domestic, the rest development.
            var tier = i < count * 0.2 ? UmpirePanel.Elite
                : i < count * 0.55 ? UmpirePanel.International
                : i < count * 0.85 ? UmpirePanel.Domestic
                : UmpirePanel.Development;

            int quality = tier switch
            {
                UmpirePanel.Elite => RandInt(16, 20),
                UmpirePanel.International => RandInt(13, 18),
                UmpirePanel.Domestic => RandInt(10, 15),
                _ => RandInt(7, 13)
            };

            double reputation = tier switch
            {
                UmpirePanel.Elite => 78 + _random.NextDouble() * 18,
                UmpirePanel.International => 58 + _random.NextDouble() * 18,
                UmpirePanel.Domestic => 38 + _random.NextDouble() * 18,
                _ => 20 + _random.NextDouble() * 16
            };

            panel.Add(new Umpire
            {
                FirstName = FirstNames[_random.Next(FirstNames.Length)],
                LastName = LastNames[_random.Next(LastNames.Length)],
                Nationality = countries[_random.Next(countries.Length)],
                DateOfBirth = _worldStartDate.AddYears(-RandInt(38, 58)).AddDays(-RandInt(0, 365)),
                Panel = tier,
                Accuracy = Clamp20(quality + RandInt(-2, 2)),
                Consistency = Clamp20(quality + RandInt(-3, 2)),
                Composure = Clamp20(quality + RandInt(-3, 3)),
                LbwJudgement = Clamp20(quality + RandInt(-2, 2)),
                MatchesOfficiated = tier switch { UmpirePanel.Elite => RandInt(120, 300), UmpirePanel.International => RandInt(60, 180), UmpirePanel.Domestic => RandInt(20, 90), _ => RandInt(2, 30) },
                Reputation = Math.Round(Math.Clamp(reputation, 0, 100), 1),
            });
        }

        return panel;
    }

    private static int Clamp20(int v) => Math.Clamp(v, 1, 20);

    /// <summary>
    /// Gives a generated player the career he would already have had by the start date. A
    /// snapshot world does not contain 25-year-olds who have never played: experience is
    /// broadly proportional to how long he has been around, with real spread, because a
    /// 28-year-old fringe player and a 28-year-old established international are both normal.
    /// Real-world import replaces this with actual career records; the shape it produces is
    /// the same.
    /// </summary>
    private void SeedExistingCareer(Player player)
    {
        int age = player.Age(_worldStartDate);
        int yearsSinceDebut = Math.Max(0, age - RandInt(17, 21));
        if (yearsSinceDebut == 0) return;

        // Better players get picked more, so games accumulate faster for them.
        double abilityFactor = 0.5 + Math.Clamp(player.CurrentAbility / 200.0, 0, 1);
        int perSeason = (int)Math.Round(RandInt(4, 14) * abilityFactor);

        var matches = new Dictionary<MatchFormat, int>
        {
            [MatchFormat.Test] = (int)(yearsSinceDebut * perSeason * 0.35),
            [MatchFormat.ODI] = (int)(yearsSinceDebut * perSeason * 0.3),
            [MatchFormat.T20] = (int)(yearsSinceDebut * perSeason * 0.35)
        };

        // Only the better players have played international cricket at all.
        int international = player.CurrentAbility >= 130 ? (int)(yearsSinceDebut * RandInt(1, 6) * abilityFactor) : 0;
        int bigMatches = international > 0 ? RandInt(0, Math.Max(1, international / 5)) : RandInt(0, 2);

        player.Experience = PlayerExperience.FromExistingCareer(
            matches, international, bigMatches, yearsSinceDebut,
            _worldStartDate.AddYears(-yearsSinceDebut));
    }

    private Player GeneratePlayer(PlayerRole role, double teamStrength)
    {
        // Team strength nudges the base skill band up/down so stronger teams field better players.
        int baseline = (int)Math.Clamp(8 + (teamStrength - 50) / 6.0, 5, 15);

        var player = new Player
        {
            FirstName = FirstNames[_random.Next(FirstNames.Length)],
            LastName = LastNames[_random.Next(LastNames.Length)],
            Nationality = _nationality,
            DateOfBirth = _worldStartDate.AddYears(-RandInt(18, 36)).AddDays(-RandInt(0, 365)),
            BattingHand = _random.NextDouble() < 0.75 ? BattingHand.Right : BattingHand.Left,
            PrimaryRole = role,
            BowlingStyle = role == PlayerRole.Batsman ? BowlingStyle.None : RandomBowlingStyle()
        };

        (player.CurrentAbility, player.PotentialAbility) = GenerateAbilityBand(baseline);

        player.Batting = GenerateBatting(role, baseline);
        player.Bowling = GenerateBowling(role, baseline);
        player.Fielding = GenerateFielding(baseline);
        player.Mental = GenerateMental(baseline);
        player.Physical = GeneratePhysical(baseline);

        // Phase 8, Slice 8.4: individual peak-timing variation. Derived RNG-FREE from attributes
        // this player has already rolled (plus his surname length for a little more spread), so
        // this adds no _random draw and does not shift the seeding stream a step - the Phase 7
        // determinism lesson. A sturdy, consistent, durable player peaks a touch later and declines
        // more gradually; an injury-prone one the reverse. Academy intake (AcademyService) rolls a
        // wider, RNG-driven offset for genuine youth prospects.
        int peakSeed = (player.Mental.Consistency + player.Physical.Recovery + player.Physical.Fitness
                        - player.Physical.InjuryProneness * 2 + player.LastName.Length) % 7 - 3;
        player.PeakAgeOffset = Math.Clamp(peakSeed, -3, 3);

        // S6: a rare heritage link to another nation (a parent's / grandparent's country). Seeded
        // RNG-FREE from a stable name hash (no _random draw - the determinism lesson), ~7% of
        // players. RepresentationDriftService can move an out-of-favour player to a heritage nation
        // subject to the ICC stand-down.
        int heritageHash = Math.Abs((player.FirstName + player.LastName).Aggregate(23, (h, ch) => unchecked(h * 31 + ch)));
        if (heritageHash % 100 < 7)
        {
            var pick = HeritagePool[heritageHash / 100 % HeritagePool.Length];
            if (!string.Equals(pick, player.Nationality, StringComparison.OrdinalIgnoreCase))
                player.HeritageNations.Add(pick);
        }

        // Small starting form nudge so squads aren't all perfectly neutral (-15..+15).
        player.Form.RecordPerformance(RandInt(-15, 15));
        _roleTraitDeriver.ApplyTo(player); // sets BattingRole/BowlingRole/traits from the attributes just generated
        player.RecalculateFormatSuitability();

        return player;
    }

    private (int current, int potential) GenerateAbilityBand(int baseline)
    {
        int current = Math.Clamp(baseline * 8 + RandInt(-15, 15), 40, 180);
        int ceiling = Math.Clamp(current + RandInt(0, 35), current, 195); // potential >= current always
        return (current, ceiling);
    }

    private BattingAttributes GenerateBatting(PlayerRole role, int baseline)
    {
        bool isBatFocused = role is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper;
        int band = isBatFocused ? baseline + 3 : Math.Max(3, baseline - 4);

        return new BattingAttributes
        {
            Technique = Rnd(band), Timing = Rnd(band), ShotSelection = Rnd(band), DefensiveAbility = Rnd(band),
            Aggression = Rnd(band), AgainstPace = Rnd(band), AgainstSpin = Rnd(band), ShortBallAbility = Rnd(band),
            SwingHandling = Rnd(band), SeamHandling = Rnd(band), SpinHandling = Rnd(band),
            DeathOverBatting = Rnd(band), PowerHitting = Rnd(band), StrikeRotation = Rnd(band),
            BoundaryHitting = Rnd(band), RiskManagement = Rnd(band)
        };
    }

    private BowlingAttributes GenerateBowling(PlayerRole role, int baseline)
    {
        bool isBowlFocused = role is PlayerRole.Bowler or PlayerRole.BowlingAllrounder or PlayerRole.BattingAllrounder;
        int band = isBowlFocused ? baseline + 3 : Math.Max(2, baseline - 6);

        return new BowlingAttributes
        {
            Pace = Rnd(band), Accuracy = Rnd(band), Swing = Rnd(band), Seam = Rnd(band), Spin = Rnd(band),
            Variation = Rnd(band), Yorker = Rnd(band), Bouncer = Rnd(band), SlowerBall = Rnd(band),
            DeathBowling = Rnd(band), NewBallBowling = Rnd(band), MiddleOverBowling = Rnd(band),
            Containment = Rnd(band), AttackingAbility = Rnd(band)
        };
    }

    private FieldingAttributes GenerateFielding(int baseline) => new()
    {
        Catching = Rnd(baseline), Reflexes = Rnd(baseline), Throwing = Rnd(baseline),
        GroundFielding = Rnd(baseline), Positioning = Rnd(baseline), BoundaryFielding = Rnd(baseline)
    };

    private MentalAttributes GenerateMental(int baseline) => new()
    {
        Composure = Rnd(baseline), Concentration = Rnd(baseline), Confidence = Rnd(baseline),
        Determination = Rnd(baseline), Leadership = Rnd(baseline), PressureHandling = Rnd(baseline),
        DecisionMaking = Rnd(baseline), Adaptability = Rnd(baseline), Professionalism = Rnd(baseline),
        Consistency = Rnd(baseline), GameAwareness = Rnd(baseline),
        // Phase 11 / Phase 15: RNG-FREE (the centre value) so adding these attributes does not
        // perturb the seeding stream - the same discipline as PeakAgeOffset. Individual variation
        // comes later, if ever, from training/ageing, not from a seed roll.
        RunningCalling = Math.Clamp(baseline, 1, 20),
        ReviewJudgement = Math.Clamp(baseline, 1, 20)
    };

    private PhysicalAttributes GeneratePhysical(int baseline) => new()
    {
        Fitness = Rnd(baseline), Stamina = Rnd(baseline), Strength = Rnd(baseline),
        Speed = Rnd(baseline), InjuryProneness = RandInt(4, 16), Recovery = Rnd(baseline)
    };

    /// <summary>Random value clustered around a band, clamped to the 1-20 attribute scale.</summary>
    private int Rnd(int band) => Math.Clamp(band + RandInt(-3, 3), 1, 20);

    private int RandInt(int minInclusive, int maxInclusive) => _random.Next(minInclusive, maxInclusive + 1);

    private static int ClampFacility(int value) => Math.Clamp(value, 5, 95);

    private BowlingStyle RandomBowlingStyle()
    {
        var styles = new[]
        {
            BowlingStyle.RightArmFast, BowlingStyle.RightArmFastMedium, BowlingStyle.RightArmMediumFast, BowlingStyle.RightArmMedium,
            BowlingStyle.RightArmOffSpin, BowlingStyle.RightArmLegSpin, BowlingStyle.LeftArmFast,
            BowlingStyle.LeftArmFastMedium, BowlingStyle.LeftArmMediumFast, BowlingStyle.LeftArmMedium,
            BowlingStyle.LeftArmOrthodox, BowlingStyle.LeftArmChinaman
        };
        return styles[_random.Next(styles.Length)];
    }
}
