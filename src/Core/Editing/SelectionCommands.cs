using System.Collections.Immutable;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>
/// Deletes several selected topics with their subtrees in one undo step (multi-select). The central
/// topic is skipped, and so is a topic inside another one being deleted, which goes with it.
/// </summary>
/// <param name="Primary">The topic selected last; the selection afterwards goes to its neighbour, as with one
/// topic.</param>
public sealed record DeleteTopics(MapId Map, ImmutableArray<TopicId> Topics, TopicId Primary) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.DeleteTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Map(document, Map, out Map map, out CommandResult refusal))
        {
            return refusal;
        }

        List<TopicId> roots = SelectionRoots.Of(map, Topics);
        if (roots.Count == 0)
        {
            return CommandResult.Refused(Refusal.CentralTopic);
        }

        // Where the selection lands: the primary's neighbour if it survives, else the nearest surviving ancestor.
        TopicId primaryRoot = roots.FirstOrDefault(root => map.Index.IsSelfOrAncestor(root, Primary), roots[^1]);
        TopicId? target = new DeleteTopic(Map, primaryRoot).Apply(document).Target?.Topic;

        Map changed = map;
        foreach (TopicId root in roots)
        {
            changed = TreeEdit.Remove(changed, root);
        }

        if (target is not { } next || !changed.Contains(next))
        {
            next = map.Index.PathTo(primaryRoot).Reverse().First(changed.Contains);
        }

        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, next));
    }
}

/// <summary>
/// Moves several selected topics to one place in one undo step: drag and drop of a multi-selection.
/// They land side by side, in the order they have in the map, where the dragged topic would have landed alone.
/// </summary>
/// <param name="Primary">The topic the pointer dragged; <paramref name="Index"/> is counted as for it alone
/// (<see cref="MoveTopic"/>: among the new parent's children after it has left).</param>
/// <param name="Side">As for <see cref="MoveTopic"/>: the side when they become or stay main topics.</param>
public sealed record MoveTopics(MapId Map, ImmutableArray<TopicId> Topics, TopicId Primary, TopicId NewParent, int Index, MainSide? Side = null) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Map(document, Map, out Map map, out CommandResult refusal))
        {
            return refusal;
        }

        if (!map.Contains(NewParent) || !map.Contains(Primary))
        {
            return CommandResult.Refused(Refusal.TopicNotFound);
        }

        List<TopicId> roots = SelectionRoots.Of(map, Topics.Add(Primary));
        if (roots.Count == 0)
        {
            return CommandResult.Refused(Refusal.CentralTopic);
        }

        if (roots.Count == 1)
        {
            return new MoveTopic(Map, roots[0], NewParent, Index, Side: Side).Apply(document);
        }

        if (roots.Any(root => map.Index.IsSelfOrAncestor(root, NewParent)))
        {
            return CommandResult.Refused(Refusal.IntoOwnSubtree);
        }

        int parentDepth = map.Index.Locate(NewParent)!.Value.Depth;
        if (roots.Any(root => TreeEdit.TooDeep(parentDepth, map.Find(root)!)))
        {
            return CommandResult.Refused(Refusal.TooDeep);
        }

        // The place, as a neighbour that stays: the child that would follow the primary, skipping moved ones.
        TopicId primaryRoot = roots.First(root => map.Index.IsSelfOrAncestor(root, Primary));
        var others = map.Find(NewParent)!.Children.Where(child => child.Id != primaryRoot).ToList();
        int at = Math.Clamp(Index, 0, others.Count);
        TopicId? before = others.Skip(at).Select(child => (TopicId?)child.Id).FirstOrDefault(id => !roots.Contains(id!.Value));

        bool toRoot = NewParent == map.Root.Id;
        MainSide? side = null;
        if (toRoot)
        {
            Topic? neighbour = before is { } b ? map.Find(b) : others.LastOrDefault(child => !roots.Contains(child.Id));
            MainSide? primarySide = map.Index.Locate(primaryRoot)!.Value.Parent == map.Root.Id ? MainSides.Of(map, map.Find(primaryRoot)!) : null;
            side = Side ?? primarySide ?? (neighbour is null ? MainSide.Right : MainSides.Of(map, neighbour));
        }

        Map moved = map;
        foreach (TopicId root in roots)
        {
            var siblings = moved.Find(NewParent)!.Children.Where(child => child.Id != root).ToList();
            int index = before is { } anchor ? siblings.FindIndex(child => child.Id == anchor) : siblings.Count;
            moved = TreeEdit.Move(moved, root, NewParent, index);
            moved = TreeEdit.Update(moved, root, topic => topic with { Side = side });
        }

        return CommandResult.Applied(document.ReplaceMap(moved), new EditTarget(Map, Primary));
    }
}

/// <summary>The topics of a selection that move or go as a whole: present, not central, not inside another
/// one.</summary>
internal static class SelectionRoots
{
    /// <returns>In the order they have in the map, so a moved group keeps its order.</returns>
    internal static List<TopicId> Of(Map map, IEnumerable<TopicId> selected)
    {
        HashSet<TopicId> set = [.. selected.Where(id => map.Contains(id) && id != map.Root.Id)];
        return
        [
            .. map.Root.DescendantsAndSelf()
                .Select(topic => topic.Id)
                .Where(id => set.Contains(id) && !map.Index.PathTo(id).Any(ancestor => ancestor != id && set.Contains(ancestor))),
        ];
    }
}
