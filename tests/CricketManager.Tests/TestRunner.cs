namespace CricketManager.Tests;

public static class TestRunner
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;
    private static string? _filter;
    private static bool _showTrace;

    /// <summary>
    /// Post-Phase-9 wiring pass: honours two command-line switches so a single failing test can be
    /// iterated on without a ~12-minute full run.
    /// <list type="bullet">
    /// <item><c>--filter &lt;substring&gt;</c> (case-insensitive) - only run tests whose name contains it.</item>
    /// <item><c>--trace</c> - print the full stack trace on a failure, not just the message.</item>
    /// </list>
    /// </summary>
    public static void Init(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--filter" && i + 1 < args.Length) _filter = args[++i];
            else if (args[i].StartsWith("--filter=")) _filter = args[i]["--filter=".Length..];
            else if (args[i] == "--trace") _showTrace = true;
        }

        if (_filter is not null) Console.WriteLine($"(filter: \"{_filter}\")\n");
    }

    private static bool ShouldRun(string name) =>
        _filter is null || name.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    public static void Run(string name, Action test)
    {
        if (!ShouldRun(name)) { _skipped++; return; }
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (Exception ex)
        {
            Report(name, ex);
        }
    }

    public static async Task RunAsync(string name, Func<Task> test)
    {
        if (!ShouldRun(name)) { _skipped++; return; }
        try
        {
            await test();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (Exception ex)
        {
            Report(name, ex);
        }
    }

    private static void Report(string name, Exception ex)
    {
        _failed++;
        Console.WriteLine($"  FAIL  {name}");
        Console.WriteLine($"        {ex.GetType().Name}: {ex.Message}");
        if (_showTrace && ex.StackTrace is not null)
            Console.WriteLine(string.Join(Environment.NewLine, ex.StackTrace.Split(Environment.NewLine).Select(l => "        " + l)));
    }

    public static int Summarize()
    {
        Console.WriteLine();
        var tail = _skipped > 0 ? $", {_skipped} skipped" : "";
        Console.WriteLine($"Results: {_passed} passed, {_failed} failed{tail} (of {_passed + _failed})");
        return _failed == 0 ? 0 : 1;
    }

    public static void IsTrue(bool condition, string message)
    {
        if (!condition) throw new Exception($"Expected true: {message}");
    }

    public static void AreEqual<T>(T expected, T actual, string message = "")
    {
        if (!Equals(expected, actual))
            throw new Exception($"Expected {expected}, got {actual}. {message}");
    }

    public static void InRange(double value, double min, double max, string message = "")
    {
        if (value < min || value > max)
            throw new Exception($"Expected {value} to be in [{min},{max}]. {message}");
    }
}
