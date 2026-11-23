using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using MindBuzz.App.Canvas;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.App.Tests;

public sealed class ViewportTests
{
    /// <summary>No endless canvas: panning stops when the map has just left the view.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Panning_far_away_stops_where_the_map_has_just_left_the_view()
    {
        var s = new Session();
        Session.Pump();

        foreach (var away in new[] { new Avalonia.Vector(-20_000, 0), new Avalonia.Vector(20_000, 0), new Avalonia.Vector(0, -20_000), new Avalonia.Vector(0, 20_000) })
        {
            s.Canvas.SetViewport(s.Canvas.Viewport.PanBy(away), animate: false);

            Core.Layout.LayoutRect content = s.Canvas.ContentBounds!.Value;
            Viewport v = s.Canvas.Viewport;
            double left = (content.X * v.Zoom) + v.Origin.X, right = (content.Right * v.Zoom) + v.Origin.X;
            double top = (content.Y * v.Zoom) + v.Origin.Y, bottom = (content.Bottom * v.Zoom) + v.Origin.Y;
            Assert.True(right >= -0.5 && left <= s.Canvas.Bounds.Width + 0.5, $"x {left}..{right}");
            Assert.True(bottom >= -0.5 && top <= s.Canvas.Bounds.Height + 0.5, $"y {top}..{bottom}");
            Assert.True(Math.Abs(right) < 1 || Math.Abs(left - s.Canvas.Bounds.Width) < 1 || Math.Abs(bottom) < 1 || Math.Abs(top - s.Canvas.Bounds.Height) < 1, "It stops right at the edge.");

            s.Canvas.FitToWindow();
            Session.Pump();
        }
    }

    [Fact]
    public void Map_and_screen_coordinates_convert_both_ways()
    {
        var viewport = new Viewport(2, new Point(100, 50));

        Point screen = viewport.ToScreen(new LayoutPoint(10, -5));
        LayoutPoint back = viewport.ToMap(screen);

        Assert.Equal(new Point(120, 40), screen);
        Assert.Equal(new LayoutPoint(10, -5), back);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(3)]
    public void Zooming_keeps_the_point_under_the_pointer_in_place(double zoom)
    {
        var viewport = new Viewport(1, new Point(400, 300));
        var pointer = new Point(250, 120);
        LayoutPoint before = viewport.ToMap(pointer);

        LayoutPoint after = viewport.ZoomAround(pointer, zoom).ToMap(pointer);

        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void Zoom_is_clamped_to_its_range()
    {
        var viewport = new Viewport(1, default);

        Assert.Equal(2, viewport.ZoomAround(default, 50).Zoom);
        Assert.Equal(0.25, viewport.ZoomAround(default, 0.001).Zoom);
    }

    [Fact]
    public void A_small_map_fits_at_100_percent_and_a_large_one_shrinks_to_fit()
    {
        var view = new Size(1000, 800);

        Viewport small = Viewport.Fit(new LayoutRect(-50, -20, 100, 40), view);
        Viewport large = Viewport.Fit(new LayoutRect(-2000, -1000, 4000, 2000), view);

        Assert.Equal(1, small.Zoom);
        Assert.True(large.Zoom < 1);
        Assert.Equal(new Point(500, 400), large.ToScreen(new LayoutPoint(0, 0)));
    }

    [Theory]
    [InlineData(1, true, 1.25)]
    [InlineData(1, false, 0.9)]
    [InlineData(0.8, true, 0.9)]
    [InlineData(1.5, true, 1.75)]
    [InlineData(2, true, 2)]
    [InlineData(0.25, false, 0.25)]
    public void Zoom_steps_follow_the_step_list(double from, bool zoomIn, double expected)
    {
        Assert.Equal(expected, Viewport.NextStep(from, zoomIn));
    }
}

public sealed class SpatialGridTests
{
    private static MapLayout SampleLayout() =>
        new LayoutEngine(new TopicText()).Layout(SyntheticMap.MapGenerator.Generate(new SyntheticMap.MapSpec(Topics: 400, Seed: 3)).Maps[0]);

    [AvaloniaFact]
    public void A_query_finds_exactly_the_topics_a_full_scan_finds()
    {
        MapLayout layout = SampleLayout();
        var grid = new SpatialGrid(layout);
        var area = new LayoutRect(-300, -200, 500, 300);

        List<int> expected = [.. Enumerable.Range(0, layout.Boxes.Count).Where(i => layout.Boxes[i].Bounds.Intersects(area))];

        Assert.Equal(expected, grid.TopicsIn(area));
    }

    [AvaloniaFact]
    public void Hit_testing_finds_the_topic_under_a_point_and_nothing_between_topics()
    {
        MapLayout layout = SampleLayout();
        var grid = new SpatialGrid(layout);
        TopicBox some = layout.Boxes[17];

        Assert.Equal(some.Id, grid.TopicAt(new LayoutPoint(some.Bounds.CenterX, some.Bounds.CenterY))?.Id);
        Assert.Null(grid.TopicAt(new LayoutPoint(layout.Bounds.Right + 100, 0)));
    }

    [AvaloniaTheory]
    [InlineData(0.25)]
    [InlineData(1)]
    [InlineData(2.5)]
    public void Hit_testing_through_the_viewport_works_at_any_zoom(double zoom)
    {
        MapLayout layout = SampleLayout();
        var grid = new SpatialGrid(layout);
        TopicBox target = layout.Boxes[42];
        var viewport = new Viewport(zoom, new Point(640, 400));

        Point onScreen = viewport.ToScreen(new LayoutPoint(target.Bounds.CenterX, target.Bounds.CenterY));

        Assert.Equal(target.Id, grid.TopicAt(viewport.ToMap(onScreen))?.Id);
    }
}

public sealed class TopicTextTests
{
    [AvaloniaFact]
    public void Text_wraps_at_the_given_width_and_is_never_empty_height()
    {
        var text = new TopicText();

        LayoutSize oneLine = text.Measure("Short", TopicLevel.Sub, 240);
        LayoutSize wrapped = text.Measure(string.Join(' ', Enumerable.Repeat("wrapping", 40)), TopicLevel.Sub, 240);
        LayoutSize empty = text.Measure(string.Empty, TopicLevel.Sub, 240);

        Assert.True(wrapped.Width <= 240);
        Assert.True(wrapped.Height > oneLine.Height * 3);
        Assert.Equal(oneLine.Height, empty.Height);
    }

    [AvaloniaFact]
    public void Levels_differ_in_size_so_hue_is_never_the_only_cue()
    {
        var text = new TopicText();

        double central = text.Measure("Topic", TopicLevel.Central, 300).Height;
        double main = text.Measure("Topic", TopicLevel.Main, 300).Height;
        double sub = text.Measure("Topic", TopicLevel.Sub, 300).Height;

        Assert.True(central > main && main > sub);
    }

    [AvaloniaFact]
    public void Shaped_text_is_kept_only_for_recently_drawn_topics()
    {
        var text = new TopicText();

        for (int frame = 0; frame < 10; frame++)
        {
            text.BeginFrame();
            for (int i = 0; i < 500; i++)
            {
                text.Get($"topic {frame} {i}", TopicLevel.Sub, 240);
            }
        }

        Assert.True(text.ShapedCount <= 2_000, $"{text.ShapedCount} shaped layouts are held.");
    }
}

public sealed class CanvasRenderTests
{
    /// <summary>
    /// The canvas on a generated map in both themes: frames to review. Also proves the brushes
    /// are resolved at draw time: the two frames must differ.
    /// </summary>
    [AvaloniaFact]
    public void A_generated_map_renders_in_both_themes_and_they_differ()
    {
        Document document = SyntheticMap.MapGenerator.Generate(new SyntheticMap.MapSpec(Topics: 45, MaxDepth: 3, MainTopics: 6, MarkerRatio: 0.2, NoteRatio: 0.15, CollapsedRatio: 0.1, Seed: 21));
        string path = Path.Join(Render.ArtifactDirectory, "sample.mbz");
        MbzFile.Save(path, document, "tests");

        string light = Render.Save(new Views.MainWindow(HostPlatform.MacOS, new FakePlatformShell(), new AppOptions(path)), ThemeVariant.Light, "canvas");
        string dark = Render.Save(new Views.MainWindow(HostPlatform.MacOS, new FakePlatformShell(), new AppOptions(path)), ThemeVariant.Dark, "canvas");

        Assert.NotEqual(File.ReadAllBytes(light), File.ReadAllBytes(dark));
    }

    [AvaloniaFact]
    public void A_new_document_shows_its_central_topic_fitted_at_100_percent()
    {
        var window = new Views.MainWindow(HostPlatform.Windows, new FakePlatformShell());
        Render.Save(window, ThemeVariant.Light, "canvas-new-document");

        MapCanvas canvas = window.FindControl<MapCanvas>("Canvas")!;
        Assert.Single(canvas.CurrentLayout!.Boxes);
        Assert.Equal(1, canvas.Viewport.Zoom);
        Assert.Equal(1, window.ViewModel.Zoom);
    }
}
