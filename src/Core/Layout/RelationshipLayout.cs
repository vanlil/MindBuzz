using MindBuzz.Core.Model;

namespace MindBuzz.Core.Layout;

/// <summary>
/// One relationship as drawn: a cubic Bézier between two topic outlines.
/// </summary>
/// <param name="Relationship">The relationship.</param>
/// <param name="FromBox">The topic the curve starts at: the source, or its nearest visible ancestor.</param>
/// <param name="ToBox">The topic the curve ends at: the target, or its nearest visible ancestor.</param>
/// <param name="Start">On the start topic's outline, facing the end.</param>
/// <param name="Control1">The first control point.</param>
/// <param name="Control2">The second control point.</param>
/// <param name="End">On the end topic's outline, facing the start.</param>
/// <param name="EndHidden">True when an end is hidden in a collapsed branch and the curve goes to its ancestor.</param>
public sealed record RelationshipCurve(
    Relationship Relationship,
    TopicBox FromBox,
    TopicBox ToBox,
    LayoutPoint Start,
    LayoutPoint Control1,
    LayoutPoint Control2,
    LayoutPoint End,
    bool EndHidden)
{
    public RelationshipId Id => Relationship.Id;

    /// <summary>A box the whole curve stays inside (the control points' hull), for culling and hit testing.</summary>
    public LayoutRect Bounds
    {
        get
        {
            double x0 = Math.Min(Math.Min(Start.X, End.X), Math.Min(Control1.X, Control2.X));
            double y0 = Math.Min(Math.Min(Start.Y, End.Y), Math.Min(Control1.Y, Control2.Y));
            double x1 = Math.Max(Math.Max(Start.X, End.X), Math.Max(Control1.X, Control2.X));
            double y1 = Math.Max(Math.Max(Start.Y, End.Y), Math.Max(Control1.Y, Control2.Y));
            return new LayoutRect(x0, y0, x1 - x0, y1 - y0);
        }
    }

    /// <summary>The point at <paramref name="t"/> (0 at the start, 1 at the end).</summary>
    public LayoutPoint At(double t)
    {
        double u = 1 - t;
        double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
        return new LayoutPoint(
            (a * Start.X) + (b * Control1.X) + (c * Control2.X) + (d * End.X),
            (a * Start.Y) + (b * Control1.Y) + (c * Control2.Y) + (d * End.Y));
    }

    /// <summary>
    /// The distance from <paramref name="point"/> to the curve, measured on a polyline of 32 segments: close enough
    /// for a click target several pixels wide ("a wide transparent hit stroke").
    /// </summary>
    public double DistanceTo(LayoutPoint point)
    {
        double best = double.MaxValue;
        LayoutPoint previous = Start;
        for (int i = 1; i <= 32; i++)
        {
            LayoutPoint next = At(i / 32.0);
            best = Math.Min(best, SegmentDistance(point, previous, next));
            previous = next;
        }

        return best;
    }

    private static double SegmentDistance(LayoutPoint p, LayoutPoint a, LayoutPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        double t = lengthSquared == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0, 1);
        double x = a.X + (t * dx) - p.X, y = a.Y + (t * dy) - p.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}

/// <summary>
/// Places the relationship curves of a laid-out map. Pure, so the geometry is tested without a
/// window. Control points are automatic: the curve leaves and enters the outlines along the line between the two
/// topics and bows away from the central topic, so it arcs around the map instead of cutting through the middle.
/// </summary>
public static class RelationshipLayout
{
    public static IReadOnlyList<RelationshipCurve> Compute(MapLayout layout, Map map)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(map);

        var curves = new List<RelationshipCurve>(map.Relationships.Length);
        LayoutRect central = layout.Boxes[0].Bounds;

        foreach (Relationship relationship in map.Relationships)
        {
            TopicBox? from = VisibleSelfOrAncestor(layout, map, relationship.From);
            TopicBox? to = VisibleSelfOrAncestor(layout, map, relationship.To);

            // Both ends inside one collapsed branch: there is nothing to draw between them.
            if (from is null || to is null || from.Id == to.Id)
            {
                continue;
            }

            bool dashed = from.Id != relationship.From || to.Id != relationship.To;
            curves.Add(Curve(relationship, from, to, central, dashed));
        }

        return curves;
    }

    /// <summary>The same curve with another bend (null: automatic): the canvas's preview while the handle is
    /// dragged.</summary>
    public static RelationshipCurve Reshape(MapLayout layout, RelationshipCurve curve, RelationshipBend? bend)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(curve);
        return Curve(curve.Relationship with { Bend = bend }, curve.FromBox, curve.ToBox, layout.Boxes[0].Bounds, curve.EndHidden);
    }

    /// <summary>
    /// The bend that makes the curve's middle pass through <paramref name="point"/>: what dragging the handle to
    /// there means. Clamped to ±10 distances, the range the file reader accepts.
    /// </summary>
    public static RelationshipBend BendThrough(RelationshipCurve curve, LayoutPoint point)
    {
        ArgumentNullException.ThrowIfNull(curve);
        (LayoutPoint middle, double ux, double uy, double nx, double ny, double d) = Frame(curve.FromBox, curve.ToBox);
        double px = point.X - middle.X, py = point.Y - middle.Y;
        return new RelationshipBend(
            Math.Clamp(((px * ux) + (py * uy)) / d, -10, 10),
            Math.Clamp(((px * nx) + (py * ny)) / d, -10, 10));
    }

    /// <summary>
    /// The frame a bend is measured in: the midpoint between the centres, the unit vector towards the target, its
    /// left normal (seen from the source, y pointing down) and the distance between the centres.
    /// </summary>
    private static (LayoutPoint Middle, double Ux, double Uy, double Nx, double Ny, double Distance) Frame(TopicBox from, TopicBox to)
    {
        double ax = from.Bounds.CenterX, ay = from.Bounds.CenterY, bx = to.Bounds.CenterX, by = to.Bounds.CenterY;
        double d = Math.Max(1, Math.Sqrt(((bx - ax) * (bx - ax)) + ((by - ay) * (by - ay))));
        double ux = (bx - ax) / d, uy = (by - ay) / d;
        return (new LayoutPoint((ax + bx) / 2, (ay + by) / 2), ux, uy, uy, -ux, d);
    }

    /// <summary>The curve a new relationship from <paramref name="from"/> to <paramref name="to"/> would get: link
    /// mode's preview.</summary>
    public static RelationshipCurve Preview(MapLayout layout, TopicBox from, TopicBox to)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        var placeholder = new Relationship(new RelationshipId("PREVIEW"), from.Id, to.Id, RelationshipDirection.OneWay);
        return Curve(placeholder, from, to, layout.Boxes[0].Bounds, dashed: true);
    }

    private static RelationshipCurve Curve(Relationship relationship, TopicBox from, TopicBox to, LayoutRect central, bool dashed)
    {
        if (relationship.Bend is { } bend)
        {
            return Bent(relationship, from, to, bend, dashed);
        }

        var a = new LayoutPoint(from.Bounds.CenterX, from.Bounds.CenterY);
        var b = new LayoutPoint(to.Bounds.CenterX, to.Bounds.CenterY);

        LayoutPoint start = OnOutline(from.Bounds.Inflate(Clearance), b);
        LayoutPoint end = OnOutline(to.Bounds.Inflate(Clearance), a);

        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Math.Max(1, Math.Sqrt((dx * dx) + (dy * dy)));

        // The normal that points away from the central topic, so the arc bows outwards.
        double nx = -dy / length, ny = dx / length;
        var middle = new LayoutPoint((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        if ((((middle.X - central.CenterX) * nx) + ((middle.Y - central.CenterY) * ny)) < 0)
        {
            (nx, ny) = (-nx, -ny);
        }

        double bow = Math.Clamp(length * 0.3, 24, 140);
        var control1 = new LayoutPoint(start.X + (dx * 0.25) + (nx * bow), start.Y + (dy * 0.25) + (ny * bow));
        var control2 = new LayoutPoint(end.X - (dx * 0.25) + (nx * bow), end.Y - (dy * 0.25) + (ny * bow));

        return new RelationshipCurve(relationship, from, to, start, control1, control2, end, dashed);
    }

    /// <summary>
    /// A dragged curve: it leaves and enters the outlines towards the bend point and passes through it at its middle.
    /// For a cubic, B(½) = (S + 3·C1 + 3·C2 + E) / 8, so moving both control points by 4/3 of the wanted offset
    /// moves the middle by exactly that offset.
    /// </summary>
    private static RelationshipCurve Bent(Relationship relationship, TopicBox from, TopicBox to, RelationshipBend bend, bool dashed)
    {
        (LayoutPoint middle, double ux, double uy, double nx, double ny, double d) = Frame(from, to);
        var through = new LayoutPoint(
            middle.X + (((bend.Along * ux) + (bend.Across * nx)) * d),
            middle.Y + (((bend.Along * uy) + (bend.Across * ny)) * d));

        LayoutPoint start = OnOutline(from.Bounds.Inflate(Clearance), through);
        LayoutPoint end = OnOutline(to.Bounds.Inflate(Clearance), through);
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double ox = (through.X - ((start.X + end.X) / 2)) * 4 / 3, oy = (through.Y - ((start.Y + end.Y) / 2)) * 4 / 3;

        var control1 = new LayoutPoint(start.X + (dx * 0.25) + ox, start.Y + (dy * 0.25) + oy);
        var control2 = new LayoutPoint(end.X - (dx * 0.25) + ox, end.Y - (dy * 0.25) + oy);
        return new RelationshipCurve(relationship, from, to, start, control1, control2, end, dashed);
    }

    /// <summary>
    /// The gap between a relationship's ends and its topics' outlines, so an arrowhead never melts into a topic's
    /// border.
    /// </summary>
    public const double Clearance = 3;

    /// <summary>Where the line from the rectangle's centre towards <paramref name="towards"/> leaves the
    /// rectangle.</summary>
    private static LayoutPoint OnOutline(LayoutRect rect, LayoutPoint towards)
    {
        double dx = towards.X - rect.CenterX, dy = towards.Y - rect.CenterY;
        if (dx == 0 && dy == 0)
        {
            return new LayoutPoint(rect.CenterX, rect.CenterY);
        }

        double scale = Math.Min(
            dx == 0 ? double.MaxValue : (rect.Width / 2) / Math.Abs(dx),
            dy == 0 ? double.MaxValue : (rect.Height / 2) / Math.Abs(dy));
        return new LayoutPoint(rect.CenterX + (dx * scale), rect.CenterY + (dy * scale));
    }

    /// <summary>The topic's box, or its nearest visible ancestor's when it is hidden in a collapsed branch.</summary>
    private static TopicBox? VisibleSelfOrAncestor(MapLayout layout, Map map, TopicId id)
    {
        IReadOnlyList<TopicId> path = map.Index.PathTo(id);
        for (int i = path.Count - 1; i >= 0; i--)
        {
            if (layout.Find(path[i]) is { } box)
            {
                return box;
            }
        }

        return null;
    }
}
