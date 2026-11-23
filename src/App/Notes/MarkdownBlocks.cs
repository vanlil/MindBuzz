using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MindBuzz.App.Notes;

/// <summary>A line kind the notes toolbar switches on and off.</summary>
internal enum MarkdownLineKind
{
    Bullet,
    Numbered,
    Quote,
}

/// <summary>
/// The notes toolbar's line and block edits: headings, bulleted and numbered lists, quotes, links and tables. Pure,
/// like
/// <see cref="MarkdownEdit.Toggle"/>: each takes the note and the selection and returns the new note and selection.
/// </summary>
internal static partial class MarkdownEdit
{
    /// <summary>
    /// The heading button: the lines of the selection (or the caret's line) go plain → H1 → H2 → H3 → plain, all to
    /// the level after the first line's.
    /// </summary>
    internal static (string Text, int SelectionStart, int SelectionLength) CycleHeading(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<(int Start, int End)> lines = TargetLines(text, ref start, ref length);
        int level = HeadingPrefix().Match(text, lines[0].Start, lines[0].End - lines[0].Start) is { Success: true } first
            ? first.Groups[1].Length
            : 0;
        int next = level is 0 ? 1 : level < 3 ? level + 1 : 0;
        string prefix = next == 0 ? string.Empty : new string('#', next) + " ";

        return EditLines(text, start, length, lines, (line, _) =>
        {
            Match heading = HeadingPrefix().Match(line);
            return prefix + (heading.Success ? line[heading.Length..] : line);
        });
    }

    /// <summary>
    /// The list and quote buttons: on for every line of the selection (or the caret's line), or off when every one
    /// already is that kind. A bullet line becomes numbered and so on, keeping its indentation; numbers count from 1.
    /// </summary>
    internal static (string Text, int SelectionStart, int SelectionLength) ToggleLineKind(string text, int start, int length, MarkdownLineKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<(int Start, int End)> lines = TargetLines(text, ref start, ref length);
        List<(int Start, int End)> filled = lines.Where(line => line.End > line.Start && !string.IsNullOrWhiteSpace(text[line.Start..line.End])).ToList();
        bool off = filled.Count > 0 && filled.TrueForAll(line => KindOf(text[line.Start..line.End], kind));

        int number = 0;
        return EditLines(text, start, length, lines, (line, _) =>
        {
            if (string.IsNullOrWhiteSpace(line) && filled.Count > 0)
            {
                return line;
            }

            if (kind == MarkdownLineKind.Quote)
            {
                Match quote = QuotePrefix().Match(line);
                return off ? line[quote.Length..] : "> " + line;
            }

            Match list = ListPrefix().Match(line);
            string indent = list.Groups[1].Value;
            string rest = list.Success ? line[list.Length..] : line.TrimStart(' ', '\t');
            if (!list.Success)
            {
                indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            }

            if (off)
            {
                return indent + rest;
            }

            number++;
            string marker = kind == MarkdownLineKind.Bullet ? "- " : number.ToString(CultureInfo.InvariantCulture) + ". ";
            return indent + marker + rest;
        });
    }

    /// <summary>
    /// The link button: the selection becomes the link's text with the address to type selected; a selected address
    /// becomes the address, with the text to type selected; with nothing selected, both are placeholders.
    /// </summary>
    internal static (string Text, int SelectionStart, int SelectionLength) InsertLink(string text, int start, int length, string placeholder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(placeholder);
        (start, length) = Clamp(text, start, length);

        const string Address = "https://";
        string selected = text.Substring(start, length).Trim();
        bool isAddress = Uri.TryCreate(selected, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "mailto";
        string link = selected.Length == 0 ? $"[{placeholder}]({Address})"
            : isAddress ? $"[{placeholder}]({selected})"
            : $"[{selected}]({Address})";
        string result = text.Remove(start, length).Insert(start, link);

        // Select what is left to type: the address after a text, else the text.
        return selected.Length > 0 && !isAddress
            ? (result, start + selected.Length + 3, Address.Length)
            : (result, start + 1, placeholder.Length);
    }

    /// <summary>
    /// The table button: a table of two columns, a header row and one empty row, on lines of its own after the caret's
    /// line, with a blank line around it (Markdown needs one before a table). The first header is selected.
    /// </summary>
    internal static (string Text, int SelectionStart, int SelectionLength) InsertTable(string text, int start, int length, string firstHeader, string secondHeader)
    {
        ArgumentNullException.ThrowIfNull(text);
        (start, length) = Clamp(text, start, length);

        int lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        int lineEnd = text.IndexOf('\n', start + length);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        bool emptyLine = string.IsNullOrWhiteSpace(text[lineStart..lineEnd]);

        // On an empty line the table takes its place; otherwise it goes on the lines after the caret's.
        string before = emptyLine ? text[..lineStart] : text[..lineEnd] + "\n";
        if (before.Length > 0 && !before.EndsWith("\n\n", StringComparison.Ordinal))
        {
            before += "\n";
        }

        string rest = text[lineEnd..];
        string table = $"| {firstHeader} | {secondHeader} |\n| --- | --- |\n|  |  |";
        string middle = rest.Length == 0 || rest.StartsWith("\n\n", StringComparison.Ordinal) || rest == "\n" ? string.Empty : "\n";
        return (before + table + middle + rest, before.Length + 2, firstHeader.Length);
    }

    private static bool KindOf(string line, MarkdownLineKind kind)
    {
        if (kind == MarkdownLineKind.Quote)
        {
            return QuotePrefix().IsMatch(line);
        }

        Match list = ListPrefix().Match(line);
        // A task line ("- [ ] …") counts as neither: the bullet button makes it a plain bullet.
        return list.Success && !list.Groups[3].Success && (kind == MarkdownLineKind.Bullet
            ? list.Groups[2].Value is "-" or "*" or "+"
            : char.IsDigit(list.Groups[2].Value[0]));
    }

    private static (int Start, int Length) Clamp(string text, int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        return (start, Math.Clamp(length, 0, text.Length - start));
    }

    /// <summary>The lines the selection touches; a selection ending at a line's start leaves that line out.</summary>
    private static List<(int Start, int End)> TargetLines(string text, ref int start, ref int length)
    {
        (start, length) = Clamp(text, start, length);
        int end = length > 0 && start + length > 0 && text[start + length - 1] == '\n' ? start + length - 1 : start + length;

        var lines = new List<(int Start, int End)>();
        int lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        while (true)
        {
            int lineEnd = text.IndexOf('\n', lineStart);
            lineEnd = lineEnd < 0 ? text.Length : lineEnd;
            lines.Add((lineStart, lineEnd));
            if (lineEnd >= end || lineEnd == text.Length)
            {
                return lines;
            }

            lineStart = lineEnd + 1;
        }
    }

    /// <summary>
    /// Rewrites each line; the selection afterwards covers the same lines, or with none the caret keeps its place in
    /// its line's text.
    /// </summary>
    private static (string Text, int SelectionStart, int SelectionLength) EditLines(
        string text, int start, int length, List<(int Start, int End)> lines, Func<string, int, string> edit)
    {
        var result = new StringBuilder(text.Length + 16);
        result.Append(text, 0, lines[0].Start);
        int caret = start;
        for (int i = 0; i < lines.Count; i++)
        {
            (int lineStart, int lineEnd) = lines[i];
            string before = text[lineStart..lineEnd];
            string after = edit(before, i);
            if (length == 0 && start >= lineStart && start <= lineEnd)
            {
                // The caret stays after the same text: its line grew or shrank at the start.
                caret = result.Length + Math.Clamp(start - lineStart + after.Length - before.Length, 0, after.Length);
            }

            result.Append(after);
            if (i < lines.Count - 1)
            {
                result.Append(text, lineEnd, lines[i + 1].Start - lineEnd);
            }
        }

        int lastEnd = result.Length;
        result.Append(text, lines[^1].End, text.Length - lines[^1].End);
        return length == 0
            ? (result.ToString(), caret, 0)
            : (result.ToString(), lines[0].Start, lastEnd - lines[0].Start);
    }

    [GeneratedRegex(@"\G(#{1,6})[ \t]+")]
    private static partial Regex HeadingPrefix();

    [GeneratedRegex(@"^[ \t]*>[ \t]?")]
    private static partial Regex QuotePrefix();

    /// <summary>Indentation, then a list marker (bullet or number), then a task box.</summary>
    [GeneratedRegex(@"^([ \t]*)([-*+]|\d{1,9}[.)])[ \t]+(\[[ xX]\][ \t]+)?")]
    private static partial Regex ListPrefix();
}
