using System.Collections.Immutable;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>
/// The few tree operations every command is built from. Each rebuilds only the path from the central topic to
/// the change (O(depth)); every other subtree is shared with the previous map.
/// </summary>
internal static class TreeEdit
{
    /// <summary>
    /// True when <paramref name="subtree"/> placed under a topic at <paramref name="parentDepth"/> would reach deeper
    /// than the file format keeps. The loader leaves such topics out, so they would be lost on the next save and open;
    /// every command that nests topics (add, add parent, move, paste) refuses instead.
    /// </summary>
    internal static bool TooDeep(int parentDepth, Topic subtree) =>
        parentDepth + 1 + Height(subtree) > Serialization.MbzLimits.MaxTopicDepth;

    /// <summary>How many levels lie below <paramref name="root"/>: 0 for a topic without subtopics. Without recursion.</summary>
    private static int Height(Topic root)
    {
        int height = 0;
        var pending = new Stack<(Topic Topic, int Depth)>([(root, 0)]);
        while (pending.TryPop(out (Topic Topic, int Depth) item))
        {
            height = Math.Max(height, item.Depth);
            foreach (Topic child in item.Topic.Children)
            {
                pending.Push((child, item.Depth + 1));
            }
        }

        return height;
    }

    /// <summary>Replaces the topic <paramref name="id"/> with <c>change(topic)</c>.</summary>
    internal static Map Update(Map map, TopicId id, Func<Topic, Topic> change)
    {
        IReadOnlyList<TopicId> path = map.Index.PathTo(id);
        return map with { Root = Rebuild(map.Root, map.Index, path, 0, change) };
    }

    /// <summary>Removes <paramref name="id"/> and its subtree, and every relationship touching any of them.</summary>
    internal static Map Remove(Map map, TopicId id)
    {
        TopicLocation location = map.Index.Locate(id)!.Value;
        HashSet<TopicId> removed = [.. location.Topic.DescendantsAndSelf().Select(topic => topic.Id)];

        Map without = Update(map, location.Parent!.Value, parent => parent with
        {
            Children = parent.Children.RemoveAt(location.IndexInParent),
        });

        return without with
        {
            Relationships = [.. map.Relationships.Where(r => !removed.Contains(r.From) && !removed.Contains(r.To))],
        };
    }

    /// <summary>
    /// Inserts <paramref name="topics"/> under <paramref name="parent"/> at <paramref name="index"/> (clamped), and
    /// expands the parent so the new topics are visible.
    /// </summary>
    internal static Map Insert(Map map, TopicId parent, int index, ImmutableArray<Topic> topics) =>
        Update(map, parent, p => p with
        {
            Children = p.Children.InsertRange(Math.Clamp(index, 0, p.Children.Length), topics),
            IsCollapsed = false,
        });

    /// <summary>
    /// Moves <paramref name="id"/> under <paramref name="newParent"/>, to position <paramref name="index"/> among
    /// the new parent's children <em>after</em> the topic has left its old place. Relationships are untouched: the
    /// topic keeps its id.
    /// </summary>
    internal static Map Move(Map map, TopicId id, TopicId newParent, int index)
    {
        Topic topic = map.Find(id)!;
        TopicLocation location = map.Index.Locate(id)!.Value;

        Map without = Update(map, location.Parent!.Value, parent => parent with
        {
            Children = parent.Children.RemoveAt(location.IndexInParent),
        });

        return Insert(without, newParent, index, [topic]);
    }

    private static Topic Rebuild(Topic node, TopicIndex index, IReadOnlyList<TopicId> path, int level, Func<Topic, Topic> change)
    {
        if (level == path.Count - 1)
        {
            return change(node);
        }

        int childIndex = index.Locate(path[level + 1])!.Value.IndexInParent;
        Topic child = Rebuild(node.Children[childIndex], index, path, level + 1, change);
        return node with { Children = node.Children.SetItem(childIndex, child) };
    }
}

/// <summary>Lookup helpers shared by the commands: each returns a refusal instead of throwing.</summary>
internal static class Resolve
{
    internal static bool Map(Document document, MapId id, out Map map, out CommandResult refusal)
    {
        map = document.FindMap(id)!;
        refusal = map is null ? CommandResult.Refused(Refusal.MapNotFound) : null!;
        return map is not null;
    }

    internal static bool Topic(Document document, MapId mapId, TopicId topicId, out Map map, out TopicLocation location, out CommandResult refusal)
    {
        location = default;
        if (!Map(document, mapId, out map, out refusal))
        {
            return false;
        }

        if (map.Index.Locate(topicId) is not { } found)
        {
            refusal = CommandResult.Refused(Refusal.TopicNotFound);
            return false;
        }

        location = found;
        return true;
    }

    /// <summary>As <see cref="Topic"/>, and refuses the central topic.</summary>
    internal static bool NonCentralTopic(Document document, MapId mapId, TopicId topicId, out Map map, out TopicLocation location, out CommandResult refusal)
    {
        if (!Topic(document, mapId, topicId, out map, out location, out refusal))
        {
            return false;
        }

        if (location.Parent is null)
        {
            refusal = CommandResult.Refused(Refusal.CentralTopic);
            return false;
        }

        return true;
    }

    /// <summary>True when <paramref name="id"/> is used anywhere in the document, as a topic, map or relationship.</summary>
    internal static bool IdInUse(Document document, string id) =>
        document.Maps.Any(map =>
            map.Id.Value == id
            || map.Index.Contains(new TopicId(id))
            || map.Relationships.Any(r => r.Id.Value == id));
}
