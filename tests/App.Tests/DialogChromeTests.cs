using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.Views;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Dialogs and menus as Windows shows them (seen on Windows).</summary>
public sealed class DialogChromeTests
{
    /// <summary>A modal dialog has nothing to minimise or maximise: Windows offered both on the save prompt.</summary>
    [AvaloniaFact]
    public void No_dialog_can_be_minimised_or_maximised()
    {
        Type[] dialogs = [.. typeof(MainWindow).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(Window)) && type != typeof(MainWindow))];
        Assert.NotEmpty(dialogs);
        foreach (Type type in dialogs)
        {
            var dialog = (Window)Activator.CreateInstance(type)!;
            Assert.False(dialog.CanMinimize, $"{type.Name} can be minimised");
            Assert.False(dialog.CanMaximize, $"{type.Name} can be maximised");
        }
    }

    /// <summary>
    /// Menu shortcuts use the app's wording, as the shortcuts dialog does: "Enter" and "Ctrl+Alt+Up" on Windows, not
    /// Avalonia's "Return" and "Ctrl+Alt+Up Arrow".
    /// </summary>
    [AvaloniaFact]
    public void Menu_shortcuts_use_the_apps_wording()
    {
        var gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Up, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt);
        var presenter = new MenuFlyoutPresenter { ItemsSource = new[] { new MenuItem { Header = "Move Up", InputGesture = gesture } } };
        var window = new Window { Width = 400, Height = 100, Content = presenter };
        window.Show();
        Session.Pump();

        TextBlock text = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TextBlock>().Single(block => block.Name == "PART_InputGestureText");
        Assert.Equal(GestureText.Format(gesture, Host.Current), text.Text);
        Assert.Equal("Ctrl+Alt+Up", GestureText.Format(gesture, HostPlatform.Windows));
        Assert.Equal("Enter", GestureText.Format(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Return), HostPlatform.Windows));
        window.Close();
    }

    /// <summary>The File menu as the in-window menu (Windows, Linux) shows it, for checking its spacing by
    /// eye.</summary>
    [AvaloniaFact]
    public void The_file_menu_renders_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session(HostPlatform.Windows);
            var presenter = new MenuFlyoutPresenter
            {
                ItemsSource = s.Main.Menus.TopLevel[0].Children!.Select(entry => entry.IsSeparator
                    ? (object)new Separator()
                    : new MenuItem { Header = entry.Header, InputGesture = entry.Gesture, Command = entry.Command }).ToList(),
            };
            var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = presenter };
            Render.Save(window, theme, "menu-file");

            var topic = new MenuFlyoutPresenter
            {
                ItemsSource = s.Main.Menus.TopLevel[2].Children!.Select(entry => entry.IsSeparator
                    ? (object)new Separator()
                    : new MenuItem { Header = entry.Header, InputGesture = entry.Gesture }).ToList(),
            };
            Render.Save(new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = topic }, theme, "menu-topic");
            s.Window.Close();
        }
    }
}
