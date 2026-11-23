using System.Collections.Immutable;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Search;

/// <summary>
/// Going to a search hit opens the collapsed branches above it, but only for the eye: browsing must not change the
/// document or add an undo step. The app keeps the ids it opened and shows the map through
/// <see cref="Apply"/>; the document keeps its own collapsed flags.
/// </summary>
public static class Reveal
{
    /// <summary>The collapsed topics above <paramref name="topic"/> that must open for it to be seen.</summary>
    public static ImmutableArray<TopicId> CollapsedAncestors(Map map, TopicId topic)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. map.Index.PathTo(topic).SkipLast(1).Where(id => map.Find(id) is { IsCollapsed: true })];
    }

    /// <summary>The map as shown: <paramref name="opened"/> topics expanded, everything else as the document has
    /// it.</summary>
    public static Map Apply(Map map, IEnumerable<TopicId> opened)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(opened);

        Map shown = map;
        foreach (TopicId id in opened)
        {
            if (shown.Find(id) is { IsCollapsed: true })
            {
                shown = TreeEdit.Update(shown, id, topic => topic with { IsCollapsed = false });
            }
        }

        return shown;
    }
}
