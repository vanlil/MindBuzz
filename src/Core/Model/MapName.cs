namespace MindBuzz.Core.Model;

/// <summary>
/// A map's name is its central topic's text, so the two are always the same. The tab
/// shows it shortened: the first line, with runs of spaces collapsed, cut at a word boundary with "…" when it is
/// too long or when more lines follow.
/// </summary>
public static class MapName
{
    /// <summary>The longest tab label in characters, "…" included. Long enough for a few words.</summary>
    public const int MaxLength = 32;

    public static string For(string centralText)
    {
        ArgumentNullException.ThrowIfNull(centralText);

        // The tab shows the title as text: no bold or italic markers.
        string[] lines = [.. TitleMarkup.Plain(centralText).Split('\n').Select(Collapse).Where(line => line.Length > 0)];
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        string first = lines[0];
        bool more = lines.Length > 1;
        if (first.Length <= MaxLength && !more)
        {
            return first;
        }

        if (first.Length <= MaxLength - 1)
        {
            return first + "…";
        }

        string cut = first[..(MaxLength - 1)];

        // Prefer a word boundary, unless that throws away more than a third of the label.
        int space = cut.LastIndexOf(' ');
        if (space >= (MaxLength * 2) / 3)
        {
            cut = cut[..space];
        }

        // Never leave half of a surrogate pair (an emoji) behind.
        if (cut.Length > 0 && char.IsHighSurrogate(cut[^1]))
        {
            cut = cut[..^1];
        }

        return cut.TrimEnd(' ', ',', ';', ':', '-', '–', '—', '.') + "…";
    }

    /// <summary>The whole text on one line, for the tab's rename box, which has room for one line only.</summary>
    public static string OneLine(string centralText)
    {
        ArgumentNullException.ThrowIfNull(centralText);
        return string.Join(' ', TitleMarkup.Plain(centralText).Split('\n').Select(Collapse).Where(line => line.Length > 0));
    }

    private static string Collapse(string line) =>
        string.Join(' ', line.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries));
}
