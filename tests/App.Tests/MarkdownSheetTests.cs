using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MindBuzz.App.Commands;
using MindBuzz.App.Printing;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using SkiaSharp;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The printable Markdown sheet.</summary>
public sealed class MarkdownSheetTests
{
    private static MainViewModel CreateMain(HostPlatform platform, FakePlatformShell? shell = null) =>
        new(new FakeUiServices(), shell ?? new FakePlatformShell(), platform);

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    public void The_sheet_fits_one_a4_page_and_is_rendered(HostPlatform platform)
    {
        IReadOnlyList<SheetSection> sheet = MarkdownSheet.Build(CreateMain(platform).Commands, platform);
        var legend = ShortcutSheet.Legend(sheet, platform);

        float? size = ShortcutSheetPdf.FittingSize(sheet, legend);
        Assert.True(size >= 8, $"The sheet only fits at {size?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no"} pt; it should stay readable.");

        // A picture to look at, at twice the page size.
        using var surface = SKSurface.Create(new SKImageInfo((int)(ShortcutSheetPdf.PageWidth * 2), (int)(ShortcutSheetPdf.PageHeight * 2)));
        surface.Canvas.Scale(2);
        ShortcutSheetPdf.Draw(surface.Canvas, sheet, legend, Localization.Strings.MarkdownSheet_Title);
        using SKData png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Join(Render.ArtifactDirectory, $"markdown-sheet-{platform}.png".ToLowerInvariant()), png.ToArray());
    }

    [Theory]
    [InlineData(HostPlatform.Windows, "Ctrl+B")]
    [InlineData(HostPlatform.MacOS, "⌘B")]
    public void The_editor_keys_come_from_the_command_table(HostPlatform platform, string bold)
    {
        SheetSection editor = MarkdownSheet.Build(CreateMain(platform).Commands, platform)[^1];

        Assert.Equal(3, editor.Rows.Count);
        Assert.Equal(bold, editor.Rows[0].Keys);
    }

    [AvaloniaFact]
    public void The_help_menu_opens_the_sheet_as_a_pdf()
    {
        var shell = new FakePlatformShell();
        MainViewModel main = CreateMain(HostPlatform.MacOS, shell);
        MenuEntry help = main.Menus.TopLevel.Single(menu => menu.Header == Localization.Strings.Menu_Help);
        Assert.Contains(help.Children!, entry => entry.Command == main.Commands[CommandId.PrintMarkdownHelp].Command);

        main.Commands[CommandId.PrintMarkdownHelp].Command.Execute(null);

        (byte[] pdf, string name) = Assert.Single(shell.OpenedPdfs);
        Assert.Equal("Markdown Help.pdf", name);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [AvaloniaFact]
    public void The_help_button_beside_edit_and_preview_opens_the_sheet()
    {
        var s = new Session();
        s.Main.Commands[CommandId.ToggleTopicPanel].Command.Execute(null);
        Session.Pump();
        Button button = s.Window.FindControl<TopicPanel>("TopicPanelView")!.FindControl<Button>("MarkdownHelpButton")!;

        s.Click(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), s.Window)!.Value);

        Assert.Equal("Markdown Help.pdf", Assert.Single(s.Shell.OpenedPdfs).FileName);
    }
}
