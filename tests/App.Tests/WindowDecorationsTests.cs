using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// The window decorations Avalonia draws on Windows come from the app's own template (Themes/WindowDecorations.axaml):
/// Semi's showed its icons as the text "Av…" and painted the title a second time over the File menu.
/// </summary>
public sealed class WindowDecorationsTests
{
    private static WindowDrawnDecorationsContent Build()
    {
        Assert.True(Application.Current!.TryFindResource(typeof(WindowDrawnDecorations), out object? found));
        ControlTheme theme = Assert.IsType<ControlTheme>(found);
        var template = theme.Setters.OfType<Setter>().Single(setter => setter.Property == WindowDrawnDecorations.TemplateProperty).Value;
        return Assert.IsAssignableFrom<IWindowDrawnDecorationsTemplate>(template).Build().Result;
    }

    [AvaloniaFact]
    public void The_caption_buttons_are_drawn_icons_and_there_is_no_second_title()
    {
        Control overlay = Build().Overlay!;
        Button[] buttons = [.. overlay.GetLogicalDescendants().OfType<Button>()];

        Assert.Equal(["PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton"], buttons.Select(button => button.Name));
        // A control with a drawn path in it, never a bare geometry: a geometry as content prints its type name.
        Assert.All(buttons, button => Assert.Contains(
            ((Control)button.Content!).GetLogicalDescendants().Prepend((Control)button.Content!),
            element => element is Avalonia.Controls.Shapes.Path));
        Assert.Empty(overlay.GetLogicalDescendants().OfType<TextBlock>());
    }

    [AvaloniaFact]
    public void The_caption_buttons_render_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var panel = (Panel)Build().Overlay!;
            StackPanel buttons = panel.Children.OfType<StackPanel>().Single();
            buttons.Height = 34;
            buttons.IsVisible = true;
            var window = new Window { Width = 220, Height = 34, Content = panel };
            Render.Save(window, theme, "caption-buttons");
        }
    }
}
