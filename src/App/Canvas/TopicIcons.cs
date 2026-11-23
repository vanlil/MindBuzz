using Avalonia;
using Avalonia.Media;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The ten topic icons, drawn on the topic's left border: a round badge in the icon's colour with
/// a symbol on a 16-unit grid, stroked like the app's other icons. A ring in the canvas colour sets
/// the badge off from the border it sits on. The canvas and the menus draw them with the same code.
/// </summary>
internal static class TopicIcons
{
    /// <summary>A symbol: its geometry on the 16-unit grid, whether it is filled or stroked, and whether it is
    /// dark.</summary>
    private sealed record Symbol(Geometry Shape, bool Filled, bool DarkGlyph);

    private static readonly Dictionary<string, Symbol> Symbols = new()
    {
        // A warning triangle with a mark; dark on amber, which white would not stand out on.
        ["alert"] = new(StreamGeometry.Parse("M 8,3.4 L 12.9,11.8 L 3.1,11.8 Z M 8,6.6 L 8,9 M 8,10.4 L 8,10.45"), false, true),
        ["exclamation"] = new(StreamGeometry.Parse("M 8,3.9 L 8,9.1 M 8,11.9 L 8,11.95"), false, false),
        ["question"] = new(StreamGeometry.Parse("M 5.9,6.1 A 2.15,2.15 0 1 1 9.5,7.7 C 8.5,8.3 8,8.8 8,9.7 M 8,11.9 L 8,11.95"), false, false),
        // A banknote: currency-neutral.
        ["money"] = new(StreamGeometry.Parse("M 3.6,5.4 L 12.4,5.4 L 12.4,10.6 L 3.6,10.6 Z M 6.7,8 A 1.3,1.3 0 1 0 9.3,8 A 1.3,1.3 0 1 0 6.7,8 Z"), false, false),
        // A light bulb; dark on yellow.
        ["idea"] = new(StreamGeometry.Parse("M 6.2,9.7 C 4.7,8.8 4.4,6.8 5.3,5.5 C 6.4,3.9 9.6,3.9 10.7,5.5 C 11.6,6.8 11.3,8.8 9.8,9.7 L 9.8,10.7 L 6.2,10.7 Z M 6.7,12.3 L 9.3,12.3"), false, true),
        ["star"] = new(StreamGeometry.Parse("M 8,3.3 L 9.35,6.45 L 12.7,6.75 L 10.15,9 L 10.9,12.3 L 8,10.55 L 5.1,12.3 L 5.85,9 L 3.3,6.75 L 6.65,6.45 Z"), true, false),
        ["flag"] = new(StreamGeometry.Parse("M 5.3,12.6 L 5.3,3.6 M 5.3,4 L 11.6,4 L 9.9,6.2 L 11.6,8.4 L 5.3,8.4"), false, false),
        ["heart"] = new(StreamGeometry.Parse("M 8,12.2 C 4.4,9.8 3.5,8 3.8,6.3 C 4.1,4.5 6.6,3.9 8,5.8 C 9.4,3.9 11.9,4.5 12.2,6.3 C 12.5,8 11.6,9.8 8,12.2 Z"), true, false),
        ["clock"] = new(StreamGeometry.Parse("M 3.8,8 A 4.2,4.2 0 1 1 12.2,8 A 4.2,4.2 0 1 1 3.8,8 Z M 8,5.4 L 8,8 L 9.9,9.2"), false, false),
        // A map pin, for places.
        ["pin"] = new(StreamGeometry.Parse("M 8,12.8 C 6,10.4 4.7,8.6 4.7,6.9 A 3.3,3.3 0 0 1 11.3,6.9 C 11.3,8.6 10,10.4 8,12.8 Z M 6.9,6.9 A 1.1,1.1 0 1 0 9.1,6.9 A 1.1,1.1 0 1 0 6.9,6.9 Z"), false, false),
    };

    /// <summary>The theme token for an icon's badge colour: "alert" becomes <c>TopicIconAlertBrush</c>.</summary>
    internal static string BrushKey(string value) => $"TopicIcon{char.ToUpperInvariant(value[0])}{value[1..]}Brush";

    /// <summary>True for an icon this version can draw; an unknown one from a newer file is left out.</summary>
    internal static bool IsKnown(string value) => Symbols.ContainsKey(value);

    /// <summary>The brushes an icon is drawn with, resolved from the theme by the caller.</summary>
    internal readonly record struct Brushes(IBrush Fill, IBrush Glyph, IBrush DarkGlyph, IBrush Ring);

    /// <summary>Draws the icon <paramref name="value"/> filling <paramref name="bounds"/>.</summary>
    internal static void Draw(DrawingContext context, string value, Rect bounds, Brushes brushes)
    {
        if (!Symbols.TryGetValue(value, out Symbol? symbol))
        {
            return;
        }

        double radius = bounds.Width / 2;
        double scale = bounds.Width / 16;
        context.DrawEllipse(brushes.Fill, new Pen(brushes.Ring, Math.Max(1.5, scale * 1.4)), bounds.Center, radius, radius);

        IBrush glyph = symbol.DarkGlyph ? brushes.DarkGlyph : brushes.Glyph;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(bounds.X, bounds.Y)))
        {
            if (symbol.Filled)
            {
                context.DrawGeometry(glyph, null, symbol.Shape);
            }
            else
            {
                context.DrawGeometry(null, new Pen(glyph, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), symbol.Shape);
            }
        }
    }

    /// <summary>Every icon value has a symbol: the model's list and the drawing cannot drift apart.</summary>
    internal static IEnumerable<string> Values => MarkerGroups.IconValues;
}
