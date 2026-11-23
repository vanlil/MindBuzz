using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Views;

/// <summary>
/// The small pictures beside a relationship's colour and line entries: a dot in the colour, a line
/// in the pattern. In Avalonia's own menus (context menus everywhere, the window menu on Windows and Linux) they are
/// controls that follow the theme; the macOS menu bar takes only bitmaps, drawn for the current theme.
/// </summary>
internal static class MenuSwatches
{
    private const double Size = 16;

    /// <summary>The picture as a control for a menu item's icon slot.</summary>
    internal static Control Create(MenuSwatch swatch)
    {
        ArgumentNullException.ThrowIfNull(swatch);

        if (swatch.TopicIcon is { } icon)
        {
            return new TopicIconSwatch(icon) { Width = Size, Height = Size };
        }

        if (swatch.Line is not { } line)
        {
            var dot = new Ellipse { Width = 11, Height = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            dot.Bind(Shape.FillProperty, dot.GetResourceObservable(swatch.BrushKey));
            return new Panel { Width = Size, Height = Size, Children = { dot } };
        }

        var stroke = new Line
        {
            StartPoint = new Point(1.5, Size / 2),
            EndPoint = new Point(Size - 1.5, Size / 2),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeDashArray = Dashes(line),
        };
        stroke.Bind(Shape.StrokeProperty, stroke.GetResourceObservable(swatch.BrushKey));
        return new Panel { Width = Size, Height = Size, Children = { stroke } };
    }

    /// <summary>The picture as a bitmap, for the macOS menu bar, in <paramref name="theme"/>'s colours.</summary>
    internal static Bitmap? Render(MenuSwatch swatch, ThemeVariant theme)
    {
        if (Application.Current is not { } app || !app.TryGetResource(swatch.BrushKey, theme, out object? value) || value is not IBrush brush)
        {
            return null;
        }

        const double Scale = 2;
        var bitmap = new RenderTargetBitmap(new PixelSize((int)(Size * Scale), (int)(Size * Scale)), new Vector(96 * Scale, 96 * Scale));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            if (swatch.TopicIcon is { } icon)
            {
                if (TopicIconSwatch.BrushesFor(app, icon, theme) is { } brushes)
                {
                    Canvas.TopicIcons.Draw(context, icon, new Rect(1, 1, Size - 2, Size - 2), brushes);
                }
            }
            else if (swatch.Line is { } line)
            {
                var pen = new Pen(brush, 2, Dashes(line) is { } dashes ? new DashStyle(dashes, 0) : null, PenLineCap.Round);
                context.DrawLine(pen, new Point(1.5, Size / 2), new Point(Size - 1.5, Size / 2));
            }
            else
            {
                context.DrawEllipse(brush, null, new Point(Size / 2, Size / 2), 5.5, 5.5);
            }
        }

        return bitmap;
    }

    /// <summary>The same patterns as the canvas, in multiples of the thickness.</summary>
    private static Avalonia.Collections.AvaloniaList<double>? Dashes(RelationshipLine line) => line switch
    {
        RelationshipLine.Dashed => [2.5, 1.5],
        RelationshipLine.Dotted => [0, 2],
        _ => null,
    };
}

/// <summary>Turns a menu entry's <see cref="MenuSwatch"/> into its picture, for the window menu's style.</summary>
internal sealed class MenuSwatchConverter : IValueConverter
{
    public static MenuSwatchConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MenuSwatch swatch ? MenuSwatches.Create(swatch) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A topic icon as a menu picture, in the current theme's colours (Canvas.TopicIcons).</summary>
internal sealed class TopicIconSwatch(string icon) : Control
{
    /// <summary>The icon's brushes in <paramref name="theme"/>, or null when the theme lacks one.</summary>
    internal static Canvas.TopicIcons.Brushes? BrushesFor(IResourceHost host, string icon, ThemeVariant theme)
    {
        IBrush? Find(string key) => host.TryFindResource(key, theme, out object? value) ? value as IBrush : null;
        return Find(Canvas.TopicIcons.BrushKey(icon)) is { } fill && Find("TopicIconGlyphBrush") is { } glyph
            && Find("TopicIconGlyphDarkBrush") is { } dark && Find("TopicIconRingBrush") is { } ring
            ? new Canvas.TopicIcons.Brushes(fill, glyph, dark, ring)
            : null;
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (BrushesFor(this, icon, ActualThemeVariant) is { } brushes)
        {
            Canvas.TopicIcons.Draw(context, icon, new Rect(1, 1, Bounds.Width - 2, Bounds.Height - 2), brushes);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
        {
            InvalidateVisual();
        }
    }
}
