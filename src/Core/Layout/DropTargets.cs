using MindBuzz.Core.Model;

namespace MindBuzz.Core.Layout;

/// <summary>Where a dragged topic would land.</summary>
public enum DropKind
{
    /// <summary>As the last subtopic of the target.</summary>
    Into,

    /// <summary>As the sibling just before the target.</summary>
    Before,

    /// <summary>As the sibling just after the target.</summary>
    After,
}

/// <summary>
/// A drop: the target the pointer is on, how, and what that means as a move. <see cref="Index"/> counts among the
/// new parent's children <em>after</em> the dragged topic has left its old place, as <c>MoveTopic</c> expects.
/// </summary>
/// <param name="Side">For a drop that makes or keeps the topic a main topic: the side it goes to.</param>
public sealed record DropPlan(TopicBox Target, DropKind Kind, TopicId NewParent, int Index, MainSide? Side = null);

/// <summary>
/// Drag and drop rules, decided on the laid-out map so they are tested without a pointer:
/// the upper quarter of a topic drops before it, the lower quarter after it, the middle into it. The central
/// topic only takes topics into it. Nothing drops into the dragged topic's own subtree, and a drop that would
/// change nothing is no drop.
/// </summary>
public static class DropTargets
{
    /// <summary>How far above and below a topic still counts as on it: half the gap between subtopics.</summary>
    private const double Reach = 7;

    public static DropPlan? Find(MapLayout layout, Map map, TopicId dragged, LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(map);

        if (map.Index.Locate(dragged) is not { Parent: { } oldParent } location)
        {
            return null;
        }

        TopicBox? target = null;
        foreach (TopicBox box in layout.Boxes)
        {
            var reach = new LayoutRect(box.Bounds.X, box.Bounds.Y - Reach, box.Bounds.Width, box.Bounds.Height + (2 * Reach));
            if (reach.Contains(point) && !map.Index.IsSelfOrAncestor(dragged, box.Id))
            {
                target = box;
            }
        }

        if (target is null)
        {
            return null;
        }

        double relative = (point.Y - target.Bounds.Y) / target.Bounds.Height;
        DropKind kind = target.Parent is null ? DropKind.Into
            : relative < 0.25 ? DropKind.Before
            : relative > 0.75 ? DropKind.After
            : DropKind.Into;

        TopicId newParent;
        int index;
        MainSide? side = null;
        if (kind == DropKind.Into)
        {
            newParent = target.Id;
            index = map.Find(target.Id)!.Children.Length;

            // Onto the central topic: to the side of it the pointer is on, so a drag can change sides.
            if (target.Parent is null)
            {
                side = point.X < target.Bounds.CenterX ? MainSide.Left : MainSide.Right;
            }
        }
        else
        {
            newParent = target.Parent!.Value;

            // Before is above on screen. Between main topics on the left side the list runs bottom to top, so above
            // is after in the list there; the topic takes the side it is dropped on.
            bool mainOnLeft = newParent == map.Root.Id && target.Side == Side.Left;
            index = map.Index.Locate(target.Id)!.Value.IndexInParent + ((kind == DropKind.After) != mainOnLeft ? 1 : 0);
            if (newParent == map.Root.Id)
            {
                side = target.Side == Side.Left ? MainSide.Left : MainSide.Right;
            }
        }

        // Leaving the old place shifts later siblings of the same parent up by one.
        if (newParent == oldParent && location.IndexInParent < index)
        {
            index--;
        }

        if (kind == DropKind.Into && newParent == oldParent)
        {
            index = map.Find(newParent)!.Children.Length - 1;
        }

        bool sameSide = side is null || location.Parent != map.Root.Id || MainSides.Of(map, location.Topic) == side;
        bool unchanged = newParent == oldParent && index == location.IndexInParent && sameSide;
        return unchanged ? null : new DropPlan(target, kind, newParent, index, side);
    }
}
