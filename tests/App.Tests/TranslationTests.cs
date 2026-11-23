using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MindBuzz.App.Commands;
using MindBuzz.App.Localization;
using MindBuzz.App.Printing;
using MindBuzz.App.Services;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using SkiaSharp;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// The translations (Strings.de/fr/es.resx): complete, with the same placeholders and access keys as English, and
/// fitting where space is measured or fixed. The language follows the OS; tests switch it with <see
/// cref="TestCulture"/>.
/// </summary>
public sealed partial class TranslationTests
{
    /// <summary>Kept in English on purpose: the licence line (the owner's choice), the product name, and Markdown
    /// syntax
    /// that is not language.</summary>
    private static readonly HashSet<string> KeptInEnglish =
    [
        "AppTitle", "About_Legal", "LinkEdit_UrlWatermark",
        "MarkdownSheet_CodeSyntax", "MarkdownSheet_EscapeSyntax", "MarkdownSheet_RuleSyntax", "MarkdownSheet_CodeBlockSyntax",
        "MarkdownSheet_AutoLinkSyntax", "MarkdownSheet_TableRowSyntax", "MarkdownSheet_TableRuleSyntax",
    ];

    private static bool IsKeptInEnglish(string key) => KeptInEnglish.Contains(key);

    public static TheoryData<string> Translations => new(TestCulture.Translations);

    private static Dictionary<string, string> Entries(CultureInfo culture, bool tryParents)
    {
        ResourceSet set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: tryParents)
            ?? throw new InvalidOperationException($"No resources for {culture.Name}.");
        return set.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex Placeholder();

    private static string Placeholders(string text) =>
        string.Join(',', Placeholder().Matches(text).Select(match => match.Groups[1].Value).Distinct().Order());

    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_string_is_translated_with_the_same_placeholders_and_access_keys(string language)
    {
        Dictionary<string, string> english = Entries(CultureInfo.InvariantCulture, tryParents: false);
        Dictionary<string, string> translated = Entries(CultureInfo.GetCultureInfo(language), tryParents: false);

        List<string> missing = [.. english.Keys.Where(key => !IsKeptInEnglish(key) && !translated.ContainsKey(key))];
        List<string> extra = [.. translated.Keys.Where(key => !english.ContainsKey(key) || IsKeptInEnglish(key))];
        List<string> placeholders = [.. translated.Where(entry => Placeholders(entry.Value) != Placeholders(english[entry.Key])).Select(entry => entry.Key)];

        // A menu label keeps its one access key.
        string[] menuLabels = ["Menu_", "Command_", "Color_", "Line_"];
        List<string> accessKeys =
        [
            .. translated
                .Where(entry => menuLabels.Any(prefix => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                    && english[entry.Key].Count(c => c == '_') != entry.Value.Count(c => c == '_'))
                .Select(entry => entry.Key),
        ];

        Assert.Empty(missing);
        Assert.Empty(extra);
        Assert.Empty(placeholders);
        Assert.Empty(accessKeys);
    }

    public static TheoryData<string, HostPlatform> LanguagesAndPlatforms
    {
        get
        {
            var data = new TheoryData<string, HostPlatform>();
            foreach (string language in new[] { "en", "de", "fr", "es" })
            {
                data.Add(language, HostPlatform.Windows);
                data.Add(language, HostPlatform.MacOS);
            }

            return data;
        }
    }

    /// <summary>The detail view is never narrower than its notes header, in every language.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(LanguagesAndPlatforms))]
    public void The_notes_header_fits_the_narrowest_detail_view_in_every_language(string language, HostPlatform platform)
    {
        using IDisposable _ = TestCulture.Use(language);
        var s = new Session(platform);
        s.Main.Execute(CommandId.ToggleTopicPanel);
        Session.Pump();
        Grid header = s.Window.FindControl<TopicPanel>("TopicPanelView")!.FindControl<Grid>("NotesHeader")!;

        header.Measure(Size.Infinity);

        Assert.True(header.DesiredSize.Width <= s.Window.MinPanelWidth, $"{language}: the header needs {header.DesiredSize.Width:0} px; the panel's minimum is {s.Window.MinPanelWidth}.");
        Assert.True(s.Window.MinWidth >= MainWindow.MinCanvasWidth + MainWindow.MinSearchWidth + s.Window.MinPanelWidth);
        File.AppendAllText(Path.Join(Render.ArtifactDirectory, "l10n-widths.txt"), $"{language} {platform}: panel {s.Window.MinPanelWidth}, window {s.Window.MinWidth}\n");
    }

    /// <summary>
    /// The sample maps in every language: no topic longer than the 4 lines a title may have, so none
    /// is cut with "…", and the links go to that language's Wikipedia. Rendered for a look (l10n-*-sample-*.png).
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(LanguagesAndPlatforms))]
    public void The_sample_maps_fit_their_topics_in_every_language(string language, HostPlatform platform)
    {
        using IDisposable _ = TestCulture.Use(language);
        var s = new Session(platform);
        s.Main.Execute(CommandId.NewSample);
        Session.Pump();

        var tooLong = new List<string>();
        foreach (Core.Model.Map map in s.Main.Document.Maps)
        {
            void Visit(Core.Model.Topic topic, int depth)
            {
                Core.Layout.TopicLevel level = depth switch { 0 => Core.Layout.TopicLevel.Central, 1 => Core.Layout.TopicLevel.Main, _ => Core.Layout.TopicLevel.Sub };
                int lines = Canvas.TitleLineGuard.Lines(topic.Text, level);
                if (lines > Canvas.TopicText.MaxLines)
                {
                    tooLong.Add($"{topic.Text} ({lines} lines)");
                }

                foreach (Core.Model.Topic child in topic.Children)
                {
                    Visit(child, depth + 1);
                }
            }

            Visit(map.Root, 0);
        }

        Assert.Empty(tooLong);
        string wikipedia = language == "en" ? "https://en.wikipedia.org/" : $"https://{language}.wikipedia.org/";
        Assert.Contains(s.Main.Document.Maps.SelectMany(map => map.Root.DescendantsAndSelf()).SelectMany(topic => topic.Links), link => link.Url.StartsWith(wikipedia, StringComparison.Ordinal));

        if (platform == HostPlatform.Windows)
        {
            s.Main.Execute(CommandId.ZoomFit);
            Session.Pump();
            Render.Save(s.Window, ThemeVariant.Light, $"l10n-{language}-sample-intro");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(LanguagesAndPlatforms))]
    public void Both_printed_sheets_fit_one_page_in_every_language(string language, HostPlatform platform)
    {
        using IDisposable _ = TestCulture.Use(language);
        CommandRegistry commands = new MainViewModel(new FakeUiServices(), new FakePlatformShell(), platform).Commands;

        foreach ((string name, IReadOnlyList<SheetSection> sheet, string title) in new[]
        {
            ("shortcut-sheet", ShortcutSheet.Build(commands, platform), Strings.Sheet_Title),
            ("markdown-sheet", MarkdownSheet.Build(commands, platform), Strings.MarkdownSheet_Title),
        })
        {
            var legend = ShortcutSheet.Legend(sheet, platform);
            float? size = ShortcutSheetPdf.FittingSize(sheet, legend);
            Assert.True(size >= 8, $"{language} {name}: fits only at {size?.ToString(CultureInfo.InvariantCulture) ?? "no"} pt.");

            using var surface = SKSurface.Create(new SKImageInfo((int)(ShortcutSheetPdf.PageWidth * 2), (int)(ShortcutSheetPdf.PageHeight * 2)));
            surface.Canvas.Scale(2);
            ShortcutSheetPdf.Draw(surface.Canvas, sheet, legend, title);
            using SKData png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Join(Render.ArtifactDirectory, $"l10n-{language}-{name}-{platform}.png".ToLowerInvariant()), png.ToArray());
        }
    }

    /// <summary>
    /// Every dialog and the main window at its narrowest, with the detail view and the search panel open: no text that
    /// does not wrap is cut off. Each is also rendered for a look (l10n-*.png).
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(TestCulture.AllLanguages), MemberType = typeof(TestCulture))]
    public async Task No_text_is_cut_off_in_the_dialogs_or_the_main_window(string language)
    {
        using IDisposable _ = TestCulture.Use(language);
        var clipped = new List<string>();

        foreach ((string name, Window window) in await Windows())
        {
            window.Show();
            Session.Pump();
            clipped.AddRange(Clipped(window).Select(text => $"{name}: {text}"));
            Render.Save(window, ThemeVariant.Light, $"l10n-{language}-{name}");
        }

        Assert.Empty(clipped);
    }

    private static async Task<List<(string Name, Window Window)>> Windows()
    {
        var ui = new FakeUiServices();
        var main = new MainViewModel(ui, new FakePlatformShell(), HostPlatform.Windows)
        {
            UpdateSource = () => Task.FromResult(UpdateAnswer.NewerRelease(new Version(1, 0, 1))),
        };
        main.Execute(CommandId.About);
        main.Execute(CommandId.KeyboardShortcuts);
        main.Execute(CommandId.CheckForUpdate);
        await Task.Yield();

        string file = "Projektplan 2026.mbz";
        string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

        var windows = new List<(string, Window)>
        {
            ("about", new DialogWindow(ui.Messages[0])),
            ("shortcuts", new ShortcutsWindow(ui.ShortcutDialogs[0])),
            ("update", new ChoiceWindow(ui.Questions[0])),
            ("save-prompt", new ChoiceWindow(new ChoiceRequest(Strings.SavePrompt_Title, Format(Strings.SavePrompt_MessageFormat, file), Strings.SavePrompt_Detail, [Strings.Answer_DontSave, Strings.Answer_Cancel, Strings.Answer_Save], 2, 1))),
            ("recovery", new ChoiceWindow(new ChoiceRequest(Strings.Recovery_Title, Format(Strings.Recovery_MessageFormat, file, DateTime.Now), Strings.Recovery_Detail, [Strings.Answer_OpenCopy, Strings.Answer_KeepDocument, Strings.Answer_Restore], 2, 1))),
            ("delete-map", new ChoiceWindow(new ChoiceRequest(Strings.DeleteMap_Title, Format(Strings.DeleteMap_MessageFormat, "Projektplan"), Strings.DeleteMap_Detail, [Strings.Answer_Cancel, Strings.Answer_Delete], 1, 0))),
            ("export", new ChoiceWindow(new ChoiceRequest(Strings.Export_Title, Format(Strings.Export_MessageFormat, "Projektplan"), Strings.Export_Detail, [Strings.Answer_Cancel, Strings.Export_Transparent, Strings.Export_White], 2, 0))),
            ("password-new", new PasswordWindow(new PasswordRequest(Strings.Password_EncryptTitle, Format(Strings.Password_EncryptMessageFormat, file), IsNew: true, Strings.Password_Encrypt))),
            ("password-open", new PasswordWindow(new PasswordRequest(Strings.Password_OpenTitle, Format(Strings.Password_OpenMessageFormat, file), IsNew: false, Strings.Password_Open, Strings.Password_Wrong))),
            ("link-edit", new LinkEditWindow(new LinkEditRequest(Strings.LinkEdit_AddTitle, "https://example.com", "Roadmap", Strings.LinkEdit_Add))),
            ("crash", new CrashWindow(new CrashRequest(Strings.Crash_Title, Strings.Crash_Message, Strings.Crash_RecoveryWritten, "System.InvalidOperationException"))),
        };

        // The main window at its narrowest, the in-window menu showing, both side panels and the update notice open.
        var s = new Session(HostPlatform.Windows);
        s.Window.Width = s.Window.MinWidth;
        s.Main.UpdateSource = () => Task.FromResult(UpdateAnswer.NewerRelease(new Version(1, 0, 1)));
        await s.Main.CheckForUpdateAtStartAsync();
        s.Main.Execute(CommandId.Find);
        s.Main.Execute(CommandId.OpenNotes);
        Session.Pump();
        windows.Add(("main-window", s.Window));

        foreach (int menu in Enumerable.Range(0, s.Main.Menus.TopLevel.Count))
        {
            MenuEntry top = s.Main.Menus.TopLevel[menu];
            var presenter = new MenuFlyoutPresenter
            {
                ItemsSource = top.Children!.Select(entry => entry.IsSeparator
                    ? (object)new Separator()
                    : new MenuItem { Header = entry.Header, InputGesture = entry.Gesture }).ToList(),
            };
            windows.Add(($"menu-{menu}", new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = presenter }));
        }

        return windows;
    }

    /// <summary>Visible single-line texts narrower on screen than they need: cut off rather than wrapped or trimmed on
    /// purpose.</summary>
    private static IEnumerable<string> Clipped(Window window)
    {
        foreach (TextBlock block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(block.Text) || block.TextWrapping != TextWrapping.NoWrap
                || block.TextTrimming != TextTrimming.None || block.Bounds.Width <= 0)
            {
                continue;
            }

            var text = new FormattedText(
                block.Text.Replace("_", string.Empty, StringComparison.Ordinal),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
                block.FontSize,
                Brushes.Black);
            double needed = text.WidthIncludingTrailingWhitespace + block.Padding.Left + block.Padding.Right;
            if (needed > block.Bounds.Width + 1)
            {
                yield return $"“{block.Text}” needs {needed:0} px, has {block.Bounds.Width:0}";
            }
        }
    }
}
