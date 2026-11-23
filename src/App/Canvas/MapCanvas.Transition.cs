using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The structure-change animation: after a collapse, expand, move, drop or paste, topics glide
/// from their old place to their new one in 160 ms, eased. Topics that appear grow out of their nearest
/// ancestor that was already there and fade in; topics that disappear glide into their nearest remaining
/// ancestor and fade out. Only geometry moves: text moves with its topic and is never animated on its own.
/// Typing and dragging never animate (the view model only asks for it on structure changes).
/// </summary>
internal sealed partial class MapCanvas
{
    private bool _animateNextLayout;
    private Map? _layoutMap;

    private Dictionary<TopicId, LayoutRect>? _transitionFrom;

    /// <summary>The relationships as they were drawn before the change, to morph from, so they never flicker.</summary>
    private Dictionary<RelationshipId, RelationshipCurve>? _transitionCurves;
    private List<TopicBox>? _transitionGone;
    private Map? _transitionOldMap;
    private TimeSpan? _transitionStart;
    private double _transitionProgress = 1;

    /// <inheritdoc cref="ViewModels.IMapView.AnimateNextLayout"/>
    public void AnimateNextLayout() => _animateNextLayout = true;

    /// <summary>True while a structure change is animating: a test seam.</summary>
    internal bool IsTransitioning => _transitionProgress < 1;

    /// <summary>
    /// Holds every transition at this progress instead of following the clock: a test seam. A test that renders a
    /// frame and then expects the transition still running otherwise fails on a slow machine, where the 160 ms
    /// are over before the frame is drawn.
    /// </summary>
    internal double? HoldTransitionAt { get; set; }

    /// <summary>The eased progress of the running transition, or null when there is none.</summary>
    private double? TransitionProgress() => _transitionProgress < 1 ? 1 - Math.Pow(1 - _transitionProgress, 3) : null;

    private void BeginTransitionIfRequested(Map map)
    {
        Map? oldMap = _layoutMap;
        MapLayout? oldLayout = _layout;
        _layoutMap = map;

        bool requested = _animateNextLayout;
        _animateNextLayout = false;

        if (!requested || oldLayout is null || oldMap is null || oldMap.Id != map.Id || TopLevel.GetTopLevel(this) is not { } top)
        {
            _transitionProgress = 1;
            return;
        }

        _transitionFrom = oldLayout.Boxes.ToDictionary(box => box.Id, box => box.Bounds);
        _transitionCurves = _curves.ToDictionary(curve => curve.Id);
        _transitionGone = [.. oldLayout.Boxes.Where(box => map.Find(box.Id) is not { } topic || IsHiddenIn(map, topic.Id))];
        _transitionOldMap = oldMap;
        _transitionStart = null;
        _transitionProgress = 0;
        top.RequestAnimationFrame(TransitionTick);
    }

    private void TransitionTick(TimeSpan now)
    {
        _transitionStart ??= now;
        _transitionProgress = HoldTransitionAt ?? Math.Clamp((now - _transitionStart.Value) / AnimationTime, 0, 1);
        InvalidateVisual();

        if (_transitionProgress < 1)
        {
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(TransitionTick);
        }
        else
        {
            _transitionFrom = null;
            _transitionCurves = null;
            _transitionGone = null;
            _transitionOldMap = null;
        }
    }

    /// <summary>
    /// Draws one frame of the transition. Few topics move in a structure change, but the frame is short-lived, so
    /// everything is drawn without culling or cached connector geometry.
    /// </summary>
    private void DrawTransition(DrawingContext context, Palette palette, IPen connectorPen, bool drawText, double progress)
    {
        MapLayout layout = _layout!;

        // Relationships first, behind everything, as in a normal frame.
        DrawTransitionRelationships(context, palette, progress);

        foreach (Connector connector in layout.Connectors)
        {
            if (layout.Find(connector.Child) is { } child && layout.Find(connector.Parent) is { } parent)
            {
                double opacity = _transitionFrom!.ContainsKey(child.Id) ? 1 : progress;
                DrawAnimatedConnector(context, connectorPen, Animated(parent, progress), Animated(child, progress), child.Side, opacity);
            }
        }

        foreach (TopicBox gone in _transitionGone!)
        {
            if (gone.Parent is { } parentId && (layout.Find(parentId) ?? _transitionGone.Find(box => box.Id == parentId)) is { } parent)
            {
                DrawAnimatedConnector(context, connectorPen, AnimatedAny(parent, progress), Leaving(gone, progress), gone.Side, 1 - progress);
            }
        }

        foreach (TopicBox gone in _transitionGone)
        {
            DrawAt(context, gone, Leaving(gone, progress), palette, drawText, 1 - progress);
        }

        foreach (TopicBox box in layout.Boxes)
        {
            DrawAt(context, box, Animated(box, progress), palette, drawText, _transitionFrom!.ContainsKey(box.Id) ? 1 : progress);
        }
    }

    /// <summary>
    /// The relationships during the transition. Each one that was drawn before and is drawn after morphs between its
    /// two shapes (control points and ends eased like the topics), its arrowheads clearing the topics where they are
    /// now. One that appears fades in on its new shape; one that goes fades out on its old one. Drawing nothing until
    /// the transition ended made every relationship blink on each collapse, expand or new topic.
    /// </summary>
    private void DrawTransitionRelationships(DrawingContext context, Palette palette, double progress)
    {
        var everywhere = new LayoutRect(-1e7, -1e7, 2e7, 2e7);
        foreach (RelationshipCurve to in _curves)
        {
            if (_transitionCurves!.TryGetValue(to.Id, out RelationshipCurve? from))
            {
                DrawRelationship(context, palette, everywhere, Morph(from, to, progress), cached: false);
            }
            else
            {
                using (context.PushOpacity(progress))
                {
                    DrawRelationship(context, palette, everywhere, to, cached: true);
                }
            }
        }

        foreach (RelationshipCurve gone in _transitionCurves!.Values.Where(curve => !_curves.Exists(now => now.Id == curve.Id)))
        {
            using (context.PushOpacity(1 - progress))
            {
                DrawRelationship(context, palette, everywhere, gone, cached: false);
            }
        }
    }

    /// <summary>A relationship between its old and new shape, its end topics where they are at this moment.</summary>
    private RelationshipCurve Morph(RelationshipCurve from, RelationshipCurve to, double t)
    {
        static LayoutPoint Mix(LayoutPoint a, LayoutPoint b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

        return to with
        {
            Start = Mix(from.Start, to.Start, t),
            Control1 = Mix(from.Control1, to.Control1, t),
            Control2 = Mix(from.Control2, to.Control2, t),
            End = Mix(from.End, to.End, t),
            FromBox = to.FromBox with { Bounds = Lerp(from.FromBox.Bounds, to.FromBox.Bounds, t) },
            ToBox = to.ToBox with { Bounds = Lerp(from.ToBox.Bounds, to.ToBox.Bounds, t) },
            EndHidden = t < 0.5 ? from.EndHidden : to.EndHidden,
        };
    }

    /// <summary>Where a topic of the new layout is now: from its old place, or out of its ancestor if it is
    /// new.</summary>
    private LayoutRect Animated(TopicBox box, double progress) => Lerp(Start(box), box.Bounds, progress);

    /// <summary>A topic of either layout.</summary>
    private LayoutRect AnimatedAny(TopicBox box, double progress) =>
        _layout!.Find(box.Id) is { } current ? Animated(current, progress) : Leaving(box, progress);

    private LayoutRect Start(TopicBox box)
    {
        if (_transitionFrom!.TryGetValue(box.Id, out LayoutRect from))
        {
            return from;
        }

        // New: starts centred on the nearest ancestor that was there before.
        foreach (TopicId ancestor in _layoutMap!.Index.PathTo(box.Id).Reverse().Skip(1))
        {
            if (_transitionFrom.TryGetValue(ancestor, out LayoutRect origin))
            {
                return CentredOn(box.Bounds, origin);
            }
        }

        return box.Bounds;
    }

    /// <summary>Where a disappearing topic is now: on its way into its nearest ancestor still on screen.</summary>
    private LayoutRect Leaving(TopicBox gone, double progress)
    {
        foreach (TopicId ancestor in _transitionOldMap!.Index.PathTo(gone.Id).Reverse().Skip(1))
        {
            if (_layout!.Find(ancestor) is { } target)
            {
                return Lerp(gone.Bounds, CentredOn(gone.Bounds, target.Bounds), progress);
            }
        }

        return gone.Bounds;
    }

    private void DrawAt(DrawingContext context, TopicBox box, LayoutRect at, Palette palette, bool drawText, double opacity)
    {
        if (opacity <= 0.01)
        {
            return;
        }

        using (context.PushOpacity(opacity))
        using (context.PushTransform(Matrix.CreateTranslation(at.X - box.Bounds.X, at.Y - box.Bounds.Y)))
        {
            DrawTopic(context, box, palette, drawText);
        }
    }

    private static void DrawAnimatedConnector(DrawingContext context, IPen pen, LayoutRect parent, LayoutRect child, Side side, double opacity)
    {
        if (opacity <= 0.01)
        {
            return;
        }

        bool right = side != Side.Left;
        var start = new Point(right ? parent.Right : parent.X, parent.CenterY);
        var end = new Point(right ? child.X : child.Right, child.CenterY);
        double half = (end.X - start.X) / 2;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(start, isFilled: false);
            stream.CubicBezierTo(new Point(start.X + half, start.Y), new Point(end.X - half, end.Y), end);
            stream.EndFigure(isClosed: false);
        }

        using (context.PushOpacity(opacity))
        {
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private static bool IsHiddenIn(Map map, TopicId id) =>
        map.Index.PathTo(id).SkipLast(1).Any(ancestor => map.Find(ancestor)!.IsCollapsed);

    private static LayoutRect CentredOn(LayoutRect box, LayoutRect target) =>
        box with { X = target.CenterX - (box.Width / 2), Y = target.CenterY - (box.Height / 2) };

    private static LayoutRect Lerp(LayoutRect from, LayoutRect to, double t) => new(
        from.X + ((to.X - from.X) * t),
        from.Y + ((to.Y - from.Y) * t),
        from.Width + ((to.Width - from.Width) * t),
        from.Height + ((to.Height - from.Height) * t));
}
