using CricketManager.Api;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GameSessionStore>();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error is KeyNotFoundException ? StatusCodes.Status404NotFound : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = error?.Message ?? "An unexpected error occurred." });
}));

var api = app.MapGroup("/api/games");

// A curated subset of event types worth surfacing as a headline after an advance - not every one
// of the ~100+ GameEventType values, which would drown the response in noise. Mirrors
// CricketManager.App.AppCommands' identical list.
var headlineEventTypes = new HashSet<GameEventType>
{
    GameEventType.SeasonCompleted, GameEventType.CoachDismissed, GameEventType.NationalCoachDismissed,
    GameEventType.PlayerRetired, GameEventType.TrophyContested, GameEventType.TeamPromoted,
    GameEventType.TeamRelegated, GameEventType.HallOfFameInduction, GameEventType.ClubTakeover,
    GameEventType.BoardroomCoup
};

// ---------------- Game lifecycle ----------------

api.MapPost("/", async (NewGameRequest req, GameSessionStore store) =>
{
    try
    {
        var session = await GameSession.CreateNewAsync(
            req.SaveDirectory, req.Seed, req.StartDate, req.Countries, req.TeamsPerCountry, req.SquadSize, req.Force);
        store.Add(session);
        return Results.Ok(Summarize(session));
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

api.MapPost("/load", async (LoadGameRequest req, GameSessionStore store) =>
{
    var session = await GameSession.LoadAsync(req.SaveDirectory);
    if (session is null) return Results.NotFound(new { error = $"No save found at '{req.SaveDirectory}'." });
    store.Add(session);
    return Results.Ok(Summarize(session));
});

api.MapGet("/{id:guid}", (Guid id, GameSessionStore store) => Results.Ok(Summarize(store.Require(id))));

api.MapPost("/{id:guid}/advance", async (Guid id, AdvanceRequest req, GameSessionStore store) =>
{
    var session = store.Require(id);
    var fromDate = session.Calendar.CurrentDate;
    var events = session.Advance(req.Days, req.Weeks, req.To);
    await session.SaveAsync();

    var headlines = events
        .Where(e => headlineEventTypes.Contains(e.Type))
        .Select(e => e.Headline)
        .Take(20)
        .ToList();

    return Results.Ok(new AdvanceResponseDto(fromDate, session.Calendar.CurrentDate, events.Count, headlines));
});

// ---------------- Global search (design decision 2c: always available, whole database) ----------------

api.MapGet("/{id:guid}/search", (Guid id, GameSessionStore store, string q) =>
{
    var world = store.Require(id).World;
    if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Results.Ok(Array.Empty<SearchResultDto>());

    var playerMatches = world.Players
        .Where(p => !p.IsRetired && p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
        .Take(15)
        .Select(p =>
        {
            var teamName = p.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var t) ? t.Name : "Free agent";
            return new SearchResultDto(p.Id, "Player", p.FullName, teamName);
        });

    var teamMatches = world.Teams.Values
        .Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
        .Take(10)
        .Select(t => new SearchResultDto(t.Id, "Team", t.Name, t.Country));

    return Results.Ok(playerMatches.Concat(teamMatches).Take(20).ToList());
});

// ---------------- Teams / fixtures ----------------

api.MapGet("/{id:guid}/teams", (Guid id, GameSessionStore store) =>
{
    var world = store.Require(id).World;
    return Results.Ok(world.Teams.Values
        .OrderBy(t => t.Name)
        .Select(t => new TeamSummaryDto(t.Id, t.Name, t.Country, t.IsNational, t.IsFranchise))
        .ToList());
});

api.MapGet("/{id:guid}/fixtures", (Guid id, GameSessionStore store, int take) =>
{
    var world = store.Require(id).World;
    var competitionsById = world.Competitions.ToDictionary(c => c.Id);

    var rows = world.Fixtures
        .Where(f => f.Status == FixtureStatus.Scheduled || f.Status == FixtureStatus.Completed)
        .OrderBy(f => f.ScheduledDate)
        .Take(take <= 0 ? 20 : take)
        .Select(f =>
        {
            var home = (f.HomeTeamId is { } h && world.Teams.TryGetValue(h, out var ht) ? ht.Name : f.HomeTeamPlaceholder) ?? "TBD";
            var away = (f.AwayTeamId is { } a && world.Teams.TryGetValue(a, out var at) ? at.Name : f.AwayTeamPlaceholder) ?? "TBD";
            var comp = competitionsById.TryGetValue(f.CompetitionId, out var c) ? c : null;
            return new FixtureSummaryDto(f.Id, home, away, f.ScheduledDate, comp?.Name ?? "Unknown", comp?.Format.ToString() ?? "", f.Status.ToString());
        })
        .ToList();

    return Results.Ok(rows);
});

// ---------------- Squad / players ----------------

api.MapGet("/{id:guid}/teams/{teamId:guid}/squad", (Guid id, Guid teamId, GameSessionStore store) =>
{
    var session = store.Require(id);
    var world = session.World;
    if (!world.Teams.TryGetValue(teamId, out var team)) return Results.NotFound();

    var players = world.Players.Where(p => team.SquadPlayerIds.Contains(p.Id) && !p.IsRetired);
    var rows = players.Select(p => PlayerProjection.ToSquadRow(p, world, session.Calendar.CurrentDate)).ToList();
    return Results.Ok(rows);
});

api.MapGet("/{id:guid}/players/{playerId:guid}", (Guid id, Guid playerId, GameSessionStore store) =>
{
    var session = store.Require(id);
    var world = session.World;
    var player = world.Players.FirstOrDefault(p => p.Id == playerId);
    if (player is null) return Results.NotFound();

    Team? team = player.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var t) ? t : null;
    // Never seed from HashCode.Combine/GetHashCode - .NET randomises those per process, so the
    // "same player, same day" read would silently change across a server restart even though it
    // looks stable within one running process. Same WorldSeed*prime+int convention GameCalendar's
    // own RandomForDay/RandomForWeek/etc already use; the Guid's own bytes (not its hash code)
    // stand in for the missing int key.
    var playerSeedComponent = BitConverter.ToInt32(playerId.ToByteArray(), 0);
    var random = new Random(unchecked(session.Calendar.WorldSeed * 7919 + playerSeedComponent + session.Calendar.CurrentDate.DayNumber));
    var dto = PlayerProjection.ToProfile(player, world, team, session.Calendar.CurrentDate, world.PlayerContracts.ToList(), random);
    return Results.Ok(dto);
});

app.MapHub<MatchReplayHub>("/hubs/match");

app.Run();

static GameSummaryDto Summarize(GameSession session)
{
    var world = session.World;
    return new GameSummaryDto(
        session.Id, session.Calendar.CurrentDate, session.Calendar.WorldSeed,
        world.Teams.Count, world.Players.Count(p => !p.IsRetired), world.Players.Count(p => p.IsRetired),
        world.Competitions.Count, world.CompetitionSeasons.Count(s => s.IsCompleted));
}
