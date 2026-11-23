using Avalonia.Controls;
using MindBuzz.Core.Layout;

namespace MindBuzz.App.Canvas;

/// <summary>
/// Topic titles have at most <see cref="TopicText.MaxLines"/> lines on the map, wrapped or typed.
/// Guards a text box where a title is edited (the in-place editor, the detail view's title box): an edit that would
/// give the title more lines is undone at once. A title that is already longer (an older file, a paste) may still be
/// shortened, so an edit is also allowed when it does not add lines. Lines are counted with the map's own text
/// layout, so the limit in the editor is exactly the limit on the map.
/// </summary>
internal sealed class TitleLineGuard(Func<TopicLevel?> level)
{
    private static readonly LayoutMetrics Metrics = new();

    private string _accepted = string.Empty;

    /// <summary>The text was set from outside (a topic opened in the editor): it is the new starting point.</summary>
    public void Reset(string text) => _accepted = text;

    /// <summary>
    /// Checks the box's new text. False when it was too long and has been put back, with the caret where the
    /// refused input would have gone.
    /// </summary>
    public bool Check(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        string text = box.Text ?? string.Empty;
        if (text == _accepted || level() is not { } current || Allows(_accepted, text, current))
        {
            _accepted = text;
            return true;
        }

        int caret = Math.Clamp(box.CaretIndex - (text.Length - _accepted.Length), 0, _accepted.Length);
        box.Text = _accepted;
        box.CaretIndex = caret;
        return false;
    }

    /// <summary>True when <paramref name="after"/> fits the limit, or adds no lines to <paramref name="before"/>.</summary>
    internal static bool Allows(string before, string after, TopicLevel level)
    {
        int lines = Lines(after, level);
        return lines <= TopicText.MaxLines || lines <= Lines(before, level);
    }

    /// <summary>How many lines <paramref name="text"/> takes on the map at <paramref name="level"/>.</summary>
    internal static int Lines(string text, TopicLevel level) => TopicText.CountLines(text, level, Metrics.TextWidthOf(level));
}
