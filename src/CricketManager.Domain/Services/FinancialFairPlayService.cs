using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7 (finance follow-up): financial fair play - the governing body's response to a club
/// that spends beyond its means.
///
/// The board already reacts to a deficit (BoardService.FinancialHealthFactor -> lower confidence
/// in the coach). This is the harder, external consequence real cricket boards impose: a club
/// running a sustained, large deficit gets a POINTS DEDUCTION on its domestic competition and a
/// SPENDING EMBARGO. The points come off the club's standing this year; the embargo
/// (ClubBoard.UnderTransferEmbargo) is a flag Phase 9's transfer market will enforce - today it
/// carries a news / board consequence and a reputation hit, which is stated rather than implied.
///
/// Recovery: once the books are genuinely back in order, the embargo is lifted.
///
/// Deliberately does NOT model a salary cap (that needs Phase 9's real wage/contract system) -
/// this is the deficit-response half only.
/// </summary>
public sealed class FinancialFairPlayService
{
    /// <summary>A deficit deeper than this is a breach.</summary>
    private const double BreachThreshold = -1_800_000;

    /// <summary>A deeper deficit than this is a severe breach - a bigger points hit.</summary>
    private const double SevereThreshold = -4_000_000;

    /// <summary>How long an embargo runs before it is reviewed.</summary>
    private const int EmbargoDays = 400;

    private readonly ForcedSaleService _forcedSale = new(); // Phase 13 (§10.8)

    /// <summary>
    /// Reviews every club at the annual rollover. Returns the disciplinary events. Applies the
    /// points deduction to the club's most relevant standing this year (in-progress or just
    /// finished); sets / lifts the embargo.
    /// </summary>
    public IReadOnlyList<GameEvent> Review(WorldState world, DateOnly date, int yearJustFinished)
    {
        var events = new List<GameEvent>();

        foreach (var team in world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise).OrderBy(t => t.Name))
        {
            double budget = team.Finances.Budget;
            var board = team.Board;

            // --- lift an embargo once the books recover ---
            if (board.UnderTransferEmbargo)
            {
                bool recovered = budget > -250_000;
                bool reviewDue = board.EmbargoUntil is { } until && until <= date;
                if (recovered || (reviewDue && budget > BreachThreshold))
                {
                    board.UnderTransferEmbargo = false;
                    board.EmbargoUntil = null;
                    events.Add(new GameEvent(date, GameEventType.DisciplinaryAction,
                        $"{team.Name}'s spending embargo is lifted - the accounts are back within the rules.", team.Id));
                    continue;
                }
                // Still in breach and the review is not due - the embargo simply stands.
                if (!reviewDue) continue;
            }

            // --- a fresh breach ---
            if (budget >= BreachThreshold) continue;

            bool severe = budget < SevereThreshold;
            int penalty = severe ? 8 : 4;

            var standing = FindStanding(world, team.Id, yearJustFinished);
            string tableTail = "";
            if (standing is not null)
            {
                standing.ApplyPointsPenalty(penalty);
                tableTail = $" - a {penalty}-point deduction is applied";
            }

            board.UnderTransferEmbargo = true;
            board.EmbargoUntil = date.AddDays(EmbargoDays);
            team.Reputation.Adjust(domesticDelta: severe ? -4 : -2);
            board.MoveFanSentiment(severe ? -8 : -4);

            events.Add(new GameEvent(date, GameEventType.DisciplinaryAction,
                $"{team.Name} are found in breach of financial fair play (deficit {budget:N0}){tableTail}, and placed under a spending embargo.",
                team.Id));

            // Phase 13 (§10.8): a SEVERE deficit forces a fire sale - the board makes the club sell
            // its most valuable asset to a club that can afford it, whatever the coach thinks.
            if (severe && _forcedSale.ForceSale(world, team, date) is { } fs)
                events.Add(fs);
        }

        return events;
    }

    /// <summary>The standing to dock points from: the club's row in whichever domestic competition it played this year (an in-progress one first, else the most recent).</summary>
    private static CompetitionStanding? FindStanding(WorldState world, Guid teamId, int year)
    {
        var candidates = world.CompetitionSeasons
            .Where(s => (s.Year == year || s.Year == year + 1) && s.ParticipatingTeamIds.Contains(teamId))
            .Where(s => world.Competitions.FirstOrDefault(c => c.Id == s.CompetitionId)?.Scope
                        is CompetitionScope.DomesticT20
                        or CompetitionScope.DomesticFirstClass
                        or CompetitionScope.DomesticListA
                        or CompetitionScope.FranchiseLeague)
            .OrderByDescending(s => !s.IsCompleted)   // an in-progress season first
            .ThenByDescending(s => s.Year)
            .ToList();

        foreach (var season in candidates)
        {
            var row = season.Standings.FirstOrDefault(r => r.TeamId == teamId);
            if (row is not null) return row;
        }
        return null;
    }
}
