using Avalonia;
using Avalonia.Media;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The collapse handles: a collapsed topic always shows a handle with the number of hidden
/// subtopics; a topic with visible subtopics shows a small "−" handle while hovered or selected. Clicking a handle
/// toggles the topic. The central topic has none: collapsing it would hide the whole map.
/// </summary>
internal sealed partial class MapCanvas
{
    private const double HandleRadius = 8;

    /// <summary>A collapse handle was clicked.</summary>
    public event EventHandler<TopicId>? HandleClicked;

    /// <summary>The gap between a topic and its handle; wide enough to clear the priority badge on the
    /// corner.</summary>
    private const double HandleGap = 5;

    private static Point HandleCentre(TopicBox box) => new(
        box.Side == Side.Left ? box.Bounds.X - HandleRadius - HandleGap : box.Bounds.Right + HandleRadius + HandleGap,
        box.Bounds.CenterY);

    /// <summary>The topic whose visible handle is under <paramref name="point"/>, or null.</summary>
    internal TopicId? HandleAt(LayoutPoint point) =>
        HandleOwnerAt(point) is { } owner && _layout?.Find(owner) is { } box
            && (box.HiddenChildren > 0 || owner == SelectedTopic || owner == _hover)
            ? owner
            : null;

    /// <summary>The topic whose handle position (visible or not) is under <paramref name="point"/>.</summary>
    private TopicId? HandleOwnerAt(LayoutPoint point)
    {
        if (_grid is null || _layout is null)
        {
            return null;
        }

        var around = new LayoutRect(point.X - 24, point.Y - 24, 48, 48);
        foreach (int index in _grid.TopicsIn(around))
        {
            TopicBox box = _layout.Boxes[index];
            if (!box.Topic.HasChildren || box.Parent is null)
            {
                continue;
            }

            Point centre = HandleCentre(box);
            double dx = point.X - centre.X;
            double dy = point.Y - centre.Y;
            if ((dx * dx) + (dy * dy) <= (HandleRadius + 2) * (HandleRadius + 2))
            {
                return box.Id;
            }
        }

        return null;
    }

    /// <summary>The "−" handle of an expanded topic: a small circle with a bar, like the collapsed one without a
    /// count.</summary>
    private static void DrawExpandedHandle(DrawingContext context, TopicBox box, Rect rect, IBrush stroke, Palette palette)
    {
        _ = rect;
        Point centre = HandleCentre(box);
        var pen = new Pen(stroke, 1.25);
        context.DrawEllipse(palette.Surface, pen, centre, HandleRadius, HandleRadius);
        context.DrawLine(new Pen(stroke, 1.5, lineCap: PenLineCap.Round), new Point(centre.X - 3.5, centre.Y), new Point(centre.X + 3.5, centre.Y));
    }
}
