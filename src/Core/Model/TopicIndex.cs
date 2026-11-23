namespace MindBuzz.Core.Model;

/// <summary>Where a topic sits in its map's tree.</summary>
/// <param name="Topic">The topic itself.</param>
/// <param name="Parent">The parent's id, or null for the central topic.</param>
/// <param name="IndexInParent">The position among its siblings; 0 for the central topic.</param>
/// <param name="Depth">0 for the central topic, 1 for main topics, 2 and more for subtopics.</param>
public readonly record struct TopicLocation(Topic Topic, TopicId? Parent, int IndexInParent, int Depth);

/// <summary>
/// Finds topics by id in one map: O(n) to build, O(1) to query. Built once per immutable map (see
/// <see cref="Map.Index"/>). Building it is ~1 ms for 5,000 topics; should that grow, it can become
/// incremental, which the immutable model allows because unchanged subtrees keep their identity.
/// </summary>
public sealed class TopicIndex
{
    private readonly Dictionary<TopicId, TopicLocation> _locations = [];

    internal TopicIndex(Topic root)
    {
        Add(root, parent: null, index: 0, depth: 0);
    }

    public int Count => _locations.Count;

    public bool Contains(TopicId id) => _locations.ContainsKey(id);

    public Topic? Find(TopicId id) => _locations.TryGetValue(id, out TopicLocation location) ? location.Topic : null;

    public TopicLocation? Locate(TopicId id) => _locations.TryGetValue(id, out TopicLocation location) ? location : null;

    /// <summary>The ids from the central topic down to <paramref name="id"/>, both included.</summary>
    public IReadOnlyList<TopicId> PathTo(TopicId id)
    {
        var path = new List<TopicId>();
        TopicId? current = id;

        while (current is { } step && _locations.TryGetValue(step, out TopicLocation location))
        {
            path.Add(step);
            current = location.Parent;
        }

        path.Reverse();
        return path;
    }

    /// <summary>True when <paramref name="ancestor"/> is <paramref name="id"/> itself or one of its
    /// ancestors.</summary>
    public bool IsSelfOrAncestor(TopicId ancestor, TopicId id)
    {
        TopicId? current = id;
        while (current is { } step && _locations.TryGetValue(step, out TopicLocation location))
        {
            if (step == ancestor)
            {
                return true;
            }

            current = location.Parent;
        }

        return false;
    }

    private void Add(Topic topic, TopicId? parent, int index, int depth)
    {
        // Iterative would avoid deep recursion, but a map 10,000 levels deep is not a mind map; the loader caps
        // depth anyway (MbzLimits.MaxDepth).
        _locations[topic.Id] = new TopicLocation(topic, parent, index, depth);

        for (int i = 0; i < topic.Children.Length; i++)
        {
            Add(topic.Children[i], topic.Id, i, depth + 1);
        }
    }
}
