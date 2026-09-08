namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// Every field here is a CACHED RESULT of a formula, not an independently editable number:
/// - SponsorshipValue is recalculated by SponsorshipValuationService (team reputation,
///   competition prestige/reputation, recent form, home-ground commercial facilities).
/// - FacilityUpkeepCost is recalculated by TeamFinanceService.CalculateAnnualUpkeep from
///   the facilities the team actually owns - it is not a flat annual figure, because
///   better facilities genuinely cost more to run.
/// - MatchdayIncomeRate is the fixed per-match baseline (broadcast/fixed gate guarantees)
///   that MatchdayRevenueService adds crowd-driven income on top of.
/// - Budget is moved by TeamFinanceService.ApplySeasonFinances and is allowed to go
///   negative - a team in financial trouble is a legitimate simulation state.
/// </summary>
public sealed class TeamFinances
{
    public double Budget { get; set; } = 1_000_000;
    public double SponsorshipValue { get; set; } = 100_000;
    public double MatchdayIncomeRate { get; set; } = 5_000; // fixed per-match baseline, before crowd income
    public double FacilityUpkeepCost { get; set; } = 50_000; // annual; recalculated from actual facilities
}
