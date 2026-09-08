using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>Breakdown of one season's finances, returned so a future UI/news system can explain WHY a budget moved rather than only showing the new number.</summary>
public sealed record SeasonFinancialResult(double SponsorshipIncome, double MatchdayIncome, double CompetitionIncome, double UpkeepCost, double WageCost, double NetResult, double ClosingBudget);

/// <summary>
/// Makes facility upkeep a CONSEQUENCE of the facilities a team owns rather than a static
/// number sitting on TeamFinances. This is what stops facility upgrades from being free:
/// better training/medical/scouting setups and a bigger stadium cost more to run every
/// year, so a board investing in infrastructure is making a real trade-off.
///
/// Player wages are passed in rather than computed here - contracts are Phase 9 territory,
/// and this service should not invent a wage model that phase will replace.
///
/// Competition income comes from CompetitionRevenueService and completes the income side:
/// sponsorship (who you are), matchday (who turns up), competition (how you did). Without it
/// a title-winning season paid exactly the same as a last-place one.
/// </summary>
public sealed class TeamFinanceService
{
    private const double UpkeepPerFacilityPoint = 400;   // annual cost per point of facility quality
    private const double UpkeepPerSeat = 1.5;            // annual stadium running cost per seat of capacity

    /// <summary>
    /// Annual cost of running this team's facilities and (if it owns one) its home ground.
    /// Pass groundCapacity 0 / null facilities for a team with no home ground of its own.
    /// </summary>
    public double CalculateAnnualUpkeep(TeamFacilities teamFacilities, GroundFacilities? groundFacilities = null, int groundCapacity = 0)
    {
        double teamPoints = teamFacilities.TrainingQuality
                          + teamFacilities.YouthDevelopmentQuality
                          + teamFacilities.ScoutingQuality
                          + teamFacilities.MedicalQuality
                          + teamFacilities.CorporateCommercialQuality;

        double groundPoints = groundFacilities is null
            ? 0
            : groundFacilities.TrainingFacilities
            + groundFacilities.MediaFacilities
            + groundFacilities.CorporateHospitality
            + groundFacilities.MedicalFacilities
            + groundFacilities.YouthFacilities
            + groundFacilities.PitchInfrastructure;

        return Math.Round((teamPoints + groundPoints) * UpkeepPerFacilityPoint + Math.Max(0, groundCapacity) * UpkeepPerSeat, 0);
    }

    /// <summary>
    /// Applies one season's income and costs to the team's budget and stores the freshly
    /// calculated upkeep back onto TeamFinances, so the stored field is a cached result of
    /// the formula rather than an independently editable number that can drift from it.
    /// A negative closing budget is allowed and NOT clamped - a team overspending itself
    /// into trouble is a legitimate outcome the board/job-security systems should react to.
    /// </summary>
    public SeasonFinancialResult ApplySeasonFinances(
        TeamFinances finances,
        double sponsorshipIncome,
        double matchdayIncome,
        double upkeepCost,
        double wageCost = 0,
        double competitionIncome = 0)
    {
        finances.SponsorshipValue = sponsorshipIncome;
        finances.FacilityUpkeepCost = upkeepCost;

        double net = sponsorshipIncome + matchdayIncome + competitionIncome - upkeepCost - wageCost;
        finances.Budget += net;

        return new SeasonFinancialResult(sponsorshipIncome, matchdayIncome, competitionIncome, upkeepCost, wageCost,
            Math.Round(net, 0), Math.Round(finances.Budget, 0));
    }
}
