using Avalonia.Input;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The menu arrangement per platform, asserted from any host.</summary>
public sealed class MenuLayoutTests
{
    private static MenuModel Menus(HostPlatform platform) =>
        new MainViewModel(new FakeUiServices(), new FakePlatformShell(), platform).Menus;

    private static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> entries) =>
        entries.SelectMany(entry => entry.HasChildren ? Flatten(entry.Children!).Prepend(entry) : [entry]);

    private static int CountAbout(IEnumerable<MenuEntry> entries) =>
        Flatten(entries).Count(entry => entry.PlainHeader == MenuText.WithoutAccessKey(Localization.Strings.Command_About));

    [Fact]
    public void On_macOS_about_is_in_the_application_menu_and_nowhere_else()
    {
        MenuModel menus = Menus(HostPlatform.MacOS);

        Assert.Equal(1, CountAbout(menus.MacApplicationItems));
        Assert.Equal(0, CountAbout(menus.TopLevel));
    }

    [Theory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.Linux)]
    public void Elsewhere_about_is_in_help_and_there_is_no_application_menu(HostPlatform platform)
    {
        MenuModel menus = Menus(platform);

        Assert.Empty(menus.MacApplicationItems);
        MenuEntry help = menus.TopLevel.Single(menu => menu.Header == Localization.Strings.Menu_Help);
        Assert.Equal(1, CountAbout(help.Children!));
    }

    [Fact]
    public void On_macOS_there_is_no_exit_item_because_the_application_menu_has_quit()
    {
        string exit = MenuText.WithoutAccessKey(Localization.Strings.Command_Quit);

        Assert.DoesNotContain(Flatten(Menus(HostPlatform.MacOS).TopLevel), entry => entry.PlainHeader == exit);
    }

    [Theory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    [InlineData(HostPlatform.Linux)]
    public void No_menu_is_empty_and_no_separator_leads_trails_or_doubles(HostPlatform platform)
    {
        foreach (MenuEntry menu in Menus(platform).TopLevel)
        {
            Assert.True(menu.HasChildren, $"{menu.Header} is empty");
            IReadOnlyList<MenuEntry> items = menu.Children!;
            Assert.False(items[0].IsSeparator);
            Assert.False(items[^1].IsSeparator);
            Assert.DoesNotContain(items.Zip(items.Skip(1)), pair => pair.First.IsSeparator && pair.Second.IsSeparator);
        }
    }

    [Theory]
    [InlineData("_File", "File")]
    [InlineData("E_xit", "Exit")]
    [InlineData("Save__as", "Save_as")]
    [InlineData("Plain", "Plain")]
    public void Access_key_underscores_are_removed_for_the_macOS_menu_bar(string header, string expected)
    {
        Assert.Equal(expected, MenuText.WithoutAccessKey(header));
    }

    [Fact]
    public void On_macOS_gestures_in_the_native_menu_are_not_bound_again_by_the_window()
    {
        var main = new MainViewModel(new FakeUiServices(), new FakePlatformShell(), HostPlatform.MacOS);
        var nativeGestures = Flatten(main.Menus.TopLevel).Select(entry => entry.Gesture).OfType<KeyGesture>().ToHashSet();

        var windowBindings = KeyBindingPlan.For(main.Commands, main.Menus, HostPlatform.MacOS);

        Assert.NotEmpty(nativeGestures);
        Assert.DoesNotContain(windowBindings, binding => nativeGestures.Contains(binding.Gesture));
    }

    [Theory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.Linux)]
    public void Elsewhere_the_window_binds_every_gesture_itself(HostPlatform platform)
    {
        var main = new MainViewModel(new FakeUiServices(), new FakePlatformShell(), platform);
        int gestureCount = main.Commands.All.Sum(command => command.Gestures.For(platform).Count);

        int bound = KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Window).Count
            + KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Canvas).Count
            + KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Panel).Count
            + KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Text).Count;

        Assert.Equal(gestureCount, bound);
    }
}
