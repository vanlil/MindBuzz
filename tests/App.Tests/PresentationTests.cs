using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MindBuzz.App.Services;
using MindBuzz.App.Views;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// Renders every screen in both themes to PNG for review. The frames land in
/// <c>tests/App.Tests/bin/Debug/net10.0/render</c>. The assertions check wiring, never colour values.
/// </summary>
public sealed class PresentationTests
{
    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    [InlineData(HostPlatform.Linux)]
    public void The_main_window_renders_in_both_themes_and_they_do_not_look_the_same(HostPlatform platform)
    {
        string name = $"main-{platform.ToString().ToLowerInvariant()}";
        string light = Render.Save(new MainWindow(platform, new FakePlatformShell()), ThemeVariant.Light, name);
        string dark = Render.Save(new MainWindow(platform, new FakePlatformShell()), ThemeVariant.Dark, name);

        // Every colour is resolved through the semantic layer at draw time, so the two frames must differ. A
        // brush captured once and reused is what this catches.
        Assert.NotEqual(File.ReadAllBytes(light), File.ReadAllBytes(dark));
    }

    [AvaloniaFact]
    public void The_about_dialog_renders_in_both_themes()
    {
        var ui = new FakeUiServices();
        new ViewModels.MainViewModel(ui, new FakePlatformShell(), HostPlatform.Windows).Execute(Commands.CommandId.About);

        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var dialog = new DialogWindow(ui.Messages[0]);
            string path = Render.Save(dialog, theme, "about");
            Assert.True(new FileInfo(path).Length > 0);
        }
    }

    [AvaloniaFact]
    public void The_about_dialog_shows_the_notices_when_there_are_some()
    {
        var ui = new FakeUiServices();
        new ViewModels.MainViewModel(ui, new FakePlatformShell(), HostPlatform.Windows).Execute(Commands.CommandId.About);
        var dialog = new DialogWindow(ui.Messages[0]);

        dialog.Show();
        SelectableTextBlock notices = dialog.GetVisualDescendants().OfType<SelectableTextBlock>().Single(block => block.Name == "NoticesText");

        Assert.True(notices.IsEffectivelyVisible);
        Assert.Contains("Avalonia", notices.Text, StringComparison.Ordinal);
        dialog.Close();
    }

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    public void The_shortcuts_dialog_renders_in_both_themes(HostPlatform platform)
    {
        var ui = new FakeUiServices();
        new ViewModels.MainViewModel(ui, new FakePlatformShell(), platform).Execute(Commands.CommandId.KeyboardShortcuts);

        foreach (ThemeVariant theme in Render.BothThemes)
        {
            string path = Render.Save(new ShortcutsWindow(ui.ShortcutDialogs[0]), theme, $"shortcuts-{platform.ToString().ToLowerInvariant()}");
            Assert.True(new FileInfo(path).Length > 0);
        }
    }

    [AvaloniaFact]
    public void The_save_prompt_renders_in_both_themes()
    {
        var request = new ChoiceRequest(
            Localization.Strings.SavePrompt_Title,
            string.Format(System.Globalization.CultureInfo.CurrentCulture, Localization.Strings.SavePrompt_MessageFormat, "Plan"),
            Localization.Strings.SavePrompt_Detail,
            [Localization.Strings.Answer_DontSave, Localization.Strings.Answer_Cancel, Localization.Strings.Answer_Save],
            DefaultAnswer: 2,
            CancelAnswer: 1);

        foreach (ThemeVariant theme in Render.BothThemes)
        {
            Assert.True(new FileInfo(Render.Save(new ChoiceWindow(request), theme, "save-prompt")).Length > 0);
        }
    }

    /// <summary>A cancel answer of its own (the recovery question): Esc closes the dialog with it, no button
    /// pressed.</summary>
    [AvaloniaFact]
    public void Esc_gives_a_cancel_answer_that_is_none_of_the_buttons()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new ChoiceWindow(new ChoiceRequest("Title", "Message", null, ["A", "B", "C"], DefaultAnswer: 2, CancelAnswer: 3));
        Task<int> answer = dialog.ShowDialog<int>(owner);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        dialog.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(answer.IsCompleted);
        Assert.Equal(3, answer.Result);
    }

    [AvaloniaFact]
    public void The_in_window_menu_is_hidden_on_macOS_and_shown_elsewhere()
    {
        var mac = new MainWindow(HostPlatform.MacOS, new FakePlatformShell());
        var windows = new MainWindow(HostPlatform.Windows, new FakePlatformShell());

        Assert.False(mac.FindControl<Menu>("WindowMenu")!.IsVisible);
        Assert.True(windows.FindControl<Menu>("WindowMenu")!.IsVisible);
    }

    /// <summary>
    /// Every icon rendered at 64 px and at real size, so a human looks at the geometry instead of reasoning
    /// about path data. Also asserts each icon has real bounds in both themes.
    /// </summary>
    [AvaloniaFact]
    public void Every_icon_renders_with_real_bounds_in_both_themes()
    {
        string[] keys = ["IconKeyboard", "IconInfo", "IconMindMap", "IconLink", "IconNote"];

        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 24, Margin = new Avalonia.Thickness(16) };
            foreach (string key in keys)
            {
                foreach (double size in new[] { 64.0, 16.0 })
                {
                    row.Children.Add(new Avalonia.Controls.Shapes.Path
                    {
                        Classes = { "icon" },
                        Width = size,
                        Height = size,
                        StrokeThickness = size > 16 ? 1.3 * size / 16 : 1.3,
                        Data = (Avalonia.Media.Geometry)Avalonia.Application.Current!.FindResource(key)!,
                    });
                }
            }

            var window = new Window { Width = 520, Height = 120, Content = row };
            Render.Save(window, theme, "icons");

            Assert.All(row.Children.OfType<Avalonia.Controls.Shapes.Path>(), path => Assert.True(path.Data!.Bounds.Width > 4, "An icon has no real bounds."));
        }
    }
}
