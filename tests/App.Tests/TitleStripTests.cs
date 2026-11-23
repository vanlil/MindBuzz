using MindBuzz.App.Views;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// Every platform's title strip, asserted from whichever machine runs the tests. Each assertion
/// pins a behaviour that once broke silently on a platform nobody was looking at.
/// </summary>
public sealed class TitleStripTests
{
    [Fact]
    public void Linux_keeps_the_window_managers_title_bar_so_the_name_never_appears_twice()
    {
        TitleStripLayout layout = TitleStripLayout.For(HostPlatform.Linux);

        Assert.False(layout.ExtendClientArea);
        Assert.False(layout.ShowTitleText);
        Assert.True(layout.ShowInWindowMenu);
    }

    [Fact]
    public void MacOS_leaves_room_for_the_traffic_lights_and_keeps_the_menu_in_the_menu_bar()
    {
        TitleStripLayout layout = TitleStripLayout.For(HostPlatform.MacOS);

        Assert.True(layout.ExtendClientArea);
        Assert.True(layout.ShowTitleText);
        Assert.False(layout.ShowInWindowMenu);
        Assert.True(layout.ContentMargin.Left > 60, "The traffic lights sit at the left and need their space.");
        Assert.True(layout.ContentMargin.Right < layout.ContentMargin.Left);
    }

    [Fact]
    public void Windows_leaves_room_for_the_caption_buttons_at_the_right()
    {
        TitleStripLayout layout = TitleStripLayout.For(HostPlatform.Windows);

        Assert.True(layout.ExtendClientArea);
        Assert.True(layout.ShowTitleText);
        Assert.True(layout.ShowInWindowMenu);
        Assert.True(layout.ContentMargin.Right > 100, "Minimise, maximise and close sit at the right.");
    }
}
