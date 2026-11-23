using Avalonia.Platform;
using SkiaSharp;

namespace MindBuzz.App.Printing;

/// <summary>
/// Draws the keyboard sheet (<see cref="ShortcutSheet"/>) as a one-page A4 PDF: black on white, two columns, the keys
/// in semi-bold, then what they do. No logo and no app name, only what helps at the keyboard.
/// SkiaSharp writes the PDF and embeds the font: it is already part of Avalonia, so no new dependency; Inter is the
/// font, because it has the macOS key symbols (⌘ ⌥ ⇧ ⌃ ↩ ⎋ ⇥ ⌫), checked with SkiaSharp 3.119.4.
/// </summary>
internal static class ShortcutSheetPdf
{
    /// <summary>A4 in PDF points (1/72 inch).</summary>
    internal const float PageWidth = 595.28f, PageHeight = 841.89f;

    private const float Margin = 36, Gutter = 26;

    /// <summary>Text sizes tried from the largest down, until the sheet fits one page.</summary>
    private static readonly float[] Sizes = [10, 9.5f, 9, 8.5f, 8];

    internal static byte[] Render(IReadOnlyList<SheetSection> sheet, IReadOnlyList<(string Symbol, string Name)> legend, string title)
    {
        using var stream = new MemoryStream();
        using (var managed = new SKManagedWStream(stream))
        using (var document = SKDocument.CreatePdf(managed, new SKDocumentPdfMetadata { Title = title, RasterDpi = 300 }))
        {
            SKCanvas canvas = document.BeginPage(PageWidth, PageHeight);
            Draw(canvas, sheet, legend, title);
            document.EndPage();
            document.Close();
        }

        return stream.ToArray();
    }

    /// <summary>Draws the page on any canvas: the PDF's, or a bitmap's for the tests' pictures.</summary>
    internal static void Draw(SKCanvas canvas, IReadOnlyList<SheetSection> sheet, IReadOnlyList<(string Symbol, string Name)> legend, string title)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        using SKTypeface regular = Inter("Inter-Regular.ttf");
        using SKTypeface bold = Inter("Inter-SemiBold.ttf");

        // Not even the smallest size fits: draw it anyway, overflowing; a test keeps the sheet within one page.
        float size = FittingSize(sheet, legend, regular, bold) ?? Sizes[^1];
        PagePlan plan = Plan(sheet, legend, regular, bold, size, force: true)!;
        Paint(canvas, plan, sheet, legend, title, regular, bold, size);
    }

    /// <summary>The largest text size at which the sheet fits one page, or null when none does: a test seam.</summary>
    internal static float? FittingSize(IReadOnlyList<SheetSection> sheet, IReadOnlyList<(string Symbol, string Name)> legend)
    {
        using SKTypeface regular = Inter("Inter-Regular.ttf");
        using SKTypeface bold = Inter("Inter-SemiBold.ttf");
        return FittingSize(sheet, legend, regular, bold);
    }

    private static float? FittingSize(IReadOnlyList<SheetSection> sheet, IReadOnlyList<(string Symbol, string Name)> legend, SKTypeface regular, SKTypeface bold) =>
        Sizes.Cast<float?>().FirstOrDefault(size => Plan(sheet, legend, regular, bold, size!.Value) is not null);

    /// <summary>Where each section goes: its column and top. A section is never split over two columns.</summary>
    private sealed record Placement(int Column, float Top);

    /// <summary>The page laid out: each section's place, the keys' width, and each row's text wrapped to lines.</summary>
    private sealed record PagePlan(IReadOnlyList<Placement> Sections, float KeysWidth, IReadOnlyList<IReadOnlyList<string[]>> Lines);

    private static float ColumnWidth => (PageWidth - (2 * Margin) - Gutter) / 2;

    private static float LineHeight(float size) => size * 1.45f;

    private static float HeadingHeight(float size) => (size * 1.3f) + (size * 1.2f);

    private static float SectionGap(float size) => size * 1.1f;

    private static float Top(float size, int legendLines) => Margin + (size * 2.6f) + (legendLines > 0 ? (size * 0.9f) + (legendLines * LineHeight(size)) : 0);

    /// <summary>
    /// Where each legend entry goes ("⌘ Command   ⌥ Option   …"): its line and left edge, wrapping at the right
    /// margin.
    /// </summary>
    private static List<(int Line, float X, string Symbol, string Name)> LegendLayout(
        IReadOnlyList<(string Symbol, string Name)> legend, SKFont keyFont, SKFont textFont, float size)
    {
        var placed = new List<(int, float, string, string)>();
        int line = 0;
        float x = Margin;
        foreach ((string symbol, string name) in legend)
        {
            float width = keyFont.MeasureText(symbol) + (size * 0.35f) + textFont.MeasureText(name);
            if (x > Margin && x + width > PageWidth - Margin)
            {
                line++;
                x = Margin;
            }

            placed.Add((line, x, symbol, name));
            x += width + (size * 1.4f);
        }

        return placed;
    }

    private static PagePlan? Plan(
        IReadOnlyList<SheetSection> sheet, IReadOnlyList<(string Symbol, string Name)> legend, SKTypeface regular, SKTypeface bold, float size, bool force = false)
    {
        using var keyFont = new SKFont(bold, size);
        using var textFont = new SKFont(regular, size);
        int legendLines = legend.Count == 0 ? 0 : LegendLayout(legend, keyFont, textFont, size)[^1].Line + 1;
        float keysWidth = sheet.SelectMany(section => section.Rows).Max(row => keyFont.MeasureText(row.Keys)) + (size * 1.2f);
        float textWidth = ColumnWidth - keysWidth;

        // A text too long for the space beside the keys wraps onto further lines.
        List<IReadOnlyList<string[]>> lines = [.. sheet.Select(section => (IReadOnlyList<string[]>)[.. section.Rows.Select(row => Wrap(row.Text, textFont, textWidth))])];

        float top = Top(size, legendLines);
        float bottom = PageHeight - Margin;
        var placements = new List<Placement>();
        int column = 0;
        float y = top;
        for (int i = 0; i < sheet.Count; i++)
        {
            float height = HeadingHeight(size) + (lines[i].Sum(row => row.Length) * LineHeight(size));
            if (y > top && y + height > bottom)
            {
                column++;
                y = top;
            }

            if (column > 1 && !force)
            {
                return null;
            }

            placements.Add(new Placement(column, y));
            y += height + SectionGap(size);
        }

        return new PagePlan(placements, keysWidth, lines);
    }

    /// <summary>Splits <paramref name="text"/> at spaces into lines no wider than <paramref name="width"/>.</summary>
    private static string[] Wrap(string text, SKFont font, float width)
    {
        var lines = new List<string>();
        string line = string.Empty;
        foreach (string word in text.Split(' '))
        {
            string longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureText(longer) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = longer;
            }
        }

        lines.Add(line);
        return [.. lines];
    }

    private static void Paint(
        SKCanvas canvas, PagePlan plan, IReadOnlyList<SheetSection> sheet, IReadOnlyList<(string Symbol, string Name)> legend, string title,
        SKTypeface regular, SKTypeface bold, float size)
    {
        canvas.Clear(SKColors.White);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var rule = new SKPaint { Color = SKColors.Black, IsAntialias = true, StrokeWidth = 0.6f, Style = SKPaintStyle.Stroke };
        using var titleFont = new SKFont(bold, size * 1.5f);
        using var headingFont = new SKFont(bold, size * 1.05f);
        using var keyFont = new SKFont(bold, size);
        using var textFont = new SKFont(regular, size);

        canvas.DrawText(title, Margin, Margin + (size * 1.5f), titleFont, ink);

        // The legend, the symbol in semi-bold.
        foreach ((int line, float x, string symbol, string name) in LegendLayout(legend, keyFont, textFont, size))
        {
            float baseline = Margin + (size * 4f) + (line * LineHeight(size));
            canvas.DrawText(symbol, x, baseline, keyFont, ink);
            canvas.DrawText(name, x + keyFont.MeasureText(symbol) + (size * 0.35f), baseline, textFont, ink);
        }

        for (int i = 0; i < sheet.Count; i++)
        {
            SheetSection section = sheet[i];
            float left = Margin + (plan.Sections[i].Column * (ColumnWidth + Gutter));
            float y = plan.Sections[i].Top + (size * 1.3f);

            canvas.DrawText(section.Title, left, y, headingFont, ink);
            y += size * 0.45f;
            canvas.DrawLine(left, y, left + ColumnWidth, y, rule);
            y += size * 0.75f;

            for (int r = 0; r < section.Rows.Count; r++)
            {
                y += LineHeight(size);
                canvas.DrawText(section.Rows[r].Keys, left, y - (size * 0.45f), keyFont, ink);
                string[] lines = plan.Lines[i][r];
                for (int l = 0; l < lines.Length; l++)
                {
                    if (l > 0)
                    {
                        y += LineHeight(size);
                    }

                    canvas.DrawText(lines[l], left + plan.KeysWidth, y - (size * 0.45f), textFont, ink);
                }
            }
        }
    }

    /// <summary>Inter from Avalonia.Fonts.Inter, the font the app itself uses.</summary>
    private static SKTypeface Inter(string file)
    {
        using Stream asset = AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/{file}"));
        using var copy = new MemoryStream();
        asset.CopyTo(copy);
        copy.Position = 0;
        return SKTypeface.FromStream(copy) ?? throw new InvalidOperationException($"Inter font {file} could not be read.");
    }
}
