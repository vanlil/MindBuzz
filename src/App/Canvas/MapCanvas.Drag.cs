using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>A finished drag: move <see cref="Topic"/> under <see cref="NewParent"/> at <see cref="Index"/>.</summary>
internal sealed record TopicDrop(TopicId Topic, TopicId NewParent, int Index, MainSide? Side = null);

/// <summary>
/// Drag and drop of topics: drag a topic onto another to make it a subtopic, or onto the upper
/// or lower edge of one to put it before or after it. The drop target and position are decided by Core's
/// <see cref="DropTargets"/>; the canvas only tracks the pointer and draws the indicator. Esc cancels. Nothing
/// is animated while dragging; the settle after the drop is.
/// </summary>
internal sealed partial class MapCanvas
{
    /// <summary>The pointer must move this far before a press on a topic becomes a drag, so clicks stay
    /// clicks.</summary>
    private const double DragThreshold = 5;

    private TopicId? _dragCandidate;
    private Point _dragStart;
    private TopicId? _dragging;
    private LayoutPoint _dragPoint;
    private DropPlan? _dropPlan;

    /// <summary>A topic was dropped somewhere it can go.</summary>
    public event EventHandler<TopicDrop>? TopicDropped;

    /// <summary>True while a topic is being dragged: a test seam.</summary>
    internal bool IsDragging => _dragging is not null;

    internal DropPlan? CurrentDropPlan => _dropPlan;

    private void ArmDrag(TopicBox hit, Point position, PointerPressedEventArgs e)
    {
        // The central topic cannot be moved.
        if (hit.Parent is null || _editing is not null)
        {
            return;
        }

        _dragCandidate = hit.Id;
        _dragStart = position;
        e.Pointer.Capture(this);
    }

    /// <summary>True when the move belongs to a drag (started or running).</summary>
    private bool MoveDrag(Point position, PointerEventArgs e)
    {
        if (_dragging is null)
        {
            if (_dragCandidate is not { } candidate || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return false;
            }

            if (Math.Abs(position.X - _dragStart.X) < DragThreshold && Math.Abs(position.Y - _dragStart.Y) < DragThreshold)
            {
                return true;
            }

            _dragging = candidate;
        }

        _dragPoint = _viewport.ToMap(position);
        _dropPlan = _layout is not null && _layoutMap is not null
            ? DropTargets.Find(_layout, _layoutMap, _dragging.Value, _dragPoint)
            : null;

        // Dragging one of several selected topics moves them all: none may land inside one of them.
        if (_dropPlan is { } plan && _layoutMap is not null && DragsGroup
            && _selectedSet.Any(id => _layoutMap.Contains(id) && _layoutMap.Index.IsSelfOrAncestor(id, plan.NewParent)))
        {
            _dropPlan = null;
        }
        InvalidateVisual();
        return true;
    }

    private bool EndDrag(PointerReleasedEventArgs e)
    {
        bool wasDragging = _dragging is not null;
        TopicId? dragged = _dragging;
        DropPlan? plan = _dropPlan;

        _dragCandidate = null;
        _dragging = null;
        _dropPlan = null;
        e.Pointer.Capture(null);

        if (!wasDragging)
        {
            return false;
        }

        InvalidateVisual();
        if (dragged is { } topic && plan is not null)
        {
            TopicDropped?.Invoke(this, new TopicDrop(topic, plan.NewParent, plan.Index, plan.Side));
        }

        return true;
    }

    /// <summary>Esc during a drag cancels it.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape && _dragging is not null)
        {
            _dragCandidate = null;
            _dragging = null;
            _dropPlan = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// The dragged branch is dimmed where it is, a ghost of the topic follows the pointer, and the drop target
    /// shows how the topic would land: an outline for "into", a line for "before" and "after".
    /// </summary>
    private void DrawDrag(DrawingContext context, Palette palette, bool drawText)
    {
        if (_dragging is not { } dragged || _layout is null || _layoutMap is null)
        {
            return;
        }

        IBrush veil = Brush("CanvasBackgroundBrush");
        List<TopicId> moving = DragsGroup ? [.. _selectedSet.Where(_layoutMap.Contains)] : [dragged];
        using (context.PushOpacity(0.6))
        {
            foreach (TopicBox box in _layout.Boxes.Where(box => moving.Any(id => _layoutMap.Index.IsSelfOrAncestor(id, box.Id))))
            {
                context.DrawRectangle(veil, null, new RoundedRect(ToRect(box.Bounds).Inflate(2), 8));
            }
        }

        if (_dropPlan is { } plan)
        {
            var accent = new Pen(palette.Selection, 2.5 / Math.Max(_viewport.Zoom, 0.5), lineCap: PenLineCap.Round);
            Rect target = ToRect(plan.Target.Bounds);

            if (plan.Kind == DropKind.Into)
            {
                var dashed = new Pen(palette.Selection, 2.5, new DashStyle([3, 2], 0), PenLineCap.Round);
                context.DrawRectangle(null, dashed, new RoundedRect(target.Inflate(5), 10));
            }
            else
            {
                double y = plan.Kind == DropKind.Before ? target.Top - 7 : target.Bottom + 7;
                context.DrawLine(accent, new Point(target.Left, y), new Point(target.Right, y));
                context.DrawEllipse(palette.Selection, null, new Point(target.Left, y), 3.5, 3.5);
            }
        }

        if (_layout.Find(dragged) is { } ghost)
        {
            var at = new LayoutRect(_dragPoint.X + 10, _dragPoint.Y + 10, ghost.Bounds.Width, ghost.Bounds.Height);
            DrawAt(context, ghost, at, palette, drawText, 0.85);
        }
    }

    /// <summary>True while the dragged topic is one of several selected: they all move.</summary>
    private bool DragsGroup => _dragging is { } dragged && _selectedSet.Count > 1 && _selectedSet.Contains(dragged);

    private static Rect ToRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
