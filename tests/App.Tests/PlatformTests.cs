using System.ComponentModel;
using System.Reflection;
using MindBuzz.App.Platform;
using MindBuzz.App.Services;
using Xunit;

namespace MindBuzz.App.Tests;

public sealed class PlatformTests
{
    [Theory]
    [InlineData("https://github.com/vanlil/MindBuzz")]
    [InlineData("http://example.com/a?b=c")]
    [InlineData("mailto:someone@example.com")]
    public void Web_and_mail_addresses_may_be_opened(string url)
    {
        Assert.True(SafeUrl.IsAllowed(url));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("example.com")]
    [InlineData("/Applications/Calculator.app")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_refused(string? url)
    {
        Assert.False(SafeUrl.IsAllowed(url));
    }

    [Fact]
    public void A_refused_url_never_reaches_the_shell()
    {
        var recorder = new ProcessStartRecorder();
        var shell = new PlatformShell(recorder.Start);

        Assert.False(shell.OpenUrl("file:///etc/passwd"));
        Assert.Empty(recorder.Started);
    }

    [Fact]
    public void An_allowed_url_is_handed_to_the_os_with_shell_execute()
    {
        var recorder = new ProcessStartRecorder();
        var shell = new PlatformShell(recorder.Start);

        Assert.True(shell.OpenUrl(AppInfo.RepositoryUrl));

        var info = Assert.Single(recorder.Started);
        Assert.Equal(AppInfo.RepositoryUrl, info.FileName);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void A_missing_url_handler_is_not_a_crash()
    {
        // Linux: xdg-open may not be installed, and Process.Start throws for a missing binary.
        var shell = new PlatformShell(_ => throw new Win32Exception(2));

        Assert.False(shell.OpenUrl(AppInfo.RepositoryUrl));
    }

    /// <summary>
    /// Guard: the platform interface's members are pinned, so a new capability cannot creep in unnoticed. Edit
    /// this list deliberately when the interface grows.
    /// </summary>
    [Fact]
    public void The_platform_shell_has_exactly_the_agreed_members()
    {
        Assert.Equal(["OpenTemporaryPdf", "OpenUrl"], MemberNames(typeof(IPlatformShell)));
    }

    /// <summary>Guard: the window seam for view models, pinned the same way.</summary>
    [Fact]
    public void The_ui_services_have_exactly_the_agreed_members()
    {
        Assert.Equal(
            ["AskAsync", "AskPasswordAsync", "EditLinkAsync", "GetClipboardAsync", "PickExportPathAsync", "PickOpenPathAsync", "PickSavePathAsync", "RequestExit", "SetClipboardAsync", "ShowCrashAsync", "ShowMessageAsync", "ShowShortcutsAsync", "ShowsDarkTheme", "UseTheme"],
            MemberNames(typeof(IUiServices)));
    }

    private static string[] MemberNames(Type type) =>
        [.. type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(member => member.Name).Order(StringComparer.Ordinal)];
}
