using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What a proposed project would cost and how long it would take, before anyone commits to it. Affordable is separate from cost so a UI can show an unaffordable option greyed out rather than hiding it.</summary>
public sealed record InfrastructureQuote(InfrastructureProjectType Type, int FromLevel, int ToLevel, double Cost, int DurationDays, bool Affordable, string Summary);

/// <summary>Outcome of completing a project, so news/board systems can report a real event rather than a number silently changing.</summary>
public sealed record ProjectCompletion(InfrastructureProject Project, string Description, Ground? NewGround);

/// <summary>
/// Owns the whole infrastructure investment loop: quote, fund, build, complete.
///
/// Three rules shape the numbers, and all three exist to stop this becoming a "spend money,
/// win game" button:
/// - **Cost rises steeply with level.** Taking medical facilities from 30 to 40 is ordinary
///   spending; taking them from 80 to 90 is a major capital project. Without this, every team
///   would eventually max everything and facilities would stop differentiating anyone.
/// - **Nothing is instant.** Section 55: invest now, benefit later. A completion date means a
///   coach who invests in youth facilities may well be sacked before the payoff arrives -
///   which is exactly the tension the spec wants.
/// - **Upkeep is permanent.** TeamFinanceService already derives annual upkeep from what a
///   team owns, so the consequence needs no extra wiring: build a 60,000-seat stadium and the
///   running cost follows you forever.
/// </summary>
public sealed class InfrastructureService
{
    /// <summary>Facilities cap below 100 - there is always somewhere better, and a hard 100 would let every wealthy club converge on identical perfection.</summary>
    public const int MaximumFacilityLevel = 95;

    private const double BaseCostPerPoint = 45_000;
    private const double CostPerSeat = 900;
    private const double NewStadiumLandAndFixedCost = 4_000_000;

    // Durations are calendar-realistic, because this game runs on a real-world clock and a
    // build that finishes in "a few weeks" would be visibly wrong to anyone watching the date
    // tick over. Reference points: a facility redevelopment is an off-season-plus project, a
    // significant new stand is roughly two to three years from commitment to first match, and
    // a new stadium is three to five. Time scales with the TARGET level as well as the number
    // of points gained - elite work is slower as well as dearer, because you are no longer
    // just building, you are building to a standard.
    private const int MinimumProjectDays = 60;
    private const int BaseDaysPerFacilityPoint = 7;
    private const int MinimumExpansionDays = 365;
    private const int MinimumNewStadiumDays = 900;
    private const int MaximumNewStadiumDays = 1825;

    // ---------------- Quoting ----------------

    /// <summary>
    /// What it would cost this team to take a facility to a target level. Cost scales with
    /// both the number of points gained and how high the target is.
    /// </summary>
    public InfrastructureQuote QuoteFacilityUpgrade(Team team, InfrastructureProjectType type, int targetLevel, Ground? homeGround = null)
    {
        int current = GetCurrentLevel(team, type, homeGround);
        int target = Math.Clamp(targetLevel, 0, MaximumFacilityLevel);

        if (target <= current)
            return new InfrastructureQuote(type, current, current, 0, 0, false,
                $"{type} is already at {current} - nothing to build.");

        int points = target - current;

        // Each individual point costs more than the last, so the marginal cost of elite
        // facilities is genuinely punishing rather than linear.
        double cost = 0;
        for (int level = current + 1; level <= target; level++)
            cost += BaseCostPerPoint * (1 + Math.Pow(level / 100.0, 2) * 4);

        cost = Math.Round(cost, 0);
        int days = EstimateFacilityDays(points, target);

        return new InfrastructureQuote(type, current, target, cost, days, team.Finances.Budget >= cost,
            $"Upgrade {type} from {current} to {target}: {cost:N0} over roughly {days} days.");
    }

    /// <summary>Cost of adding seats to an existing ground. Expansion is cheaper per seat than building new, which is why clubs expand rather than relocate.</summary>
    public InfrastructureQuote QuoteStadiumExpansion(Team team, Ground ground, int additionalSeats)
    {
        if (additionalSeats <= 0)
            return new InfrastructureQuote(InfrastructureProjectType.StadiumExpansion, ground.Capacity, ground.Capacity, 0, 0, false, "No additional seats requested.");

        double cost = Math.Round(additionalSeats * CostPerSeat, 0);
        int days = EstimateExpansionDays(additionalSeats);

        return new InfrastructureQuote(InfrastructureProjectType.StadiumExpansion, ground.Capacity, ground.Capacity + additionalSeats,
            cost, days, team.Finances.Budget >= cost,
            $"Expand {ground.Name} by {additionalSeats:N0} seats to {ground.Capacity + additionalSeats:N0}: {cost:N0} over roughly {days} days.");
    }

    /// <summary>
    /// Cost of building a new home ground. Deliberately expensive and slow (18 months plus) -
    /// a new stadium should be a once-in-a-generation decision, not a routine purchase.
    /// </summary>
    public InfrastructureQuote QuoteNewStadium(Team team, int capacity)
    {
        int seats = Math.Clamp(capacity, 5_000, 110_000);
        double cost = Math.Round(NewStadiumLandAndFixedCost + seats * CostPerSeat * 1.4, 0);
        int days = Math.Clamp(MinimumNewStadiumDays + seats / 40, MinimumNewStadiumDays, MaximumNewStadiumDays);

        return new InfrastructureQuote(InfrastructureProjectType.NewStadium, 0, seats, cost, days, team.Finances.Budget >= cost,
            $"Build a new {seats:N0}-seat home ground: {cost:N0} over roughly {days} days.");
    }

    /// <summary>Facility work: slower per point the higher the target, floored at a full off-season.</summary>
    public int EstimateFacilityDays(int points, int targetLevel)
    {
        if (points <= 0) return 0;
        double daysPerPoint = BaseDaysPerFacilityPoint + Math.Clamp(targetLevel, 0, 100) / 100.0 * 14;
        return Math.Max(MinimumProjectDays, (int)Math.Round(points * daysPerPoint));
    }

    /// <summary>Stand work: roughly a year minimum, and a genuinely large expansion runs to two or three.</summary>
    public int EstimateExpansionDays(int additionalSeats) =>
        additionalSeats <= 0 ? 0 : Math.Max(MinimumExpansionDays, 240 + additionalSeats / 25);

    // ---------------- Geography ----------------

    /// <summary>
    /// Known cricketing cities per country. Deliberately data in one replaceable place: this is
    /// exactly what the external data layer should own once real-world import/modding lands, and
    /// the same list will be needed by the fixture scheduler.
    /// </summary>
    private static readonly Dictionary<string, string[]> CitiesByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pakistan"] = new[] { "Lahore", "Karachi", "Rawalpindi", "Multan", "Faisalabad", "Peshawar", "Quetta", "Islamabad", "Sialkot", "Hyderabad" },
        ["India"] = new[] { "Mumbai", "Delhi", "Kolkata", "Chennai", "Bengaluru", "Hyderabad", "Ahmedabad", "Mohali", "Pune", "Jaipur", "Lucknow", "Indore" },
        ["Australia"] = new[] { "Melbourne", "Sydney", "Brisbane", "Adelaide", "Perth", "Hobart", "Canberra" },
        ["England"] = new[] { "London", "Manchester", "Birmingham", "Leeds", "Nottingham", "Southampton", "Bristol", "Durham", "Cardiff" },
        ["South Africa"] = new[] { "Johannesburg", "Cape Town", "Durban", "Centurion", "Gqeberha", "Bloemfontein", "Paarl" },
        ["New Zealand"] = new[] { "Auckland", "Wellington", "Christchurch", "Hamilton", "Napier", "Dunedin", "Mount Maunganui" },
        ["Sri Lanka"] = new[] { "Colombo", "Kandy", "Galle", "Dambulla", "Hambantota" },
        ["Bangladesh"] = new[] { "Dhaka", "Chattogram", "Sylhet", "Khulna" },
        ["West Indies"] = new[] { "Bridgetown", "Kingston", "Port of Spain", "Gros Islet", "North Sound", "Providence" },
        ["Zimbabwe"] = new[] { "Harare", "Bulawayo" },
        ["Afghanistan"] = new[] { "Kabul", "Kandahar", "Khost" },
        ["Ireland"] = new[] { "Dublin", "Belfast" }
    };

    public static IReadOnlyList<string> GetKnownCities(string country) =>
        CitiesByCountry.TryGetValue(country, out var cities) ? cities : Array.Empty<string>();

    /// <summary>
    /// Why a proposed new ground can't be built, or null if it can. Geography is validated
    /// rather than assumed: a side can only build at home, the city has to be a real one for
    /// that country, and two grounds can't share a name in the same city.
    /// </summary>
    public string? ValidateNewStadium(Team team, string groundName, string city, int capacity, IEnumerable<Ground> existingGrounds)
    {
        if (string.IsNullOrWhiteSpace(groundName)) return "A ground needs a name.";
        if (string.IsNullOrWhiteSpace(city)) return "A ground needs a city.";
        if (capacity < 5_000) return "A senior venue needs a capacity of at least 5,000.";
        if (capacity > 110_000) return "No cricket ground on earth holds more than 110,000.";

        var knownCities = GetKnownCities(team.Country);
        if (knownCities.Count > 0 && !knownCities.Contains(city.Trim(), StringComparer.OrdinalIgnoreCase))
            return $"{city} is not a known cricketing city in {team.Country}.";

        if (existingGrounds.Any(g =>
                string.Equals(g.Name, groundName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.City, city.Trim(), StringComparison.OrdinalIgnoreCase)))
            return $"There is already a ground called {groundName} in {city}.";

        return null;
    }

    // ---------------- Committing ----------------

    /// <summary>
    /// Funds and starts a facility project. Returns null when the team can't afford it -
    /// budget is debited immediately, because construction is paid for as it happens, and
    /// letting a team commit to work it can't pay for would make the budget meaningless.
    /// </summary>
    public InfrastructureProject? StartFacilityUpgrade(Team team, InfrastructureProjectType type, int targetLevel, DateOnly startDate, Ground? homeGround = null)
    {
        var quote = QuoteFacilityUpgrade(team, type, targetLevel, homeGround);
        if (!quote.Affordable || quote.Cost <= 0) return null;

        team.Finances.Budget -= quote.Cost;

        var project = new InfrastructureProject
        {
            TeamId = team.Id,
            GroundId = IsGroundFacility(type) ? homeGround?.Id : null,
            Type = type,
            FromLevel = quote.FromLevel,
            ToLevel = quote.ToLevel,
            Cost = quote.Cost,
            StartDate = startDate,
            ExpectedCompletionDate = startDate.AddDays(quote.DurationDays)
        };
        project.Begin();
        return project;
    }

    public InfrastructureProject? StartStadiumExpansion(Team team, Ground ground, int additionalSeats, DateOnly startDate)
    {
        var quote = QuoteStadiumExpansion(team, ground, additionalSeats);
        if (!quote.Affordable || quote.Cost <= 0) return null;

        team.Finances.Budget -= quote.Cost;

        var project = new InfrastructureProject
        {
            TeamId = team.Id,
            GroundId = ground.Id,
            Type = InfrastructureProjectType.StadiumExpansion,
            FromLevel = ground.Capacity,
            ToLevel = ground.Capacity + additionalSeats,
            Capacity = additionalSeats,
            Cost = quote.Cost,
            StartDate = startDate,
            ExpectedCompletionDate = startDate.AddDays(quote.DurationDays)
        };
        project.Begin();
        return project;
    }

    /// <summary>
    /// Commissions a new home ground. Geography is enforced rather than assumed: a team can
    /// only build in its own country, and the city must be named. A domestic side spawning a
    /// stadium in another country would be nonsense the moment anyone looked at the fixture
    /// list, and it's cheaper to refuse it here than to explain it later.
    /// </summary>
    public InfrastructureProject? StartNewStadium(Team team, string groundName, string city, int capacity, DateOnly startDate, IEnumerable<Ground>? existingGrounds = null)
    {
        if (ValidateNewStadium(team, groundName, city, capacity, existingGrounds ?? Array.Empty<Ground>()) is not null) return null;

        var quote = QuoteNewStadium(team, capacity);
        if (!quote.Affordable) return null;

        team.Finances.Budget -= quote.Cost;

        var project = new InfrastructureProject
        {
            TeamId = team.Id,
            GroundId = null,
            Type = InfrastructureProjectType.NewStadium,
            Cost = quote.Cost,
            NewGroundName = groundName.Trim(),
            City = city.Trim(),
            Country = team.Country,          // geography follows the team, never the caller
            Capacity = quote.ToLevel,
            StartDate = startDate,
            ExpectedCompletionDate = startDate.AddDays(quote.DurationDays)
        };
        project.Begin();
        return project;
    }

    // ---------------- Completing ----------------

    /// <summary>
    /// Completes every project that has come due by this date and applies its effect.
    /// Grounds are looked up by id; a NewStadium project creates its Ground here and points
    /// the team at it, which is the moment the investment becomes real.
    /// </summary>
    public IReadOnlyList<ProjectCompletion> AdvanceTo(
        DateOnly date,
        IEnumerable<InfrastructureProject> projects,
        IReadOnlyDictionary<Guid, Team> teams,
        IDictionary<Guid, Ground> grounds)
    {
        var completions = new List<ProjectCompletion>();

        // Materialised before iterating: completing a NewStadium adds to the grounds dictionary,
        // and a lazy sequence over a collection being mutated mid-loop throws.
        foreach (var project in projects.Where(p => p.IsDueOn(date)).OrderBy(p => p.ExpectedCompletionDate).ToList())
        {
            if (!teams.TryGetValue(project.TeamId, out var team)) continue;

            Ground? newGround = null;
            string description;

            if (project.Type == InfrastructureProjectType.NewStadium)
            {
                newGround = new Ground
                {
                    Name = project.NewGroundName,
                    City = project.City,
                    Country = project.Country,
                    Capacity = project.Capacity,
                    EstablishedYear = date.Year,
                    // A brand-new ground has no history and no aura. Reputation has to be
                    // earned by hosting cricket that matters - a new stadium is not Lord's.
                    Reputation = 25,
                    HasFloodlights = true,
                    Ends = new List<string> { $"{project.City} North End", $"{project.City} South End" }
                };
                // A new square is unworn and properly drained - the one thing a brand-new
                // ground is genuinely better at from day one.
                newGround.Facilities.PitchInfrastructure = 65;
                newGround.HomeTeamIds.Add(team.Id);
                grounds[newGround.Id] = newGround;
                team.HomeGroundId = newGround.Id;

                description = $"{team.Name} have moved into their new {project.Capacity:N0}-seat home, {newGround.Name} in {newGround.City}.";
            }
            else if (project.Type == InfrastructureProjectType.StadiumExpansion)
            {
                if (project.GroundId is null || !grounds.TryGetValue(project.GroundId.Value, out var ground)) continue;
                ground.Capacity += project.Capacity;
                description = $"{ground.Name} has been expanded to {ground.Capacity:N0} seats.";
            }
            else
            {
                Ground? ground = project.GroundId is not null && grounds.TryGetValue(project.GroundId.Value, out var g) ? g : null;
                ApplyFacilityLevel(team, project.Type, project.ToLevel, ground);
                description = $"{team.Name}'s {project.Type} facilities have been upgraded to {project.ToLevel}.";
            }

            project.Complete(date, newGround?.Id);
            completions.Add(new ProjectCompletion(project, description, newGround));
        }

        return completions;
    }

    // ---------------- level plumbing ----------------

    private static bool IsGroundFacility(InfrastructureProjectType type) => type is
        InfrastructureProjectType.GroundMediaFacilities or InfrastructureProjectType.GroundHospitality or
        InfrastructureProjectType.GroundMedicalFacilities or InfrastructureProjectType.GroundPitchInfrastructure or
        InfrastructureProjectType.GroundYouthFacilities or InfrastructureProjectType.GroundTrainingFacilities;

    public static int GetCurrentLevel(Team team, InfrastructureProjectType type, Ground? ground)
    {
        var tf = team.Facilities;
        var gf = ground?.Facilities;

        return type switch
        {
            InfrastructureProjectType.TrainingFacilities => tf.TrainingQuality,
            InfrastructureProjectType.YouthDevelopment => tf.YouthDevelopmentQuality,
            InfrastructureProjectType.Scouting => tf.ScoutingQuality,
            InfrastructureProjectType.Medical => tf.MedicalQuality,
            InfrastructureProjectType.CorporateCommercial => tf.CorporateCommercialQuality,
            InfrastructureProjectType.GroundMediaFacilities => gf?.MediaFacilities ?? 0,
            InfrastructureProjectType.GroundHospitality => gf?.CorporateHospitality ?? 0,
            InfrastructureProjectType.GroundMedicalFacilities => gf?.MedicalFacilities ?? 0,
            InfrastructureProjectType.GroundPitchInfrastructure => gf?.PitchInfrastructure ?? 0,
            InfrastructureProjectType.GroundYouthFacilities => gf?.YouthFacilities ?? 0,
            InfrastructureProjectType.GroundTrainingFacilities => gf?.TrainingFacilities ?? 0,
            InfrastructureProjectType.StadiumExpansion => ground?.Capacity ?? 0,
            _ => 0
        };
    }

    private static void ApplyFacilityLevel(Team team, InfrastructureProjectType type, int level, Ground? ground)
    {
        var tf = team.Facilities;
        var gf = ground?.Facilities;

        switch (type)
        {
            case InfrastructureProjectType.TrainingFacilities: tf.TrainingQuality = level; break;
            case InfrastructureProjectType.YouthDevelopment: tf.YouthDevelopmentQuality = level; break;
            case InfrastructureProjectType.Scouting: tf.ScoutingQuality = level; break;
            case InfrastructureProjectType.Medical: tf.MedicalQuality = level; break;
            case InfrastructureProjectType.CorporateCommercial: tf.CorporateCommercialQuality = level; break;
            case InfrastructureProjectType.GroundMediaFacilities: if (gf is not null) gf.MediaFacilities = level; break;
            case InfrastructureProjectType.GroundHospitality: if (gf is not null) gf.CorporateHospitality = level; break;
            case InfrastructureProjectType.GroundMedicalFacilities: if (gf is not null) gf.MedicalFacilities = level; break;
            case InfrastructureProjectType.GroundPitchInfrastructure: if (gf is not null) gf.PitchInfrastructure = level; break;
            case InfrastructureProjectType.GroundYouthFacilities: if (gf is not null) gf.YouthFacilities = level; break;
            case InfrastructureProjectType.GroundTrainingFacilities: if (gf is not null) gf.TrainingFacilities = level; break;
        }
    }
}
