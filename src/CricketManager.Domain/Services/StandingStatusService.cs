using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Meeting-driven-selection ticket, fold-in item 10: a periodic "where do we actually stand"
/// digest - what the board, the dressing room and the key players think - distinct from the
/// weekly NEWS digest (which is about events out in the world). This is the coach's own read on
/// the room he runs.
///
/// Monthly. Always produced for the human-controlled club (it is a decision aid). For an AI club
/// it is only surfaced when something is genuinely off - a board on the edge, a fractured room, a
/// live crisis - so the news feed does not fill with routine "everything is fine" lines.
///
/// Deterministic - reads current state, no RNG.
/// </summary>
public sealed class StandingStatusService
{
    public IEnumerable<GameEvent> ReviewMonthly(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();
        if (date.Day > 4) return events; // once a month, near the start

        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise).OrderBy(t => t.Name))
        {
            var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            bool human = coach?.IsHumanControlled == true;

            var crisis = world.Storylines.FirstOrDefault(s => !s.Resolved && s.TeamId == team.Id
                && s.Kind is ValueObjects.StorylineKind.CrisisClub or ValueObjects.StorylineKind.CaptainUnderFire or ValueObjects.StorylineKind.SelectorsAtWar);

            bool notable = team.BoardConfidence < 32 || team.DressingRoomHarmony < 36 || crisis is not null;
            if (!human && !notable) continue;

            var digest = Build(world, team, coach, crisis);
            events.Add(new GameEvent(date, GameEventType.StandingStatusDigest, digest, team.Id));
        }

        return events;
    }

    private static string Build(WorldState world, Team team, Coach? coach, ValueObjects.Storyline? crisis)
    {
        string board = team.BoardConfidence switch
        {
            >= 70 => "The board is fully behind the direction.",
            >= 45 => "The board is patient but watching.",
            >= 30 => "The board's confidence is thin - results are needed.",
            _ => "The board has all but run out of patience."
        };

        string room = team.DressingRoomHarmony switch
        {
            >= 62 => "The dressing room is united.",
            >= 45 => "The dressing room is settled enough.",
            >= 34 => "There is friction in the group.",
            _ => "The dressing room is fractured."
        };

        var squad = team.SquadPlayerIds
            .Select(id => world.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p is { IsRetired: false }).Select(p => p!)
            .ToList();

        string players;
        if (squad.Count == 0)
            players = "";
        else
        {
            int unhappy = squad.Count(p => p.CoachTrust < 35 || p.Form.Confidence < 30);
            var lowestTrust = squad.OrderBy(p => p.CoachTrust).FirstOrDefault();
            players = unhappy >= 3
                ? $" {unhappy} players are unhappy with their standing or out of form"
                    + (lowestTrust is not null && lowestTrust.CoachTrust < 30 ? $", {lowestTrust.FullName} the most restless." : ".")
                : " The senior players are with the coach.";
        }

        string panel = team.IsNational && team.NationalBoard is { } nb
            ? nb.ConsecutiveOutvotes >= 2
                ? " The selection panel and the final call are not aligned."
                : " The selectors and the coach are pulling the same way."
            : "";

        string story = crisis is not null ? $" [{crisis.Kind}] {crisis.Summary}" : "";

        return $"{team.Name} - the state of things: {board} {room}{players}{panel}{story}";
    }
}
