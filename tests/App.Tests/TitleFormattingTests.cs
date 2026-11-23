using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using MindBuzz.App.Canvas;
using MindBuzz.App.Commands;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Bold, italic and underline in topic titles, stored as Markdown.</summary>
public sealed class TitleFormattingTests
{
    private static void SetCentral(Session s, string text)
    {
        s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, text));
        Session.Pump();
    }

    /// <summary>Opens the editor on the central topic with the caret at <paramref name="caret"/>.</summary>
    private static TextBox Edit(Session s, int caret)
    {
        s.Press(Key.F2);
        TextBox editor = s.Canvas.Editor;
        editor.CaretIndex = caret;
        editor.SelectionStart = editor.SelectionEnd = caret;
        Session.Pump();
        return editor;
    }

    private static ToggleButton Button(Session s, int index) =>
        (ToggleButton)((StackPanel)s.Canvas.FormatBar.Child!).Children[index];

    [AvaloniaFact]
    public void Ctrl_b_in_a_word_makes_it_bold_and_the_map_shows_it_without_markers()
    {
        var s = new Session();
        SetCentral(s, "make this bold");
        double plainWidth = s.Canvas.Layout!.Find(s.Central.Id)!.Text.Width;
        TextBox editor = Edit(s, 7);

        s.Press(Key.B, RawInputModifiers.Control);

        Assert.True(s.Canvas.IsEditorOpen);
        Assert.Equal("make **this** bold", editor.Text);
        Assert.Equal(9, editor.CaretIndex);

        s.Press(Key.Enter);

        Assert.Equal("make **this** bold", s.Central.Text);
        Assert.Equal("make this bold", s.Map.Name);
        Assert.True(s.Canvas.Layout!.Find(s.Central.Id)!.Text.Width > plainWidth, "bold is wider; the map measures the text without its markers");
        Assert.True(s.Canvas.Layout!.Find(s.Central.Id)!.Text.Width < TopicText.MeasureAsTyped("make **this** bold", TopicLevel.Central, 1000).Width);
    }

    [AvaloniaFact]
    public void A_selection_gets_exactly_that_part_and_styles_combine()
    {
        var s = new Session();
        SetCentral(s, "make this bold");
        TextBox editor = Edit(s, 0);
        editor.SelectionStart = 5;
        editor.SelectionEnd = 9;
        Session.Pump();

        s.Press(Key.U, RawInputModifiers.Control);
        s.Press(Key.I, RawInputModifiers.Control);

        Assert.Equal("make ++*this*++ bold", editor.Text);
        Assert.Equal("this", editor.SelectedText);
    }

    [AvaloniaFact]
    public void At_the_end_of_the_text_the_style_is_for_the_letters_typed_next()
    {
        var s = new Session();
        SetCentral(s, "make");
        TextBox editor = Edit(s, 4);

        s.Press(Key.B, RawInputModifiers.Control);
        Assert.True(Button(s, 0).IsChecked, "the bold button shows what comes next");
        s.Type(" it");

        Assert.Equal("make **it**", editor.Text);
        Assert.Equal("make [bold:it]", Describe(editor.Text!));
    }

    [AvaloniaFact]
    public void Moving_the_caret_first_forgets_the_style_switched_for_typing()
    {
        var s = new Session();
        SetCentral(s, "make it");
        TextBox editor = Edit(s, 7);

        s.Press(Key.B, RawInputModifiers.Control);
        editor.CaretIndex = 4;
        Session.Pump();
        s.Type("s");

        Assert.Equal("makes it", editor.Text);
    }

    [AvaloniaFact]
    public void The_format_bar_hovers_over_the_editor_follows_the_caret_and_its_buttons_keep_the_editor_open()
    {
        var s = new Session();
        SetCentral(s, "make **this** bold");
        Assert.False(s.Canvas.FormatBar.IsVisible);
        TextBox editor = Edit(s, 8);

        Assert.True(s.Canvas.FormatBar.IsVisible);
        Assert.True(s.Canvas.FormatBar.Bounds.Bottom <= editor.Bounds.Top || s.Canvas.FormatBar.Bounds.Top >= editor.Bounds.Bottom);
        Assert.True(Button(s, 0).IsChecked);
        Assert.False(Button(s, 1).IsChecked);

        ToggleButton italic = Button(s, 1);
        s.Click(italic.TranslatePoint(new Point(italic.Bounds.Width / 2, italic.Bounds.Height / 2), s.Window)!.Value);

        Assert.True(s.Canvas.IsEditorOpen, "a button press does not commit the edit");
        Assert.True(editor.IsFocused);
        Assert.Equal("make ***this*** bold", editor.Text);
        Assert.True(Button(s, 1).IsChecked);

        s.Press(Key.Enter);
        Assert.False(s.Canvas.FormatBar.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows, "Bold  (Ctrl+B)")]
    [InlineData(HostPlatform.MacOS, "Bold  (⌘B)")]
    public void The_buttons_name_their_shortcut(HostPlatform platform, string expected)
    {
        var s = new Session(platform);
        Assert.Equal(expected, ToolTip.GetTip(Button(s, 0)));
        Assert.Equal(s.Main.TooltipOf(CommandId.Underline), ToolTip.GetTip(Button(s, 2)));
    }

    [AvaloniaFact]
    public void The_title_box_in_the_detail_view_formats_too()
    {
        var s = new Session();
        SetCentral(s, "make this bold");
        s.Main.Execute(CommandId.ToggleTopicPanel);
        Session.Pump();
        TextBox title = s.Window.FindControl<TopicPanel>("TopicPanelView")!.FindControl<TextBox>("TitleBox")!;
        title.Focus();
        Session.Pump();
        title.SelectionStart = 0;
        title.SelectionEnd = 4;
        title.CaretIndex = 4;
        title.SelectionStart = 0;
        Session.Pump();

        s.Press(Key.B, RawInputModifiers.Control);
        s.Press(Key.Enter);

        Assert.Equal("**make** this bold", s.Central.Text);
    }

    [AvaloniaFact]
    public void Search_finds_the_words_across_markers()
    {
        var s = new Session();
        SetCentral(s, "Buy **fresh** milk");

        var hits = Core.Search.MapSearch.Find([s.Map], "fresh milk", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Single(hits);
    }

    /// <summary>Formatted titles on the map, in both themes, and the editor with its format bar.</summary>
    [AvaloniaFact]
    public void Formatted_titles_render_in_both_themes()
    {
        var frames = new List<string>();
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            SetCentral(s, "Plan the **big** *launch*");
            s.Press(Key.Insert);
            s.Type("Buy");
            s.Press(Key.B, RawInputModifiers.Control);
            s.Type(" fresh");
            s.Press(Key.Enter);
            s.Press(Key.Enter);
            s.Type("Call ++Anna++ and *really* ***confirm***");
            s.Press(Key.Enter);
            s.Press(Key.Enter);
            s.Type("Being edited");
            s.Canvas.Editor.SelectionStart = 0;
            s.Canvas.Editor.SelectionEnd = 5;
            Session.Pump();
            s.Press(Key.U, RawInputModifiers.Control);
            frames.Add(Render.Save(s.Window, theme, "title-formatting"));
        }

        Assert.NotEqual(File.ReadAllBytes(frames[0]), File.ReadAllBytes(frames[1]));
    }

    private static string Describe(string markup)
    {
        ParsedTitle parsed = TitleMarkup.Parse(markup);
        return string.Concat(parsed.Runs.Select(run =>
        {
            string part = parsed.Plain.Substring(run.Start, run.Length);
            return run.Style == TitleStyle.Bold ? $"[bold:{part}]" : part;
        }));
    }
}
