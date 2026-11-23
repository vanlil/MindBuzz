using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>Where a new topic goes, relative to an existing one.</summary>
public enum NewTopicPlacement
{
    /// <summary>The last subtopic of the reference topic.</summary>
    Subtopic,

    /// <summary>The next sibling of the reference topic.</summary>
    SiblingAfter,

    /// <summary>The previous sibling of the reference topic.</summary>
    SiblingBefore,
}

/// <summary>
/// Adds a topic. The text may be empty: a new topic starts empty in edit mode, and the editing session's
/// <see cref="SetTopicText"/> commands share this command's <see cref="CoalesceKey"/>, so adding and typing are
/// one undo step.
/// </summary>
/// <param name="Side">For a new main topic added to the central topic: the side, e.g. the shorter one, which only the
/// app can measure. Without it, the side with fewer main topics. A new sibling of a main topic takes that topic's
/// side.</param>
public sealed record AddTopic(MapId Map, TopicId Reference, NewTopicPlacement Placement, TopicId NewId, string Text = "", object? CoalesceKey = null, MainSide? Side = null)
    : IDocumentCommand
{
    public CommandKind Kind => CommandKind.AddTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Topic(document, Map, Reference, out Map map, out TopicLocation reference, out CommandResult refusal))
        {
            return refusal;
        }

        if (Resolve.IdInUse(document, NewId.Value))
        {
            return CommandResult.Refused(Refusal.IdInUse);
        }

        var topic = new Topic(NewId, Text);
        if (TreeEdit.TooDeep(Placement == NewTopicPlacement.Subtopic ? reference.Depth : reference.Depth - 1, topic))
        {
            return CommandResult.Refused(Refusal.TooDeep);
        }

        Map changed;
        if (Placement == NewTopicPlacement.Subtopic)
        {
            if (reference.Parent is null)
            {
                topic = topic with { Side = Side ?? MainSides.Fewer(map) };
            }

            changed = TreeEdit.Insert(map, Reference, int.MaxValue, [topic]);
        }
        else
        {
            // The central topic has no siblings. Mind-mapping apps add a main topic instead; the app decides that by
            // sending Subtopic, so the model stays strict.
            if (reference.Parent is not { } parent)
            {
                return CommandResult.Refused(Refusal.CentralTopic);
            }

            // On the left side the list runs bottom to top: "after" (below on screen) is earlier in the list.
            bool mainOnLeft = parent == map.Root.Id && MainSides.Of(map, reference.Topic) == MainSide.Left;
            bool below = Placement == NewTopicPlacement.SiblingAfter;
            int index = reference.IndexInParent + (below != mainOnLeft ? 1 : 0);
            if (parent == map.Root.Id)
            {
                topic = topic with { Side = MainSides.Of(map, reference.Topic) };
            }

            changed = TreeEdit.Insert(map, parent, index, [topic]);
        }

        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, NewId));
    }
}

/// <summary>Inserts a new topic between <see cref="Topic"/> and its parent ("add a parent").</summary>
public sealed record InsertParent(MapId Map, TopicId Topic, TopicId NewId, string Text = "", object? CoalesceKey = null)
    : IDocumentCommand
{
    public CommandKind Kind => CommandKind.InsertParent;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.NonCentralTopic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (Resolve.IdInUse(document, NewId.Value))
        {
            return CommandResult.Refused(Refusal.IdInUse);
        }

        // The new parent takes the topic's place, so the topic and everything below it move one level down.
        if (TreeEdit.TooDeep(location.Depth, location.Topic))
        {
            return CommandResult.Refused(Refusal.TooDeep);
        }

        // A new main topic takes the side of the main topic it now holds.
        var newParent = new Model.Topic(NewId, Text)
        {
            Children = [location.Topic with { Side = null }],
            Side = location.Parent == map.Root.Id ? MainSides.Of(map, location.Topic) : null,
        };
        Map changed = TreeEdit.Update(map, location.Parent!.Value, parent => parent with
        {
            Children = parent.Children.SetItem(location.IndexInParent, newParent),
        });

        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, NewId));
    }
}

/// <summary>
/// Deletes a topic with its whole subtree and every relationship attached to any of them. No
/// confirmation: it can be undone. The selection moves to the next sibling, else the previous one,
/// else the parent.
/// </summary>
public sealed record DeleteTopic(MapId Map, TopicId Topic) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.DeleteTopic;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.NonCentralTopic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        Model.Topic parent = map.Find(location.Parent!.Value)!;
        TopicId next =
            location.IndexInParent + 1 < parent.Children.Length ? parent.Children[location.IndexInParent + 1].Id
            : location.IndexInParent > 0 ? parent.Children[location.IndexInParent - 1].Id
            : parent.Id;

        return CommandResult.Applied(document.ReplaceMap(TreeEdit.Remove(map, Topic)), new EditTarget(Map, next));
    }
}

/// <summary>
/// Sets a topic's text. Empty or whitespace-only text is refused; deleting a new topic whose
/// editing session ended empty is the app's job, with <see cref="DeleteTopic"/>.
/// </summary>
public sealed record SetTopicText(MapId Map, TopicId Topic, string Text, object? CoalesceKey = null) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.EditText;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(Text))
        {
            return CommandResult.Refused(Refusal.EmptyText);
        }

        if (!Resolve.Topic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (location.Topic.Text == Text)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map changed = TreeEdit.Update(map, Topic, topic => topic with { Text = Text });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }
}

/// <summary>Sets a topic's Markdown note, verbatim. Empty removes it.</summary>
public sealed record SetNote(MapId Map, TopicId Topic, string Note, object? CoalesceKey = null) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.EditNote;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(Note);

        if (!Resolve.Topic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        if (location.Topic.Note == Note)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        // Shortening a note that is already too long is allowed, so it can be brought back under the limit.
        if (Note.Length > Model.Topic.MaxNoteLength && Note.Length > location.Topic.Note.Length)
        {
            return CommandResult.Refused(Refusal.NoteTooLong);
        }

        Map changed = TreeEdit.Update(map, Topic, topic => topic with { Note = Note });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }
}

/// <summary>
/// Sets or toggles a marker: setting the marker a topic already has removes it; setting another
/// value of the same group replaces it. <see cref="Value"/> null removes the group's marker.
/// </summary>
public sealed record SetMarker(MapId Map, TopicId Topic, string Group, string? Value) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.SetMarker;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Value is not null && !MarkerGroups.IsKnown(new Marker(Group, Value)))
        {
            return CommandResult.Refused(Refusal.UnknownMarker);
        }

        if (!Resolve.Topic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        Marker? current = location.Topic.MarkerOf(Group);
        string? next = current?.Value == Value ? null : Value;

        if (current is null && next is null)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map changed = TreeEdit.Update(map, Topic, topic => topic with { Markers = WithMarker(topic, Group, next) });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }

    /// <summary>The topic's markers with <paramref name="group"/> set to <paramref name="value"/> (or removed), sorted
    /// by group.</summary>
    internal static System.Collections.Immutable.ImmutableArray<Marker> WithMarker(Model.Topic topic, string group, string? value)
    {
        IEnumerable<Marker> others = topic.Markers.Where(marker => marker.Group != group);
        IEnumerable<Marker> all = value is null ? others : others.Append(new Marker(group, value));
        return [.. all.OrderBy(marker => MarkerGroups.OrderOf(marker.Group)).ThenBy(marker => marker.Group, StringComparer.Ordinal)];
    }
}

/// <summary>
/// Moves a group's marker one step forward or back through its values, wrapping through "none"
/// (Alt+Ctrl+P for progress).
/// </summary>
public sealed record CycleMarker(MapId Map, TopicId Topic, string Group, bool Forward = true) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.SetMarker;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (MarkerGroups.ValuesOf(Group) is not { } values)
        {
            return CommandResult.Refused(Refusal.UnknownMarker);
        }

        if (!Resolve.Topic(document, Map, Topic, out Map map, out TopicLocation location, out CommandResult refusal))
        {
            return refusal;
        }

        // Positions: -1 is "none", then each value. Forward from none is the first value.
        int position = location.Topic.MarkerOf(Group) is { } marker ? IndexOf(values, marker.Value) : -1;
        int count = values.Count + 1;
        int next = ((position + 1 + (Forward ? 1 : -1) + count) % count) - 1;
        string? value = next < 0 ? null : values[next];

        Map changed = TreeEdit.Update(map, Topic, topic => topic with { Markers = SetMarker.WithMarker(topic, Group, value) });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
