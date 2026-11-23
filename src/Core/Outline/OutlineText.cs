using System.Collections.Immutable;
using System.Text;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;

namespace MindBuzz.Core.Outline;

/// <summary>
/// The plain-text form of topics on the clipboard: one topic per line, indented with one tab
/// per level. Other apps get this when topics are copied; pasting indented text from another app creates a
/// subtree.
/// </summary>
public static class OutlineText
{
    /// <summary>
    /// Writes topics as an outline. A topic's line breaks become spaces, because a line is a topic. Notes, links
    /// and markers are not part of plain text; the private clipboard format carries them.
    /// </summary>
    public static string Write(IEnumerable<Topic> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var text = new StringBuilder();
        foreach (Topic root in roots)
        {
            Append(text, root, 0);
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, Topic topic, int depth)
    {
        text.Append('\t', depth).Append(OneLine(TitleMarkup.Plain(topic.Text))).Append('\n');
        foreach (Topic child in topic.Children)
        {
            Append(text, child, depth + 1);
        }
    }

    /// <summary>
    /// Reads an outline into topics. Indentation is relative: a line indented more than the line above is its
    /// child, whatever the amount, and tabs and spaces both count (a tab as four spaces). Empty lines are skipped,
    /// and a leading list marker ("- ", "* ", "• ") is removed, so Markdown lists and outlines from other apps
    /// paste as expected.
    /// </summary>
    public static ImmutableArray<Topic> Read(string text, IIdSource ids)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ids);

        // Built mutable, frozen at the end: Node is the outline's own scaffolding.
        var roots = new List<Node>();
        var stack = new Stack<(int Indent, Node Node)>();

        foreach (string raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            int indent = IndentOf(raw, out int contentStart);
            string content = StripListMarker(raw[contentStart..].TrimEnd());
            if (content.Length == 0)
            {
                continue;
            }

            var node = new Node(content);
            // A line deeper than the file format allows joins the deepest level instead (MbzLimits.MaxTopicDepth),
            // so a pasted staircase of indents never builds a tree too deep to save, or to walk.
            while (stack.Count > 0 && (stack.Peek().Indent >= indent || stack.Count >= MbzLimits.MaxTopicDepth))
            {
                stack.Pop();
            }

            (stack.Count == 0 ? roots : stack.Peek().Node.Children).Add(node);
            stack.Push((indent, node));
        }

        return [.. roots.Select(node => node.ToTopic(ids))];
    }

    private static string OneLine(string text) =>
        text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

    private static int IndentOf(string line, out int contentStart)
    {
        int indent = 0;
        int i = 0;
        for (; i < line.Length && (line[i] == ' ' || line[i] == '\t'); i++)
        {
            indent += line[i] == '\t' ? 4 : 1;
        }

        contentStart = i;
        return indent;
    }

    private static string StripListMarker(string content)
    {
        foreach (string marker in new[] { "- ", "* ", "• ", "+ " })
        {
            if (content.StartsWith(marker, StringComparison.Ordinal))
            {
                return content[marker.Length..].TrimStart();
            }
        }

        return content;
    }

    private sealed class Node(string text)
    {
        public List<Node> Children { get; } = [];

        public Topic ToTopic(IIdSource ids)
        {
            TopicId id = ids.NextTopic();
            return new Topic(id, text) { Children = [.. Children.Select(child => child.ToTopic(ids))] };
        }
    }
}
