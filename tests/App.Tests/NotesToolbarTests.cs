using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.Notes;
using MindBuzz.App.Views;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The notes toolbar in the detail view.</summary>
public sealed class NotesToolbarTests
{
    private static TopicPanel Panel(Session s) => s.Window.FindControl<TopicPanel>("TopicPanelView")!;

    private static Button ToolButton(Session s, string name) => Panel(s).FindControl<Button>(name)!;

    private static void ClickTool(Session s, string name)
    {
        Button button = ToolButton(s, name);
        s.Click(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), s.Window)!.Value);
    }

    // --- the edits ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Title", 2, "# Title", 4)]
    [InlineData("# Title", 4, "## Title", 5)]
    [InlineData("## Title", 5, "### Title", 6)]
    [InlineData("### Title", 6, "Title", 2)]
    [InlineData("first\nsecond", 8, "first\n# second", 10)]
    public void The_heading_button_goes_from_plain_through_three_levels_back_to_plain(string text, int caret, string expected, int expectedCaret)
    {
        (string result, int start, int length) = MarkdownEdit.CycleHeading(text, caret, 0);

        Assert.Equal(expected, result);
        Assert.Equal(expectedCaret, start);
        Assert.Equal(0, length);
    }

    [Theory]
    [InlineData("one\ntwo", 0, 7, "Bullet", "- one\n- two")]
    [InlineData("- one\n- two", 0, 11, "Bullet", "one\ntwo")]
    [InlineData("- one\n- two", 0, 11, "Numbered", "1. one\n2. two")]
    [InlineData("1. one\n2. two", 0, 13, "Bullet", "- one\n- two")]
    [InlineData("- [x] done", 0, 0, "Bullet", "- done")]
    [InlineData("  - nested", 3, 0, "Numbered", "  1. nested")]
    [InlineData("one\n\ntwo", 0, 8, "Bullet", "- one\n\n- two")]
    [InlineData("said", 0, 0, "Quote", "> said")]
    [InlineData("> said", 3, 0, "Quote", "said")]
    [InlineData("", 0, 0, "Bullet", "- ")]
    public void The_list_and_quote_buttons_switch_the_lines_on_and_off(string text, int start, int length, string kind, string expected)
    {
        Assert.Equal(expected, MarkdownEdit.ToggleLineKind(text, start, length, Enum.Parse<MarkdownLineKind>(kind)).Text);
    }

    [Fact]
    public void A_list_button_keeps_the_caret_after_the_same_text()
    {
        // "one|" becomes "- one|".
        (_, int caret, _) = MarkdownEdit.ToggleLineKind("one", 3, 0, MarkdownLineKind.Bullet);
        Assert.Equal(5, caret);
    }

    [Theory]
    [InlineData("see docs", 4, 4, "see [docs](https://)", "https://")]
    [InlineData("see https://example.com", 4, 19, "see [link text](https://example.com)", "link text")]
    [InlineData("see ", 4, 0, "see [link text](https://)", "link text")]
    public void The_link_button_selects_what_is_left_to_type(string text, int start, int length, string expected, string selected)
    {
        (string result, int newStart, int newLength) = MarkdownEdit.InsertLink(text, start, length, "link text");

        Assert.Equal(expected, result);
        Assert.Equal(selected, result.Substring(newStart, newLength));
    }

    [Theory]
    [InlineData("", 0, "| A | B |\n| --- | --- |\n|  |  |")]
    [InlineData("Intro", 2, "Intro\n\n| A | B |\n| --- | --- |\n|  |  |")]
    [InlineData("Intro\nMore", 2, "Intro\n\n| A | B |\n| --- | --- |\n|  |  |\n\nMore")]
    [InlineData("Intro\n\n", 7, "Intro\n\n| A | B |\n| --- | --- |\n|  |  |")]
    public void The_table_button_puts_a_table_on_lines_of_its_own(string text, int caret, string expected)
    {
        (string result, int start, int length) = MarkdownEdit.InsertTable(text, caret, 0, "A", "B");

        Assert.Equal(expected, result);
        Assert.Equal("A", result.Substring(start, length));
    }

    // --- the toolbar -------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_toolbar_button_edits_the_note_and_the_keyboard_stays_in_the_editor()
    {
        var s = new Session();
        s.Press(Key.T, RawInputModifiers.Control);
        s.Type("milk");

        ClickTool(s, "NoteBulletButton");
        s.Type(" and bread");

        Assert.Equal("- milk and bread", s.Central.Note);
    }

    [AvaloniaFact]
    public void The_toolbar_works_in_edit_mode_only()
    {
        var s = new Session();
        s.Main.UpdateNote(s.Main.ActiveMap.Id, s.Central.Id, "A note");
        s.Main.Execute(CommandId.ToggleTopicPanel);
        Session.Pump();

        Assert.True(s.Main.Panel.IsPreview);
        Assert.False(Panel(s).FindControl<StackPanel>("NotesToolbar")!.IsEffectivelyEnabled);
    }

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows, "Bold  (Ctrl+B)")]
    [InlineData(HostPlatform.MacOS, "Bold  (⌘B)")]
    public void Buttons_with_a_shortcut_name_it_in_their_tooltip(HostPlatform platform, string expected)
    {
        var s = new Session(platform);

        Assert.Equal(expected, ToolTip.GetTip(ToolButton(s, "NoteBoldButton")));
        Assert.Equal(Localization.Strings.Notes_ToolTable, ToolTip.GetTip(ToolButton(s, "NoteTableButton")));
    }

    /// <summary>The detail view is never narrower than the notes header: toolbar, Edit/Preview and Help on one
    /// row.</summary>
    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    public void The_notes_header_fits_the_narrowest_detail_view(HostPlatform platform)
    {
        var s = new Session(platform);
        s.Main.Execute(CommandId.ToggleTopicPanel);
        Session.Pump();
        Grid header = Panel(s).FindControl<Grid>("NotesHeader")!;

        header.Measure(Size.Infinity);
        double needed = header.DesiredSize.Width;

        Assert.True(needed <= MainWindow.EnglishMinPanelWidth, $"The header needs {needed:0} px; the panel's minimum is {MainWindow.EnglishMinPanelWidth}.");
        Assert.True(needed > MainWindow.EnglishMinPanelWidth - 40, $"The header needs only {needed:0} px; the minimum of {MainWindow.EnglishMinPanelWidth} wastes room.");
        Assert.Equal(MainWindow.EnglishMinPanelWidth, s.Window.MinPanelWidth);
        Assert.Equal(980, s.Window.MinWidth);

        // The window leaves room for the map, the search panel and the detail view side by side.
        Assert.True(s.Window.MinWidth >= MainWindow.MinCanvasWidth + MainWindow.MinSearchWidth + s.Window.MinPanelWidth);
    }

    [AvaloniaFact]
    public void The_toolbar_renders_in_both_themes()
    {
        var frames = new List<string>();
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            s.Press(Key.T, RawInputModifiers.Control);
            s.Type("Shopping");
            ClickTool(s, "NoteHeadingButton");
            frames.Add(Render.Save(s.Window, theme, "notes-toolbar"));
        }

        Assert.NotEqual(File.ReadAllBytes(frames[0]), File.ReadAllBytes(frames[1]));
    }
}
