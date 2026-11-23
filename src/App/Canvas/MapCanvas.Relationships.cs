using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using MindBuzz.App.Commands;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>The keys link mode understands.</summary>
internal enum LinkKey
{
    Up,
    Down,
    Left,
    Right,
    Commit,
    CommitTwoWay,
    Cancel,
    Backspace,
}

/// <summary>
/// Relationships on the canvas: orange Bézier curves between topic outlines with arrowheads,
/// dashed when an end is hidden in a collapsed branch, selectable with a click through a wide invisible hit
/// band; and link mode, where the keyboard or a click picks the target of a new relationship.
/// </summary>
internal sealed partial class MapCanvas
{
    /// <summary>How far from the curve a click still hits it, in screen pixels ("a wide hit stroke").</summary>
    private const double RelationshipHitWidth = 7;

    private List<RelationshipCurve> _curves = [];
    private readonly Dictionary<RelationshipId, StreamGeometry> _curveGeometry = [];
    private RelationshipId? _hoverRelationship;

    /// <summary>While the selected relationship's handle is dragged: the shape it would get.</summary>
    private RelationshipCurve? _bending;

    /// <summary>The handle's radius on screen, and how far from it a press still grabs it.</summary>
    private const double BendHandleRadius = 5;
    private const double BendHandleReach = 9;

    /// <summary>The selected relationship's handle was dragged and let go.</summary>
    public event EventHandler<(RelationshipId Relationship, RelationshipBend Bend)>? RelationshipBent;

    /// <summary>True while a relationship's handle is dragged: a test seam.</summary>
    internal bool IsBending => _bending is not null;

    public event EventHandler<LinkKey>? LinkKeyPressed;

    /// <summary>Text typed in link mode, to find a target.</summary>
    public event EventHandler<string>? LinkTyped;

    /// <summary>A click on a target in link mode, and whether Shift was held (two-way).</summary>
    public event EventHandler<(TopicId Target, bool TwoWay)>? LinkPicked;

    public RelationshipId? SelectedRelationship
    {
        get => GetValue(SelectedRelationshipProperty);
        set => SetValue(SelectedRelationshipProperty, value);
    }

    public TopicId? LinkSource
    {
        get => GetValue(LinkSourceProperty);
        set => SetValue(LinkSourceProperty, value);
    }

    public IReadOnlySet<TopicId>? SearchHits
    {
        get => GetValue(SearchHitsProperty);
        set => SetValue(SearchHitsProperty, value);
    }

    public TopicId? LinkTarget
    {
        get => GetValue(LinkTargetProperty);
        set => SetValue(LinkTargetProperty, value);
    }

    public IReadOnlyList<MenuEntry>? RelationshipMenu
    {
        get => GetValue(RelationshipMenuProperty);
        set => SetValue(RelationshipMenuProperty, value);
    }

    /// <summary>The curves of the current layout: a test seam.</summary>
    internal IReadOnlyList<RelationshipCurve> Curves => _curves;

    /// <summary>How many relationships the last frame drew: a test seam for drawing them during transitions.</summary>
    internal int RelationshipsDrawn { get; private set; }

    private void LayoutRelationships(Map map)
    {
        _curves = [.. RelationshipLayout.Compute(_layout!, map)];
        _curveGeometry.Clear();
    }

    /// <summary>The relationship under <paramref name="point"/>, the nearest one if several are close.</summary>
    internal RelationshipCurve? RelationshipAt(LayoutPoint point)
    {
        double reach = RelationshipHitWidth / _viewport.Zoom;
        RelationshipCurve? best = null;
        double bestDistance = reach;

        foreach (RelationshipCurve curve in _curves)
        {
            if (!curve.Bounds.Inflate(reach).Contains(point))
            {
                continue;
            }

            double distance = curve.DistanceTo(point);
            if (distance <= bestDistance)
            {
                best = curve;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>How visible a relationship is while one of its ends is hidden in a collapsed branch.</summary>
    private const double HiddenEndOpacity = 0.4;

    /// <summary>
    /// The pen pattern of a line type. Lengths are in multiples of the thickness; with round caps a zero dash is a dot.
    /// <paramref name="skipped"/> is the length of curve left out before the drawn part (see <see cref="Trimmed"/>):
    /// the
    /// pattern starts that far in, so it stays put on the curve while the view pans.
    /// </summary>
    private static DashStyle? DashesOf(RelationshipLine line, double skipped, double thickness) => line switch
    {
        RelationshipLine.Dashed => new DashStyle([4, 3], skipped / thickness),
        RelationshipLine.Dotted => new DashStyle([0, 2.5], skipped / thickness),
        _ => null,
    };

    private void DrawRelationships(DrawingContext context, Palette palette, LayoutRect visible)
    {
        foreach (RelationshipCurve stored in _curves)
        {
            // While its handle is dragged, the curve changes every frame, so it is built fresh instead of cached.
            RelationshipCurve curve = _bending is { } bending && bending.Id == stored.Id ? bending : stored;
            DrawRelationship(context, palette, visible, curve, cached: ReferenceEquals(curve, stored));
        }
    }

    /// <summary>One relationship: the selection glow, the shadow, the line and its arrowheads.</summary>
    /// <param name="cached">True to take the curve's geometry from the cache: false for a curve that changes every
    /// frame.</param>
    private void DrawRelationship(DrawingContext context, Palette palette, LayoutRect visible, RelationshipCurve curve, bool cached)
    {
        if (!curve.Bounds.Inflate(12).Intersects(visible))
        {
            return;
        }

        RelationshipsDrawn++;

        bool selected = curve.Id == SelectedRelationship;
        bool hovered = curve.Id == _hoverRelationship;
        StreamGeometry geometry = cached ? CurveGeometry(curve) : CurveGeometryUncached(curve);

        if (selected)
        {
            // The selection glow: a wide, soft band under the curve.
            context.DrawGeometry(null, new Pen(palette.SelectionHalo, 9, lineCap: PenLineCap.Round), geometry);
        }

        double thickness = selected ? 2.5 : hovered ? 2 : palette.RelationshipThickness;

        // An end hidden in a collapsed branch: the same line, faded, through faded brushes (see Palette).
        bool faded = curve.EndHidden && !selected;
        IBrush shadow = faded ? palette.FadedLineShadow : palette.LineShadow;

        // The line stops inside its arrowheads, so neither it nor its shadow shows through or beside them.
        bool twoWay = curve.Relationship.Direction == RelationshipDirection.TwoWay;
        double trim = ArrowSize(thickness) * 0.7;
        (StreamGeometry? line, double skipped) = Trimmed(curve, twoWay ? trim : 0, trim, visible);
        StreamGeometry? endArrow = ArrowGeometry(curve.End, curve.Control2, thickness, curve.ToBox.Bounds);
        StreamGeometry? startArrow = twoWay ? ArrowGeometry(curve.Start, curve.Control1, thickness, curve.FromBox.Bounds) : null;

        if (_viewport.Zoom >= TextZoomThreshold)
        {
            using (context.PushTransform(Matrix.CreateTranslation(0, LineShadowOffset)))
            {
                if (line is not null)
                {
                    context.DrawGeometry(null, new Pen(shadow, thickness + 1, DashesOf(curve.Relationship.Line, skipped, thickness + 1), PenLineCap.Round), line);
                }

                foreach (StreamGeometry? arrow in (StreamGeometry?[])[endArrow, startArrow])
                {
                    if (arrow is not null)
                    {
                        context.DrawGeometry(shadow, null, arrow);
                    }
                }
            }
        }

        // Its own colour also while selected, so a new colour shows at once; the glow beneath and
        // the thicker line mark the selection.
        IBrush brush = (faded ? palette.FadedRelationshipColors : palette.RelationshipColors)[(int)curve.Relationship.Color];
        if (line is not null)
        {
            context.DrawGeometry(null, new Pen(brush, thickness, DashesOf(curve.Relationship.Line, skipped, thickness), PenLineCap.Round), line);
        }

        if (endArrow is not null)
        {
            context.DrawGeometry(brush, null, endArrow);
        }

        if (startArrow is not null)
        {
            context.DrawGeometry(brush, null, startArrow);
        }
    }

    /// <summary>
    /// The selected relationship's bend handle: a round knob at the curve's middle, the same size on screen at every
    /// zoom. Drawn after the topics, so a topic lying over the curve's middle never hides it; a
    /// press there reaches the handle before the topic.
    /// </summary>
    private void DrawBendHandle(DrawingContext context, Palette palette)
    {
        RelationshipCurve? curve = _bending ?? SelectedCurve;
        if (curve is null || curve.Id != SelectedRelationship || LinkSource is not null)
        {
            return;
        }

        LayoutPoint middle = curve.At(0.5);
        double radius = BendHandleRadius / _viewport.Zoom;
        context.DrawEllipse(palette.Surface, new Pen(palette.Selection, 2 / _viewport.Zoom), new Point(middle.X, middle.Y), radius, radius);
    }

    private RelationshipCurve? SelectedCurve =>
        SelectedRelationship is { } id ? _curves.Find(curve => curve.Id == id) : null;

    /// <summary>The selected relationship's handle position on screen: a test seam.</summary>
    internal Point? BendHandleOnScreen => SelectedCurve is { } curve ? _viewport.ToScreen(curve.At(0.5)) : null;

    /// <summary>A press on the selected relationship's handle starts bending it.</summary>
    private bool BeginBend(Point position, PointerPressedEventArgs e)
    {
        if (LinkSource is not null || SelectedCurve is not { } curve)
        {
            return false;
        }

        Point handle = _viewport.ToScreen(curve.At(0.5));
        if (Math.Abs(handle.X - position.X) > BendHandleReach || Math.Abs(handle.Y - position.Y) > BendHandleReach)
        {
            return false;
        }

        Focus();
        _bending = curve;
        e.Pointer.Capture(this);
        return true;
    }

    /// <summary>The curve follows the pointer live; nothing changes in the document until the button is
    /// released.</summary>
    private bool MoveBend(Point position)
    {
        if (_bending is not { } curve || _layout is null)
        {
            return false;
        }

        RelationshipBend bend = RelationshipLayout.BendThrough(curve, _viewport.ToMap(position));
        _bending = RelationshipLayout.Reshape(_layout, curve, bend);
        InvalidateVisual();
        return true;
    }

    /// <summary>Letting go records the shape, as one undo step.</summary>
    private bool EndBend(PointerReleasedEventArgs e)
    {
        if (_bending is not { } curve)
        {
            return false;
        }

        _bending = null;
        e.Pointer.Capture(null);
        if (curve.Relationship.Bend is { } bend && _curves.Find(c => c.Id == curve.Id)?.Relationship.Bend != bend)
        {
            RelationshipBent?.Invoke(this, (curve.Id, bend));
        }

        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// An arrowhead at <paramref name="tip"/>, pointing along the curve's tangent there (from the control point to
    /// the tip). Following the tangent is what makes it read as an arrow, not a kink.
    /// </summary>
    /// <param name="topic">The topic the arrow points at: where the curve meets it at a flat angle, a wing would cut
    /// into its border, so the head turns towards the topic's centre until both wings stay clear of it.
    /// The tip stays where it is, <see cref="RelationshipLayout.Clearance"/> outside the outline.</param>
    private static void DrawArrowhead(DrawingContext context, IBrush brush, LayoutPoint tip, LayoutPoint control, double thickness, LayoutRect? topic = null)
    {
        if (ArrowGeometry(tip, control, thickness, topic) is { } geometry)
        {
            context.DrawGeometry(brush, null, geometry);
        }
    }

    /// <summary>An arrowhead's length for a line of <paramref name="thickness"/>.</summary>
    private static double ArrowSize(double thickness) => 6.5 + (thickness * 1.5);

    /// <summary>The arrowhead as a filled triangle; null when the curve has no direction at its end.</summary>
    private static StreamGeometry? ArrowGeometry(LayoutPoint tip, LayoutPoint control, double thickness, LayoutRect? topic = null)
    {
        double dx = tip.X - control.X, dy = tip.Y - control.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            return null;
        }

        (dx, dy) = (dx / length, dy / length);
        double size = ArrowSize(thickness);
        double half = size * 0.45;

        if (topic is { } box)
        {
            double cx = box.CenterX - tip.X, cy = box.CenterY - tip.Y;
            double toCentre = Math.Max(0.001, Math.Sqrt((cx * cx) + (cy * cy)));
            (cx, cy) = (cx / toCentre, cy / toCentre);
            LayoutRect keepOut = box.Inflate(1);
            for (int step = 1; step <= 8 && WingInside(keepOut, tip, dx, dy, size, half); step++)
            {
                double blend = step / 8.0;
                double bx = (dx * (1 - blend)) + (cx * blend), by = (dy * (1 - blend)) + (cy * blend);
                double norm = Math.Max(0.001, Math.Sqrt((bx * bx) + (by * by)));
                (dx, dy) = (bx / norm, by / norm);
            }
        }

        var tipPoint = new Point(tip.X, tip.Y);
        var baseCentre = new Point(tip.X - (dx * size), tip.Y - (dy * size));

        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(tipPoint, isFilled: true);
            stream.LineTo(new Point(baseCentre.X - (dy * half), baseCentre.Y + (dx * half)));
            stream.LineTo(new Point(baseCentre.X + (dy * half), baseCentre.Y - (dx * half)));
            stream.EndFigure(isClosed: true);
        }

        return geometry;
    }

    /// <summary>
    /// The curve without its first <paramref name="fromStart"/> and last <paramref name="fromEnd"/> units of length,
    /// as a cubic of its own (de Casteljau's split), so a dashed or dotted pattern still runs evenly; and of that only
    /// the part near <paramref name="visible"/>. Null when none of it is near.
    /// <para>
    /// The cut to the view is what keeps panning at 60 fps: at 100 % a relationship across a large map is tens of
    /// thousands of units long, and Skia strokes all of it, twice with the shadow, however little shows (34 fps
    /// on a 5,000-topic map with 200 relationships; 60 with the cut). <c>skipped</c> is the length left out before the
    /// part returned, for the dash pattern.
    /// </para>
    /// </summary>
    private static (StreamGeometry? Geometry, double Skipped) Trimmed(RelationshipCurve curve, double fromStart, double fromEnd, LayoutRect visible)
    {
        double t0 = ParameterAtDistance(curve, fromStart, fromEnd: false);
        double t1 = ParameterAtDistance(curve, fromEnd, fromEnd: true);
        if (t1 <= t0)
        {
            (t0, t1) = (0, 1);
        }

        // A curve wholly in view needs no cut; the common case when the map is fitted, so it skips the sampling.
        (double From, double To, double Skipped)? inView = visible.Contains(new LayoutPoint(curve.Bounds.X, curve.Bounds.Y))
            && visible.Contains(new LayoutPoint(curve.Bounds.Right, curve.Bounds.Bottom))
                ? (t0, t1, 0)
                : VisibleRange(curve, t0, t1, visible);
        if (inView is not { } range)
        {
            return (null, 0);
        }

        (LayoutPoint p0, LayoutPoint p1, LayoutPoint p2, LayoutPoint p3) = Split(curve, range.From, range.To);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(new Point(p0.X, p0.Y), isFilled: false);
            stream.CubicBezierTo(new Point(p1.X, p1.Y), new Point(p2.X, p2.Y), new Point(p3.X, p3.Y));
            stream.EndFigure(isClosed: false);
        }

        return (geometry, range.Skipped);
    }

    /// <summary>
    /// The parameters between <paramref name="t0"/> and <paramref name="t1"/> where the curve comes near
    /// <paramref name="visible"/>, and the length before them; null when it never does. Samples the curve: a sample
    /// within one step's arc of the view brackets every visible part, and one more sample on each side keeps the cut
    /// ends
    /// off screen.
    /// </summary>
    internal static (double From, double To, double Skipped)? VisibleRange(RelationshipCurve curve, double t0, double t1, LayoutRect visible)
    {
        // About one sample per half view of length (the control polygon bounds the arc's length): the cut stays correct
        // with fewer samples, only looser, and a short curve needs few.
        double leg = Math.Max(Distance(curve.Start, curve.Control1), Math.Max(Distance(curve.Control1, curve.Control2), Distance(curve.Control2, curve.End)));
        double polygon = Distance(curve.Start, curve.Control1) + Distance(curve.Control1, curve.Control2) + Distance(curve.Control2, curve.End);
        int steps = Math.Clamp((int)Math.Ceiling(polygon * (t1 - t0) / Math.Max(Math.Max(visible.Width, visible.Height) / 2, 1)), 8, 128);
        var points = new LayoutPoint[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            points[i] = curve.At(t0 + ((t1 - t0) * i / steps));
        }

        // A cubic's speed never exceeds three times its longest control leg, so the arc between two samples is at most
        // that times the parameter step: every point of the curve lies within it of a sample.
        LayoutRect near = visible.Inflate(3 * leg * (t1 - t0) / steps);
        int first = -1, last = -1;
        for (int i = 0; i <= steps; i++)
        {
            if (near.Contains(points[i]))
            {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        if (first < 0)
        {
            return null;
        }

        first = Math.Max(first - 1, 0);
        last = Math.Min(last + 1, steps);
        // The length left out, finer than the samples, so a dash pattern does not jump when the cut moves while
        // panning.
        double from = t0 + ((t1 - t0) * first / steps);
        int fine = first * 8;
        double skipped = 0;
        LayoutPoint previous = curve.At(t0);
        for (int i = 1; i <= fine; i++)
        {
            LayoutPoint point = curve.At(t0 + ((from - t0) * i / fine));
            skipped += Distance(previous, point);
            previous = point;
        }

        return (from, t0 + ((t1 - t0) * last / steps), skipped);
    }

    private static double Distance(LayoutPoint a, LayoutPoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>The curve parameter <paramref name="distance"/> along the curve from its start, or back from its
    /// end.</summary>
    private static double ParameterAtDistance(RelationshipCurve curve, double distance, bool fromEnd)
    {
        if (distance <= 0)
        {
            return fromEnd ? 1 : 0;
        }

        const int Steps = 48;
        LayoutPoint previous = curve.At(fromEnd ? 1 : 0);
        double walked = 0;
        for (int i = 1; i <= Steps; i++)
        {
            double t = fromEnd ? 1 - ((double)i / Steps) : (double)i / Steps;
            LayoutPoint point = curve.At(t);
            double step = Math.Sqrt(Math.Pow(point.X - previous.X, 2) + Math.Pow(point.Y - previous.Y, 2));
            if (walked + step >= distance)
            {
                double back = (walked + step - distance) / Math.Max(step, 1e-9) / Steps;
                return fromEnd ? t + back : t - back;
            }

            walked += step;
            previous = point;
        }

        return fromEnd ? 0 : 1;
    }

    /// <summary>The control points of the part of the curve between parameters <paramref name="t0"/> and <paramref
    /// name="t1"/>.</summary>
    private static (LayoutPoint, LayoutPoint, LayoutPoint, LayoutPoint) Split(RelationshipCurve curve, double t0, double t1)
    {
        static LayoutPoint Lerp(LayoutPoint a, LayoutPoint b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

        // Keep [t0, 1] of the whole curve, then the part of that up to where t1 falls in it.
        static (LayoutPoint, LayoutPoint, LayoutPoint, LayoutPoint) Tail(LayoutPoint a, LayoutPoint b, LayoutPoint c, LayoutPoint d, double t)
        {
            LayoutPoint ab = Lerp(a, b, t), bc = Lerp(b, c, t), cd = Lerp(c, d, t);
            LayoutPoint abc = Lerp(ab, bc, t), bcd = Lerp(bc, cd, t);
            return (Lerp(abc, bcd, t), bcd, cd, d);
        }

        static (LayoutPoint, LayoutPoint, LayoutPoint, LayoutPoint) Head(LayoutPoint a, LayoutPoint b, LayoutPoint c, LayoutPoint d, double t)
        {
            LayoutPoint ab = Lerp(a, b, t), bc = Lerp(b, c, t), cd = Lerp(c, d, t);
            LayoutPoint abc = Lerp(ab, bc, t), bcd = Lerp(bc, cd, t);
            return (a, ab, abc, Lerp(abc, bcd, t));
        }

        (LayoutPoint a, LayoutPoint b, LayoutPoint c, LayoutPoint d) = Tail(curve.Start, curve.Control1, curve.Control2, curve.End, t0);
        return Head(a, b, c, d, t0 >= 1 ? 1 : (t1 - t0) / (1 - t0));
    }

    /// <summary>True when either wing of an arrowhead pointing along (dx, dy) lies inside <paramref
    /// name="box"/>.</summary>
    private static bool WingInside(LayoutRect box, LayoutPoint tip, double dx, double dy, double size, double half)
    {
        double bx = tip.X - (dx * size), by = tip.Y - (dy * size);
        return box.Contains(new LayoutPoint(bx - (dy * half), by + (dx * half)))
            || box.Contains(new LayoutPoint(bx + (dy * half), by - (dx * half)));
    }

    private StreamGeometry CurveGeometry(RelationshipCurve curve)
    {
        if (_curveGeometry.TryGetValue(curve.Id, out StreamGeometry? cached))
        {
            return cached;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(new Point(curve.Start.X, curve.Start.Y), isFilled: false);
            stream.CubicBezierTo(
                new Point(curve.Control1.X, curve.Control1.Y),
                new Point(curve.Control2.X, curve.Control2.Y),
                new Point(curve.End.X, curve.End.Y));
            stream.EndFigure(isClosed: false);
        }

        _curveGeometry[curve.Id] = geometry;
        return geometry;
    }

    /// <summary>
    /// Link mode: the target gets a dashed accent outline ("link-mode target") and a dashed preview
    /// curve runs from the source to it.
    /// </summary>
    private void DrawLinkMode(DrawingContext context, Palette palette)
    {
        if (LinkSource is not { } source || LinkTarget is not { } target || _layout is null)
        {
            return;
        }

        TopicBox? from = _layout.Find(source);
        TopicBox? to = _layout.Find(target);
        if (to is null)
        {
            return;
        }

        var outline = new Rect(to.Bounds.X, to.Bounds.Y, to.Bounds.Width, to.Bounds.Height).Inflate(5);
        context.DrawRectangle(null, new Pen(palette.LinkTarget, 2.5, new DashStyle([3, 2], 0), PenLineCap.Round), new RoundedRect(outline, 10));

        if (from is not null && from.Id != to.Id)
        {
            RelationshipCurve preview = RelationshipLayout.Preview(_layout, from, to);
            var pen = new Pen(palette.Relationship, palette.RelationshipThickness, new DashStyle([4, 3], 0), PenLineCap.Round);
            context.DrawGeometry(null, pen, CurveGeometryUncached(preview));
            DrawArrowhead(context, palette.Relationship, preview.End, preview.Control2, palette.RelationshipThickness);
        }
    }

    private static StreamGeometry CurveGeometryUncached(RelationshipCurve curve)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(new Point(curve.Start.X, curve.Start.Y), isFilled: false);
            stream.CubicBezierTo(new Point(curve.Control1.X, curve.Control1.Y), new Point(curve.Control2.X, curve.Control2.Y), new Point(curve.End.X, curve.End.Y));
            stream.EndFigure(isClosed: false);
        }

        return geometry;
    }

    /// <summary>
    /// Link mode takes the keys before the key bindings see them: arrows move the target, Enter links, Esc cancels.
    /// Esc on a selected relationship returns to its topic.
    /// </summary>
    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (LinkSource is not null)
        {
            LinkKey? key = e.Key switch
            {
                Key.Up => LinkKey.Up,
                Key.Down => LinkKey.Down,
                Key.Left => LinkKey.Left,
                Key.Right => LinkKey.Right,
                Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Shift) => LinkKey.CommitTwoWay,
                Key.Enter => LinkKey.Commit,
                Key.Escape => LinkKey.Cancel,
                Key.Back => LinkKey.Backspace,
                _ => null,
            };

            if (key is { } pressed)
            {
                LinkKeyPressed?.Invoke(this, pressed);
                e.Handled = true;
            }

            return;
        }

        // Esc while bending drops the new shape; the relationship stays selected.
        if (e.Key == Key.Escape && _bending is not null)
        {
            _bending = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && SelectedRelationship is not null && _dragging is null)
        {
            SetCurrentValue(SelectedRelationshipProperty, null);
            e.Handled = true;
            return;
        }

        // Esc on several selected topics keeps only the last one.
        if (e.Key == Key.Escape && _selectedSet.Count > 1 && _dragging is null && _editing is null && SelectedTopic is { } primary)
        {
            SelectOnlyRequested?.Invoke(this, primary);
            e.Handled = true;
            return;
        }

        // Esc with nothing else to cancel: the window may use it (to leave distraction-free full screen).
        if (e.Key == Key.Escape && _dragging is null && _editing is null && EscapeUnused?.Invoke() == true)
        {
            e.Handled = true;
        }
    }

    /// <summary>Asked when Esc on the map has nothing left to cancel; true when the owner used it.</summary>
    internal Func<bool>? EscapeUnused { get; set; }

    private void InstallRelationshipKeys() => AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
}
