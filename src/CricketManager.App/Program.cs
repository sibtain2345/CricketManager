using CricketManager.App;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "new": return await RunNew(args);
        case "advance": return await RunAdvance(args);
        case "status": return await RunStatus(args);
        case "-h" or "--help" or "help": PrintUsage(); return 0;
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        CricketManager.App - a headless console game loop.

        Usage:
          dotnet run --project src/CricketManager.App -- new <save-dir> [--seed N] [--start yyyy-MM-dd]
              [--countries England,Australia,India] [--teams-per-country N] [--squad-size N] [--force]

          dotnet run --project src/CricketManager.App -- advance <save-dir> [--days N | --weeks N | --to yyyy-MM-dd]
              (defaults to --days 1 when nothing is given)

          dotnet run --project src/CricketManager.App -- status <save-dir>
        """);
}

static async Task<int> RunNew(string[] args)
{
    var saveDir = RequireSaveDirectory(args);
    var options = ParseOptions(args);

    int? seed = options.TryGetValue("seed", out var seedStr) ? int.Parse(seedStr) : null;
    DateOnly? start = options.TryGetValue("start", out var startStr) ? DateOnly.Parse(startStr) : null;
    string[]? countries = options.TryGetValue("countries", out var countriesStr)
        ? countriesStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        : null;
    int teamsPerCountry = options.TryGetValue("teams-per-country", out var tpc) ? int.Parse(tpc) : 4;
    int squadSize = options.TryGetValue("squad-size", out var ss) ? int.Parse(ss) : 14;
    bool force = HasFlag(args, "force");

    var result = await AppCommands.NewGame(saveDir, seed, start, countries, teamsPerCountry, squadSize, force);

    Console.WriteLine($"New game created at '{result.SaveDirectory}'.");
    Console.WriteLine($"  World seed:   {result.WorldSeed}");
    Console.WriteLine($"  Start date:   {result.StartDate:yyyy-MM-dd}");
    Console.WriteLine($"  Countries:    {string.Join(", ", result.Countries)}");
    Console.WriteLine($"  Teams:        {result.TeamCount}");
    Console.WriteLine($"  Players:      {result.PlayerCount}");
    Console.WriteLine($"  Competitions: {result.CompetitionCount}");
    return 0;
}

static async Task<int> RunAdvance(string[] args)
{
    var saveDir = RequireSaveDirectory(args);
    var options = ParseOptions(args);

    int? days = options.TryGetValue("days", out var d) ? int.Parse(d) : null;
    int? weeks = options.TryGetValue("weeks", out var w) ? int.Parse(w) : null;
    DateOnly? to = options.TryGetValue("to", out var t) ? DateOnly.Parse(t) : null;

    var result = await AppCommands.Advance(saveDir, days, weeks, to);

    Console.WriteLine($"Advanced {result.FromDate:yyyy-MM-dd} -> {result.ToDate:yyyy-MM-dd} ({result.TotalEvents} events).");
    if (result.Headlines.Count > 0)
    {
        Console.WriteLine("Headlines:");
        foreach (var headline in result.Headlines)
            Console.WriteLine($"  - {headline}");
    }
    return 0;
}

static async Task<int> RunStatus(string[] args)
{
    var saveDir = RequireSaveDirectory(args);
    var result = await AppCommands.Status(saveDir);

    Console.WriteLine($"Save: '{saveDir}'");
    Console.WriteLine($"  Date:        {result.CurrentDate:yyyy-MM-dd}");
    Console.WriteLine($"  World seed:  {result.WorldSeed}");
    Console.WriteLine($"  Teams:       {result.TeamCount}");
    Console.WriteLine($"  Players:     {result.ActivePlayerCount} active, {result.RetiredPlayerCount} retired");
    Console.WriteLine($"  Competitions:{result.CompetitionCount} ({result.SeasonsCompleted} seasons completed)");
    Console.WriteLine(result.HumanCoachName is null
        ? "  Human coach: none (fully AI-driven)"
        : $"  Human coach: {result.HumanCoachName} at {result.HumanTeamName}");
    return 0;
}

static string RequireSaveDirectory(string[] args)
{
    // args[0] is the command; the first non-flag, non-flag-value token after it is the save dir.
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            return args[i];
    }
    throw new InvalidOperationException("A save directory is required.");
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < args.Length - 1; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal) && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            options[args[i][2..]] = args[i + 1];
    }
    return options;
}

static bool HasFlag(string[] args, string name) =>
    args.Any(a => string.Equals(a, $"--{name}", StringComparison.OrdinalIgnoreCase));
