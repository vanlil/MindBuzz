using System.Collections.Immutable;
using System.Globalization;
using MindBuzz.Core.Model;

namespace MindBuzz.SyntheticMap;

/// <summary>What to generate. Every measurement and most tests use maps made from one of these.</summary>
/// <param name="Topics">Topics per map, the central topic included.</param>
/// <param name="Maps">Maps in the document.</param>
/// <param name="MaxDepth">Deepest level below the central topic.</param>
/// <param name="MaxChildren">Most subtopics per topic (main topics are capped at <paramref
/// name="MainTopics"/>).</param>
/// <param name="MainTopics">Most main topics, i.e. children of the central topic.</param>
/// <param name="MaxWords">Most words per topic text.</param>
/// <param name="NoteRatio">Share of topics with a Markdown note.</param>
/// <param name="LinkRatio">Share of topics with internet links.</param>
/// <param name="MarkerRatio">Share of topics with markers.</param>
/// <param name="CollapsedRatio">Share of topics with subtopics that are collapsed.</param>
/// <param name="Relationships">Relationships per map.</param>
/// <param name="Seed">The same seed gives the same document, byte for byte.</param>
public sealed record MapSpec(
    int Topics = 200,
    int Maps = 1,
    int MaxDepth = 5,
    int MaxChildren = 8,
    int MainTopics = 8,
    int MaxWords = 6,
    double NoteRatio = 0.1,
    double LinkRatio = 0.1,
    double MarkerRatio = 0.15,
    double CollapsedRatio = 0.05,
    int Relationships = 10,
    int Seed = 42);

/// <summary>Generates documents from a <see cref="MapSpec"/>, deterministically.</summary>
public static class MapGenerator
{
    // German and English, with umlauts, ß and an emoji, so text measurement and the file format see real text.
    private static readonly string[] Words =
    [
        "Plan", "Idee", "Projekt", "Review", "Budget", "Team", "Kunde", "Migration", "Cloud", "Backup",
        "Netzwerk", "Sicherheit", "Prüfung", "Übergabe", "Größe", "Straße", "release", "design", "keyboard",
        "map", "notes", "links", "deadline", "Abstimmung", "Dokumentation", "VPN", "Firewall", "Lizenz",
        "Schulung", "Rollout", "Test", "Pilot", "Zertifikat", "Server", "Client", "Wartung", "Q3", "2026", "🐝",
    ];

    public static Document Generate(MapSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var random = new Random(spec.Seed);
        var ids = new SeededIdSource(spec.Seed);

        ImmutableArray<Map> maps =
        [
            .. Enumerable.Range(1, Math.Max(1, spec.Maps)).Select(number => GenerateMap(spec, number, random, ids)),
        ];

        return new Document(maps);
    }

    private static Map GenerateMap(MapSpec spec, int number, Random random, IIdSource ids)
    {
        var root = new Node(ids.NextTopic(), Text(random, spec.MaxWords), 0);
        var all = new List<Node> { root };
        var open = new List<Node> { root };

        while (all.Count < spec.Topics && open.Count > 0)
        {
            Node parent = open[random.Next(open.Count)];
            var child = new Node(ids.NextTopic(), Text(random, spec.MaxWords), parent.Depth + 1);
            parent.Children.Add(child);
            all.Add(child);

            int limit = parent.Depth == 0 ? spec.MainTopics : spec.MaxChildren;
            if (parent.Children.Count >= limit)
            {
                open.Remove(parent);
            }

            if (child.Depth < spec.MaxDepth)
            {
                open.Add(child);
            }
        }

        foreach (Node node in all)
        {
            if (random.NextDouble() < spec.NoteRatio)
            {
                node.Note = Note(random);
            }

            if (random.NextDouble() < spec.LinkRatio)
            {
                int count = 1 + random.Next(3);
                for (int i = 0; i < count; i++)
                {
                    string word = Words[random.Next(Words.Length - 1)].ToLowerInvariant();
                    node.Links.Add(new InternetLink(
                        string.Create(CultureInfo.InvariantCulture, $"https://example.com/{Uri.EscapeDataString(word)}/{random.Next(1000)}"),
                        i == 0 ? string.Empty : Text(random, 3)));
                }
            }

            if (random.NextDouble() < spec.MarkerRatio)
            {
                if (random.Next(2) == 0)
                {
                    node.Markers.Add(new Marker(MarkerGroups.Priority, MarkerGroups.PriorityValues[random.Next(5)]));
                }

                node.Markers.Add(new Marker(MarkerGroups.Progress, MarkerGroups.ProgressValues[random.Next(5)]));
            }

            node.IsCollapsed = node.Depth > 0 && node.Children.Count > 0 && random.NextDouble() < spec.CollapsedRatio;
        }

        var relationships = ImmutableArray.CreateBuilder<Relationship>();
        for (int attempt = 0; relationships.Count < spec.Relationships && all.Count > 1 && attempt < spec.Relationships * 10; attempt++)
        {
            TopicId from = all[random.Next(all.Count)].Id;
            TopicId to = all[random.Next(all.Count)].Id;
            if (from == to || relationships.Any(r => r.Connects(from, to)))
            {
                continue;
            }

            relationships.Add(new Relationship(ids.NextRelationship(), from, to, random.Next(4) == 0 ? RelationshipDirection.TwoWay : RelationshipDirection.OneWay));
        }

        // Every main topic has its side, as in any map the app saves.
        return MainSides.Fill(new Map(ids.NextMap(), root.ToTopic())) with { Relationships = relationships.ToImmutable() };
    }

    private static string Text(Random random, int maxWords)
    {
        int count = 1 + random.Next(Math.Max(1, maxWords));
        var words = new string[count];
        for (int i = 0; i < count; i++)
        {
            words[i] = Words[random.Next(Words.Length)];
        }

        // Now and then a line break, as people put in long topics.
        if (count > 3 && random.Next(10) == 0)
        {
            words[count / 2] += "\n";
        }

        return string.Join(' ', words).Replace("\n ", "\n", StringComparison.Ordinal);
    }

    private static string Note(Random random) => string.Join(
        '\n',
        $"## {Text(random, 3)}",
        string.Empty,
        $"{Text(random, 12)} **{Text(random, 2)}**.",
        string.Empty,
        $"- {Text(random, 4)}",
        $"- [x] {Text(random, 4)}",
        $"- [ ] {Text(random, 4)}",
        string.Empty,
        "| A | B |",
        "|---|---|",
        $"| {random.Next(100)} | {Text(random, 2)} |");

    /// <summary>Mutable scaffolding while the tree grows; frozen into immutable topics at the end.</summary>
    private sealed class Node(TopicId id, string text, int depth)
    {
        public TopicId Id { get; } = id;

        public int Depth { get; } = depth;

        public List<Node> Children { get; } = [];

        public List<InternetLink> Links { get; } = [];

        public List<Marker> Markers { get; } = [];

        public string Note { get; set; } = string.Empty;

        public bool IsCollapsed { get; set; }

        public Topic ToTopic() => new(Id, text)
        {
            Children = [.. Children.Select(child => child.ToTopic())],
            Links = [.. Links],
            Markers = [.. Markers],
            Note = Note,
            IsCollapsed = IsCollapsed,
        };
    }
}
