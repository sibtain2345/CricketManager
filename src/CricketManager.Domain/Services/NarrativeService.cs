using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 12 (§14.2): the season NARRATIVE tracker. NewsEngine produces disconnected headlines;
/// this connects them into storylines that build over weeks and pay off - a breakout star, a
/// captain under fire, a club in crisis, a dominant run, a redemption.
///
/// A live storyline is not just flavour: it feeds the sim's own machinery - a breakout star walks
/// out under a genuine <see cref="PressureMoment"/>, a captain-under-fire story grinds his
/// <see cref="Player.CaptainTrust"/>-side standing, and a resolved story emits a payoff headline.
///
/// Reviewed monthly. Deliberately conservative - a storyline needs a real, repeated signal to
/// start, and it fades on its own if nothing keeps it alive.
/// </summary>
public sealed class NarrativeService
{
    private const int FadeAfterDays = 120;

    public IEnumerable<GameEvent> ReviewMonthly(WorldState world, DateOnly date)
    {
        var events = new List<GameEvent>();
        var live = world.Storylines.Where(s => !s.Resolved).ToList();

        // --- new storylines ---

        // Breakout star: a young player with 2+ milestone/award headlines in the last 90 days.
        var since = date.AddDays(-90);
        var youngHeroes = world.NewsArchive
            .Where(n => n.Date >= since && n.SubjectId is not null
                        && n.Category is NewsCategory.Milestones)
            .GroupBy(n => n.SubjectId!.Value)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToList();
        foreach (var pid in youngHeroes)
        {
            var p = world.Players.FirstOrDefault(x => x.Id == pid);
            if (p is null || p.IsRetired || p.Age(date) > 24) continue;
            if (live.Any(s => s.SubjectId == pid && s.Kind == StorylineKind.BreakoutStar)) continue;

            var story = new Storyline
            {
                Kind = StorylineKind.BreakoutStar, SubjectId = pid, TeamId = p.CurrentTeamId,
                Started = date, LastUpdated = date, Intensity = 45,
                Summary = $"{p.FullName} is the breakout name of the season."
            };
            world.Storylines.Add(story);
            live.Add(story);
            // He now carries real expectation into the next big match.
            p.PendingPressureMoment ??= new PressureMoment(date, 20);
            events.Add(new GameEvent(date, GameEventType.NarrativeUpdate,
                $"{p.FullName} is the story of the season - every innings now comes with expectation.", pid, p.CurrentTeamId));
        }

        // §18.7: a genuine ground hoodoo, and the player is about to play there again. Reuses
        // Player.Matchups (the same "ground:{id}" data PreMatchReportService.GroundHoodoos already
        // reads pre-match) - this is the narrative-arc SIDE of the same real signal, not a new one.
        var upcoming = date.AddDays(21);
        foreach (var fixture in world.Fixtures.Where(f => f.Status == Enums.FixtureStatus.Scheduled
                     && f.ScheduledDate > date && f.ScheduledDate <= upcoming && f.GroundId is not null))
        {
            var ground = world.Grounds.TryGetValue(fixture.GroundId!.Value, out var g) ? g : null;
            if (ground is null) continue;
            var teamIds = new[] { fixture.HomeTeamId, fixture.AwayTeamId }.Where(id => id is not null).Select(id => id!.Value);
            foreach (var teamId in teamIds)
            {
                if (!world.Teams.TryGetValue(teamId, out var t)) continue;
                foreach (var pid2 in t.SquadPlayerIds)
                {
                    var p2 = world.Players.FirstOrDefault(x => x.Id == pid2);
                    if (p2 is null || p2.IsRetired) continue;
                    string key = $"ground:{ground.Id}";
                    if (!p2.Matchups.TryGetValue(key, out var mc) || mc.SampleCount < 5 || mc.BlendedConfidence > -25) continue;
                    if (live.Any(s => s.SubjectId == p2.Id && s.Kind == StorylineKind.GroundHoodoo && s.TeamId == ground.Id)) continue;

                    var hoodoo = new Storyline
                    {
                        Kind = StorylineKind.GroundHoodoo, SubjectId = p2.Id, TeamId = ground.Id,
                        Started = date, LastUpdated = date, Intensity = 35,
                        Summary = $"{p2.FullName} has never fired at {ground.Name} - can he finally end it?"
                    };
                    world.Storylines.Add(hoodoo);
                    live.Add(hoodoo);
                    events.Add(new GameEvent(date, GameEventType.NarrativeUpdate,
                        $"{p2.FullName} returns to {ground.Name}, the one ground that has never worked out for him - the hoodoo is a real talking point again.",
                        p2.Id, teamId));
                }
            }
        }

        // Captain under fire / club in crisis / dominant run.
        foreach (var team in world.Teams.Values.Where(t => !t.IsFranchise).OrderBy(t => t.Name))
        {
            bool losing = team.CurrentStreak <= -3;
            bool boardShaky = team.BoardConfidence < 38;
            bool fracturedRoom = team.DressingRoomHarmony < 36;
            bool winning = team.CurrentStreak >= 5;

            var captainId = team.GetCaptain(MatchFormat.T20) ?? team.GetCaptain(MatchFormat.ODI) ?? team.GetCaptain(MatchFormat.Test);

            if (losing && boardShaky && captainId is { } cid
                && !live.Any(s => s.TeamId == team.Id && s.Kind == StorylineKind.CaptainUnderFire))
            {
                var cap = world.Players.FirstOrDefault(p => p.Id == cid);
                var story = new Storyline
                {
                    Kind = StorylineKind.CaptainUnderFire, SubjectId = cid, TeamId = team.Id,
                    Started = date, LastUpdated = date, Intensity = 55,
                    Summary = $"{cap?.FullName ?? "The captain"}'s position is under real scrutiny at {team.Name}."
                };
                world.Storylines.Add(story); live.Add(story);
                if (cap is not null) cap.CaptainTrust = Math.Clamp(cap.CaptainTrust - 4, 0, 100);
                events.Add(new GameEvent(date, GameEventType.NarrativeUpdate,
                    $"The pressure is mounting on {cap?.FullName ?? "the captain"} - {team.Name}'s run has the media circling.", cid, team.Id));
            }

            if (fracturedRoom && team.BoardConfidence < 32
                && !live.Any(s => s.TeamId == team.Id && s.Kind == StorylineKind.CrisisClub))
            {
                var story = new Storyline
                {
                    Kind = StorylineKind.CrisisClub, SubjectId = team.Id, TeamId = team.Id,
                    Started = date, LastUpdated = date, Intensity = 60,
                    Summary = $"{team.Name} are a club in crisis - a divided dressing room and a board at the end of its patience."
                };
                world.Storylines.Add(story); live.Add(story);
                events.Add(new GameEvent(date, GameEventType.NarrativeUpdate,
                    $"{team.Name} are in open crisis - the dressing room is split and the board is out of patience.", team.Id));
            }

            if (winning && !live.Any(s => s.TeamId == team.Id && s.Kind == StorylineKind.DominantRun))
            {
                var story = new Storyline
                {
                    Kind = StorylineKind.DominantRun, SubjectId = team.Id, TeamId = team.Id,
                    Started = date, LastUpdated = date, Intensity = 50,
                    Summary = $"{team.Name} are on a tear - {team.CurrentStreak} wins on the bounce."
                };
                world.Storylines.Add(story); live.Add(story);
                events.Add(new GameEvent(date, GameEventType.NarrativeUpdate,
                    $"{team.Name} are the form side of the competition - {team.CurrentStreak} straight wins.", team.Id));
            }
        }

        // --- reinforce / resolve / fade ---
        foreach (var story in world.Storylines.Where(s => !s.Resolved).ToList())
        {
            var team = story.TeamId is { } tid && world.Teams.TryGetValue(tid, out var t) ? t : null;

            // Redemption: an under-fire captain / crisis club whose side has turned it around.
            if (team is not null && (story.Kind is StorylineKind.CaptainUnderFire or StorylineKind.CrisisClub)
                && (team.CurrentStreak >= 3 || team.BoardConfidence > 55))
            {
                story.Resolved = true;
                var subjectName = world.Players.FirstOrDefault(p => p.Id == story.SubjectId)?.FullName ?? team.Name;
                events.Add(new GameEvent(date, GameEventType.NarrativeUpdate,
                    $"Redemption for {subjectName} - {team.Name} have turned the corner and the questions have stopped.",
                    story.SubjectId, story.TeamId));
                world.Storylines.Add(new Storyline
                {
                    Kind = StorylineKind.Redemption, SubjectId = story.SubjectId, TeamId = story.TeamId,
                    Started = date, LastUpdated = date, Intensity = 40,
                    Summary = $"{subjectName} has answered the critics."
                });
                continue;
            }

            // Reinforce while the signal persists.
            if (story.Kind == StorylineKind.CaptainUnderFire && team is { CurrentStreak: <= -2 })
                story.Reinforce(date, 6);
            else if (story.Kind == StorylineKind.DominantRun && team is { CurrentStreak: >= 4 })
                story.Reinforce(date, 5);

            // Fade.
            if ((date.DayNumber - story.LastUpdated.DayNumber) >= FadeAfterDays)
            {
                story.Resolved = true;
                events.Add(new GameEvent(date, GameEventType.NarrativeUpdate,
                    $"The {StoryLabel(story.Kind)} story around {(team?.Name ?? "the subject")} has quietly run its course.",
                    story.SubjectId, story.TeamId));
            }
        }

        // Prune old resolved storylines.
        var stale = world.Storylines.Where(s => s.Resolved && (date.DayNumber - s.LastUpdated.DayNumber) > 200).ToList();
        foreach (var s in stale) world.Storylines.Remove(s);

        return events;
    }

    private static string StoryLabel(StorylineKind kind) => kind switch
    {
        StorylineKind.BreakoutStar => "breakout-star",
        StorylineKind.CaptainUnderFire => "captain-under-fire",
        StorylineKind.CrisisClub => "crisis",
        StorylineKind.DominantRun => "dominant-run",
        _ => "redemption"
    };
}
