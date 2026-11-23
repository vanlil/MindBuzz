using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.Views;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Distraction-free full screen and Copy Map as Image.</summary>
public sealed class FullScreenTests
{
    private static Control[] Bars(Session s) =>
    [
        s.Window.FindControl<Border>("TitleStrip")!,
        s.Window.FindControl<Border>("ToolbarRow")!,
        s.Window.FindControl<SheetTabs>("Tabs")!,
        s.Window.FindControl<Border>("StatusBar")!,
    ];

    [AvaloniaFact]
    public void Full_screen_shows_only_the_map_and_the_same_key_brings_everything_back()
    {
        var s = new Session(HostPlatform.Windows);

        s.Press(Key.F11);

        Assert.True(s.Main.IsFullScreen);
        Assert.Equal(WindowState.FullScreen, s.Window.WindowState);
        Assert.All(Bars(s), bar => Assert.False(bar.IsVisible));

        s.Press(Key.F11);

        Assert.False(s.Main.IsFullScreen);
        Assert.NotEqual(WindowState.FullScreen, s.Window.WindowState);
        Assert.All(Bars(s), bar => Assert.True(bar.IsVisible));
    }

    [AvaloniaFact]
    public void Search_and_the_detail_view_open_inside_full_screen()
    {
        var s = new Session();
        s.Main.Execute(CommandId.ToggleFullScreen);

        s.Main.Execute(CommandId.Find);
        s.Main.Execute(CommandId.ToggleTopicPanel);
        Session.Pump();

        Assert.True(s.Main.IsFullScreen);
        Assert.True(s.Window.FindControl<SearchPanel>("SearchPanelView")!.IsVisible);
        Assert.True(s.Window.FindControl<TopicPanel>("TopicPanelView")!.IsVisible);
    }

    /// <summary>Esc peels off what it can cancel first: a multi-selection, then full screen.</summary>
    [AvaloniaFact]
    public void Esc_on_the_map_leaves_full_screen_when_it_has_nothing_else_to_cancel()
    {
        var s = new Session();
        s.Main.Execute(CommandId.NewSubtopic);
        s.Type("One");
        s.Press(Key.Enter);
        s.Main.ToggleSelected(s.Central.Id);
        s.Main.Execute(CommandId.ToggleFullScreen);
        Session.Pump();

        s.Press(Key.Escape);
        Assert.True(s.Main.IsFullScreen);
        Assert.Single(s.Main.SelectedTopics);

        s.Press(Key.Escape);
        Assert.False(s.Main.IsFullScreen);
    }

    [AvaloniaFact]
    public void Esc_while_typing_cancels_the_text_and_stays_in_full_screen()
    {
        var s = new Session();
        s.Main.Execute(CommandId.ToggleFullScreen);
        s.Press(Key.F2);
        s.Type(" more");

        s.Press(Key.Escape);

        Assert.False(s.Canvas.IsEditorOpen);
        Assert.True(s.Main.IsFullScreen);
    }

    [AvaloniaFact]
    public void Leaving_full_screen_the_systems_way_ends_the_mode()
    {
        var s = new Session();
        s.Main.Execute(CommandId.ToggleFullScreen);

        s.Window.WindowState = WindowState.Normal;

        Assert.False(s.Main.IsFullScreen);
        Assert.All(Bars(s), bar => Assert.True(bar.IsVisible));
    }

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows, "F11")]
    [InlineData(HostPlatform.MacOS, "⌃⌘F")]
    public void Both_commands_are_in_the_menus_and_on_the_toolbar(HostPlatform platform, string key)
    {
        var s = new Session(platform);
        IReadOnlyList<ToolbarItem> toolbar = MenuLayout.Toolbar(s.Main.Commands, platform);

        Assert.Contains(toolbar, item => item.Command == s.Main.Commands[CommandId.CopyMapImage].Command && item.IconKey == "IconCopyImage");
        ToolbarItem full = Assert.Single(toolbar, item => item.Command == s.Main.Commands[CommandId.ToggleFullScreen].Command);
        Assert.EndsWith($"({key})", full.Tooltip, StringComparison.Ordinal);
        Assert.Contains(s.Main.CanvasMenu, entry => entry.Command == s.Main.Commands[CommandId.ToggleFullScreen].Command);
    }

    [AvaloniaFact]
    public async Task Copy_map_as_image_puts_a_picture_on_the_clipboard()
    {
        var s = new Session();

        s.Main.Execute(CommandId.CopyMapImage);
        Session.Pump();

        using Bitmap? picture = await Avalonia.Input.Platform.ClipboardExtensions.TryGetBitmapAsync(s.Window.Clipboard!);
        Assert.NotNull(picture);
        Assert.True(picture.PixelSize.Width > 100);
        Assert.Equal(Localization.Strings.Status_MapImageCopied, s.Main.StatusText);
    }

    [AvaloniaFact]
    public void Full_screen_with_search_and_detail_view_renders_in_both_themes()
    {
        var frames = new List<string>();
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            s.Main.Execute(CommandId.NewSample);
            Session.Pump();
            s.Main.Execute(CommandId.ToggleFullScreen);
            s.Main.Execute(CommandId.ToggleTopicPanel);
            Session.Pump();
            frames.Add(Render.Save(s.Window, theme, "full-screen"));
        }

        Assert.NotEqual(File.ReadAllBytes(frames[0]), File.ReadAllBytes(frames[1]));
    }
}
