using System.Collections.Immutable;
using System.Text;

namespace MindBuzz.Core.Model;

/// <summary>Bold, italic and underline in a topic's title; they combine.</summary>
[Flags]
public enum TitleStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
}

/// <summary>A stretch of a title's plain text in one style.</summary>
public readonly record struct TitleRun(int Start, int Length, TitleStyle Style);

/// <summary>
/// A title read from its Markdown: the text without markers, its styled runs, and for each character where it stands
/// in the Markdown, so a caret or a selection can be carried from one to the other.
/// </summary>
public sealed class ParsedTitle
{
    internal ParsedTitle(string plain, TitleStyle[] styles, int[] starts, int[] ends)
    {
        Plain = plain;
        Styles = styles;
        Starts = starts;
        Ends = ends;
    }

    /// <summary>The text as it is shown: no markers, no escapes.</summary>
    public string Plain { get; }

    /// <summary>The style of each character of <see cref="Plain"/>.</summary>
    public IReadOnlyList<TitleStyle> Styles { get; }

    /// <summary>Where character <c>i</c> of <see cref="Plain"/> starts in the Markdown (its backslash, if
    /// escaped).</summary>
    public IReadOnlyList<int> Starts { get; }

    /// <summary>Where character <c>i</c> of <see cref="Plain"/> ends in the Markdown.</summary>
    public IReadOnlyList<int> Ends { get; }

    /// <summary>The runs of equal style, plain ones included, in order.</summary>
    public ImmutableArray<TitleRun> Runs
    {
        get
        {
            var runs = ImmutableArray.CreateBuilder<TitleRun>();
            int start = 0;
            for (int i = 1; i <= Plain.Length; i++)
            {
                if (i == Plain.Length || Styles[i] != Styles[start])
                {
                    runs.Add(new TitleRun(start, i - start, Styles[start]));
                    start = i;
                }
            }

            return runs.ToImmutable();
        }
    }

    /// <summary>The first character at or after Markdown position <paramref name="position"/>.</summary>
    public int PlainIndexOf(int position)
    {
        int index = 0;
        while (index < Plain.Length && Starts[index] < position)
        {
            index++;
        }

        return index;
    }
}

/// <summary>
/// A topic's title is stored and edited as Markdown with three markers only: <c>**bold**</c>,
/// <c>*italic*</c> and <c>++underline++</c>. Markdown has no underline; <c>++</c> is the "inserted text" notation of
/// markdown-it-ins (https://github.com/markdown-it/markdown-it-ins). Everything else is plain text: no headings,
/// links or HTML. A backslash keeps a marker character plain (<c>\*</c>).
/// <para>
/// Read like Markdown's emphasis: a marker opens only before a non-space and closes only after one, and a marker
/// without its partner stays text. So "3 * 4" and "C++" are text, as typed.
/// </para>
/// </summary>
public static class TitleMarkup
{
    public static string Marker(TitleStyle style) => style switch
    {
        TitleStyle.Bold => "**",
        TitleStyle.Italic => "*",
        TitleStyle.Underline => "++",
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "One style at a time."),
    };

    /// <summary>The title as shown, without markers. Cheap for a title without any.</summary>
    public static string Plain(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        return HasMarkers(markup) ? Parse(markup).Plain : markup;
    }

    /// <summary>True when <paramref name="markup"/> may hold a marker or an escape.</summary>
    public static bool HasMarkers(string markup) => markup.AsSpan().IndexOfAny('*', '+', '\\') >= 0;

    public static ParsedTitle Parse(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        // 1. Tokens: characters, and markers with the stretch of the Markdown they take.
        var tokens = new List<Token>();
        for (int i = 0; i < markup.Length;)
        {
            char c = markup[i];
            if (c == '\\' && i + 1 < markup.Length && IsMarkerChar(markup[i + 1]))
            {
                tokens.Add(Token.Text(i, 2, markup[i + 1]));
                i += 2;
            }
            else if (c is '*' or '+')
            {
                int run = i;
                while (run < markup.Length && markup[run] == c)
                {
                    run++;
                }

                bool canOpen = run < markup.Length && !char.IsWhiteSpace(markup[run]);
                bool canClose = i > 0 && !char.IsWhiteSpace(markup[i - 1]);
                int at = i;
                if (c == '*')
                {
                    // "***" is bold and italic at once.
                    for (; run - at >= 2; at += 2)
                    {
                        tokens.Add(Token.Marker(at, 2, TitleStyle.Bold, canOpen, canClose));
                    }

                    if (run - at == 1)
                    {
                        tokens.Add(Token.Marker(at, 1, TitleStyle.Italic, canOpen, canClose));
                    }
                }
                else
                {
                    for (; run - at >= 2; at += 2)
                    {
                        tokens.Add(Token.Marker(at, 2, TitleStyle.Underline, canOpen, canClose));
                    }

                    if (run - at == 1)
                    {
                        tokens.Add(Token.Text(at, 1, '+'));
                    }
                }

                i = run;
            }
            else
            {
                tokens.Add(Token.Text(i, 1, c));
                i++;
            }
        }

        // 2. Pair the markers; one left without a partner is text.
        var open = new Dictionary<TitleStyle, int>();
        for (int t = 0; t < tokens.Count; t++)
        {
            Token token = tokens[t];
            if (token.Style == TitleStyle.None)
            {
                continue;
            }

            if (open.TryGetValue(token.Style, out int opener) && token.CanClose)
            {
                tokens[opener] = tokens[opener] with { Paired = true };
                tokens[t] = token with { Paired = true };
                open.Remove(token.Style);
            }
            else if (!open.ContainsKey(token.Style) && token.CanOpen)
            {
                open[token.Style] = t;
            }
        }

        // 3. The plain text, styled by the paired markers it sits between.
        var plain = new StringBuilder(markup.Length);
        var styles = new List<TitleStyle>(markup.Length);
        var starts = new List<int>(markup.Length);
        var ends = new List<int>(markup.Length);
        TitleStyle style = TitleStyle.None;
        foreach (Token token in tokens)
        {
            if (token.Paired)
            {
                style ^= token.Style;
                continue;
            }

            if (token.Style == TitleStyle.None)
            {
                Add(token.Char, token.Start, token.Start + token.Length);
            }
            else
            {
                for (int k = 0; k < token.Length; k++)
                {
                    Add(markup[token.Start + k], token.Start + k, token.Start + k + 1);
                }
            }
        }

        starts.Add(markup.Length);
        ends.Add(markup.Length);
        return new ParsedTitle(plain.ToString(), [.. styles], [.. starts], [.. ends]);

        void Add(char c, int start, int end)
        {
            plain.Append(c);
            styles.Add(style);
            starts.Add(start);
            ends.Add(end);
        }
    }

    /// <summary>
    /// The Markdown for <paramref name="plain"/> in <paramref name="styles"/> (one per character). Markers change only
    /// where the style changes; a marker character in the text is escaped only where it would be read as a marker.
    /// </summary>
    public static string Write(string plain, IReadOnlyList<TitleStyle> styles)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(styles);

        styles = Settle(plain, styles);
        string bare = Write(plain, styles, escapeAll: false);
        if (Matches(Parse(bare), plain, styles))
        {
            return bare;
        }

        return Write(plain, styles, escapeAll: true);
    }

    /// <summary>
    /// Bold, italic or underline on or off: for the selection, without the spaces at its ends, or with
    /// no selection for the word the caret is inside. Off when every character already has it, else on.
    /// </summary>
    /// <returns>The new Markdown and selection, or null with no selection outside a word: then the style applies to
    /// what is typed next (<see cref="StyleTyped"/>).</returns>
    public static (string Text, int SelectionStart, int SelectionLength)? Toggle(string markup, int start, int length, TitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(markup);
        start = Math.Clamp(start, 0, markup.Length);
        length = Math.Clamp(length, 0, markup.Length - start);

        ParsedTitle parsed = Parse(markup);
        string plain = parsed.Plain;
        int from = parsed.PlainIndexOf(start);
        int to = parsed.PlainIndexOf(start + length);
        while (from < to && char.IsWhiteSpace(plain[from]))
        {
            from++;
        }

        while (to > from && char.IsWhiteSpace(plain[to - 1]))
        {
            to--;
        }

        int caret = from;
        bool word = false;
        if (to == from)
        {
            // No selection: the word the caret is inside. At a word's edge, as at the end of the text, the style is
            // for what comes next, as in word processors.
            (int wordStart, int wordEnd) = WordAround(plain, caret);
            if (!(wordStart < caret && caret < wordEnd))
            {
                return null;
            }

            (from, to, word) = (wordStart, wordEnd, true);
        }

        TitleStyle[] styles = [.. parsed.Styles];
        bool all = true;
        for (int i = from; i < to; i++)
        {
            all &= char.IsWhiteSpace(plain[i]) || styles[i].HasFlag(style);
        }

        for (int i = from; i < to; i++)
        {
            styles[i] = all ? styles[i] & ~style : styles[i] | style;
        }

        string written = Write(plain, styles);
        ParsedTitle after = Parse(written);
        return word
            ? (written, after.Ends[caret - 1], 0)
            : (written, after.Starts[from], after.Ends[to - 1] - after.Starts[from]);
    }

    /// <summary>
    /// After typing at a caret that had a style switched for what comes next: the typed characters, the
    /// <paramref name="length"/> Markdown characters at <paramref name="start"/>, get <paramref name="flip"/> switched.
    /// </summary>
    /// <returns>The new Markdown and the caret, after the typed text.</returns>
    public static (string Text, int Caret) StyleTyped(string markup, int start, int length, TitleStyle flip)
    {
        ArgumentNullException.ThrowIfNull(markup);

        ParsedTitle parsed = Parse(markup);
        int from = parsed.PlainIndexOf(start);
        int to = parsed.PlainIndexOf(start + length);
        if (to <= from || flip == TitleStyle.None)
        {
            return (markup, start + length);
        }

        TitleStyle[] styles = [.. parsed.Styles];
        for (int i = from; i < to; i++)
        {
            styles[i] ^= flip;
        }

        string written = Write(parsed.Plain, styles);
        return (written, Parse(written).Ends[to - 1]);
    }

    /// <summary>
    /// The styles at the selection, for the toggle buttons: those every non-space character of a selection has; with no
    /// selection, those of the word the caret is inside, or else those text typed at the caret would get.
    /// </summary>
    public static TitleStyle StyleAt(string markup, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(markup);
        start = Math.Clamp(start, 0, markup.Length);
        length = Math.Clamp(length, 0, markup.Length - start);

        ParsedTitle parsed = Parse(markup);
        int from = parsed.PlainIndexOf(start);
        int to = parsed.PlainIndexOf(start + length);
        if (to == from)
        {
            (int wordStart, int wordEnd) = WordAround(parsed.Plain, from);
            if (wordStart < from && from < wordEnd)
            {
                (from, to) = (wordStart, wordEnd);
            }
            else
            {
                // A character typed here: its style is what the markers around the caret give it.
                ParsedTitle typed = Parse(markup.Insert(start, "x"));
                return typed.Styles[typed.PlainIndexOf(start)];
            }
        }

        TitleStyle common = TitleStyle.Bold | TitleStyle.Italic | TitleStyle.Underline;
        bool any = false;
        for (int i = from; i < to; i++)
        {
            if (!char.IsWhiteSpace(parsed.Plain[i]))
            {
                common &= parsed.Styles[i];
                any = true;
            }
        }

        return any ? common : TitleStyle.None;
    }

    private static string Write(string plain, IReadOnlyList<TitleStyle> styles, bool escapeAll)
    {
        var text = new StringBuilder(plain.Length + 8);
        TitleStyle current = TitleStyle.None;
        bool loneBackslash = false;
        for (int i = 0; i <= plain.Length; i++)
        {
            TitleStyle next = i < plain.Length ? styles[i] : TitleStyle.None;
            if (next != current)
            {
                // Closing before opening, in the reverse order of opening, so the markers nest: "++**a**++".
                foreach (TitleStyle style in (ReadOnlySpan<TitleStyle>)[TitleStyle.Italic, TitleStyle.Bold, TitleStyle.Underline])
                {
                    if (current.HasFlag(style) && !next.HasFlag(style))
                    {
                        Append(Marker(style));
                    }
                }

                foreach (TitleStyle style in (ReadOnlySpan<TitleStyle>)[TitleStyle.Underline, TitleStyle.Bold, TitleStyle.Italic])
                {
                    if (next.HasFlag(style) && !current.HasFlag(style))
                    {
                        Append(Marker(style));
                    }
                }

                current = next;
            }

            if (i < plain.Length)
            {
                char c = plain[i];
                Append(escapeAll && c is '*' or '+' ? "\\" + c : c.ToString());
                loneBackslash = c == '\\';
            }
        }

        return text.ToString();

        void Append(string piece)
        {
            // A plain backslash before anything a backslash would escape is doubled, so it stays a backslash.
            if (loneBackslash && IsMarkerChar(piece[0]))
            {
                text.Append('\\');
            }

            loneBackslash = false;
            text.Append(piece);
        }
    }

    /// <summary>
    /// Spaces take only the styles of the text on both sides of them: a marker cannot open before or close after a
    /// space, and a space at the end of a run would leave its marker as text.
    /// </summary>
    private static TitleStyle[] Settle(string plain, IReadOnlyList<TitleStyle> styles)
    {
        TitleStyle[] settled = [.. styles];
        for (int i = 0; i < plain.Length;)
        {
            if (!char.IsWhiteSpace(plain[i]))
            {
                i++;
                continue;
            }

            int end = i;
            while (end < plain.Length && char.IsWhiteSpace(plain[end]))
            {
                end++;
            }

            TitleStyle around = i > 0 && end < plain.Length ? settled[i - 1] & settled[end] : TitleStyle.None;
            for (int k = i; k < end; k++)
            {
                settled[k] = around;
            }

            i = end;
        }

        return settled;
    }

    private static bool Matches(ParsedTitle parsed, string plain, IReadOnlyList<TitleStyle> styles)
    {
        if (parsed.Plain != plain)
        {
            return false;
        }

        for (int i = 0; i < plain.Length; i++)
        {
            if (parsed.Styles[i] != styles[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMarkerChar(char c) => c is '*' or '+' or '\\';

    /// <summary>
    /// The word around <paramref name="caret"/>: letters, digits and underscores, with an apostrophe or hyphen inside
    /// it
    /// ("don't", "e-mail").
    /// </summary>
    private static (int Start, int End) WordAround(string text, int caret)
    {
        static bool Letter(char c) => char.IsLetterOrDigit(c) || c == '_';
        static bool Joiner(char c) => c is '\'' or '’' or '-';

        int start = caret, end = caret;
        while (start > 0 && (Letter(text[start - 1]) || (Joiner(text[start - 1]) && start - 1 > 0 && Letter(text[start - 2]) && start < text.Length && Letter(text[start]))))
        {
            start--;
        }

        while (end < text.Length && (Letter(text[end]) || (Joiner(text[end]) && end > 0 && Letter(text[end - 1]) && end + 1 < text.Length && Letter(text[end + 1]))))
        {
            end++;
        }

        return (start, end);
    }

    private readonly record struct Token(int Start, int Length, char Char, TitleStyle Style, bool CanOpen, bool CanClose, bool Paired)
    {
        public static Token Text(int start, int length, char c) => new(start, length, c, TitleStyle.None, false, false, false);

        public static Token Marker(int start, int length, TitleStyle style, bool canOpen, bool canClose) =>
            new(start, length, '\0', style, canOpen, canClose, false);
    }
}
