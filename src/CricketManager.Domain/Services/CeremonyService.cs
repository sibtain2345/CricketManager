using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 16 (§15.3): a milestone is not just a line on a ticker - the big ones get a moment. A
/// 100th (or 200th, 300th) cap earns a presentation and a guard of honour from the opposition; a
/// 10,000-run or 500-wicket landmark is a stoppage and a lap of the ground. This turns the
/// relevant <see cref="GameEventType.PlayerMilestone"/> events into a <see cref="GameEventType.MilestoneCeremony"/>
/// news moment.
///
/// Deliberately NEWS ONLY - no morale / reputation / harmony change. A ceremony happens for the
/// players who play the most, so a stat effect would let the strongest side compound an advantage
/// every season; the 25-year competitiveness acceptance test caught exactly that. A bounded
/// morale/reputation effect is on the register as a deferred follow-up.
///
/// Reads the milestone events <see cref="MilestoneService"/> already produced (so it never
/// re-detects anything), and is called straight after them in the per-match hooks.
/// </summary>
public sealed class CeremonyService
{
    public IReadOnlyList<GameEvent> Process(
        WorldState world, IReadOnlyList<GameEvent> milestoneEvents, DateOnly date)
    {
        var ceremonies = new List<GameEvent>();

        foreach (var ev in milestoneEvents.Where(e => e.Type == GameEventType.PlayerMilestone))
        {
            if (ev.SubjectId is not { } pid) continue;
            var player = world.Players.FirstOrDefault(p => p.Id == pid);
            if (player is null) continue;

            // Only the genuinely ceremonial ones.
            string h = ev.Headline;
            bool bigCap = h.Contains("100th") || h.Contains("150th") || h.Contains("200th") || h.Contains("250th") || h.Contains("300th");
            bool bigWicket = h.Contains("wicket") && (h.Contains("500") || h.Contains("700"));
            bool bigVolume = h.Contains("10,000") || h.Contains("12,500") || h.Contains("15,000") || h.Contains("20,000") || bigWicket;
            if (!bigCap && !bigVolume) continue;

            // Deliberately NEWS ONLY - no morale / reputation / harmony change. A ceremony is a
            // nice moment; giving it a stat effect lets the side that plays most (and so hits the
            // most milestones) compound an advantage every season, which the long-run
            // competitiveness acceptance test rightly rejects. The register carries the
            // "morale/reputation effect" as a deferred follow-up.
            string what = bigCap
                ? $"receives a guard of honour and a presentation from both sides to mark the landmark"
                : $"is applauded from the field as the ground rises to a career milestone";
            ceremonies.Add(new GameEvent(date, GameEventType.MilestoneCeremony,
                $"{player.FullName} {what}. {ev.Headline}", player.Id, player.CurrentTeamId));
        }

        return ceremonies;
    }
}
