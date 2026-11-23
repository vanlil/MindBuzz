using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace MindBuzz.App.Notes;

/// <summary>
/// Paints the spans <see cref="MarkdownSyntax"/> finds into the notes editor (basic Markdown syntax
/// colouring). Brushes are looked up in the owner's theme at paint time, so a theme change only needs a redraw.
/// </summary>
internal sealed class NoteColorizer(Control owner) : DocumentColorizingTransformer
{
    /// <summary>Sorted by start. Every span lies within one line.</summary>
    private MarkdownSpan[] _spans = [];

    /// <summary>Rescans the text. Call it on every change, before the view redraws.</summary>
    public void Update(string text) =>
        _spans = [.. MarkdownSyntax.Scan(text).OrderBy(span => span.Start)];

    protected override void ColorizeLine(DocumentLine line)
    {
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;

        int first = FirstAtOrAfter(lineStart);
        for (int i = first; i < _spans.Length && _spans[i].Start < lineEnd; i++)
        {
            MarkdownSpan span = _spans[i];
            int start = Math.Max(span.Start, lineStart);
            int end = Math.Min(span.Start + span.Length, lineEnd);
            if (end > start)
            {
                ChangeLinePart(start, end, element => Paint(element, span.Kind));
            }
        }
    }

    private int FirstAtOrAfter(int offset)
    {
        int low = 0;
        int high = _spans.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_spans[middle].Start < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void Paint(VisualLineElement element, MarkdownSpanKind kind)
    {
        VisualLineElementTextRunProperties run = element.TextRunProperties;
        Typeface typeface = run.Typeface;

        switch (kind)
        {
            case MarkdownSpanKind.Heading:
                run.SetForegroundBrush(Brush("NoteSyntaxHeadingBrush"));
                run.SetTypeface(new Typeface(typeface.FontFamily, typeface.Style, FontWeight.Bold));
                break;
            case MarkdownSpanKind.Strong:
                run.SetTypeface(new Typeface(typeface.FontFamily, typeface.Style, FontWeight.Bold));
                break;
            case MarkdownSpanKind.Emphasis:
                run.SetTypeface(new Typeface(typeface.FontFamily, FontStyle.Italic, typeface.Weight));
                break;
            case MarkdownSpanKind.Strikethrough:
                run.SetTextDecorations(TextDecorations.Strikethrough);
                break;
            case MarkdownSpanKind.Code or MarkdownSpanKind.Fence:
                run.SetForegroundBrush(Brush("NoteSyntaxCodeBrush"));
                run.SetBackgroundBrush(Brush("NoteCodeBackgroundBrush"));
                break;
            case MarkdownSpanKind.Quote:
                run.SetForegroundBrush(Brush("NoteSyntaxQuoteBrush"));
                break;
            case MarkdownSpanKind.ListMarker:
                run.SetForegroundBrush(Brush("NoteSyntaxMarkerBrush"));
                break;
            case MarkdownSpanKind.Link:
                run.SetForegroundBrush(Brush("NoteSyntaxLinkBrush"));
                break;
        }
    }

    private IBrush? Brush(string key) =>
        owner.TryFindResource(key, owner.ActualThemeVariant, out object? value) ? value as IBrush : null;
}
