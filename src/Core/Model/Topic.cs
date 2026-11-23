using System.Collections.Immutable;

namespace MindBuzz.Core.Model;

/// <summary>
/// One topic: text, ordered subtopics, a collapsed flag, internet links, a Markdown note and markers. Immutable: an
/// edit produces a new topic and new ancestors up to the
/// root, while every untouched subtree is shared by reference with the previous document. That sharing is what
/// makes undo snapshots cheap and lets the layout engine skip unchanged subtrees.
/// <para>
/// Equality is by reference for the collections inside (it is a record over <see cref="ImmutableArray{T}"/>).
/// Use <see cref="DocumentEquality"/> to compare two documents structurally.
/// </para>
/// </summary>
public sealed record Topic
{
    public Topic(TopicId id, string text)
    {
        Id = id;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public TopicId Id { get; init; }

    /// <summary>Plain text; may contain line breaks (<c>\n</c>). Never null.</summary>
    public string Text { get; init; }

    public ImmutableArray<Topic> Children { get; init; } = [];

    /// <summary>True when the subtopics are hidden. Saved in the file.</summary>
    public bool IsCollapsed { get; init; }

    public ImmutableArray<InternetLink> Links { get; init; } = [];

    /// <summary>
    /// The most characters an edit may give a note: a note is a note, not a book, and the editor's
    /// colouring and the preview redo the whole note on every keystroke. A longer note from a file or a paste is kept
    /// and can be shortened.
    /// </summary>
    public const int MaxNoteLength = 32_000;

    /// <summary>The Markdown note, stored verbatim. Empty when the topic has none.</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>At most one marker per group, in group order.</summary>
    public ImmutableArray<Marker> Markers { get; init; } = [];

    /// <summary>
    /// For a main topic, the side of the central topic it stays on (sides are
    /// sticky). Saved in the file. Null on other topics, and on main topics of files that predate it, which
    /// <see cref="MainSides.Fill"/> settles when they are loaded.
    /// </summary>
    public MainSide? Side { get; init; }

    public bool HasChildren => !Children.IsEmpty;

    /// <summary>The topic and every descendant, depth first, parent before children (document order).</summary>
    public IEnumerable<Topic> DescendantsAndSelf()
    {
        var stack = new Stack<Topic>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            Topic topic = stack.Pop();
            yield return topic;

            for (int i = topic.Children.Length - 1; i >= 0; i--)
            {
                stack.Push(topic.Children[i]);
            }
        }
    }

    /// <summary>The marker of <paramref name="group"/>, or null.</summary>
    public Marker? MarkerOf(string group)
    {
        foreach (Marker marker in Markers)
        {
            if (marker.Group == group)
            {
                return marker;
            }
        }

        return null;
    }
}
