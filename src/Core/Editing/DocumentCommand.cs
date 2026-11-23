using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>
/// What a command does, for the app's "Undo …" labels (the words live in the app's <c>Strings.resx</c>).
/// </summary>
public enum CommandKind
{
    AddTopic,
    InsertParent,
    DeleteTopic,
    EditText,
    MoveTopic,
    Collapse,
    Paste,
    AddRelationship,
    DeleteRelationship,
    ChangeRelationship,
    AddLink,
    EditLink,
    RemoveLink,
    MoveLink,
    EditNote,
    SetMarker,
    AddMap,
    RenameMap,
    DeleteMap,
    MoveMap,
}

/// <summary>Why a command did not change the document. The app turns these into short messages.</summary>
public enum Refusal
{
    None,

    /// <summary>The command would not change anything, e.g. collapsing a collapsed topic. Not recorded in
    /// history.</summary>
    NoChange,

    MapNotFound,
    TopicNotFound,
    RelationshipNotFound,
    LinkNotFound,

    /// <summary>The central topic cannot be moved, deleted, or given a sibling or parent.</summary>
    CentralTopic,

    EmptyText,
    EmptyName,

    /// <summary>A topic cannot be moved into its own subtree.</summary>
    IntoOwnSubtree,

    /// <summary>Already first, already last, nothing to promote into, and so on.</summary>
    NoPlaceToMove,

    SelfRelationship,
    DuplicateRelationship,
    UrlNotAllowed,
    UnknownMarker,

    /// <summary>The note would grow beyond <see cref="Model.Topic.MaxNoteLength"/>.</summary>
    NoteTooLong,

    /// <summary>Topics would be nested deeper than <see cref="Serialization.MbzLimits.MaxTopicDepth"/> (add, add
    /// parent, move, paste).</summary>
    TooDeep,

    /// <summary>The last remaining map cannot be deleted.</summary>
    LastMap,

    /// <summary>An id the command brings in already exists in the document.</summary>
    IdInUse,
}

/// <summary>What the view should show after a change: a map, and in it a topic or a relationship.</summary>
public readonly record struct EditTarget(MapId Map, TopicId? Topic = null, RelationshipId? Relationship = null);

/// <summary>The outcome of applying a command.</summary>
public sealed record CommandResult
{
    private CommandResult(Document? after, Refusal refusal, EditTarget? target)
    {
        After = after;
        Refusal = refusal;
        Target = target;
    }

    /// <summary>The new document, or null when refused.</summary>
    public Document? After { get; }

    public Refusal Refusal { get; }

    /// <summary>Where the selection goes after the change. Null when refused.</summary>
    public EditTarget? Target { get; }

    public bool IsApplied => After is not null;

    public static CommandResult Applied(Document after, EditTarget target) => new(after, Refusal.None, target);

    public static CommandResult Refused(Refusal refusal) => new(null, refusal, null);
}

/// <summary>
/// A change to the document. Every change goes through one, and no view or view model mutates
/// the model directly. A command stores <em>intent</em> ("add a sibling after X") and resolves it against the
/// document it is applied to, so it never acts on stale positions.
/// </summary>
public interface IDocumentCommand
{
    CommandKind Kind { get; }

    /// <summary>
    /// Commands with the same non-null key, executed one after another, become one undo step: an editing
    /// session, a drag, a burst of note typing. The app makes a new key per session.
    /// </summary>
    object? CoalesceKey => null;

    CommandResult Apply(Document document);
}
