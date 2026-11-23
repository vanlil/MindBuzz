using System.Collections.Immutable;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;

namespace MindBuzz.Core.Editing;

/// <summary>
/// Copied topics with everything in them: text, notes, links, markers, and the relationships
/// whose both ends are inside. This is the private clipboard format's content; the app serialises it as JSON
/// and also writes a plain-text outline for other apps.
/// </summary>
/// <param name="Roots">The copied subtrees, in order. One for a copy; several for a pasted text outline.</param>
/// <param name="Relationships">Relationships between topics inside <paramref name="Roots"/>.</param>
public sealed record Clip(ImmutableArray<Topic> Roots, ImmutableArray<Relationship> Relationships)
{
    /// <summary>Copies a topic's subtree from a map, with its internal relationships.</summary>
    public static Clip? Copy(Map map, TopicId topic)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (map.Find(topic) is not { } root)
        {
            return null;
        }

        HashSet<TopicId> inside = [.. root.DescendantsAndSelf().Select(t => t.Id)];
        return new Clip([root], [.. map.Relationships.Where(r => inside.Contains(r.From) && inside.Contains(r.To))]);
    }

    /// <summary>
    /// Copies several selected topics (multi-select): their subtrees in map order, a topic inside
    /// another selected one only once, and the relationships between any of them. Pasted, they come back side by side.
    /// </summary>
    /// <param name="withCentral">False for Cut, which cannot take the central topic: it is left out of the clip, as it
    /// stays in the map.</param>
    public static Clip? Copy(Map map, IReadOnlyCollection<TopicId> topics, bool withCentral = true)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(topics);

        List<TopicId> roots = withCentral && topics.Contains(map.Root.Id) ? [map.Root.Id] : SelectionRoots.Of(map, topics);
        if (roots.Count == 0)
        {
            return null;
        }

        ImmutableArray<Topic> subtrees = [.. roots.Select(id => map.Find(id)!)];
        HashSet<TopicId> inside = [.. subtrees.SelectMany(root => root.DescendantsAndSelf()).Select(t => t.Id)];
        return new Clip(subtrees, [.. map.Relationships.Where(r => inside.Contains(r.From) && inside.Contains(r.To))]);
    }

    /// <summary>
    /// The same content with new ids for every topic and relationship, so it can be pasted any number of times.
    /// Relationship ends are remapped to the new topic ids. A copied central topic pastes as an ordinary topic.
    /// </summary>
    public Clip WithFreshIds(IIdSource ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var map = new Dictionary<TopicId, TopicId>();

        Topic Renew(Topic topic)
        {
            TopicId id = ids.NextTopic();
            map[topic.Id] = id;
            return topic with { Id = id, Children = [.. topic.Children.Select(Renew)] };
        }

        ImmutableArray<Topic> roots = [.. Roots.Select(Renew)];
        ImmutableArray<Relationship> relationships =
        [
            .. Relationships
                .Where(r => map.ContainsKey(r.From) && map.ContainsKey(r.To))
                .Select(r => r with { Id = ids.NextRelationship(), From = map[r.From], To = map[r.To] }),
        ];

        return new Clip(roots, relationships);
    }
}

/// <summary>
/// Pastes a clip as the last subtopics of <see cref="Parent"/>. The clip must carry fresh ids
/// (<see cref="Clip.WithFreshIds"/>); ids already in the document are refused rather than silently renamed.
/// </summary>
/// <param name="Side">Pasted onto the central topic: the side the new main topics go to (the app passes the shorter);
/// without it, the side with fewer main topics.</param>
public sealed record PasteClip(MapId Map, TopicId Parent, Clip Clip, MainSide? Side = null) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.Paste;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(Clip);

        if (Clip.Roots.IsDefaultOrEmpty)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        if (!Resolve.Topic(document, Map, Parent, out Map map, out TopicLocation parent, out CommandResult refusal))
        {
            return refusal;
        }

        if (Clip.Roots.Any(root => TreeEdit.TooDeep(parent.Depth, root)))
        {
            return CommandResult.Refused(Refusal.TooDeep);
        }

        IEnumerable<string> incoming = Clip.Roots
            .SelectMany(root => root.DescendantsAndSelf().Select(topic => topic.Id.Value))
            .Concat(Clip.Relationships.Select(r => r.Id.Value));

        if (incoming.Any(id => Resolve.IdInUse(document, id)))
        {
            return CommandResult.Refused(Refusal.IdInUse);
        }

        MainSide side = Side ?? MainSides.Fewer(map);
        var roots = Clip.Roots.Select(root => root with { Side = Parent == map.Root.Id ? side : null }).ToImmutableArray();
        Map changed = TreeEdit.Insert(map, Parent, int.MaxValue, roots);
        changed = changed with { Relationships = changed.Relationships.AddRange(Clip.Relationships) };
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Clip.Roots[0].Id));
    }
}
