using System.Text.RegularExpressions;

namespace MindBuzz.App.Notes;

/// <summary>What a stretch of Markdown source is, for the editor's syntax colouring.</summary>
internal enum MarkdownSpanKind
{
    Heading,
    Emphasis,
    Strong,
    Strikethrough,
    Code,
    Fence,
    Quote,
    ListMarker,
    Link,
}

/// <summary>One coloured stretch of the note's source.</summary>
internal readonly record struct MarkdownSpan(int Start, int Length, MarkdownSpanKind Kind);

/// <summary>
/// The editor's basic Markdown colouring: a line-by-line scan, not a parse. It knows fenced code blocks across
/// lines, and headings, quotes, list markers, emphasis, inline code and links within a line. Pure, so it is
/// tested without an editor; the colouring transformer only paints what this returns.
/// </summary>
internal static partial class MarkdownSyntax
{
    internal static IReadOnlyList<MarkdownSpan> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var spans = new List<MarkdownSpan>();
        string? fence = null;
        int offset = 0;

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimStart(' ');
            bool fenceLine = trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);

            if (fence is not null)
            {
                spans.Add(new MarkdownSpan(offset, line.Length, MarkdownSpanKind.Fence));
                if (fenceLine && trimmed.StartsWith(fence, StringComparison.Ordinal))
                {
                    fence = null;
                }
            }
            else if (fenceLine)
            {
                fence = trimmed[..3];
                spans.Add(new MarkdownSpan(offset, line.Length, MarkdownSpanKind.Fence));
            }
            else
            {
                ScanLine(line, offset, spans);
            }

            offset += line.Length + 1;
        }

        return spans;
    }

    private static void ScanLine(string line, int offset, List<MarkdownSpan> spans)
    {
        if (HeadingPattern().IsMatch(line))
        {
            spans.Add(new MarkdownSpan(offset, line.TrimEnd('\r').Length, MarkdownSpanKind.Heading));
            return;
        }

        if (QuotePattern().IsMatch(line))
        {
            spans.Add(new MarkdownSpan(offset, line.TrimEnd('\r').Length, MarkdownSpanKind.Quote));
            return;
        }

        if (ListPattern().Match(line) is { Success: true } list)
        {
            Group marker = list.Groups["marker"];
            spans.Add(new MarkdownSpan(offset + marker.Index, marker.Length, MarkdownSpanKind.ListMarker));
        }

        // Code first: emphasis markers inside `code` are literal.
        var code = new List<(int Start, int End)>();
        foreach (Match match in CodePattern().Matches(line))
        {
            spans.Add(new MarkdownSpan(offset + match.Index, match.Length, MarkdownSpanKind.Code));
            code.Add((match.Index, match.Index + match.Length));
        }

        bool InCode(Match match) => code.Exists(c => match.Index < c.End && match.Index + match.Length > c.Start);

        foreach ((Regex pattern, MarkdownSpanKind kind) in Inline)
        {
            foreach (Match match in pattern.Matches(line))
            {
                if (!InCode(match))
                {
                    spans.Add(new MarkdownSpan(offset + match.Index, match.Length, kind));
                }
            }
        }
    }

    private static readonly (Regex Pattern, MarkdownSpanKind Kind)[] Inline =
    [
        (StrongPattern(), MarkdownSpanKind.Strong),
        (EmphasisPattern(), MarkdownSpanKind.Emphasis),
        (StrikePattern(), MarkdownSpanKind.Strikethrough),
        (LinkPattern(), MarkdownSpanKind.Link),
    ];

    [GeneratedRegex(@"^ {0,3}#{1,6}(\s|$)")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^ {0,3}>")]
    private static partial Regex QuotePattern();

    [GeneratedRegex(@"^\s*(?<marker>[-*+]|\d{1,9}[.)])(\s+\[[ xX]\])?(?=\s)")]
    private static partial Regex ListPattern();

    [GeneratedRegex(@"`[^`\n]+`")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"(\*\*|__)(?=\S)[^\n]*?\S\1")]
    private static partial Regex StrongPattern();

    [GeneratedRegex(@"(?<![*_\w])([*_])(?![*_\s])[^\n*_]*?[^\s*_]\1(?![*_\w])")]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex(@"~~(?=\S)[^\n]*?\S~~")]
    private static partial Regex StrikePattern();

    [GeneratedRegex(@"!?\[[^\]\n]*\]\([^)\n]*\)|<(https?|mailto):[^>\s]+>")]
    private static partial Regex LinkPattern();
}

/// <summary>Ctrl/⌘+B and Ctrl/⌘+I in the notes editor. Pure, so it is tested without an editor.</summary>
internal static partial class MarkdownEdit
{
    /// <summary>
    /// Wraps text in <paramref name="marker"/> ("**" bold, "*" italic), or unwraps it when it is already wrapped, so
    /// the key toggles:
    /// <list type="bullet">
    /// <item>a selection: exactly the selected text, without the spaces at its ends, which Markdown would not
    /// format; over several lines, each line on its own, after its list, quote or heading marker, because emphasis
    /// does not reach across paragraphs;</item>
    /// <item>no selection: the word the caret is in, and the caret stays where it was in the word;</item>
    /// <item>no selection and no word: a pair of markers with the caret between.</item>
    /// </list>
    /// </summary>
    /// <returns>The new text and the new selection.</returns>
    internal static (string Text, int SelectionStart, int SelectionLength) Toggle(string text, int start, int length, string marker)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(marker);

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);

        if (length > 0 && text.AsSpan(start, length).Contains('\n'))
        {
            return ToggleLines(text, start, length, marker);
        }

        // Spaces at the ends of a selection are left out: "**word **" is not bold in Markdown.
        int trimmedStart = start, trimmedEnd = start + length;
        while (trimmedStart < trimmedEnd && char.IsWhiteSpace(text[trimmedStart]))
        {
            trimmedStart++;
        }

        while (trimmedEnd > trimmedStart && char.IsWhiteSpace(text[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd > trimmedStart)
        {
            return ToggleRange(text, trimmedStart, trimmedEnd - trimmedStart, marker);
        }

        // No selection (or only spaces): the word around the caret.
        int caret = start;
        (int wordStart, int wordEnd) = WordAt(text, caret);
        if (wordEnd > wordStart)
        {
            (string toggled, int innerStart, _) = ToggleRange(text, wordStart, wordEnd - wordStart, marker);
            return (toggled, caret + (innerStart - wordStart), 0);
        }

        string pair = text.Insert(caret, marker + marker);
        return (pair, caret + marker.Length, 0);
    }

    /// <summary>Wraps or unwraps one stretch of a single line; the new selection is the stretch without
    /// markers.</summary>
    private static (string Text, int SelectionStart, int SelectionLength) ToggleRange(string text, int start, int length, string marker)
    {
        int m = marker.Length;

        // Already wrapped around the stretch: "**word**" with "word" selected.
        if (start >= m && start + length + m <= text.Length
            && string.CompareOrdinal(text, start - m, marker, 0, m) == 0
            && string.CompareOrdinal(text, start + length, marker, 0, m) == 0
            && !(m == 1 && IsDoubled(text, start - m, start + length, marker[0])))
        {
            string unwrapped = text.Remove(start + length, m).Remove(start - m, m);
            return (unwrapped, start - m, length);
        }

        // The stretch includes the markers: "**word**" selected.
        string selected = text.Substring(start, length);
        if (IsWrapped(selected, marker))
        {
            string inner = selected[m..^m];
            return (text.Remove(start, length).Insert(start, inner), start, inner.Length);
        }

        string wrapped = text.Insert(start + length, marker).Insert(start, marker);
        return (wrapped, start + m, length);
    }

    /// <summary>
    /// A selection over several lines: each line's text is wrapped on its own, or all are unwrapped when every one
    /// already is. The new selection covers the same lines.
    /// </summary>
    private static (string Text, int SelectionStart, int SelectionLength) ToggleLines(string text, int start, int length, string marker)
    {
        int end = start + length;
        var parts = new List<(int Start, int End)>();
        for (int lineStart = start; lineStart < end;)
        {
            int newline = text.IndexOf('\n', lineStart, end - lineStart);
            int lineEnd = newline < 0 ? end : newline;

            int from = lineStart, to = lineEnd;
            if (from == 0 || text[from - 1] == '\n')
            {
                from += BlockPrefix().Match(text, from, to - from).Length;
            }

            while (from < to && char.IsWhiteSpace(text[from]))
            {
                from++;
            }

            while (to > from && char.IsWhiteSpace(text[to - 1]))
            {
                to--;
            }

            if (to > from)
            {
                parts.Add((from, to));
            }

            lineStart = lineEnd + 1;
        }

        if (parts.Count == 0)
        {
            return (text, start, length);
        }

        bool unwrap = parts.TrueForAll(part => IsWrapped(text[part.Start..part.End], marker));
        int m = marker.Length;
        string result = text;
        int delta = 0;
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            (int from, int to) = parts[i];
            if (unwrap)
            {
                result = result.Remove(to - m, m).Remove(from, m);
                delta -= 2 * m;
            }
            else if (!IsWrapped(text[from..to], marker))
            {
                result = result.Insert(to, marker).Insert(from, marker);
                delta += 2 * m;
            }
        }

        return (result, start, length + delta);
    }

    /// <summary>
    /// The word around <paramref name="caret"/>: letters, digits and underscores, with an apostrophe or hyphen
    /// inside it ("don't", "e-mail"). Empty when the caret touches no word.
    /// </summary>
    private static (int Start, int End) WordAt(string text, int caret)
    {
        static bool Letter(char c) => char.IsLetterOrDigit(c) || c == '_';
        static bool Joiner(char c) => c is '\'' or '’' or '-';

        int wordStart = caret, wordEnd = caret;
        while (wordStart > 0 && (Letter(text[wordStart - 1]) || (Joiner(text[wordStart - 1]) && wordStart - 1 > 0 && Letter(text[wordStart - 2]) && wordStart < text.Length && Letter(text[wordStart]))))
        {
            wordStart--;
        }

        while (wordEnd < text.Length && (Letter(text[wordEnd]) || (Joiner(text[wordEnd]) && wordEnd > 0 && Letter(text[wordEnd - 1]) && wordEnd + 1 < text.Length && Letter(text[wordEnd + 1]))))
        {
            wordEnd++;
        }

        return (wordStart, wordEnd);
    }

    /// <summary>True when <paramref name="stretch"/> starts and ends with the marker, and is not merely bold for
    /// "*".</summary>
    private static bool IsWrapped(string stretch, string marker)
    {
        int m = marker.Length;
        return stretch.Length >= 2 * m && stretch.StartsWith(marker, StringComparison.Ordinal) && stretch.EndsWith(marker, StringComparison.Ordinal)
            && !(m == 1 && stretch.Length > 2 && stretch[1] == marker[0] && stretch[^2] == marker[0]);
    }

    /// <summary>True when a single marker is really half of a double one ("**bold**" is not italic).</summary>
    private static bool IsDoubled(string text, int before, int after, char marker) =>
        (before > 0 && text[before - 1] == marker) || (after + 1 < text.Length && text[after + 1] == marker);

    /// <summary>What starts a line before its text: indentation, list and task markers, quote markers, a
    /// heading.</summary>
    [GeneratedRegex(@"\G[ \t]*(?:(?:[-*+]|\d{1,9}[.)])[ \t]+(?:\[[ xX]\][ \t]+)?|>[ \t]?|#{1,6}[ \t]+)*")]
    private static partial Regex BlockPrefix();
}
