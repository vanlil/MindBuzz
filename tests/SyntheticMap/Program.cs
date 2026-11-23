using System.Diagnostics;
using System.Globalization;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;

namespace MindBuzz.SyntheticMap;

/// <summary>
/// Generates a seeded document, saves it, loads it back and prints timings. Measurements are
/// made with this tool against generated data, never by feel.
/// <code>
/// dotnet run --project tests/SyntheticMap -c Release -- --topics 5000 --seed 42 --out big.mbz
/// </code>
/// Options: --topics, --maps, --depth, --children, --main, --words, --notes, --links, --markers, --collapsed,
/// --relationships, --seed, --out (default: a temporary file, deleted afterwards).
/// </summary>
internal static class Program
{
    private const string Generator = "MindBuzz SyntheticMap";

    private static int Main(string[] args)
    {
        MapSpec spec;
        string? output;
        try
        {
            (spec, output) = Parse(args);
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var clock = Stopwatch.StartNew();
        Document document = MapGenerator.Generate(spec);
        TimeSpan generate = clock.Elapsed;

        string path = output ?? Path.Join(Path.GetTempPath(), $"synthetic-{Guid.NewGuid():N}.mbz");
        try
        {
            clock.Restart();
            MbzFile.Save(path, document, Generator);
            TimeSpan save = clock.Elapsed;

            // Warm up once, then the median of five loads: the first load pays for JIT.
            MbzFile.Load(path);
            var loads = new List<double>();
            LoadResult loaded = null!;
            for (int i = 0; i < 5; i++)
            {
                clock.Restart();
                loaded = MbzFile.Load(path);
                loads.Add(clock.Elapsed.TotalMilliseconds);
            }

            loads.Sort();
            bool equal = loaded.Document is not null && DocumentEquality.AreEqual(document, loaded.Document);
            bool sameBytes = loaded.Document is not null
                && MbzWriter.ToBytes(loaded.Document, Generator).AsSpan().SequenceEqual(File.ReadAllBytes(path));

            var invariant = CultureInfo.InvariantCulture;
            Console.WriteLine(string.Create(invariant, $"topics      {document.TopicCount} in {document.Maps.Length} map(s), seed {spec.Seed}"));
            Console.WriteLine(string.Create(invariant, $"file        {new FileInfo(path).Length / 1024.0:0.0} KiB  {(output is null ? "(temporary)" : path)}"));
            Console.WriteLine(string.Create(invariant, $"generate    {generate.TotalMilliseconds:0.0} ms"));
            Console.WriteLine(string.Create(invariant, $"save        {save.TotalMilliseconds:0.0} ms (atomic, flushed to disk)"));
            Console.WriteLine(string.Create(invariant, $"load        {loads[2]:0.0} ms median of 5 (min {loads[0]:0.0}, max {loads[4]:0.0})"));
            Console.WriteLine($"problems    {loaded.Problems.Length}");
            Console.WriteLine($"round trip  {(equal ? "equal" : "DIFFERENT")}, bytes {(sameBytes ? "identical" : "DIFFERENT")}");

            return equal && sameBytes && loaded.Problems.IsEmpty ? 0 : 1;
        }
        finally
        {
            if (output is null)
            {
                File.Delete(path);
            }
        }
    }

    private static (MapSpec Spec, string? Output) Parse(string[] args)
    {
        var spec = new MapSpec();
        string? output = null;

        for (int i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new FormatException($"{args[i]} needs a value.");
            int Int() => int.Parse(Value(), CultureInfo.InvariantCulture);
            double Ratio() => double.Parse(Value(), CultureInfo.InvariantCulture);

            spec = args[i] switch
            {
                "--topics" => spec with { Topics = Int() },
                "--maps" => spec with { Maps = Int() },
                "--depth" => spec with { MaxDepth = Int() },
                "--children" => spec with { MaxChildren = Int() },
                "--main" => spec with { MainTopics = Int() },
                "--words" => spec with { MaxWords = Int() },
                "--notes" => spec with { NoteRatio = Ratio() },
                "--links" => spec with { LinkRatio = Ratio() },
                "--markers" => spec with { MarkerRatio = Ratio() },
                "--collapsed" => spec with { CollapsedRatio = Ratio() },
                "--relationships" => spec with { Relationships = Int() },
                "--seed" => spec with { Seed = Int() },
                "--out" => SetOutput(spec, Value(), out output),
                _ => throw new FormatException($"Unknown option {args[i]}."),
            };
        }

        return (spec, output);
    }

    private static MapSpec SetOutput(MapSpec spec, string path, out string? output)
    {
        output = Path.GetFullPath(path);
        return spec;
    }
}
