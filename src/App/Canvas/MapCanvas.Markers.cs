using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>A click on a topic's note or link indicator; <see cref="Direct"/> is Ctrl (⌘ on macOS) held.</summary>
internal sealed record IndicatorClick(TopicId Topic, IndicatorKind Kind, bool Direct);

/// <summary>
/// Markers and indicators inside a topic, drawn as geometry where Core's
/// <see cref="TopicParts"/> places them. Priority is a filled circle with its digit; progress is a ring that fills
/// like a pie, with a check mark when done. Shape and digit carry the meaning, colour only reinforces it, so a
/// greyscale print still reads.
/// </summary>
internal sealed partial class MapCanvas
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    /// <summary>The priority digits, shaped once; their brush is the theme's, updated per frame.</summary>
    private readonly SolidColorBrush _digitBrush = new(Colors.White);
    private Avalonia.Media.TextFormatting.TextLayout[]? _digits;

    private Geometry? _linkIcon;
    private Geometry? _noteIcon;
    private bool _indicatorCursor;

    /// <summary>The note or link indicator of a topic was clicked.</summary>
    public event EventHandler<IndicatorClick>? IndicatorClicked;

    /// <summary>
    /// The priority badge and the progress bar hovering on the topic's border (TopicParts), each with a ring in the
    /// canvas colour that sets it off from the border, like the icon.
    /// </summary>
    private void DrawMarkers(DrawingContext context, TopicBox box, Palette palette)
    {
        if (TopicParts.Priority(box) is { } priority && int.TryParse(priority.Marker.Value, out int level) && level is >= 1 and <= 5)
        {
            LayoutRect bounds = priority.Bounds;
            DrawPriority(context, new Point(bounds.CenterX, bounds.CenterY), level, palette);
        }

        if (TopicParts.Progress(box) is { } progress && int.TryParse(progress.Marker.Value, out int percent))
        {
            DrawProgressBar(context, progress.Bounds, percent, palette, StrokeOf(box.Level, palette));
        }
    }

    /// <summary>The topic's icon over its side border (TopicParts.Icon), drawn after the outline so it sits on
    /// top.</summary>
    private static void DrawTopicIcon(DrawingContext context, TopicBox box, Palette palette)
    {
        if (TopicParts.Icon(box) is { } icon && palette.Icons.TryGetValue(icon.Marker.Value, out TopicIcons.Brushes brushes))
        {
            LayoutRect bounds = icon.Bounds;
            TopicIcons.Draw(context, icon.Marker.Value, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), brushes);
        }
    }

    private void DrawPriority(DrawingContext context, Point centre, int priority, Palette palette)
    {
        // A third larger than the first badge, the digit with it.
        context.DrawEllipse(palette.Priority[priority - 1], new Pen(palette.IconRing, 1.5), centre, 10, 10);

        _digitBrush.Color = palette.OnFill is ISolidColorBrush solid ? solid.Color : Colors.White;
        _digits ??=
        [
            .. Enumerable.Range(1, 5).Select(n => new Avalonia.Media.TextFormatting.TextLayout(
                n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new Typeface(new FontFamily("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.Bold),
                13,
                _digitBrush)),
        ];

        Avalonia.Media.TextFormatting.TextLayout digit = _digits[priority - 1];
        digit.Draw(context, new Point(centre.X - (digit.WidthIncludingTrailingWhitespace / 2), centre.Y - (digit.Height / 2)));
    }

    /// <summary>The topic's outline colour at its level: the progress bar matches its topic.</summary>
    private static IBrush StrokeOf(TopicLevel level, Palette palette) => level switch
    {
        TopicLevel.Central => palette.CentralStroke,
        TopicLevel.Main => palette.MainStroke,
        _ => palette.SubStroke,
    };

    /// <summary>
    /// A slim rounded bar: a light track filled from the left to the percentage. Done is a full bar with a check badge
    /// at its right end, so it differs in shape from 75 % too, and none of it relies on colour alone.
    /// </summary>
    private static void DrawProgressBar(DrawingContext context, LayoutRect bounds, int percent, Palette palette, IBrush colour)
    {
        var track = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        double radius = bounds.Height / 2;
        context.DrawRectangle(palette.ProgressTrack, new Pen(palette.IconRing, 1.5), new RoundedRect(track.Inflate(0.75), radius + 0.75));
        context.DrawRectangle(null, new Pen(colour, 1), new RoundedRect(track, radius));

        double fraction = Math.Clamp(percent, 0, 100) / 100.0;
        if (fraction > 0)
        {
            context.DrawRectangle(colour, null, new RoundedRect(track.WithWidth(Math.Max(bounds.Height, bounds.Width * fraction)), radius));
        }

        if (percent >= 100)
        {
            var centre = new Point(track.Right, track.Center.Y);
            context.DrawEllipse(colour, new Pen(palette.IconRing, 1.5), centre, 6, 6);
            var check = new StreamGeometry();
            using (StreamGeometryContext stream = check.Open())
            {
                stream.BeginFigure(new Point(centre.X - 2.8, centre.Y + 0.1), isFilled: false);
                stream.LineTo(new Point(centre.X - 0.8, centre.Y + 2.1));
                stream.LineTo(new Point(centre.X + 3, centre.Y - 2));
                stream.EndFigure(isClosed: false);
            }

            context.DrawGeometry(null, new Pen(palette.OnFill, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), check);
        }
    }

    private void DrawIndicators(DrawingContext context, TopicBox box, Palette palette)
    {
        IReadOnlyList<(IndicatorKind Kind, LayoutRect Bounds)> indicators = TopicParts.Indicators(box);
        if (indicators.Count == 0)
        {
            return;
        }

        _linkIcon ??= this.FindResource("IconLink") as Geometry;
        _noteIcon ??= this.FindResource("IconNote") as Geometry;

        foreach ((IndicatorKind kind, LayoutRect bounds) in indicators)
        {
            if ((kind == IndicatorKind.Link ? _linkIcon : _noteIcon) is not { } icon)
            {
                continue;
            }

            // The icons are drawn on a 16 px grid; scaling the pen up again keeps the stroke 1.3 px at 100 %.
            double scale = bounds.Width / 16;
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(bounds.X, bounds.Y)))
            {
                context.DrawGeometry(null, new Pen(palette.Indicator, 1.3 / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), icon);
            }
        }
    }

    /// <summary>A hand over the indicators, which are clickable; the default cursor everywhere else.</summary>
    private void UpdateIndicatorCursor(LayoutPoint point)
    {
        bool over = _editing is null && _grid?.TopicAt(point) is { } box && TopicParts.IndicatorAt(box, point) is not null;
        if (over != _indicatorCursor)
        {
            _indicatorCursor = over;
            Cursor = over ? HandCursor : null;
        }
    }
}
