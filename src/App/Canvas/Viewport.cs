using Avalonia;
using MindBuzz.Core.Layout;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The canvas's view of the map: a zoom factor and where the map's origin (the central topic's centre) sits on
/// screen. Pure, so pan, zoom and the map/screen conversions that hit testing relies on are tested without a
/// window (a transform for drawing, a separate coordinate space for hit testing).
/// </summary>
internal readonly record struct Viewport(double Zoom, Point Origin)
{
    /// <summary>
    /// The zoom range, everywhere: wheel, pinch, keys, slider and Fit Map. A larger map than fits at
    /// 25 % is fitted at 25 % and scrolled.
    /// </summary>
    public const double MinZoom = 0.25;

    public const double MaxZoom = 2;

    /// <summary>The steps of Zoom in and Zoom out.</summary>
    public static IReadOnlyList<double> Steps { get; } = [0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1, 1.25, 1.5, 1.75, 2];

    public Matrix Transform => Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Origin.X, Origin.Y);

    public Point ToScreen(LayoutPoint point) => new((point.X * Zoom) + Origin.X, (point.Y * Zoom) + Origin.Y);

    public LayoutPoint ToMap(Point point) => new((point.X - Origin.X) / Zoom, (point.Y - Origin.Y) / Zoom);

    /// <summary>The part of the map the screen rectangle <paramref name="view"/> shows.</summary>
    public LayoutRect Visible(Size view)
    {
        LayoutPoint topLeft = ToMap(default);
        return new LayoutRect(topLeft.X, topLeft.Y, view.Width / Zoom, view.Height / Zoom);
    }

    public Viewport PanBy(Vector delta) => this with { Origin = Origin + delta };

    /// <summary>Zooms so the map point under <paramref name="anchor"/> stays under it.</summary>
    public Viewport ZoomAround(Point anchor, double zoom)
    {
        double clamped = Math.Clamp(zoom, MinZoom, MaxZoom);
        LayoutPoint fixedPoint = ToMap(anchor);
        return new Viewport(clamped, new Point(anchor.X - (fixedPoint.X * clamped), anchor.Y - (fixedPoint.Y * clamped)));
    }

    /// <summary>
    /// The viewport showing all of <paramref name="bounds"/> centred in <paramref name="view"/>, never above 100 %:
    /// a small map opens at its real size rather than blown up ("a map opens fitted").
    /// </summary>
    public static Viewport Fit(LayoutRect bounds, Size view, double margin = 32)
    {
        double width = Math.Max(1, view.Width - (2 * margin));
        double height = Math.Max(1, view.Height - (2 * margin));
        double zoom = Math.Clamp(Math.Min(width / Math.Max(1, bounds.Width), height / Math.Max(1, bounds.Height)), MinZoom, 1);

        return new Viewport(zoom, new Point(
            (view.Width / 2) - (bounds.CenterX * zoom),
            (view.Height / 2) - (bounds.CenterY * zoom)));
    }

    public static double NextStep(double zoom, bool zoomIn)
    {
        const double Tolerance = 0.005;
        return zoomIn
            ? Steps.FirstOrDefault(step => step > zoom + Tolerance, MaxZoom)
            : Steps.LastOrDefault(step => step < zoom - Tolerance, MinZoom);
    }

    /// <summary>
    /// The zoom after <paramref name="notches"/> mouse-wheel notches: 3.125 % each, eight to every quarter, so wheeling
    /// always stops at exactly 25, 50, 75, 100, 125 %… An uneven zoom (fitted) first goes to the next step on the grid.
    /// </summary>
    public static double WheelStep(double zoom, int notches)
    {
        double level = (zoom - MinZoom) / WheelGrid;
        double nearest = Math.Round(level);
        double from = Math.Abs(level - nearest) < 1e-6 ? nearest : notches > 0 ? Math.Floor(level) : Math.Ceiling(level);
        return Math.Clamp(MinZoom + ((from + notches) * WheelGrid), MinZoom, MaxZoom);
    }

    /// <summary>
    /// <paramref name="to"/>, unless the way from <paramref name="from"/> passes a quarter (50, 75, 100 %…): then that
    /// quarter, so continuous wheel input (a trackpad, a smoothed mouse) also stops on each of them.
    /// </summary>
    public static double StopAtQuarter(double from, double to)
    {
        const double Quarter = 0.25;
        const double Tolerance = 1e-6;
        double mark = to > from
            ? Math.Floor((from / Quarter) + Tolerance + 1) * Quarter
            : Math.Ceiling((from / Quarter) - Tolerance - 1) * Quarter;
        return to > from ? Math.Min(to, mark) : Math.Max(to, mark);
    }

    private const double WheelGrid = 0.25 / 8;
}
