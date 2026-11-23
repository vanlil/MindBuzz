using Avalonia.Headless.XUnit;
using MindBuzz.App.Commands;
using MindBuzz.App.Platform;
using MindBuzz.App.Printing;
using MindBuzz.App.ViewModels;
using SkiaSharp;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The printable keyboard sheet.</summary>
public sealed class ShortcutSheetTests
{
    private static MainViewModel CreateMain(HostPlatform platform, FakePlatformShell? shell = null) =>
        new(new FakeUiServices(), shell ?? new FakePlatformShell(), platform);

    [Theory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    [InlineData(HostPlatform.Linux)]
    public void Every_command_with_a_shortcut_is_on_the_sheet(HostPlatform platform)
    {
        var covered = ShortcutSheet.Covered().ToHashSet();
        CommandId[] missing =
        [
            .. CreateMain(platform).Commands.All
                .Where(command => command.Gestures.KeyGestures(platform).Count > 0 && !covered.Contains(command.Id))
                .Select(command => command.Id),
        ];

        Assert.Empty(missing);
    }

    [Fact]
    public void No_command_is_on_the_sheet_twice()
    {
        List<CommandId> covered = [.. ShortcutSheet.Covered()];
        Assert.Equal(covered.Count, covered.Distinct().Count());
    }

    [Fact]
    public void Each_platform_writes_its_own_keys()
    {
        IReadOnlyList<SheetSection> windows = ShortcutSheet.Build(CreateMain(HostPlatform.Windows).Commands, HostPlatform.Windows);
        IReadOnlyList<SheetSection> mac = ShortcutSheet.Build(CreateMain(HostPlatform.MacOS).Commands, HostPlatform.MacOS);
        string Keys(IReadOnlyList<SheetSection> sheet) => string.Concat(sheet.SelectMany(section => section.Rows).Select(row => row.Keys));

        Assert.Contains("Ctrl+", Keys(windows), StringComparison.Ordinal);
        Assert.DoesNotContain("⌘", Keys(windows), StringComparison.Ordinal);
        Assert.Empty(ShortcutSheet.Legend(windows, HostPlatform.Windows));

        Assert.Contains("⌘", Keys(mac), StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+", Keys(mac), StringComparison.Ordinal);
        Assert.Contains(ShortcutSheet.Legend(mac, HostPlatform.MacOS), key => key.Symbol == "⌘");
    }

    /// <summary>Shift+F10 and the Menu key open the context menu on Windows and Linux only.</summary>
    [Theory]
    [InlineData(HostPlatform.Windows, true)]
    [InlineData(HostPlatform.Linux, true)]
    [InlineData(HostPlatform.MacOS, false)]
    public void The_context_menu_key_is_on_the_sheet_only_where_it_works(HostPlatform platform, bool listed)
    {
        IReadOnlyList<SheetSection> sheet = ShortcutSheet.Build(CreateMain(platform).Commands, platform);

        Assert.Equal(listed, sheet.SelectMany(section => section.Rows).Any(row => row.Text == MindBuzz.App.Localization.Strings.Sheet_ContextMenu));
    }

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    public void The_sheet_fits_one_a4_page_and_is_rendered(HostPlatform platform)
    {
        IReadOnlyList<SheetSection> sheet = ShortcutSheet.Build(CreateMain(platform).Commands, platform);
        var legend = ShortcutSheet.Legend(sheet, platform);

        float? size = ShortcutSheetPdf.FittingSize(sheet, legend);
        Assert.True(size >= 8, $"The sheet only fits at {size?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no"} pt; it should stay readable.");

        byte[] pdf = ShortcutSheetPdf.Render(sheet, legend, "Keyboard shortcuts");
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);

        // A picture to look at, at twice the page size.
        using var surface = SKSurface.Create(new SKImageInfo((int)(ShortcutSheetPdf.PageWidth * 2), (int)(ShortcutSheetPdf.PageHeight * 2)));
        surface.Canvas.Scale(2);
        ShortcutSheetPdf.Draw(surface.Canvas, sheet, legend, "Keyboard shortcuts");
        using SKData png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Join(Render.ArtifactDirectory, $"shortcut-sheet-{platform}.png".ToLowerInvariant()), png.ToArray());
    }

    [AvaloniaFact]
    public void The_help_command_opens_the_sheet_as_a_pdf()
    {
        var shell = new FakePlatformShell();
        MainViewModel main = CreateMain(HostPlatform.MacOS, shell);

        main.Commands[CommandId.PrintShortcuts].Command.Execute(null);

        (byte[] pdf, string name) = Assert.Single(shell.OpenedPdfs);
        Assert.Equal("Keyboard Shortcuts.pdf", name);
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public void The_pdf_is_written_to_temp_opened_and_deleted_again()
    {
        var recorder = new ProcessStartRecorder();
        var shell = new PlatformShell(recorder.Start);

        Assert.True(shell.OpenTemporaryPdf([1, 2, 3], "Keyboard Shortcuts.pdf"));

        string path = Assert.Single(recorder.Started).FileName;
        Assert.True(recorder.Started[0].UseShellExecute);
        Assert.Equal("Keyboard Shortcuts.pdf", Path.GetFileName(path));
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(path), StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));

        shell.DeleteTemporaryFolders();
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }
}
