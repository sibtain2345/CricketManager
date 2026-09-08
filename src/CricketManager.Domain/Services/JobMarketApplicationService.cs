using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>One application to a vacancy - who applied, how well they fit, and how keen they are.</summary>
public sealed record JobApplication(
    Guid AdvertId, Guid CandidateId, bool CandidateIsCoach,
    double RoleFit, double CultureFit, double Keenness, string Note);

/// <summary>
/// Post-Phase-6 (section A): the candidate-initiated half of the job market. An EMPLOYED or
/// unemployed coach or staff member weighs a vacancy on genuine career benefit - not just raw
/// reputation. A batting coach at a big club applying for a HEAD-coach vacancy at a smaller club
/// can be a real step up, because the ROLE is a step up even though the club is a step down.
///
/// Composes the existing signals: career benefit (role step + club step + ambition), culture fit
/// (RoleFitService.CultureFit), the section-G relocation factor, and current satisfaction. The
/// board's / head coach's side ranks applicants on role fit AND culture, filtered by the advert's
/// listed requirements.
/// </summary>
public sealed class JobMarketApplicationService
{
    private readonly RoleFitService _fit = new();

    /// <summary>Rough role seniority, for the "is this role a step up" read. Head coach is the top.</summary>
    private static int RoleRank(StaffRole? role) => role switch
    {
        null => 6,                                   // head coach
        StaffRole.GeneralManager => 5,
        StaffRole.AssistantCoach => 4,
        StaffRole.ChiefScout or StaffRole.HeadPhysiotherapist => 4,
        StaffRole.BattingCoach or StaffRole.BowlingCoach or StaffRole.FieldingCoach
            or StaffRole.StrengthAndConditioning or StaffRole.MentalPerformanceCoach or StaffRole.Mentor => 3,
        StaffRole.Analyst or StaffRole.DataAnalyst or StaffRole.SportsScientist or StaffRole.Physiotherapist => 2,
        _ => 1
    };

    private static double TeamStanding(Team? team) =>
        team is null ? 40 : (team.Strength + team.Reputation.Domestic * 2 + team.Reputation.Continental) / 4;

    // ---------------- coach applicants ----------------

    public JobApplication? Consider(Coach candidate, VacancyAdvert advert, Team hiringTeam, Team? currentTeam,
        StaffRole? candidateCurrentRole, DateOnly asOf, Random random)
    {
        if (candidate.IsRetired) return null;
        if (currentTeam?.Id == hiringTeam.Id) return null;

        double roleFit = _fit.FitFor(candidate, advert.Role);
        if (roleFit < advert.MinRoleFit || candidate.Reputation.Domestic < advert.MinReputation) return null;
        if (advert.MinLicense != CoachingLicense.None && candidate.License < advert.MinLicense) return null;

        int roleStep = RoleRank(advert.Role) - RoleRank(candidateCurrentRole);
        double clubStep = TeamStanding(hiringTeam) - TeamStanding(currentTeam);

        double ambition = AbilityScale.AttributeToHundred(candidate.Attributes.Ambition) / 100.0;
        double satisfaction = candidate.CareerSatisfaction / 100.0;
        bool differentCountry = !string.Equals(currentTeam?.Country ?? candidate.Nationality, hiringTeam.Country, StringComparison.OrdinalIgnoreCase);
        double relocation = _fit.RelocationComfort(candidate.Attributes.Adaptability, differentCountry);
        double culture = _fit.CultureFit(candidate.Philosophy, hiringTeam.CulturalIdentity);

        double keenness = Keenness(roleStep, clubStep, ambition, satisfaction, relocation, culture, candidate.CurrentTeamId is null);

        // §4.7: the dream job. If this advert is for it, the coach is all-in almost regardless of
        // the career maths.
        bool dreamJob = candidate.DreamJobTeamId == hiringTeam.Id && advert.Role is null;
        if (dreamJob) keenness = Math.Clamp(keenness + 0.6, 0.7, 1.0);

        if (keenness < 0.25 && random.NextDouble() > keenness * 2) return null;

        return new JobApplication(advert.Id, candidate.Id, CandidateIsCoach: true,
            Math.Round(roleFit, 1), Math.Round(culture, 1), Math.Round(keenness, 2),
            dreamJob ? $"{candidate.FullName} has always wanted the {hiringTeam.Name} job - he is all-in."
                     : ApplicationNote(candidate.FullName, advert, roleStep, clubStep));
    }

    // ---------------- staff applicants ----------------

    public JobApplication? Consider(StaffMember candidate, VacancyAdvert advert, Team hiringTeam, Team? currentTeam,
        DateOnly asOf, Random random)
    {
        if (currentTeam?.Id == hiringTeam.Id) return null;
        if (advert.Role is null && !_fit.HeadCoachFitEligible(candidate)) return null;

        double roleFit = _fit.FitFor(candidate, advert.Role);
        if (roleFit < advert.MinRoleFit || candidate.Reputation < advert.MinReputation) return null;

        int roleStep = RoleRank(advert.Role) - RoleRank(candidate.Role);
        double clubStep = TeamStanding(hiringTeam) - TeamStanding(currentTeam);

        double ambition = AbilityScale.AttributeToHundred(candidate.Ambition) / 100.0;
        double satisfaction = candidate.CareerSatisfaction / 100.0;
        bool differentCountry = !string.Equals(currentTeam?.Country ?? candidate.BasedInCountry, hiringTeam.Country, StringComparison.OrdinalIgnoreCase);
        double relocation = _fit.RelocationComfort(candidate.Adaptability, differentCountry);
        double culture = _fit.CultureFit(candidate.Philosophy, hiringTeam.CulturalIdentity);

        double keenness = Keenness(roleStep, clubStep, ambition, satisfaction, relocation, culture, candidate.TeamId is null);
        if (keenness < 0.25 && random.NextDouble() > keenness * 2) return null;

        return new JobApplication(advert.Id, candidate.Id, CandidateIsCoach: false,
            Math.Round(roleFit, 1), Math.Round(culture, 1), Math.Round(keenness, 2),
            ApplicationNote(candidate.FullName, advert, roleStep, clubStep));
    }

    private static double Keenness(int roleStep, double clubStep, double ambition, double satisfaction,
        double relocation, double culture, bool unemployed)
    {
        // A role step up is worth a lot on its own; a club step up adds to it; a big club step
        // DOWN only bites if the role is not also going up.
        double roleTerm = Math.Clamp(roleStep, -2, 3) * 0.16;
        double clubTerm = Math.Clamp(clubStep / 40.0, -1, 1) * 0.14;
        if (roleStep > 0 && clubTerm < 0) clubTerm *= 0.4; // "the job is a promotion even if the club isn't"

        double core = 0.42 + roleTerm + clubTerm
                      + (ambition - 0.5) * 0.18
                      - (satisfaction - 0.55) * 0.30      // a happy incumbent is harder to tempt
                      + (culture - 50) / 50.0 * 0.10;

        core *= 0.7 + relocation * 0.3;                   // section G: relocation discomfort scales the whole appetite
        if (unemployed) core = Math.Clamp(core + 0.18, 0, 1);
        return Math.Clamp(core, 0, 1);
    }

    private static string ApplicationNote(string name, VacancyAdvert advert, int roleStep, double clubStep) =>
        roleStep > 0 ? $"{name} applies for the {advert.RoleLabel} job - a genuine step up in responsibility."
        : clubStep > 10 ? $"{name} applies for the {advert.RoleLabel} job at a bigger club."
        : $"{name} applies for the {advert.RoleLabel} job.";

    /// <summary>
    /// The hiring side's decision: rank the applicants on role fit and culture fit (weighted by
    /// how much the club cares about identity), break ties on keenness. Returns them best-first.
    /// </summary>
    public IReadOnlyList<JobApplication> Rank(IEnumerable<JobApplication> applications, double cultureWeight = 0.30) =>
        applications
            .OrderByDescending(a => a.RoleFit * (1 - cultureWeight) + a.CultureFit * cultureWeight)
            .ThenByDescending(a => a.Keenness)
            .ToList();
}
