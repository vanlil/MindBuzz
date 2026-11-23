using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>
/// Moves a topic under a new parent, at a position among the new parent's children counted <em>after</em> the
/// topic has left its old place. Drag and drop, and every reorder, promote and demote, end up here.
/// </summary>
/// <param name="Side">When the topic becomes, or stays, a main topic: its side. Without it, a main topic keeps its own,
/// and a topic that becomes one takes the side of the main topic it lands next to.</param>
public sealed record MoveTopic(MapId Map, TopicId Topic, TopicId NewParent, int Index, object? CoalesceKey = null, MainSide? Side = null) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.NonCentralTopic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (!map.Contains(NewParent))
        {
            return CommandResult.Refused(Refusal.TopicNotFound);
        }

        if (map.Index.IsSelfOrAncestor(Topic, NewParent))
        {
            return CommandResult.Refused(Refusal.IntoOwnSubtree);
        }

        if (TreeEdit.TooDeep(map.Index.Locate(NewParent)!.Value.Depth, location.Topic))
        {
            return CommandResult.Refused(Refusal.TooDeep);
        }

        int siblingsAfterRemoval = map.Find(NewParent)!.Children.Length - (location.Parent == NewParent ? 1 : 0);
        int index = Math.Clamp(Index, 0, siblingsAfterRemoval);

        bool toRoot = NewParent == map.Root.Id;
        MainSide? oldSide = location.Parent == map.Root.Id ? MainSides.Of(map, location.Topic) : null;
        if (location.Parent == NewParent && location.IndexInParent == index && (!toRoot || Side is null || Side == oldSide))
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map moved = TreeEdit.Move(map, Topic, NewParent, index);
        MainSide? side = null;
        if (toRoot)
        {
            Topic[] others = [.. moved.Root.Children.Where(child => child.Id != Topic)];
            Topic? neighbour = index > 0 && index - 1 < others.Length ? others[index - 1] : others.FirstOrDefault();
            side = Side ?? oldSide ?? (neighbour is null ? MainSide.Right : MainSides.Of(moved, neighbour));
        }

        moved = TreeEdit.Update(moved, Topic, topic => topic with { Side = side });
        return CommandResult.Applied(document.ReplaceMap(moved), new EditTarget(Map, Topic));
    }
}

/// <summary>The keyboard moves among siblings (Alt+Ctrl+Up, Down, Home, End).</summary>
public enum ReorderDirection
{
    Up,
    Down,
    First,
    Last,
}

/// <summary>Reorders a topic among its siblings.</summary>
public sealed record ReorderTopic(MapId Map, TopicId Topic, ReorderDirection Direction) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.NonCentralTopic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (location.Parent == map.Root.Id)
        {
            return ReorderMain(document, map, location);
        }

        int last = map.Find(location.Parent!.Value)!.Children.Length - 1;
        int target = Direction switch
        {
            ReorderDirection.Up => location.IndexInParent - 1,
            ReorderDirection.Down => location.IndexInParent + 1,
            ReorderDirection.First => 0,
            _ => last,
        };

        if (target < 0 || target > last || target == location.IndexInParent)
        {
            return CommandResult.Refused(Refusal.NoPlaceToMove);
        }

        return new MoveTopic(Map, Topic, location.Parent.Value, target).Apply(document);
    }

    /// <summary>
    /// A main topic moves among the main topics on its own side, up and down as seen on screen; it never changes side
    /// this way. On the left side the list runs bottom to top, so "up" is later in the list.
    /// </summary>
    private CommandResult ReorderMain(Document document, Map map, TopicLocation location)
    {
        MainSide side = MainSides.Of(map, location.Topic);
        IReadOnlyList<Topic> column = MainSides.TopToBottom(map, side);
        int at = column.ToList().FindIndex(topic => topic.Id == Topic);
        int target = Direction switch
        {
            ReorderDirection.Up => at - 1,
            ReorderDirection.Down => at + 1,
            ReorderDirection.First => 0,
            _ => column.Count - 1,
        };

        if (target < 0 || target >= column.Count || target == at)
        {
            return CommandResult.Refused(Refusal.NoPlaceToMove);
        }

        // Land next to the topic now at the target place on screen: below it is after it in the list on the right
        // side and before it on the left, where the list runs bottom to top.
        bool below = Direction is ReorderDirection.Down or ReorderDirection.Last;
        bool afterInList = below != (side == MainSide.Left);
        int neighbour = map.Root.Children.IndexOf(column[target]);
        int neighbourAfterRemoval = neighbour > location.IndexInParent ? neighbour - 1 : neighbour;
        return new MoveTopic(Map, Topic, map.Root.Id, afterInList ? neighbourAfterRemoval + 1 : neighbourAfterRemoval, Side: side).Apply(document);
    }
}

/// <summary>Moves a main topic to the other side of the central topic, keeping its place in the list (Move to Other
/// Side).</summary>
public sealed record SetMainSide(MapId Map, TopicId Topic, MainSide Side) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.NonCentralTopic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (location.Parent != map.Root.Id)
        {
            return CommandResult.Refused(Refusal.NoPlaceToMove);
        }

        if (MainSides.Of(map, location.Topic) == Side)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        return CommandResult.Applied(document.ReplaceMap(TreeEdit.Update(map, Topic, topic => topic with { Side = Side })), new EditTarget(Map, Topic));
    }
}

/// <summary>
/// Balance Map: the main topics are split between the sides once, so
/// both are about equally tall, keeping their clockwise order: the first ones in that order go right, the rest left.
/// The heights come from the app's layout, which knows the text sizes.
/// </summary>
/// <param name="Heights">Each main topic's subtree height as laid out.</param>
public sealed record BalanceMap(MapId Map, IReadOnlyDictionary<TopicId, double> Heights) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(Heights);

        if (!Resolve.Map(document, Map, out Map map, out CommandResult refusal))
        {
            return refusal;
        }

        // Clockwise: the right side top to bottom, then the left side bottom to top, which is list order per side.
        Topic[] clockwise = [.. map.Root.Children.Where(c => MainSides.Of(map, c) == MainSide.Right), .. map.Root.Children.Where(c => MainSides.Of(map, c) == MainSide.Left)];
        double Height(Topic topic) => Heights.TryGetValue(topic.Id, out double height) ? height : 0;
        double total = clockwise.Sum(Height);

        int rightCount = 0;
        double best = double.MaxValue;
        double running = 0;
        for (int k = 0; k <= clockwise.Length; k++)
        {
            double difference = Math.Abs(running - (total - running));
            if (difference < best - 1e-9)
            {
                best = difference;
                rightCount = k;
            }

            if (k < clockwise.Length)
            {
                running += Height(clockwise[k]);
            }
        }

        Topic[] arranged = [.. clockwise.Select((topic, i) => topic with { Side = i < rightCount ? MainSide.Right : MainSide.Left })];
        if (arranged.Select(t => (t.Id, t.Side)).SequenceEqual(map.Root.Children.Select(t => (t.Id, (MainSide?)MainSides.Of(map, t)))))
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map balanced = map with { Root = map.Root with { Children = [.. arranged] } };
        return CommandResult.Applied(document.ReplaceMap(balanced), new EditTarget(Map, map.Root.Id));
    }
}

/// <summary>Makes a topic the next sibling of its parent. Main topics cannot be promoted.</summary>
public sealed record PromoteTopic(MapId Map, TopicId Topic) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.NonCentralTopic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        TopicLocation parent = map.Index.Locate(location.Parent!.Value)!.Value;
        if (parent.Parent is not { } grandparent)
        {
            return CommandResult.Refused(Refusal.NoPlaceToMove);
        }

        // The parent keeps its index; the topic lands right after it.
        return new MoveTopic(Map, Topic, grandparent, parent.IndexInParent + 1).Apply(document);
    }
}

/// <summary>Makes a topic the last subtopic of its previous sibling.</summary>
public sealed record DemoteTopic(MapId Map, TopicId Topic) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.NonCentralTopic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (location.IndexInParent == 0)
        {
            return CommandResult.Refused(Refusal.NoPlaceToMove);
        }

        Topic previous = map.Find(location.Parent!.Value)!.Children[location.IndexInParent - 1];
        return new MoveTopic(Map, Topic, previous.Id, int.MaxValue).Apply(document);
    }
}

/// <summary>Collapses or expands one topic. Undoable, because the state is saved in the file.</summary>
public sealed record SetCollapsed(MapId Map, TopicId Topic, bool Collapsed) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.Collapse;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Topic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (!location.Topic.HasChildren || location.Topic.IsCollapsed == Collapsed)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        // Collapsing the central topic would hide the whole map, and it has no handle to bring it back.
        if (Collapsed && location.Parent is null)
        {
            return CommandResult.Refused(Refusal.CentralTopic);
        }

        Map changed = TreeEdit.Update(map, Topic, topic => topic with { IsCollapsed = Collapsed });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }
}

/// <summary>
/// Shows the map to a number of levels (Alt+Shift+1…9; <c>int.MaxValue</c> for "show all"):
/// every topic with subtopics at depth below <see cref="Levels"/> is expanded, every one at or below it is
/// collapsed. The central topic is depth 0, so <c>Levels = 1</c> shows the central topic and its main topics.
/// </summary>
public sealed record ShowLevels(MapId Map, int Levels, TopicId? Selection = null) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.Collapse;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Map(document, Map, out Map map, out CommandResult refusal))
        {
            return refusal;
        }

        Topic root = Apply(map.Root, depth: 0);
        if (ReferenceEquals(root, map.Root))
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        // Keep the selection where it was if still visible; otherwise its nearest visible ancestor.
        Map changed = map with { Root = root };
        TopicId target = Selection is { } selected && changed.Contains(selected) ? VisibleAncestor(changed, selected) : root.Id;
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, target));
    }

    /// <summary>Returns the same instance when nothing in the subtree changes, so untouched subtrees stay
    /// shared.</summary>
    private Topic Apply(Topic topic, int depth)
    {
        if (!topic.HasChildren)
        {
            return topic;
        }

        bool collapsed = depth >= Levels;
        var children = topic.Children;

        if (!collapsed)
        {
            var builder = children.ToBuilder();
            bool changedChild = false;
            for (int i = 0; i < builder.Count; i++)
            {
                Topic child = Apply(builder[i], depth + 1);
                changedChild |= !ReferenceEquals(child, builder[i]);
                builder[i] = child;
            }

            if (changedChild)
            {
                children = builder.ToImmutable();
            }
        }

        return collapsed == topic.IsCollapsed && children == topic.Children
            ? topic
            : topic with { IsCollapsed = collapsed, Children = children };
    }

    private static TopicId VisibleAncestor(Map map, TopicId id)
    {
        IReadOnlyList<TopicId> path = map.Index.PathTo(id);
        foreach (TopicId step in path)
        {
            if (map.Find(step)!.IsCollapsed)
            {
                return step;
            }
        }

        return id;
    }
}

/// <summary>
/// Shows one more level below a topic (Ctrl+D, "Show next level of topics"): the topic itself if it
/// is collapsed, otherwise every collapsed topic at the edge of what is visible in its subtree.
/// </summary>
public sealed record ShowNextLevel(MapId Map, TopicId Topic) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.Collapse;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Topic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        Topic expanded = Expand(location.Topic);
        if (ReferenceEquals(expanded, location.Topic))
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map changed = TreeEdit.Update(map, Topic, _ => expanded);
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }

    /// <summary>Expands the visible frontier; returns the same instance when there is none.</summary>
    private static Topic Expand(Topic topic)
    {
        if (!topic.HasChildren)
        {
            return topic;
        }

        if (topic.IsCollapsed)
        {
            return topic with { IsCollapsed = false };
        }

        var children = topic.Children.ToBuilder();
        bool changed = false;
        for (int i = 0; i < children.Count; i++)
        {
            Topic child = Expand(children[i]);
            changed |= !ReferenceEquals(child, children[i]);
            children[i] = child;
        }

        return changed ? topic with { Children = children.ToImmutable() } : topic;
    }
}
