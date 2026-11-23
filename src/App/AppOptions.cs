namespace MindBuzz.App;

/// <summary>
/// The command line. Not settings: nothing is remembered. Unknown or mistyped switches are
/// ignored rather than stopping start-up.
/// </summary>
/// <param name="FilePath">A document to open at start-up, or null for a new document.</param>
/// <param name="ShowStats">Show the canvas's frame statistics (<c>--stats</c>; always on in Debug builds).</param>
/// <param name="Benchmark">Run the canvas benchmark, print the numbers and quit (<c>--bench</c>).</param>
/// <param name="CheckForUpdate">Ask GitHub for a newer release once the window is up. On for
/// every real start except the benchmark; off by default, so a test that builds a window never reaches the
/// network.</param>
internal sealed record AppOptions(string? FilePath = null, bool ShowStats = false, bool Benchmark = false, bool CheckForUpdate = false)
{
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? file = null;
        bool stats = false;
        bool bench = false;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--stats":
                    stats = true;
                    break;
                case "--bench":
                    bench = true;
                    stats = true;
                    break;
                default:
                    // The first argument that is not a switch is the file. macOS adds a -psn_… argument when
                    // launched from Finder on older systems; it starts with a dash and is skipped like any switch.
                    if (file is null && !arg.StartsWith('-'))
                    {
                        file = arg;
                    }

                    break;
            }
        }

#if DEBUG
        stats = true;
#endif

        return new AppOptions(file, stats, bench, CheckForUpdate: !bench);
    }
}
