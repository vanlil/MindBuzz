using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace MindBuzz.Core.Model;

/// <summary>
/// One map: exactly one central topic (the root of its tree) and the relationships between its topics. The map has no
/// name of its own: its tab shows the central topic's text.
/// </summary>
public sealed record Map(MapId Id, Topic Root)
{
    /// <summary>The tab's label: the central topic's text, shortened (see <see cref="MapName.For"/>).</summary>
    public string Name => MapName.For(Root.Text);

    public ImmutableArray<Relationship> Relationships { get; init; } = [];

    /// <summary>
    /// Where each topic sits, built on first use and cached per map instance. A map is immutable, so the cache can
    /// never go stale; an edit makes a new map, which builds its own index when asked.
    /// </summary>
    public TopicIndex Index => IndexCache.GetValue(this, static map => new TopicIndex(map.Root));

    public Topic? Find(TopicId id) => Index.Find(id);

    public bool Contains(TopicId id) => Index.Contains(id);

    private static readonly ConditionalWeakTable<Map, TopicIndex> IndexCache = [];
}

/// <summary>A document: one or more maps, in tab order.</summary>
public sealed record Document
{
    public Document(ImmutableArray<Map> maps)
    {
        if (maps.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A document has at least one map.", nameof(maps));
        }

        Maps = maps;
    }

    public ImmutableArray<Map> Maps { get; init; }

    /// <summary>
    /// A new document: one map with only a central topic ("New document"). The text is passed in
    /// because it is user-visible text from the app's <c>Strings.resx</c>.
    /// </summary>
    public static Document CreateNew(IIdSource ids, string centralTopicText)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return new Document([new Map(ids.NextMap(), new Topic(ids.NextTopic(), centralTopicText))]);
    }

    public Map? FindMap(MapId id)
    {
        foreach (Map map in Maps)
        {
            if (map.Id == id)
            {
                return map;
            }
        }

        return null;
    }

    public int IndexOfMap(MapId id)
    {
        for (int i = 0; i < Maps.Length; i++)
        {
            if (Maps[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Every topic of every map.</summary>
    public int TopicCount => Maps.Sum(map => map.Index.Count);

    internal Document ReplaceMap(Map map)
    {
        int index = IndexOfMap(map.Id);
        return this with { Maps = Maps.SetItem(index, map) };
    }
}
