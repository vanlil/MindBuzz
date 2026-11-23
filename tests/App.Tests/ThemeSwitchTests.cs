using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Controls;
using MindBuzz.App.Commands;
using MindBuzz.App.Views;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The theme follows the OS while the app runs: a switch restyles the open window.</summary>
public sealed class ThemeSwitchTests
{
    [AvaloniaFact]
    public void Switching_the_theme_while_open_restyles_the_canvas_the_bars_and_the_panel()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        try
        {
            var s = new Session();
            s.Main.Execute(CommandId.NewSample);
            s.Main.Execute(CommandId.ToggleTopicPanel);
            Session.Pump();
            byte[] light = Render.Pixels(s.Window);

            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            Session.Pump();
            byte[] dark = Render.Pixels(s.Window);

            Size size = s.Window.ClientSize;
            Assert.True(Render.DiffersNear(light, dark, s.Window, new Point(40, size.Height / 2), 2), "canvas");
            Assert.True(Render.DiffersNear(light, dark, s.Window, new Point(size.Width - 20, size.Height - 120), 2), "panel");
            Assert.True(Render.DiffersNear(light, dark, s.Window, new Point(size.Width / 2, 50), 2), "toolbar");
        }
        finally
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    /// <summary>
    /// The macOS menu bar's colour pictures follow the theme in place. Replacing the whole menu bar during a theme
    /// change made Avalonia throw and the window kept its old theme (seen on macOS).
    /// </summary>
    [AvaloniaFact]
    public void The_menu_pictures_are_redrawn_in_place_for_the_new_theme()
    {
        var s = new Session(HostPlatform.MacOS);
        NativeMenu menu = NativeMenuFactory.Build(s.Main.Menus.TopLevel, ThemeVariant.Light);
        NativeMenuItem[] pictured = [.. Pictured(menu)];
        Avalonia.Media.Imaging.Bitmap?[] before = [.. pictured.Select(item => item.Icon)];

        NativeMenuFactory.Recolor(menu, s.Main.Menus.TopLevel, ThemeVariant.Dark);

        Assert.NotEmpty(pictured);
        Assert.Equal(pictured, Pictured(menu));
        Assert.All(pictured.Zip(before), pair => Assert.NotSame(pair.Second, pair.First.Icon));
    }

    private static IEnumerable<NativeMenuItem> Pictured(NativeMenu menu) =>
        menu.Items.OfType<NativeMenuItem>().SelectMany(item =>
            (item.Icon is null ? [] : new[] { item }).Concat(item.Menu is { } sub ? Pictured(sub) : []));
}

/// <summary>View → Light Theme / Dark Theme, for this session only.</summary>
public sealed class ThemeCommandTests
{
    [Fact]
    public void The_theme_in_use_is_greyed_out_and_the_other_switches()
    {
        var ui = new FakeUiServices();
        var main = new ViewModels.MainViewModel(ui, new FakePlatformShell(), HostPlatform.MacOS);

        Assert.False(main.Commands[Commands.CommandId.LightTheme].Command.CanExecute(null));
        Assert.True(main.Commands[Commands.CommandId.DarkTheme].Command.CanExecute(null));

        main.Commands[Commands.CommandId.DarkTheme].Command.Execute(null);

        Assert.True(ui.DarkTheme);
        Assert.True(main.Commands[Commands.CommandId.LightTheme].Command.CanExecute(null));
        Assert.False(main.Commands[Commands.CommandId.DarkTheme].Command.CanExecute(null));
    }

    [AvaloniaFact]
    public void The_window_uses_the_chosen_theme()
    {
        var s = new Session();
        try
        {
            new Services.AvaloniaUiServices(s.Window).UseTheme(dark: true);
            Assert.Equal(ThemeVariant.Dark, s.Window.ActualThemeVariant);
            new Services.AvaloniaUiServices(s.Window).UseTheme(dark: false);
            Assert.Equal(ThemeVariant.Light, s.Window.ActualThemeVariant);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
