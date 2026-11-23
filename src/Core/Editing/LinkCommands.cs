using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>Adds an internet link at the end of a topic's list. Disallowed schemes are refused.</summary>
public sealed record AddLink(MapId Map, TopicId Topic, InternetLink Link) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.AddLink;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(Link);

        if (!LinkUrl.IsAllowed(Link.Url))
        {
            return CommandResult.Refused(Refusal.UrlNotAllowed);
        }

        if (!Resolve.Topic(document, Map, Topic, out Map map, out _, out CommandResult refusal))
        {
            return refusal;
        }

        Map changed = TreeEdit.Update(map, Topic, topic => topic with { Links = topic.Links.Add(Link) });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }
}

/// <summary>Replaces the link at <see cref="Index"/>.</summary>
public sealed record EditLink(MapId Map, TopicId Topic, int Index, InternetLink Link) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.EditLink;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(Link);

        if (!LinkUrl.IsAllowed(Link.Url))
        {
            return CommandResult.Refused(Refusal.UrlNotAllowed);
        }

        if (!LinkEdit.Find(document, Map, Topic, Index, out Map map, out Topic topic, out CommandResult refusal))
        {
            return refusal;
        }

        if (topic.Links[Index] == Link)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map changed = TreeEdit.Update(map, Topic, t => t with { Links = t.Links.SetItem(Index, Link) });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }
}

public sealed record RemoveLink(MapId Map, TopicId Topic, int Index) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.RemoveLink;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!LinkEdit.Find(document, Map, Topic, Index, out Map map, out _, out CommandResult refusal))
        {
            return refusal;
        }

        Map changed = TreeEdit.Update(map, Topic, t => t with { Links = t.Links.RemoveAt(Index) });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }
}

/// <summary>Moves a link within the topic's list (the link list's Alt+Up/Down).</summary>
public sealed record MoveLink(MapId Map, TopicId Topic, int Index, int NewIndex) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveLink;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!LinkEdit.Find(document, Map, Topic, Index, out Map map, out Topic topic, out CommandResult refusal))
        {
            return refusal;
        }

        if (NewIndex < 0 || NewIndex >= topic.Links.Length)
        {
            return CommandResult.Refused(Refusal.NoPlaceToMove);
        }

        if (NewIndex == Index)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        InternetLink link = topic.Links[Index];
        Map changed = TreeEdit.Update(map, Topic, t => t with { Links = t.Links.RemoveAt(Index).Insert(NewIndex, link) });
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, Topic));
    }
}

internal static class LinkEdit
{
    internal static bool Find(Document document, MapId mapId, TopicId topicId, int index, out Map map, out Topic topic, out CommandResult refusal)
    {
        topic = null!;
        if (!Resolve.Topic(document, mapId, topicId, out map, out TopicLocation location, out refusal))
        {
            return false;
        }

        topic = location.Topic;
        if (index < 0 || index >= topic.Links.Length)
        {
            refusal = CommandResult.Refused(Refusal.LinkNotFound);
            return false;
        }

        return true;
    }
}
