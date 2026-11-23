using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>
/// Sets a relationship's colour or line pattern; a null part stays as it is. The current value is
/// refused as no change.
/// </summary>
public sealed record SetRelationshipStyle(MapId Map, RelationshipId Relationship, RelationshipColor? Color = null, RelationshipLine? Line = null) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.ChangeRelationship;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Map(document, Map, out Map map, out CommandResult refusal))
        {
            return refusal;
        }

        int index = map.Relationships.IndexOf(map.Relationships.FirstOrDefault(r => r.Id == Relationship)!);
        if (index < 0)
        {
            return CommandResult.Refused(Refusal.RelationshipNotFound);
        }

        Relationship current = map.Relationships[index];
        Relationship changed = current with { Color = Color ?? current.Color, Line = Line ?? current.Line };
        if (changed == current)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        return CommandResult.Applied(
            document.ReplaceMap(map with { Relationships = map.Relationships.SetItem(index, changed) }),
            new EditTarget(Map, current.From, Relationship));
    }
}

/// <summary>
/// Links two topics of the same map. A topic linked to itself, or two topics that are already
/// linked (in either direction), are refused.
/// </summary>
public sealed record AddRelationship(MapId Map, RelationshipId NewId, TopicId From, TopicId To, RelationshipDirection Direction)
    : IDocumentCommand
{
    public CommandKind Kind => CommandKind.AddRelationship;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Resolve.Map(document, Map, out Map map, out CommandResult refusal))
        {
            return refusal;
        }

        if (!map.Contains(From) || !map.Contains(To))
        {
            return CommandResult.Refused(Refusal.TopicNotFound);
        }

        if (From == To)
        {
            return CommandResult.Refused(Refusal.SelfRelationship);
        }

        if (map.Relationships.Any(r => r.Connects(From, To)))
        {
            return CommandResult.Refused(Refusal.DuplicateRelationship);
        }

        if (Resolve.IdInUse(document, NewId.Value))
        {
            return CommandResult.Refused(Refusal.IdInUse);
        }

        Map changed = map with { Relationships = map.Relationships.Add(new Relationship(NewId, From, To, Direction)) };
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, From, NewId));
    }
}

public sealed record DeleteRelationship(MapId Map, RelationshipId Relationship) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.DeleteRelationship;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!RelationshipEdit.Find(document, Map, Relationship, out Map map, out int index, out CommandResult refusal))
        {
            return refusal;
        }

        // The selection returns to the relationship's source topic ("Esc returns to the topic").
        TopicId from = map.Relationships[index].From;
        Map changed = map with { Relationships = map.Relationships.RemoveAt(index) };
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, from));
    }
}

/// <summary>Switches one-way and two-way, or reverses a relationship's direction.</summary>
public sealed record ChangeRelationship(MapId Map, RelationshipId Relationship, RelationshipChange Change) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.ChangeRelationship;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!RelationshipEdit.Find(document, Map, Relationship, out Map map, out int index, out CommandResult refusal))
        {
            return refusal;
        }

        Relationship current = map.Relationships[index];
        Relationship next = Change switch
        {
            RelationshipChange.MakeOneWay => current with { Direction = RelationshipDirection.OneWay },
            RelationshipChange.MakeTwoWay => current with { Direction = RelationshipDirection.TwoWay },
            RelationshipChange.ToggleTwoWay => current with
            {
                Direction = current.Direction == RelationshipDirection.OneWay ? RelationshipDirection.TwoWay : RelationshipDirection.OneWay,
            },
            _ => current with { From = current.To, To = current.From, Bend = current.Bend?.Reversed() },
        };

        if (next == current)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map changed = map with { Relationships = map.Relationships.SetItem(index, next) };
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, next.From, Relationship));
    }
}

/// <summary>Sets a relationship's dragged shape, or (null) returns it to the automatic one.</summary>
public sealed record SetRelationshipBend(MapId Map, RelationshipId Relationship, RelationshipBend? Bend) : IDocumentCommand
{
    public CommandKind Kind => CommandKind.ChangeRelationship;

    public CommandResult Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!RelationshipEdit.Find(document, Map, Relationship, out Map map, out int index, out CommandResult refusal))
        {
            return refusal;
        }

        Relationship current = map.Relationships[index];
        if (current.Bend == Bend)
        {
            return CommandResult.Refused(Refusal.NoChange);
        }

        Map changed = map with { Relationships = map.Relationships.SetItem(index, current with { Bend = Bend }) };
        return CommandResult.Applied(document.ReplaceMap(changed), new EditTarget(Map, current.From, Relationship));
    }
}

public enum RelationshipChange
{
    MakeOneWay,
    MakeTwoWay,
    ToggleTwoWay,
    Reverse,
}

internal static class RelationshipEdit
{
    internal static bool Find(Document document, MapId mapId, RelationshipId id, out Map map, out int index, out CommandResult refusal)
    {
        index = -1;
        if (!Resolve.Map(document, mapId, out map, out refusal))
        {
            return false;
        }

        for (int i = 0; i < map.Relationships.Length; i++)
        {
            if (map.Relationships[i].Id == id)
            {
                index = i;
                return true;
            }
        }

        refusal = CommandResult.Refused(Refusal.RelationshipNotFound);
        return false;
    }
}
