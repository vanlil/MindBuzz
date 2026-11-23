using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MindBuzz.App.Canvas;
using MindBuzz.App.Commands;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Export as PNG.</summary>
public sealed class ExportTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mindbuzz-export-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static Avalonia.Controls.Canvas Host(Session s) => s.Window.FindControl<Avalonia.Controls.Canvas>("ExportHost")!;

    private static Map Generated(int topics, double collapsed = 0) =>
        SyntheticMap.MapGenerator.Generate(new SyntheticMap.MapSpec(Topics: topics, NoteRatio: 0.2, LinkRatio: 0.2, MarkerRatio: 0.3, Relationships: 6, CollapsedRatio: collapsed, Seed: 9)).Maps[0];

    /// <summary>The picture's pixels, 4 bytes each (BGRA, so byte 3 is the first pixel's alpha).</summary>
    private static byte[] Pixels(Bitmap bitmap)
    {
        int stride = bitmap.PixelSize.Width * 4;
        byte[] bytes = new byte[stride * bitmap.PixelSize.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        return bytes;
    }

    [AvaloniaFact]
    public void The_picture_holds_the_whole_map_with_a_margin_at_twice_the_scale()
    {
        var s = new Session();
        Map map = Generated(200);
        LayoutRect content = new LayoutEngine(new TopicText()).Layout(map).Bounds;

        using RenderTargetBitmap bitmap = MapExport.Render(Host(s), map, whiteBackground: true);

        Assert.True(bitmap.PixelSize.Width >= (content.Width + (2 * MapExport.Margin)) * 2 - 2);
        Assert.True(bitmap.PixelSize.Height >= (content.Height + (2 * MapExport.Margin)) * 2 - 2);
        Assert.Empty(Host(s).Children);
    }

    [AvaloniaFact]
    public void A_transparent_background_is_see_through_and_a_white_one_is_not()
    {
        var s = new Session();
        Map map = Generated(30);

        using RenderTargetBitmap clear = MapExport.Render(Host(s), map, whiteBackground: false);
        using RenderTargetBitmap white = MapExport.Render(Host(s), map, whiteBackground: true);

        Assert.Equal(0, Pixels(clear)[3]);
        Assert.Equal(255, Pixels(white)[3]);
    }

    [AvaloniaFact]
    public void The_picture_is_the_same_in_the_dark_theme()
    {
        var s = new Session();
        Map map = Generated(30);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        using RenderTargetBitmap light = MapExport.Render(Host(s), map, whiteBackground: true);
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        using RenderTargetBitmap dark = MapExport.Render(Host(s), map, whiteBackground: true);
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;

        Assert.Equal(Pixels(light), Pixels(dark));
    }

    [AvaloniaFact]
    public void Collapsed_branches_stay_collapsed_in_the_picture()
    {
        var s = new Session();
        Map map = Generated(200);
        Map collapsed = map with { Root = map.Root with { Children = [.. map.Root.Children.Select(c => c with { IsCollapsed = c.HasChildren })] } };

        using RenderTargetBitmap full = MapExport.Render(Host(s), map, whiteBackground: true);
        using RenderTargetBitmap folded = MapExport.Render(Host(s), collapsed, whiteBackground: true);

        Assert.True(folded.PixelSize.Height < full.PixelSize.Height);
    }

    [Fact]
    public void A_huge_map_is_drawn_smaller_to_stay_within_the_limits()
    {
        Assert.Equal(2, MapExport.ScaleFor(1000, 800));
        double scale = MapExport.ScaleFor(9000, 30000);
        Assert.True(30000 * scale <= MapExport.MaxSide + 0.5);
        Assert.True(9000 * 30000 * scale * scale <= MapExport.MaxPixels + 1);
    }

    [AvaloniaFact]
    public async Task File_export_asks_for_the_background_then_the_place_and_writes_a_png()
    {
        var ui = new FakeUiServices();
        var s = new Session(ui: ui);
        s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "Launch plan"));
        string path = Path.Join(_folder, "Launch");

        ui.Answers.Enqueue(1);
        ui.PickedPaths.Enqueue(path);
        s.Main.Execute(CommandId.ExportPng);
        await Task.Yield();
        Session.Pump();

        Assert.True(File.Exists(path + ".png"));
        Assert.Equal("Launch plan", ui.SuggestedNames[^1]);
        using (var bitmap = new Bitmap(path + ".png"))
        {
            Assert.True(bitmap.PixelSize.Width > 100);
        }

        ui.Answers.Enqueue(0);
        s.Main.Execute(CommandId.ExportPng);
        await Task.Yield();
        Assert.Single(Directory.GetFiles(_folder));
    }

    /// <summary>A generated 1,000-topic map, white and transparent, saved next to the renders for
    /// review.</summary>
    [AvaloniaFact]
    public void A_thousand_topic_map_exports_for_review()
    {
        var s = new Session();
        Map map = Generated(1000, collapsed: 0.1);

        using RenderTargetBitmap white = MapExport.Render(Host(s), map, whiteBackground: true);
        using RenderTargetBitmap clear = MapExport.Render(Host(s), Generated(40), whiteBackground: false);
        white.Save(Path.Join(Render.ArtifactDirectory, "export-1000-white.png"), new PngBitmapEncoderOptions());
        clear.Save(Path.Join(Render.ArtifactDirectory, "export-40-transparent.png"), new PngBitmapEncoderOptions());

        Assert.True(white.PixelSize.Width > 1000);
    }
}
