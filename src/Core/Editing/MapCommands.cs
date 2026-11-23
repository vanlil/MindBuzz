using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>Adds a map with only a central topic, at the end of the tabs (the + button).</summary>
public sealed record AddMap(MapId NewId, TopicId CentralId, string CentralText) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.AddMap;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(CentralText))
        {
            return CommandResult.Refused(Refusal.EmptyText);
        }

        if (Resolve.IdInUse(document, NewId.Value) || Resolve.IdInUse(document, CentralId.Value) || NewId.Value == CentralId.Value)
        {
            return CommandResult.Refused(Refusal.IdInUse);
        }

        var map = new Map(NewId, new Topic(CentralId, CentralText));
        return CommandResult.Applied(document with { Maps = document.Maps.Add(map) }, new EditTarget(NewId, CentralId));
    }
}

/// <summary>
/// Renames a map, which means setting its central topic's text: the two are always the same.
/// Empty names are refused.
/// </summary>
public sealed record RenameMap(MapId Map, string Name) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.RenameMap;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(Name))
        {
            return CommandResult.Refused(Refusal.EmptyName);
        }

        if (!Resolve.Map(document, Map, out Map map, out CommandResult refusal))
        {
            return refusal;
        }

        if (map.Root.Text == Name)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        return CommandResult.Applied(document.ReplaceMap(map with { Root = map.Root with { Text = Name } }), new EditTarget(Map, map.Root.Id));
    }
}

/// <summary>
/// Deletes a map. The last remaining map cannot be deleted. The next tab becomes active, or the
/// previous one when the last tab was deleted.
/// </summary>
public sealed record DeleteMap(MapId Map) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.DeleteMap;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        int index = document.IndexOfMap(Map);
        if (index < 0)
        {
            return CommandResult.Refused(Refusal.MapNotFound);
        }

        if (document.Maps.Length == 1)
        {
            return CommandResult.Refused(Refusal.LastMap);
        }

        var maps = document.Maps.RemoveAt(index);
        Map next = maps[Math.Min(index, maps.Length - 1)];
        return CommandResult.Applied(document with { Maps = maps }, new EditTarget(next.Id, next.Root.Id));
    }
}

/// <summary>Moves a map's tab to a new position (drag or keyboard).</summary>
public sealed record MoveMap(MapId Map, int NewIndex) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.MoveMap;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        int index = document.IndexOfMap(Map);
        if (index < 0)
        {
            return CommandResult.Refused(Refusal.MapNotFound);
        }

        if (NewIndex < 0 || NewIndex >= document.Maps.Length)
        {
            return CommandResult.Refused(Refusal.NoPlaceToMove);
        }

        if (NewIndex == index)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map map = document.Maps[index];
        return CommandResult.Applied(
            document with { Maps = document.Maps.RemoveAt(index).Insert(NewIndex, map) },
            new EditTarget(Map, map.Root.Id));
    }
}
