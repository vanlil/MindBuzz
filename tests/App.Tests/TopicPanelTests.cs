using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MindBuzz.App.Commands;
using MindBuzz.App.Notes;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The topic panel, the notes editor and the preview, end to end.</summary>
public sealed class TopicPanelTests
{
    private const string SampleNote = """
        # Launch checklist

        The **release** goes out on *Friday*, ~~Thursday~~ after `build.sh --publish`.

        - [x] Write the notes
        - [ ] Tag the build

        | Step | Owner | Done |
        |:-----|:-----:|-----:|
        | Build | Axel | yes |
        | Test | CI | no |

        > Ask before pushing.

        ```
        dotnet test
        ```

        See [the plan](https://example.com/plan) or <mailto:team@example.com>.
        """;

    private static TopicPanel Panel(Session s) => s.Window.FindControl<TopicPanel>("TopicPanelView")!;

    private static void Run(Session s, CommandId id) => s.Main.Commands[id].Command.Execute(null);

    private static bool EditorHasFocus(Session s) => Panel(s).NoteEditor.IsKeyboardFocusWithin;

    [AvaloniaFact]
    public void Ctrl_t_opens_the_panel_on_an_empty_note_in_edit_mode_with_the_keyboard_in_the_editor()
    {
        var s = new Session();
        Assert.False(Panel(s).IsVisible);

        s.Press(Key.T, RawInputModifiers.Control);

        Assert.True(s.Main.Panel.IsOpen);
        Assert.True(Panel(s).IsVisible);
        Assert.True(s.Main.Panel.IsEditMode);
        Assert.True(EditorHasFocus(s));

        s.Press(Key.T, RawInputModifiers.Control);
        Assert.False(Panel(s).IsVisible);
        Assert.True(s.Canvas.IsFocused);
    }

    [AvaloniaFact]
    public void Typing_in_the_editor_writes_the_note_and_ctrl_z_undoes_the_burst_in_one_step()
    {
        var s = new Session();
        s.Press(Key.T, RawInputModifiers.Control);

        s.Type("Hello");
        Assert.Equal("Hello", s.Central.Note);
        Assert.True(s.Main.IsDirty);

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal(string.Empty, s.Central.Note);
        Assert.Equal(string.Empty, Panel(s).NoteEditor.Text);
        Assert.True(EditorHasFocus(s));
    }

    [AvaloniaFact]
    public void Typing_beyond_the_note_limit_is_refused_and_the_editor_keeps_the_note()
    {
        var s = new Session();
        s.Press(Key.T, RawInputModifiers.Control);
        string full = new('a', Topic.MaxNoteLength);
        s.Main.UpdateNote(s.Main.ActiveMap.Id, s.Central.Id, full);

        s.Type("b");

        Assert.Equal(full, s.Central.Note);
        Assert.Equal(full, Panel(s).NoteEditor.Text);
        Assert.Contains("32", s.Main.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void The_preview_is_rendered_only_while_the_panel_is_open()
    {
        var s = new Session();
        s.Main.UpdateNote(s.Main.ActiveMap.Id, s.Central.Id, "# Note");
        s.Main.EndNoteBurst();
        s.Main.Select(null);
        s.Main.Select(s.Central.Id);
        Assert.Null(Panel(s).PreviewContent);

        Run(s, CommandId.ToggleTopicPanel);

        Assert.NotNull(Panel(s).PreviewContent);
    }

    [AvaloniaFact]
    public void A_pause_in_typing_starts_a_new_undo_step()
    {
        var clock = new SteppingClock();
        var main = new MainViewModel(new FakeUiServices(), new FakePlatformShell(), HostPlatform.Windows, clock);
        MapId map = main.ActiveMap.Id;
        TopicId topic = main.ActiveMap.Root.Id;

        main.UpdateNote(map, topic, "a");
        clock.Now += TimeSpan.FromMilliseconds(500);
        main.UpdateNote(map, topic, "ab");
        clock.Now += MainViewModel.NoteBurstPause + TimeSpan.FromSeconds(1);
        main.UpdateNote(map, topic, "abc");

        main.Execute(CommandId.Undo);
        Assert.Equal("ab", main.ActiveMap.Root.Note);
        main.Execute(CommandId.Undo);
        Assert.Equal(string.Empty, main.ActiveMap.Root.Note);
    }

    [AvaloniaFact]
    public void Leaving_the_editor_ends_the_burst()
    {
        var s = new Session();
        s.Press(Key.T, RawInputModifiers.Control);
        s.Type("one");
        s.Press(Key.Escape);
        Assert.True(s.Canvas.IsFocused);
        Assert.True(s.Main.Panel.IsOpen);

        s.Press(Key.F6);
        Assert.True(EditorHasFocus(s));
        s.Type(" two");
        Assert.Equal("one two", s.Central.Note);

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal("one", s.Central.Note);
    }

    [AvaloniaFact]
    public void F6_moves_between_the_map_and_the_panel_and_opens_a_closed_panel()
    {
        var s = new Session();

        s.Press(Key.F6);
        Assert.True(s.Main.Panel.IsOpen);
        Assert.True(EditorHasFocus(s));

        s.Press(Key.F6);
        Assert.True(s.Canvas.IsFocused);

        s.Press(Key.F6, RawInputModifiers.Shift);
        Assert.True(EditorHasFocus(s));
    }

    [AvaloniaFact]
    public void The_panel_follows_the_selection_and_opens_a_note_in_preview()
    {
        var s = new Session();
        s.Press(Key.Insert);
        s.Type("Child");
        s.Press(Key.Enter);
        TopicId child = s.Map.Root.Children[0].Id;
        s.Main.Apply(new SetNote(s.Map.Id, child, "**Bold** note"));
        s.Press(Key.T, RawInputModifiers.Control);
        s.Press(Key.Escape);

        s.Main.Select(child);
        Session.Pump();
        Assert.Equal("Child", s.Main.Panel.TopicTitle);
        Assert.True(s.Main.Panel.IsPreview);
        Assert.NotNull(Panel(s).PreviewContent);

        s.Main.Select(s.Central.Id);
        Session.Pump();
        Assert.True(s.Main.Panel.IsEditMode);
        Assert.Equal(string.Empty, Panel(s).NoteEditor.Text);
    }

    [AvaloniaFact]
    public void Ctrl_e_in_the_panel_switches_edit_and_preview_and_does_nothing_on_the_map()
    {
        var s = new Session();
        s.Main.Apply(new SetNote(s.Map.Id, s.Central.Id, "Text"));
        s.Press(Key.T, RawInputModifiers.Control);
        Assert.True(s.Main.Panel.IsPreview);

        s.Press(Key.E, RawInputModifiers.Control);
        Assert.True(s.Main.Panel.IsEditMode);
        Assert.True(EditorHasFocus(s));

        s.Press(Key.Escape);
        s.Press(Key.E, RawInputModifiers.Control);
        Assert.True(s.Main.Panel.IsEditMode);
    }

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows, RawInputModifiers.Control)]
    [InlineData(HostPlatform.MacOS, RawInputModifiers.Meta)]
    public void Ctrl_e_twice_from_the_editor_goes_to_preview_and_back_keeping_the_keyboard_in_the_panel(HostPlatform platform, RawInputModifiers command)
    {
        // ⌘T is a macOS menu-bar key, and the headless platform has no menu bar; ⌘E is the panel's own key.
        var s = new Session(platform);
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();
        s.Type("Hello");

        s.Press(Key.E, command);
        Assert.True(s.Main.Panel.IsPreview);
        Assert.True(Panel(s).IsKeyboardFocusWithin);

        s.Press(Key.E, command);
        Assert.True(s.Main.Panel.IsEditMode);
        Assert.True(EditorHasFocus(s));

        // Typing goes on at the end; nothing is selected and replaced.
        Assert.Equal(0, Panel(s).NoteEditor.SelectionLength);
        s.Type("!");
        Assert.Equal("Hello!", s.Central.Note);
    }

    private static TextBox TitleBox(Session s) => Panel(s).FindControl<TextBox>("TitleBox")!;

    [AvaloniaFact]
    public void Text_typed_in_the_title_box_goes_to_its_own_topic_when_the_selection_moves_meanwhile()
    {
        var s = new Session();
        Run(s, CommandId.NewMap);
        string second = s.Central.Text;
        Run(s, CommandId.PreviousMap);
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();
        TitleBox(s).Focus();
        TitleBox(s).Text = "Typed for the first map";

        // A window key that changes the selection while the box has the keyboard.
        Run(s, CommandId.NextMap);
        s.Press(Key.Enter);

        // The edit lands on the first map, which is shown again, as after an undo there.
        Assert.Equal(second, s.Main.Document.Maps[1].Root.Text);
        Assert.Equal("Typed for the first map", s.Main.Document.Maps[0].Root.Text);
    }

    [AvaloniaFact]
    public void Undo_and_redo_are_off_while_the_title_box_has_the_keyboard_so_it_undoes_its_own_typing()
    {
        var s = new Session();
        s.Main.Apply(new SetTopicText(s.Main.ActiveMap.Id, s.Central.Id, "Changed"));
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();

        TitleBox(s).Focus();
        Assert.False(s.Main.CanExecute(CommandId.Undo));

        s.Canvas.Focus();
        Assert.True(s.Main.CanExecute(CommandId.Undo));
    }

    [AvaloniaFact]
    public void The_title_box_renames_the_topic_on_enter_as_one_undo_step()
    {
        var s = new Session();
        string before = s.Central.Text;
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();
        TitleBox(s).Focus();
        TitleBox(s).Text = "Roadmap";

        s.Press(Key.Enter, RawInputModifiers.Shift);
        Assert.Contains("\n", TitleBox(s).Text, StringComparison.Ordinal);
        TitleBox(s).Text = "Roadmap";

        s.Press(Key.Enter);
        Assert.Equal("Roadmap", s.Central.Text);
        Assert.False(TitleBox(s).IsKeyboardFocusWithin);
        Assert.True(s.Canvas.IsFocused);

        TitleBox(s).Focus();
        TitleBox(s).Text = "Thrown away";
        s.Press(Key.Escape);
        Assert.Equal("Roadmap", s.Central.Text);
        Assert.Equal("Roadmap", TitleBox(s).Text);
        Assert.True(s.Canvas.IsFocused);

        Run(s, CommandId.Undo);
        Assert.Equal(before, s.Central.Text);
    }

    [AvaloniaFact]
    public void Leaving_the_title_box_commits_and_empty_text_is_refused()
    {
        var s = new Session();
        string before = s.Central.Text;
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();

        TitleBox(s).Focus();
        TitleBox(s).Text = "   ";
        s.Canvas.Focus();
        Session.Pump();
        Assert.Equal(before, s.Central.Text);
        Assert.Equal(before, TitleBox(s).Text);

        TitleBox(s).Focus();
        TitleBox(s).Text = "Plan";
        Assert.False(s.Main.Commands[CommandId.Copy].Command.CanExecute(null));
        s.Canvas.Focus();
        Session.Pump();
        Assert.Equal("Plan", s.Central.Text);
    }

    [AvaloniaFact]
    public void Clicking_a_label_in_the_panel_commits_the_title_and_leaves_the_box()
    {
        var s = new Session();
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();
        TitleBox(s).Focus();
        TitleBox(s).Text = "Clicked away";

        TextBlock label = Panel(s).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Localization.Strings.Panel_Priority);
        s.Click(label.TranslatePoint(new Point(4, 4), s.Window)!.Value);

        Assert.Equal("Clicked away", s.Central.Text);
        Assert.False(TitleBox(s).IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void Maximize_hides_the_map_and_going_back_to_the_map_restores_the_normal_size()
    {
        var s = new Session();
        s.Press(Key.T, RawInputModifiers.Control);

        s.Press(Key.T, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(s.Main.Panel.IsMaximized);
        Assert.False(s.Canvas.IsVisible);
        Assert.True(Panel(s).IsKeyboardFocusWithin);

        s.Press(Key.Escape);
        Assert.False(s.Main.Panel.IsMaximized);
        Assert.True(s.Canvas.IsVisible);
        Assert.True(s.Canvas.IsFocused);

        Run(s, CommandId.ToggleMaximizePanel);
        Run(s, CommandId.ToggleTopicPanel);
        Assert.False(s.Main.Panel.IsMaximized);
        Assert.True(s.Canvas.IsVisible);
    }

    [AvaloniaFact]
    public void Ctrl_b_and_ctrl_i_wrap_the_selection_and_toggle_back()
    {
        var s = new Session();
        s.Press(Key.T, RawInputModifiers.Control);
        s.Type("make this bold");
        Panel(s).NoteEditor.Select(10, 4);

        s.Press(Key.B, RawInputModifiers.Control);
        Assert.Equal("make this **bold**", s.Central.Note);
        Assert.Equal("bold", Panel(s).NoteEditor.SelectedText);

        s.Press(Key.B, RawInputModifiers.Control);
        Assert.Equal("make this bold", s.Central.Note);

        s.Press(Key.I, RawInputModifiers.Control);
        Assert.Equal("make this *bold*", s.Central.Note);
    }

    [AvaloniaFact]
    public void Ctrl_b_without_a_selection_makes_the_word_at_the_caret_bold_and_the_caret_stays()
    {
        var s = new Session();
        s.Press(Key.T, RawInputModifiers.Control);
        s.Type("make this bold");
        Panel(s).NoteEditor.CaretOffset = 7;

        s.Press(Key.B, RawInputModifiers.Control);

        Assert.Equal("make **this** bold", s.Central.Note);
        Assert.Equal(9, Panel(s).NoteEditor.CaretOffset);
        Assert.Equal(0, Panel(s).NoteEditor.SelectionLength);

        // Typing goes on where the caret was: between "th" and "is".
        s.Type("x");
        Assert.Equal("make **thxis** bold", s.Central.Note);
    }

    [AvaloniaFact]
    public void While_the_editor_has_the_keyboard_topic_commands_are_off_so_menu_keys_reach_the_text()
    {
        var s = new Session();
        Assert.True(s.Main.Commands[CommandId.Copy].Command.CanExecute(null));

        s.Press(Key.T, RawInputModifiers.Control);
        Assert.False(s.Main.Commands[CommandId.Copy].Command.CanExecute(null));
        Assert.False(s.Main.Commands[CommandId.Paste].Command.CanExecute(null));
        Assert.False(s.Main.Commands[CommandId.AddLink].Command.CanExecute(null));

        s.Press(Key.Escape);
        Assert.True(s.Main.Commands[CommandId.Copy].Command.CanExecute(null));
    }

    [AvaloniaFact]
    public void Ctrl_shift_n_opens_the_detail_view_in_edit_with_the_caret_after_the_note()
    {
        var s = new Session();
        s.Main.Apply(new SetNote(s.Map.Id, s.Central.Id, "Existing"));

        s.Press(Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.True(s.Main.Panel.IsOpen);
        Assert.True(s.Main.Panel.IsEditMode);
        Assert.True(EditorHasFocus(s));
        s.Type(" more");
        Assert.Equal("Existing more", s.Central.Note);
    }

    [AvaloniaFact]
    public void Clicking_the_note_indicator_opens_the_panel_on_the_note()
    {
        var s = new Session();
        s.Main.Apply(new SetNote(s.Map.Id, s.Central.Id, "A note"));
        Session.Pump();
        (_, LayoutRect bounds) = TopicParts.Indicators(s.Canvas.Layout!.Find(s.Central.Id)!).Single();
        Avalonia.Point at = s.Canvas.TranslatePoint(s.Canvas.Viewport.ToScreen(new LayoutPoint(bounds.CenterX, bounds.CenterY)), s.Window)!.Value;

        s.Click(at);

        Assert.True(s.Main.Panel.IsOpen);
        Assert.True(s.Main.Panel.IsPreview);
    }

    [AvaloniaFact]
    public void Panel_marker_buttons_set_and_clear_markers()
    {
        var s = new Session();
        Run(s, CommandId.ToggleTopicPanel);

        s.Main.Panel.Priorities[1].Command.Execute(null);
        Assert.Equal("2", s.Central.MarkerOf(MarkerGroups.Priority)?.Value);
        Assert.True(s.Main.Panel.Priorities[1].IsActive);

        s.Main.Panel.Priorities[1].Command.Execute(null);
        Assert.Null(s.Central.MarkerOf(MarkerGroups.Priority));
    }

    [AvaloniaFact]
    public void Panel_links_follow_undo()
    {
        var s = new Session();
        Run(s, CommandId.ToggleTopicPanel);
        s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink("https://example.com", "Example")));
        Assert.Single(s.Main.Panel.Links!.Links);

        Run(s, CommandId.Undo);
        Assert.Empty(s.Main.Panel.Links!.Links);
    }

    [AvaloniaFact]
    public void Preview_links_open_only_through_the_guarded_gate()
    {
        var s = new Session();
        s.Main.Apply(new SetNote(s.Map.Id, s.Central.Id, "[safe](https://example.com) and [unsafe](javascript:alert(1))"));
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();

        Button link = Assert.Single(Panel(s).PreviewContent!.GetLogicalDescendants().OfType<Button>());
        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(["https://example.com"], s.Shell.OpenedUrls);
    }

    [AvaloniaFact]
    public void Renders_the_panel_in_both_themes_in_preview_and_edit()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            foreach (bool preview in (bool[])[true, false])
            {
                var s = new Session();
                s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "Launch plan"));
                s.Main.Apply(new SetNote(s.Map.Id, s.Central.Id, SampleNote));
                s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink("https://example.com/roadmap", "Roadmap")));
                s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink("mailto:team@example.com")));
                s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink("https://example.com/budget", "Budget")));
                s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink("https://example.com/risks", "Risks")));
                s.Main.Apply(new SetMarker(s.Map.Id, s.Central.Id, MarkerGroups.Priority, "2"));
                s.Main.Apply(new SetMarker(s.Map.Id, s.Central.Id, MarkerGroups.Progress, "50"));
                Run(s, CommandId.ToggleTopicPanel);
                if (!preview)
                {
                    Run(s, CommandId.ToggleNotesMode);
                }

                Session.Pump();
                Render.Save(s.Window, theme, preview ? "panel-preview" : "panel-edit");
            }
        }
    }

    /// <summary>A clock tests move by hand.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

/// <summary>The Markdown preview renderer, construct by construct: the control tree's shape, never its
/// colours.</summary>
public sealed class MarkdownRendererTests
{
    private static StackPanel Render(string markdown, List<string>? opened = null) =>
        MarkdownRenderer.Render(markdown, url => opened?.Add(url));

    private static string TextOf(SelectableTextBlock block) =>
        block.Inlines is { } inlines ? string.Concat(inlines.Select(Flatten)) : block.Text ?? string.Empty;

    private static string Flatten(Inline inline) => inline switch
    {
        Run run => run.Text ?? string.Empty,
        Span span => string.Concat(span.Inlines.Select(Flatten)),
        LineBreak => "\n",
        InlineUIContainer { Child: Button { Content: TextBlock label } } => string.Concat(label.Inlines!.Select(Flatten)),
        _ => string.Empty,
    };

    [AvaloniaFact]
    public void Emphasis_nested_deeper_than_the_limit_is_shown_as_text_instead_of_overflowing_the_stack()
    {
        // 60,000 stars on each side nest 30,000 spans; this crashed the process before the limit.
        string stars = new('*', 60_000);
        var paragraph = (SelectableTextBlock)Render(stars + "x" + stars).Children.Single();

        int depth = 0;
        Inline? inline = paragraph.Inlines!.Single();
        while (inline is Span span)
        {
            depth++;
            inline = span.Inlines.LastOrDefault();
        }

        Assert.InRange(depth, 1, MarkdownRenderer.MaxInlineDepth);
        Assert.Contains("x", TextOf(paragraph), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Blocks_nested_too_deep_for_markdig_show_the_note_as_plain_text()
    {
        string note = new string('>', 200) + " x";

        var text = (SelectableTextBlock)Render(note).Children.Single();

        Assert.Equal(note, text.Text);
    }

    [AvaloniaFact]
    public void Headings_get_a_class_per_level()
    {
        StackPanel root = Render("# One\n\n### Three");

        Assert.Collection(
            root.Children.Cast<SelectableTextBlock>(),
            h1 => Assert.Contains("mdH1", h1.Classes),
            h3 => Assert.Contains("mdH3", h3.Classes));
    }

    [AvaloniaFact]
    public void Emphasis_strong_strikethrough_and_inline_code_become_spans_and_runs()
    {
        var paragraph = (SelectableTextBlock)Render("*a* **b** ~~c~~ `d`").Children.Single();
        List<Inline> inlines = [.. paragraph.Inlines!];

        Assert.IsType<Italic>(inlines[0]);
        Assert.IsType<Bold>(inlines[2]);
        Assert.Equal(Avalonia.Media.TextDecorations.Strikethrough, ((Span)inlines[4]).TextDecorations);
        Assert.Contains("mdCodeInline", ((Run)inlines[6]).Classes);
        Assert.Equal("a b c d", TextOf(paragraph));
    }

    [AvaloniaFact]
    public void Task_lists_show_their_state_and_numbered_lists_count_from_their_start()
    {
        StackPanel root = Render("- [x] done\n- [ ] open\n\n3. three\n4. four");

        var tasks = (Grid)root.Children[0];
        Assert.Equal([true, false], tasks.Children.OfType<CheckBox>().Select(box => box.IsChecked == true));

        var numbered = (Grid)root.Children[1];
        Assert.Equal(["3.", "4."], numbered.Children.OfType<TextBlock>().Where(t => t.Classes.Contains("mdBullet")).Select(t => t.Text));
    }

    [AvaloniaFact]
    public void Tables_keep_rows_columns_header_and_alignment()
    {
        var frame = (Border)((ScrollViewer)Render("| a | b |\n|:-|-:|\n| 1 | 2 |").Children.Single()).Content!;
        var grid = (Grid)frame.Child!;

        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(2, grid.RowDefinitions.Count);
        List<Border> cells = [.. grid.Children.Cast<Border>()];
        Assert.All(cells.Take(2), cell => Assert.Contains("mdHeader", cell.Classes));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, ((Control)cells[3].Child!).HorizontalAlignment);
    }

    [AvaloniaFact]
    public void Code_blocks_quotes_and_rules_are_their_own_blocks()
    {
        StackPanel root = Render("```\nx = 1\n```\n\n> quoted\n\n---");

        Assert.Contains("mdCodeBlock", root.Children[0].Classes);
        Assert.Equal("x = 1", ((SelectableTextBlock)((ScrollViewer)((Border)root.Children[0]).Child!).Content!).Text);
        Assert.Contains("mdQuote", root.Children[1].Classes);
        Assert.Contains("mdRule", root.Children[2].Classes);
    }

    [AvaloniaFact]
    public void Raw_html_is_shown_as_text_not_rendered()
    {
        StackPanel root = Render("<b>bold?</b>\n\nInline <i>tag</i> here");

        Assert.All(root.Children, child => Assert.IsType<SelectableTextBlock>(child));
        Assert.Contains("<b>bold?</b>", TextOf((SelectableTextBlock)root.Children[0]), StringComparison.Ordinal);
        Assert.Contains("<i>", TextOf((SelectableTextBlock)root.Children[1]), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Images_are_never_loaded_their_alt_text_is_shown()
    {
        var paragraph = (SelectableTextBlock)Render("![a chart](https://example.com/chart.png)").Children.Single();

        Run run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines!));
        Assert.Contains("a chart", run.Text, StringComparison.Ordinal);
        Assert.Contains("mdImage", run.Classes);
    }

    [AvaloniaTheory]
    [InlineData("[x](https://example.com)", true)]
    [InlineData("[x](http://example.com)", true)]
    [InlineData("[x](mailto:a@example.com)", true)]
    [InlineData("<https://example.com>", true)]
    [InlineData("[x](javascript:alert(1))", false)]
    [InlineData("[x](file:///etc/passwd)", false)]
    [InlineData("[x](relative/path)", false)]
    public void Only_allowed_schemes_become_clickable_links(string markdown, bool clickable)
    {
        var opened = new List<string>();
        var paragraph = (SelectableTextBlock)Render(markdown, opened).Children.Single();

        Button? button = paragraph.Inlines!.OfType<InlineUIContainer>().Select(c => c.Child).OfType<Button>().SingleOrDefault();
        Assert.Equal(clickable, button is not null);
        button?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(clickable ? 1 : 0, opened.Count);
    }
}

/// <summary>The editor's colouring scan and the bold and italic toggles. Pure.</summary>
public sealed class MarkdownEditingTests
{
    [Fact]
    public void Scan_finds_headings_fences_quotes_markers_code_and_emphasis()
    {
        const string text = "# Title\n- item with `code` and **bold**\n```\n# not a heading\n```\n> quote *it*";

        IReadOnlyList<MarkdownSpan> spans = MarkdownSyntax.Scan(text);
        string Of(MarkdownSpan span) => text.Substring(span.Start, span.Length);

        Assert.Contains(spans, s => s.Kind == MarkdownSpanKind.Heading && Of(s) == "# Title");
        Assert.Contains(spans, s => s.Kind == MarkdownSpanKind.ListMarker && Of(s) == "-");
        Assert.Contains(spans, s => s.Kind == MarkdownSpanKind.Code && Of(s) == "`code`");
        Assert.Contains(spans, s => s.Kind == MarkdownSpanKind.Strong && Of(s) == "**bold**");
        Assert.Contains(spans, s => s.Kind == MarkdownSpanKind.Fence && Of(s) == "# not a heading");
        Assert.DoesNotContain(spans, s => s.Kind == MarkdownSpanKind.Heading && Of(s) == "# not a heading");
        Assert.Contains(spans, s => s.Kind == MarkdownSpanKind.Quote && Of(s) == "> quote *it*");
    }

    [Fact]
    public void Emphasis_markers_inside_code_are_literal()
    {
        const string text = "`a*b*c`";

        Assert.DoesNotContain(MarkdownSyntax.Scan(text), s => s.Kind == MarkdownSpanKind.Emphasis);
    }

    [Theory]
    [InlineData("word", 0, 4, "**", "**word**", 2, 4)]
    [InlineData("**word**", 2, 4, "**", "word", 0, 4)]
    [InlineData("**word**", 0, 8, "**", "word", 0, 4)]
    [InlineData("**word**", 2, 4, "*", "***word***", 3, 4)]
    [InlineData("*word*", 1, 4, "*", "word", 0, 4)]

    // A selection: exactly the selected part, without spaces at its ends.
    [InlineData("one two three", 4, 3, "**", "one **two** three", 6, 3)]
    [InlineData("one two three", 3, 5, "**", "one **two** three", 6, 3)]
    [InlineData("one two three", 2, 5, "**", "on**e two** three", 4, 5)]

    // No selection: the word the caret is in, the caret staying where it was in the word.
    [InlineData("ab", 1, 0, "*", "*ab*", 2, 0)]
    [InlineData("say hello now", 6, 0, "**", "say **hello** now", 8, 0)]
    [InlineData("say hello now", 9, 0, "**", "say **hello** now", 11, 0)]
    [InlineData("say **hello** now", 8, 0, "**", "say hello now", 6, 0)]
    [InlineData("I don't know", 4, 0, "*", "I *don't* know", 5, 0)]
    [InlineData("an e-mail", 5, 0, "*", "an *e-mail*", 6, 0)]

    // No selection and no word: a pair with the caret between.
    [InlineData("one  two", 4, 0, "**", "one **** two", 6, 0)]
    [InlineData("", 0, 0, "*", "**", 1, 0)]

    // Over several lines: each line's text on its own, after list, task, quote and heading markers.
    [InlineData("first line\nsecond line", 0, 22, "**", "**first line**\n**second line**", 0, 30)]
    [InlineData("- one\n- [ ] two\n\n> three", 0, 24, "*", "- *one*\n- [ ] *two*\n\n> *three*", 0, 30)]
    [InlineData("**a**\n**b**", 0, 11, "**", "a\nb", 0, 3)]
    public void Toggle_wraps_and_unwraps(string text, int start, int length, string marker, string expected, int newStart, int newLength)
    {
        Assert.Equal((expected, newStart, newLength), MarkdownEdit.Toggle(text, start, length, marker));
    }
}

/// <summary>A click on the empty map clears the selection; a drag there pans and keeps it.</summary>
public sealed class EmptyClickTests
{
    private static Point Empty(Session s) => new(20, s.Canvas.TranslatePoint(new Point(0, 20), s.Window)!.Value.Y);

    [AvaloniaFact]
    public void A_click_on_the_empty_map_clears_the_selection_and_an_arrow_key_selects_the_central_topic()
    {
        var s = new Session();
        Assert.NotNull(s.Main.SelectedTopic);

        s.Click(Empty(s));
        Assert.Null(s.Main.SelectedTopic);
        Assert.True(s.Canvas.IsFocused);

        s.Press(Key.Right);
        Assert.Equal(s.Central.Id, s.Main.SelectedTopic);
    }

    [AvaloniaFact]
    public void Dragging_the_empty_map_pans_and_keeps_the_selection()
    {
        var s = new Session();
        Point from = Empty(s);

        s.Drag(from, from + new Vector(60, 40));

        Assert.Equal(s.Central.Id, s.Main.SelectedTopic);
    }
}
