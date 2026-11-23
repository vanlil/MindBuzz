using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// Export as PNG: the whole map as it is expanded, with a margin, in the light theme's colours
/// whatever the OS theme, on white or transparent. Drawn by a canvas of its own, so the window's selection, zoom and
/// search marks stay out of the picture.
/// </summary>
internal static class MapExport
{
    /// <summary>Room around the map, in map units.</summary>
    internal const double Margin = 32;

    /// <summary>Two pixels per map unit: sharp on slides and in print, like a Retina screen.</summary>
    internal const double PreferredScale = 2;

    /// <summary>
    /// The largest picture: 64 million pixels (256 MB while it is drawn) and 16,384 pixels a side, which image
    /// programs and Skia handle. A huge map is drawn at a smaller scale to fit.
    /// </summary>
    internal const double MaxPixels = 64_000_000;

    internal const double MaxSide = 16_384;

    /// <summary>The scale a map of <paramref name="width"/> × <paramref name="height"/> map units is drawn
    /// at.</summary>
    internal static double ScaleFor(double width, double height) =>
        Math.Min(PreferredScale, Math.Min(Math.Sqrt(MaxPixels / Math.Max(1, width * height)), MaxSide / Math.Max(width, height)));

    /// <summary>
    /// Draws <paramref name="map"/>. The canvas is placed in <paramref name="host"/>, a clipped spot in the window,
    /// for the time it takes: there it finds the app's theme resources like any other control.
    /// </summary>
    internal static RenderTargetBitmap Render(Panel host, Map map, bool whiteBackground)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(map);

        var canvas = new MapCanvas { IsExport = true, ExportBackground = whiteBackground ? Brushes.White : null, Map = map };
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = canvas, Width = 1, Height = 1 };
        host.Children.Add(scope);
        try
        {
            LayoutRect content = canvas.ContentBounds ?? new LayoutRect(0, 0, 1, 1);
            double width = Math.Ceiling(content.Width + (2 * Margin));
            double height = Math.Ceiling(content.Height + (2 * Margin));
            double scale = ScaleFor(width, height);

            // Drawn at 96 DPI on a canvas as large as the picture, zoomed to the scale. A bitmap at 192 DPI scaled some
            // drawing twice and some once in Avalonia 12.1.6 (topics and text slid away from their lines); the
            // canvas's own zoom draws every scale right, as on screen.
            int pixelWidth = Math.Max(1, (int)Math.Round(width * scale));
            int pixelHeight = Math.Max(1, (int)Math.Round(height * scale));
            scope.Width = pixelWidth;
            scope.Height = pixelHeight;
            scope.Measure(new Size(pixelWidth, pixelHeight));
            scope.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
            canvas.ShowExactly(new Viewport(scale, new Point((Margin - content.X) * scale, (Margin - content.Y) * scale)));

            var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
            bitmap.Render(scope);
            return bitmap;
        }
        finally
        {
            host.Children.Remove(scope);
        }
    }
}
