using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 13 (§9.7): multi-year sponsorship deals - a title, a kit and (for a club with a big
/// enough ground) a stadium-naming deal, each on its own 3-5 year term with a fixed annual value
/// and a performance clause. Signed when there is no deal in that slot, renewed when one expires,
/// always against what the club commands NOW - so a club that has grown lands a bigger deal and
/// one that has slipped takes the hit, in a lumpy multi-year way (the same shape as the broadcast
/// deal in Wave 4 of the Completion Pass).
///
/// Runs once a year, from ProcessPhase7AnnualBusiness. The per-year sponsorship FORMULA in
/// SeasonFinanceService still runs underneath - the contracted deals are added on top of a
/// reduced base, so a club with no deals is unaffected and a fully-sponsored one earns more,
/// steadier money.
/// </summary>
public sealed class SponsorshipService
{
    private static readonly string[] TitleSponsors = { "Meridian Bank", "Arclight Energy", "Vantage Airlines", "Corsa Motors", "Northgate Telecom", "Solaris Group" };
    private static readonly string[] KitSponsors = { "Apex", "Strident", "Voltek", "Kestrel", "Momentum" };

    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date)
    {
        int year = date.Year;
        foreach (var team in world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise).OrderBy(t => t.Name))
        {
            // Drop expired deals.
            team.SponsorContracts.RemoveAll(c => c.IsExpired(year));

            var homeGround = team.HomeGroundId is { } gid && world.Grounds.TryGetValue(gid, out var g) ? g : null;
            double commercialPull = team.Reputation.Domestic * 0.6 + team.Board.Wealth * 0.2 + team.Board.FanSentiment * 0.2;

            foreach (var slot in new[] { SponsorSlot.Title, SponsorSlot.Kit, SponsorSlot.Stadium })
            {
                if (team.SponsorContracts.Any(c => c.Slot == slot)) continue;
                if (slot == SponsorSlot.Stadium && (homeGround is null || homeGround.Capacity < 25_000)) continue;

                double baseValue = slot switch
                {
                    SponsorSlot.Title => 150_000 + commercialPull * 9_000,
                    SponsorSlot.Kit => 60_000 + commercialPull * 3_500,
                    _ => 40_000 + commercialPull * 2_500
                };
                // A prestige-agenda board chases the marquee deal harder.
                if (team.Board.Agenda == ValueObjects.ChairmanAgenda.Prestige) baseValue *= 1.15;

                int term = 3 + (int)(commercialPull % 3); // 3-5 years
                var partner = slot switch
                {
                    SponsorSlot.Title => TitleSponsors[(int)(commercialPull) % TitleSponsors.Length],
                    SponsorSlot.Kit => KitSponsors[(int)(commercialPull) % KitSponsors.Length],
                    _ => $"{TitleSponsors[(int)(commercialPull + 2) % TitleSponsors.Length]} Arena"
                };

                var deal = new SponsorContract
                {
                    Slot = slot, Partner = partner,
                    AnnualValue = System.Math.Round(baseValue, 0),
                    PerformanceBonusShare = slot == SponsorSlot.Title ? 0.18 : 0.10,
                    SignedYear = year, ExpiresYear = year + term
                };
                team.SponsorContracts.Add(deal);
                yield return new GameEvent(date, GameEventType.TeamFinancesSettled,
                    $"{team.Name} sign a {term}-year {DescribeSlot(slot)} deal with {partner} worth {deal.AnnualValue:N0} a year.",
                    team.Id);
            }
        }
    }

    /// <summary>What SeasonFinanceService adds for a club's contracted deals this year: the base values plus a performance bonus if they won a trophy (full) or finished top-3 in a league (half).</summary>
    public static double ContractedIncome(Team team, int year, bool wonTrophy, bool finishedTop3)
    {
        double total = 0;
        foreach (var c in team.SponsorContracts.Where(c => c.SignedYear <= year && !c.IsExpired(year)))
        {
            total += c.AnnualValue;
            if (wonTrophy) total += c.AnnualValue * c.PerformanceBonusShare;
            else if (finishedTop3) total += c.AnnualValue * c.PerformanceBonusShare * 0.5;
        }
        return total;
    }

    private static string DescribeSlot(SponsorSlot s) => s switch
    {
        SponsorSlot.Title => "front-of-shirt",
        SponsorSlot.Kit => "kit",
        _ => "stadium-naming"
    };
}
