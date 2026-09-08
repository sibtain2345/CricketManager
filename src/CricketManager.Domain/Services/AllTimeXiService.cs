using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 16 (§15.5): every few years the media names a Team of the Era - the best XI of the
/// period just gone, picked from career output plus standing. Pure flavour, but the kind that
/// makes a long save feel like it has a history: a player who makes the decade XI has genuinely
/// been one of the greats of his time.
///
/// Reads <see cref="WorldState.CareerStats"/> and reputation only - no RNG, no new storage. The
/// world clock calls it on a period boundary at the annual rollover.
/// </summary>
public sealed class AllTimeXiService
{
    private sealed record Line(Player Player, int Runs, int Wickets, int Catches, int Hundreds, int FiveFors)
    {
        public double BatScore => Runs / 45.0 + Hundreds * 8 + Player.Reputation.Worldwide * 0.4;
        public double BowlScore => Wickets * 2.2 + FiveFors * 6 + Player.Reputation.Worldwide * 0.4;
        public double KeepScore => Catches + Runs / 60.0 + Player.Reputation.Worldwide * 0.3;
    }

    public GameEvent? NameTeamOfEra(WorldState world, DateOnly date, int yearsCovered)
    {
        var lines = world.CareerStats.Values
            .GroupBy(s => s.PlayerId)
            .Select(g =>
            {
                var p = world.Players.FirstOrDefault(x => x.Id == g.Key);
                return p is null ? null : new Line(p,
                    g.Sum(s => s.Runs), g.Sum(s => s.Wickets), g.Sum(s => s.Catches),
                    g.Sum(s => s.Hundreds), g.Sum(s => s.FiveWicketHauls));
            })
            .Where(l => l is not null).Select(l => l!)
            .ToList();

        if (lines.Count < 11) return null;

        var pickedIds = new HashSet<Guid>();
        var picked = new List<Player>();

        void Take(IEnumerable<Line> ranked, int n)
        {
            foreach (var l in ranked)
            {
                if (picked.Count >= 11 || n <= 0) break;
                if (!pickedIds.Add(l.Player.Id)) continue;
                picked.Add(l.Player);
                n--;
            }
        }

        Take(lines.Where(l => l.Player.PrimaryRole == PlayerRole.WicketKeeper).OrderByDescending(l => l.KeepScore), 1);
        Take(lines.Where(l => !pickedIds.Contains(l.Player.Id)).OrderByDescending(l => l.BatScore), 6);
        Take(lines.Where(l => !pickedIds.Contains(l.Player.Id)).OrderByDescending(l => l.BowlScore), 11);

        if (picked.Count < 11) return null;

        var names = string.Join(", ", picked.Take(11).Select(p => p.FullName));
        var headline = $"The panel names its Team of the Last {yearsCovered} Years: {names}.";

        // §20.4: the dominant TEAM of the period - most titles across the era's competitions.
        int eraStart = date.Year - yearsCovered;
        var titleCounts = world.CompetitionSeasons
            .Where(s => s.Year >= eraStart && s.Year < date.Year && s.IsCompleted && s.ChampionTeamId is not null)
            .GroupBy(s => s.ChampionTeamId!.Value)
            .Select(g => (TeamId: g.Key, Titles: g.Count()))
            .OrderByDescending(x => x.Titles)
            .ToList();
        Guid? dominantTeamId = titleCounts.Count > 0 && titleCounts[0].Titles >= 2 ? titleCounts[0].TeamId : null;

        // §20.4 (Follow-up Pass 4): the era's storylines - not just the single most intense one.
        // Up to three genuinely notable arcs that actually ran during the period, ranked by how
        // far each one built. The first is still "the" defining story for anything that only ever
        // read the one line.
        var eraStorylines = world.Storylines
            .Where(s => s.Started.Year >= eraStart && s.Started.Year < date.Year)
            .OrderByDescending(s => s.Intensity)
            .Take(3)
            .Select(s => s.Summary)
            .ToList();
        string? defining = eraStorylines.Count > 0 ? eraStorylines[0] : null;

        // §20.4 (Follow-up Pass 4): a light "rivalry of the era" read. Reuses Rivalry's own contest
        // history (a trophy contested, or intensity reinforced) during the window rather than a
        // second, invented measure of what mattered - deterministic, no RNG.
        string? rivalryOfEra = null;
        var eraRivalry = world.Rivalries
            .Where(r => (r.LastReinforced is { } lr && lr.Year >= eraStart && lr.Year < date.Year)
                     || (r.TrophyName is not null && r.LastContestedYear >= eraStart && r.LastContestedYear < date.Year))
            .OrderByDescending(r => r.Intensity)
            .FirstOrDefault();
        if (eraRivalry is not null
            && world.Teams.TryGetValue(eraRivalry.TeamAId, out var rivalA)
            && world.Teams.TryGetValue(eraRivalry.TeamBId, out var rivalB))
        {
            string pairing = eraRivalry.TrophyName ?? $"{rivalA.Name} v {rivalB.Name}";
            rivalryOfEra = eraRivalry.TrophyHolderId is { } holderId && world.Teams.TryGetValue(holderId, out var holder)
                ? $"{pairing} defined the era - {holder.Name} hold it now."
                : $"{pairing} defined the era.";
        }

        if (dominantTeamId is { } dtid && world.Teams.TryGetValue(dtid, out var dominantTeam))
            headline += $" {dominantTeam.Name} were the team of the era, with {titleCounts[0].Titles} titles.";
        if (defining is not null)
            headline += $" The defining story: {defining}";
        if (rivalryOfEra is not null)
            headline += $" {rivalryOfEra}";

        // §15.5: persist it - a save's own history of who the panel rated the greats of each era -
        // rather than a line of news that's gone the moment it scrolls past.
        world.EraTeams.Add(new ValueObjects.EraTeam(date.Year, yearsCovered, picked.Take(11).Select(p => p.Id).ToList(), headline,
            dominantTeamId, defining, eraStorylines, rivalryOfEra));

        // §15.5: a pundit's own vote - a dissenting name he'd have picked instead, from his own
        // biased view of the field (see Pundit.BiasStrength / former club). RNG-free (a stable pick
        // from the runner-up list), a real "the panel had it wrong" flavour line.
        if (world.Pundits.Count > 0)
        {
            var runnerUp = lines.Where(l => !pickedIds.Contains(l.Player.Id))
                .OrderByDescending(l => Math.Max(l.BatScore, l.BowlScore)).FirstOrDefault();
            var pundit = world.Pundits[date.Year % world.Pundits.Count];
            var weakest = picked.Take(11).OrderBy(p => p.Reputation.Worldwide).First();
            if (runnerUp is not null && runnerUp.Player.Id != weakest.Id)
                headline += $" {pundit.Name} disagrees - his own vote drops {weakest.FullName} for {runnerUp.Player.FullName}.";
        }

        return new GameEvent(date, GameEventType.AllTimeXiNamed, headline, picked[0].Id);
    }
}
