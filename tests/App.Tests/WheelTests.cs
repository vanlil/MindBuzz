using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MindBuzz.App.Canvas;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The wheel and a trackpad pan; Ctrl/⌘ zooms around the pointer and Shift pans sideways with
/// either.</summary>
public sealed class WheelTests
{
    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, -2, true)]
    [InlineData(0, 0.37, false)]
    [InlineData(0.2, 1, false)]
    [InlineData(0, 0, false)]
    public void On_windows_a_notched_wheel_sends_whole_steps(double x, double y, bool notch) =>
        Assert.Equal(notch, MapCanvas.IsWheelNotch(new Vector(x, y), HostPlatform.Windows));

    /// <summary>The values a Bluetooth mouse and the built-in trackpad sent on macOS.</summary>
    [Theory]
    [InlineData(0, -0.020001220703125, true)]
    [InlineData(0, 0.020001220703125, true)]
    [InlineData(0, 0.02, false)]
    [InlineData(0, -0.06, false)]
    [InlineData(0, 0.1, false)]
    [InlineData(0, -0.12, false)]
    [InlineData(-0.02, -0.1, false)]
    [InlineData(0, 0, false)]
    public void On_macos_a_wheel_notch_is_not_a_whole_number_of_pixels(double x, double y, bool notch) =>
        Assert.Equal(notch, MapCanvas.IsWheelNotch(new Vector(x, y), HostPlatform.MacOS));

    [AvaloniaFact]
    public void The_wheel_pans_and_ctrl_or_cmd_with_the_wheel_zooms()
    {
        var s = new Session();
        Point middle = s.Canvas.TranslatePoint(new Point(s.Canvas.Bounds.Width / 2, s.Canvas.Bounds.Height / 2), s.Window)!.Value;
        double zoom = s.Canvas.Viewport.Zoom;

        // One wheel notch as this host's platform sends it.
        double notch = Host.Current == HostPlatform.MacOS ? 0.020001220703125 : 1;
        Point origin = s.Canvas.Viewport.Origin;
        s.Window.MouseWheel(middle, new Vector(0, notch));
        Session.Pump();
        Assert.Equal(zoom, s.Canvas.Viewport.Zoom);
        Assert.NotEqual(origin, s.Canvas.Viewport.Origin);

        origin = s.Canvas.Viewport.Origin;
        s.Window.MouseWheel(middle, new Vector(0.1, 0.4));
        Session.Pump();
        Assert.Equal(zoom, s.Canvas.Viewport.Zoom);
        Assert.NotEqual(origin, s.Canvas.Viewport.Origin);

        s.Window.MouseWheel(middle, new Vector(0, -notch), RawInputModifiers.Shift);
        Session.Pump();
        Assert.Equal(zoom, s.Canvas.Viewport.Zoom);

        RawInputModifiers command = Host.Current == HostPlatform.MacOS ? RawInputModifiers.Meta : RawInputModifiers.Control;
        s.Window.MouseWheel(middle, new Vector(0, notch), command);
        Session.Pump();
        Assert.True(s.Canvas.Viewport.Zoom > zoom, "Ctrl/⌘ with a wheel notch zooms in");

        zoom = s.Canvas.Viewport.Zoom;
        s.Window.MouseWheel(middle, new Vector(0.1, 0.4), command);
        Session.Pump();
        Assert.True(s.Canvas.Viewport.Zoom > zoom, "Ctrl/⌘ with a trackpad zooms in");
    }

    [Theory]
    [InlineData(0.37)]
    [InlineData(0.913)]
    [InlineData(1.9)]
    public void Wheeling_from_any_zoom_passes_exactly_100_percent(double start)
    {
        int towards = start < 1 ? 1 : -1;
        var seen = new List<double>();
        double zoom = start;
        for (int i = 0; i < 40; i++)
        {
            zoom = Viewport.WheelStep(zoom, towards);
            seen.Add(zoom);
        }

        Assert.Contains(seen, z => Math.Abs(z - 1) < 1e-9);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Wheeling_through_the_range_stops_on_every_quarter_in_small_steps(int towards)
    {
        double zoom = towards > 0 ? Viewport.MinZoom : Viewport.MaxZoom;
        var seen = new List<double> { zoom };
        for (int i = 0; i < 100; i++)
        {
            double next = Viewport.WheelStep(zoom, towards);
            Assert.True(Math.Abs(next - zoom) <= 0.032, $"{zoom} to {next} is more than about 3 %");
            zoom = next;
            seen.Add(zoom);
        }

        Assert.Equal(towards > 0 ? 2 : 0.25, zoom);
        foreach (double quarter in new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2 })
        {
            Assert.Contains(seen, z => Math.Abs(z - quarter) < 1e-9);
        }
    }

    [Theory]
    [InlineData(0.98, 1.05, 1)]
    [InlineData(1, 1.05, 1.05)]
    [InlineData(1.3, 1.2, 1.25)]
    [InlineData(1, 0.95, 0.95)]
    [InlineData(0.6, 0.55, 0.55)]
    public void Continuous_wheel_zoom_stops_on_a_quarter_it_passes(double from, double to, double expected)
    {
        Assert.Equal(expected, Viewport.StopAtQuarter(from, to), 9);
    }

    [Fact]
    public void One_notch_in_and_one_out_returns_to_the_same_zoom()
    {
        double zoom = Viewport.WheelStep(Viewport.WheelStep(1, 1), -1);
        Assert.Equal(1, zoom, 9);
    }
}
