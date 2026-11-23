using MindBuzz.Core.Model;

namespace MindBuzz.Core.Layout;

/// <summary>The keyboard's selection moves (Navigation).</summary>
public enum NavigationMove
{
    Up,
    Down,
    Left,
    Right,

    /// <summary>First sibling (Home).</summary>
    FirstSibling,

    /// <summary>Last sibling (End).</summary>
    LastSibling,

    /// <summary>The central topic (Ctrl+Home).</summary>
    Central,

    /// <summary>The parent (Ctrl+Backspace).</summary>
    Parent,

    /// <summary>The next visible topic in outline order (Tab).</summary>
    Next,

    /// <summary>The previous visible topic in outline order (Shift+Tab).</summary>
    Previous,
}

/// <summary>
/// Where the selection goes for a keyboard move, decided on the laid-out map. Pure, so every rule is tested
/// without a window. The rules:
/// <list type="bullet">
/// <item>Left and Right go to the parent or to a child, side-aware: on the left half, Left goes outwards to
/// the children. From the central topic they go to the main topic on that side nearest to the centre,
/// vertically.</item>
/// <item>Up and Down are spatial: the nearest visible topic above or below on the same side, even in another
/// branch.</item>
/// </list>
/// </summary>
public static class Navigator
{
    /// <summary>The topic to select, or null when the move leads nowhere (the selection stays).</summary>
    public static TopicId? Move(MapLayout layout, Map map, TopicId from, NavigationMove move)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(map);

        if (layout.Find(from) is not { } current)
        {
            return null;
        }

        return move switch
        {
            NavigationMove.Left => Sideways(layout, current, Side.Left),
            NavigationMove.Right => Sideways(layout, current, Side.Right),
            NavigationMove.Up => Vertical(layout, current, up: true),
            NavigationMove.Down => Vertical(layout, current, up: false),
            NavigationMove.FirstSibling => Sibling(map, current, first: true),
            NavigationMove.LastSibling => Sibling(map, current, first: false),
            NavigationMove.Central => Different(map.Root.Id, from),
            NavigationMove.Parent => current.Parent,
            NavigationMove.Next => Outline(layout, map, from, step: 1),
            _ => Outline(layout, map, from, step: -1),
        };
    }

    /// <summary>Towards <paramref name="direction"/>: outwards to a child, inwards to the parent.</summary>
    private static TopicId? Sideways(MapLayout layout, TopicBox current, Side direction)
    {
        if (current.Side == Side.Center)
        {
            return NearestVertically(layout.Boxes.Where(box => box.Depth == 1 && box.Side == direction), current.Bounds.CenterY);
        }

        if (current.Side == direction)
        {
            return NearestVertically(layout.Boxes.Where(box => box.Parent == current.Id), current.Bounds.CenterY);
        }

        return current.Parent;
    }

    /// <summary>
    /// The nearest topic above or below on the same side. Horizontal distance counts double: a topic straight
    /// above beats one that is a little closer vertically but far off to the side.
    /// </summary>
    private static TopicId? Vertical(MapLayout layout, TopicBox current, bool up)
    {
        if (current.Side == Side.Center)
        {
            return null;
        }

        TopicBox? best = null;
        double bestScore = double.MaxValue;

        foreach (TopicBox candidate in layout.Boxes)
        {
            if (candidate.Side != current.Side || candidate.Id == current.Id)
            {
                continue;
            }

            double dy = up ? current.Bounds.Y - candidate.Bounds.Bottom : candidate.Bounds.Y - current.Bounds.Bottom;
            if (dy < -0.5)
            {
                continue;
            }

            double gap = Math.Max(0, Math.Max(candidate.Bounds.X - current.Bounds.Right, current.Bounds.X - candidate.Bounds.Right));
            double score = Math.Max(0, dy) + (2 * gap);

            // Ties go to the one in document order first, so the result never depends on dictionary order.
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best?.Id;
    }

    private static TopicId? Sibling(Map map, TopicBox current, bool first)
    {
        if (current.Parent is not { } parent)
        {
            return null;
        }

        var siblings = map.Find(parent)!.Children;
        TopicId target = first ? siblings[0].Id : siblings[^1].Id;
        return Different(target, current.Id);
    }

    /// <summary>The next or previous visible topic in outline (document) order, without wrapping.</summary>
    private static TopicId? Outline(MapLayout layout, Map map, TopicId from, int step)
    {
        List<TopicId> visible = [.. map.Root.DescendantsAndSelf().Select(topic => topic.Id).Where(id => layout.Find(id) is not null)];
        int index = visible.IndexOf(from) + step;
        return index >= 0 && index < visible.Count ? visible[index] : null;
    }

    private static TopicId? NearestVertically(IEnumerable<TopicBox> candidates, double y)
    {
        TopicBox? best = null;
        foreach (TopicBox candidate in candidates)
        {
            if (best is null || Math.Abs(candidate.Bounds.CenterY - y) < Math.Abs(best.Bounds.CenterY - y))
            {
                best = candidate;
            }
        }

        return best?.Id;
    }

    private static TopicId? Different(TopicId target, TopicId from) => target == from ? null : target;
}
