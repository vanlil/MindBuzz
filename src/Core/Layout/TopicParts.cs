using MindBuzz.Core.Model;

namespace MindBuzz.Core.Layout;

/// <summary>The small icons at the right inside a topic.</summary>
public enum IndicatorKind
{
    Note,
    Link,
}

/// <summary>
/// Where the parts of a laid-out topic sit: the icon, the priority badge and the progress bar hovering on its border,
/// and the indicators inside, right of the text. The layout engine reserves the space they need inside; drawing and
/// hit testing both read the positions from here, so a click lands on what is drawn.
/// </summary>
public static class TopicParts
{
    /// <summary>
    /// True when the icon sits on the right border: on the left side of the map everything that hovers on a topic is
    /// mirrored, so the icon is on the inner side there and the collapse handle keeps the outer one.
    /// </summary>
    public static bool IconOnRight(TopicBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return box.Side == Side.Left;
    }

    /// <summary>
    /// The topic's icon and where it sits: centred on the left border (the right border on the left side of the map),
    /// vertically centred, as large as <see cref="TopicBox.IconSize"/>. Null without an icon.
    /// </summary>
    public static (Marker Marker, LayoutRect Bounds)? Icon(TopicBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (box.Topic.MarkerOf(MarkerGroups.Icon) is not { } icon || box.IconSize <= 0)
        {
            return null;
        }

        double size = box.IconSize;
        double x = IconOnRight(box) ? box.Bounds.Right : box.Bounds.X;
        return (icon, new LayoutRect(x - (size / 2), box.Bounds.CenterY - (size / 2), size, size));
    }

    /// <summary>
    /// The priority badge: centred on the top-right corner (top-left on the left side of the map), clear of the icon,
    /// the connector and the collapse handle. Null without a priority.
    /// </summary>
    public static (Marker Marker, LayoutRect Bounds)? Priority(TopicBox box, LayoutMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (box.Topic.MarkerOf(MarkerGroups.Priority) is not { } priority)
        {
            return null;
        }

        double size = (metrics ?? LayoutMetrics.Default).MarkerSize;
        double x = box.Side == Side.Left ? box.Bounds.X : box.Bounds.Right;
        return (priority, new LayoutRect(x - (size / 2), box.Bounds.Y - (size / 2), size, size));
    }

    /// <summary>The progress bar's height, centred on the lower border.</summary>
    public const double ProgressBarHeight = 5;

    /// <summary>
    /// The progress bar: half the topic's width (24 to 96 units), centred on the lower border.
    /// Null without a progress marker.
    /// </summary>
    public static (Marker Marker, LayoutRect Bounds)? Progress(TopicBox box)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (box.Topic.MarkerOf(MarkerGroups.Progress) is not { } progress)
        {
            return null;
        }

        double width = Math.Clamp(box.Bounds.Width / 2, 24, 96);
        return (progress, new LayoutRect(box.Bounds.CenterX - (width / 2), box.Bounds.Bottom - (ProgressBarHeight / 2), width, ProgressBarHeight));
    }

    /// <summary>How far an icon moves a topic's content away from its border: the half of it inside, and a
    /// gap.</summary>
    public static double IconInset(double iconSize, LayoutMetrics? metrics = null) =>
        (iconSize / 2) + (metrics ?? LayoutMetrics.Default).MarkerGap;

    /// <summary>
    /// The note indicator, then the link indicator, left to right, ending at the right padding, level with the first
    /// line of text: on a topic of several lines they sit at the top right, not in the middle.
    /// </summary>
    public static IReadOnlyList<(IndicatorKind Kind, LayoutRect Bounds)> Indicators(TopicBox box, LayoutMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(box);
        metrics ??= LayoutMetrics.Default;

        var kinds = new List<IndicatorKind>(2);
        if (box.Topic.Note.Length > 0)
        {
            kinds.Add(IndicatorKind.Note);
        }

        if (!box.Topic.Links.IsEmpty)
        {
            kinds.Add(IndicatorKind.Link);
        }

        double size = metrics.IndicatorSize;
        double iconRight = IconOnRight(box) && box.Topic.MarkerOf(MarkerGroups.Icon) is not null ? IconInset(box.IconSize, metrics) : 0;
        double right = box.Bounds.Right - iconRight - metrics.PaddingOf(box.Level).Width;
        var parts = new List<(IndicatorKind, LayoutRect)>(kinds.Count);
        for (int i = 0; i < kinds.Count; i++)
        {
            int fromRight = kinds.Count - i;
            double x = right - (fromRight * size) - ((fromRight - 1) * metrics.IndicatorGap);
            double centreY = box.LineHeight > 0 ? box.Text.Y + (box.LineHeight / 2) : box.Bounds.CenterY;
            parts.Add((kinds[i], new LayoutRect(x, centreY - (size / 2), size, size)));
        }

        return parts;
    }

    public static IndicatorKind? IndicatorAt(TopicBox box, LayoutPoint point, LayoutMetrics? metrics = null)
    {
        foreach ((IndicatorKind kind, LayoutRect bounds) in Indicators(box, metrics))
        {
            // A little slack around the 14 px icon: it is a small target.
            if (bounds.Inflate(2).Contains(point))
            {
                return kind;
            }
        }

        return null;
    }
}
