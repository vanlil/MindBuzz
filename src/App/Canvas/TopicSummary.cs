using System.Globalization;
using MindBuzz.App.Localization;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// What the tooltip over a topic says: its progress, priority, links, note and icon, one per line,
/// for the topics that have any of them. The markers on the map are small; this spells them out.
/// </summary>
internal static class TopicSummary
{
    /// <summary>How much of a note's first line the tooltip shows.</summary>
    private const int NoteLength = 60;

    /// <summary>The summary, or null for a topic with nothing to summarise.</summary>
    internal static string? For(Topic topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        var lines = new List<string>();

        if (topic.MarkerOf(MarkerGroups.Progress) is { } progress)
        {
            lines.Add(progress.Value == "100"
                ? Strings.Tip_ProgressDone
                : string.Format(CultureInfo.CurrentCulture, Strings.Tip_ProgressFormat, progress.Value));
        }

        if (topic.MarkerOf(MarkerGroups.Priority) is { } priority)
        {
            lines.Add(priority.Value switch
            {
                "1" => Strings.Tip_PriorityHighest,
                "5" => Strings.Tip_PriorityLowest,
                _ => string.Format(CultureInfo.CurrentCulture, Strings.Tip_PriorityFormat, priority.Value),
            });
        }

        if (topic.Links.Length > 0)
        {
            lines.Add(topic.Links.Length == 1 ? Strings.Tip_OneLink : string.Format(CultureInfo.CurrentCulture, Strings.Tip_LinksFormat, topic.Links.Length));
        }

        if (NoteLine(topic.Note) is { } note)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Tip_NoteFormat, note));
        }

        if (topic.MarkerOf(MarkerGroups.Icon) is { } icon && Strings.ResourceManager.GetString($"Icon_{icon.Value}", Strings.Culture) is { } name)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Tip_IconFormat, name));
        }

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    /// <summary>The note's first line with text, without Markdown's leading marks, cut at a word with "…".</summary>
    private static string? NoteLine(string note)
    {
        string? line = note.Split('\n').Select(text => text.Trim().TrimStart('#', '>', '-', '*', '+', ' ').Replace("**", string.Empty, StringComparison.Ordinal).Trim())
            .FirstOrDefault(text => text.Length > 0);
        if (line is null || line.Length <= NoteLength)
        {
            return line;
        }

        int cut = line.LastIndexOf(' ', NoteLength);
        return string.Concat(line.AsSpan(0, cut > NoteLength / 2 ? cut : NoteLength), "…");
    }
}
