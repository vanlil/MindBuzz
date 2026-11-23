using MindBuzz.App.Localization;
using MindBuzz.App.Services;
using MindBuzz.App.ViewModels;
using Xunit;

namespace MindBuzz.App.Tests;

public sealed class MainViewModelTests
{
    private readonly FakeUiServices _ui = new();
    private readonly FakePlatformShell _shell = new();

    private MainViewModel Create(HostPlatform platform = HostPlatform.Windows) => new(_ui, _shell, platform);

    [Fact]
    public void About_shows_the_version_the_repository_link_the_licence_and_the_notices()
    {
        Create().Execute(Commands.CommandId.About);

        MessageRequest about = Assert.Single(_ui.Messages);
        Assert.Contains(AppInfo.Version, about.Version, StringComparison.Ordinal);
        Assert.Equal(AppInfo.RepositoryUrl, about.LinkText);
        Assert.Equal(Strings.About_Legal, about.Legal);
        Assert.False(string.IsNullOrWhiteSpace(about.Notices));
    }

    [Fact]
    public void The_about_link_goes_through_the_platform_shell()
    {
        Create().Execute(Commands.CommandId.About);

        _ui.Messages[0].OpenLink!();

        Assert.Equal([AppInfo.RepositoryUrl], _shell.OpenedUrls);
    }

    [Fact]
    public void The_version_comes_from_the_build_and_is_never_empty()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void The_bundled_notices_are_embedded_and_name_what_the_app_ships()
    {
        // A missing resource hides the About section silently; this test is what notices it first.
        Assert.Contains("Avalonia", AppInfo.ThirdPartyNotices, StringComparison.Ordinal);
        Assert.Contains("Inter", AppInfo.ThirdPartyNotices, StringComparison.Ordinal);
        Assert.Contains("SIL Open Font License", AppInfo.ThirdPartyNotices, StringComparison.Ordinal);
    }

    [Fact]
    public void Quit_asks_the_window_to_close()
    {
        Create().Execute(Commands.CommandId.Quit);

        Assert.Equal(1, _ui.ExitRequests);
    }

    [Theory]
    [InlineData(HostPlatform.Windows, "F1")]
    [InlineData(HostPlatform.MacOS, "F1")]
    public void The_shortcuts_dialog_lists_commands_with_gestures_formatted_for_the_platform(HostPlatform platform, string expected)
    {
        Create(platform).Execute(Commands.CommandId.KeyboardShortcuts);

        IReadOnlyList<ShortcutRow> rows = Assert.Single(_ui.ShortcutDialogs);
        ShortcutRow shortcuts = Assert.Single(rows, row => row.Command == "Keyboard Shortcuts");
        Assert.Equal(expected, shortcuts.Gestures);
        Assert.DoesNotContain(rows, row => row.Command.Contains('_', StringComparison.Ordinal));
    }

    [Fact]
    public void Commands_without_a_gesture_on_the_platform_are_not_listed()
    {
        Create(HostPlatform.Windows).Execute(Commands.CommandId.KeyboardShortcuts);

        Assert.DoesNotContain(_ui.ShortcutDialogs[0], row => row.Command == "About MindBuzz");
    }
}
