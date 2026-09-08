using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-rectification follow-up: the media build-up and fall-out around a franchise auction - the
/// thing that makes an auction feel like an event rather than a spreadsheet operation. Modelled on
/// how a real franchise auction is actually covered:
///
/// - <b>Preview</b> (a few days out): each franchise's purse and its biggest squad need, the
///   marquee names in the pool, the retention picture, and a pre-auction press conference where a
///   franchise's management names its priorities.
/// - <b>Report</b> (straight after): the most expensive buy, the bargain / uncapped "find of the
///   auction", the notable unsold names, and a per-franchise verdict (did they address their needs
///   inside their purse), plus a post-auction press conference reacting to the day.
///
/// Everything is a <see cref="GameEvent"/> the NewsEngine classifies; nothing here invents a fact
/// the auction did not produce.
/// </summary>
public sealed class FranchiseAuctionMediaService
{
    private readonly XiSelectionService _xi = new(); // requirement F: the post-auction likely-XI read

    // ---------------- preview ----------------

    public IReadOnlyList<GameEvent> Preview(
        WorldState world, Competition competition, CompetitionSeason season, DateOnly date, bool mega, Random random)
    {
        var events = new List<GameEvent>();
        var franchiseIds = season.ParticipatingTeamIds.ToHashSet();
        var franchises = world.Teams.Values.Where(t => franchiseIds.Contains(t.Id)).OrderBy(t => t.Name).ToList();
        if (franchises.Count == 0) return events;

        double basis = FranchiseFinanceService.PurseBasis(world, competition);
        var playersById = world.Players.ToDictionary(p => p.Id);

        events.Add(new GameEvent(date, GameEventType.AuctionPreview,
            $"BUILD-UP: the {competition.Name} {season.Year} {(mega ? "MEGA" : "mini")} auction is days away. "
            + (mega
                ? $"A full squad reset - each of the {franchises.Count} franchises works with a purse of about {basis:N0} and up to six retentions plus Right-to-Match cards."
                : $"A top-up auction - squads carry over, and each franchise has roughly {basis * 0.30:N0} to fill the gaps."),
            competition.Id));

        // Per-franchise: purse and the position they most need to strengthen.
        foreach (var f in franchises)
        {
            var squad = f.SquadPlayerIds.Select(id => playersById.GetValueOrDefault(id)).Where(p => p is not null).Select(p => p!).ToList();
            var need = SquadNeeds.WeakestGroup(squad, MatchFormat.T20, minShortfall: -1);
            string needText = need is { } n ? n.Group.Label : "squad depth";
            double purse = mega ? basis : basis * 0.30;
            events.Add(new GameEvent(date, GameEventType.AuctionPreview,
                $"{f.Name} go into the auction with ~{purse:N0} to spend and a clear need for {needText}.",
                f.Id, competition.Id));
        }

        // Marquee names in the pool.
        var marquee = world.Players
            .Where(p => !p.IsRetired && p.AcademyTeamId is null && p.CurrentTeamId is not null
                        && world.Teams.TryGetValue(p.CurrentTeamId.Value, out var t) && !t.IsFranchise)
            .Where(p => !world.PlayerContracts.Any(c => c.Kind == ContractKind.Franchise && c.CompetitionId == competition.Id
                        && c.Status == ContractStatus.Active && c.PlayerId == p.Id)) // not already retained
            .OrderByDescending(SquadNeeds.OverallScore)
            .Take(5)
            .ToList();
        if (marquee.Count > 0)
        {
            events.Add(new GameEvent(date, GameEventType.AuctionPreview,
                $"Headline names in the pool: {string.Join(", ", marquee.Select(p => p.FullName))}. Expect a bidding war for the best of them.",
                competition.Id));

            // §8.8: a MOCK AUCTION - the pundits' predictions of who lands the marquee names.
            var mock = new List<string>();
            var predictedSpend = franchises.ToDictionary(f => f.Id, _ => 0.0);
            double marqueeBudget = (mega ? basis : basis * 0.30);
            foreach (var star in marquee)
            {
                // Predict the franchise with the biggest need for his role, most purse "left", and most ambition.
                var pick = franchises
                    .Where(f => predictedSpend[f.Id] < marqueeBudget * 0.7)
                    .OrderByDescending(f =>
                    {
                        var sq = f.SquadPlayerIds.Select(id => playersById.GetValueOrDefault(id)).Where(p => p is not null).Select(p => p!).ToList();
                        var n = SquadNeeds.WeakestGroup(sq, MatchFormat.T20, minShortfall: -1);
                        double roleFit = n is { } nn && nn.Group.Matches(star) ? 1.0 : 0.4;
                        return roleFit * 2 + f.Board.Ambition / 100.0 - predictedSpend[f.Id] / Math.Max(1, marqueeBudget);
                    })
                    .ThenBy(f => f.Name)
                    .FirstOrDefault();
                if (pick is null) continue;
                double predictedPrice = FranchiseFinanceService.PurseBasis(world, competition) * 0.14 * (0.9 + SquadNeeds.OverallScore(star) / 200.0);
                predictedSpend[pick.Id] += predictedPrice;
                mock.Add($"{star.FullName} -> {pick.Name} (~{predictedPrice:N0})");
            }
            if (mock.Count > 0)
                events.Add(new GameEvent(date, GameEventType.AuctionPreview,
                    $"MOCK AUCTION: our panel's best guess at where the big names land - {string.Join("; ", mock)}. The real thing rarely goes to script.",
                    competition.Id));
        }

        // A pre-auction press conference from the most ambitious franchise.
        var ambitious = franchises.OrderByDescending(f => f.Board.Ambition).First();
        var coach = ambitious.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
        string who = coach?.FullName ?? $"{ambitious.Name}'s management";
        var ambSquad = ambitious.SquadPlayerIds.Select(id => playersById.GetValueOrDefault(id)).Where(p => p is not null).Select(p => p!).ToList();
        var ambNeed = SquadNeeds.WeakestGroup(ambSquad, MatchFormat.T20, minShortfall: -1);
        events.Add(new GameEvent(date, GameEventType.AuctionPressConference,
            $"PRESS CONFERENCE - {who}: \"We've done our homework. We know the one or two players who genuinely change our season, "
            + $"and we'll go hard for {(ambNeed is { } an ? "a " + an.Group.Label + " option" : "the players on our list")}. "
            + "We're not here to make up the numbers.\"",
            ambitious.Id, competition.Id));

        return events;
    }

    // ---------------- report ----------------

    public IReadOnlyList<GameEvent> Report(
        WorldState world, Competition competition, CompetitionSeason season,
        FranchiseAuctionService.AuctionSummary summary, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();
        var sold = summary.Lots.Where(l => l.WinningFranchiseId is not null).ToList();
        if (sold.Count == 0) return events;

        var playersById = world.Players.ToDictionary(p => p.Id);
        string TeamName(Guid? id) => id is { } g && world.Teams.TryGetValue(g, out var t) ? t.Name : "a franchise";

        // Most expensive buy.
        var priciest = sold.OrderByDescending(l => l.FinalPrice).First();
        events.Add(new GameEvent(date, GameEventType.AuctionReport,
            $"ROUND-UP: the biggest buy of the {competition.Name} auction is {priciest.PlayerName}, to {TeamName(priciest.WinningFranchiseId)} for {priciest.FinalPrice:N0}"
            + (priciest.ViaRtm ? " (via Right-to-Match)" : "") + ".",
            priciest.PlayerId, competition.Id));

        // Find of the auction: a low base price, a genuinely useful player, bought cheap.
        var find = sold
            .Where(l => playersById.TryGetValue(l.PlayerId, out var p) && SquadNeeds.OverallScore(p) >= 60)
            .OrderBy(l => l.FinalPrice / Math.Max(1, l.BasePrice))
            .ThenBy(l => l.FinalPrice)
            .FirstOrDefault();
        if (find is not null)
        {
            var p = playersById[find.PlayerId];
            events.Add(new GameEvent(date, GameEventType.AuctionReport,
                $"Bargain of the day: {find.PlayerName} to {TeamName(find.WinningFranchiseId)} for just {find.FinalPrice:N0} - a real find at the price.",
                find.PlayerId, competition.Id));
            events.Add(new GameEvent(date, GameEventType.TournamentAward,
                $"Smart pick-up: {find.PlayerName} is widely rated the shrewdest buy of the {competition.Name} auction.",
                find.PlayerId, competition.Id));
        }

        // Notable unsold.
        var unsold = summary.Lots.Where(l => l.WinningFranchiseId is null)
            .Where(l => playersById.TryGetValue(l.PlayerId, out var p) && SquadNeeds.OverallScore(p) >= 58)
            .OrderByDescending(l => playersById.TryGetValue(l.PlayerId, out var p) ? SquadNeeds.OverallScore(p) : 0)
            .Take(3)
            .ToList();
        if (unsold.Count > 0)
            events.Add(new GameEvent(date, GameEventType.AuctionReport,
                $"Went unsold despite a decent record: {string.Join(", ", unsold.Select(l => l.PlayerName))}. A blow to their season, and a chance for a replacement signing.",
                competition.Id));

        // Per-franchise verdict.
        var franchiseIds = season.ParticipatingTeamIds.ToHashSet();
        foreach (var f in world.Teams.Values.Where(t => franchiseIds.Contains(t.Id)).OrderBy(t => t.Name))
        {
            double remaining = summary.RemainingPurseByFranchise.GetValueOrDefault(f.Id, 0);
            var squad = f.SquadPlayerIds.Select(id => playersById.GetValueOrDefault(id)).Where(p => p is not null).Select(p => p!).ToList();
            var need = SquadNeeds.WeakestGroup(squad, MatchFormat.T20, minShortfall: 0);
            string grade = need is null && squad.Count >= 13 ? "A - a balanced squad, needs met"
                : squad.Count >= 12 ? "B - a workable squad with one soft spot"
                : "C - still light, more work to do";
            events.Add(new GameEvent(date, GameEventType.AuctionReport,
                $"{f.Name}'s auction verdict: {grade} ({squad.Count} players, {remaining:N0} purse unspent).",
                f.Id, competition.Id));

            // Meeting-driven-selection ticket (F): the post-auction squad review - what the
            // franchise landed, its biggest gap, and the likely XI it will build from the group.
            if (squad.Count >= 11)
            {
                var bought = summary.Lots
                    .Where(l => l.WinningFranchiseId == f.Id)
                    .OrderByDescending(l => l.FinalPrice).ToList();
                var xi = _xi.SelectXi(squad, MatchFormat.T20, date);
                string topBuy = bought.Count > 0 ? $"headline buy {bought[0].PlayerName}" : "no auction buys of note";
                string gap = need is { } n ? $"the room's worry is {n.Group.Label}" : "the group looks balanced";
                string keeper = xi.IsSpecialistWicketkeeper ? "a specialist keeper in place" : "no specialist keeper - a real question";
                events.Add(new GameEvent(date, GameEventType.PostAuctionReview,
                    $"{f.Name} review the auction: {topBuy}, {gap}. Projected XI has {keeper}; "
                    + $"{xi.Bowlers.Count} front-line bowling options.",
                    f.Id, competition.Id));
            }
        }

        // Post-auction press conference from the franchise that landed the priciest buy.
        var buyer = priciest.WinningFranchiseId is { } bid && world.Teams.TryGetValue(bid, out var bt) ? bt : null;
        if (buyer is not null)
        {
            var coach = buyer.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            string who = coach?.FullName ?? $"{buyer.Name}'s management";
            events.Add(new GameEvent(date, GameEventType.AuctionPressConference,
                $"PRESS CONFERENCE - {who}: \"We identified {priciest.PlayerName} early and we were always going to get our man. "
                + "Yes, it cost - but he's a match-winner, and match-winners win you tournaments. We're delighted with our squad.\"",
                buyer.Id, competition.Id));
        }

        return events;
    }
}
