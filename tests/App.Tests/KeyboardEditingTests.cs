using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using MindBuzz.App.Canvas;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// Building a map with the keyboard alone, end to end through the real window: keys go into the headless
/// platform, through the key bindings and the in-place text box, into the view model and the history.
/// </summary>
public sealed class KeyboardEditingTests
{
    [AvaloniaFact]
    public void A_new_document_is_ready_to_type_into_and_typing_replaces_the_central_text()
    {
        var s = new Session();

        s.Type("Plan");
        Assert.True(s.Canvas.IsEditorOpen);
        s.Press(Key.Enter);

        Assert.False(s.Canvas.IsEditorOpen);
        Assert.Equal("Plan", s.Central.Text);
        Assert.Equal(CommandKind.EditText, s.Main.History.UndoKind);
    }

    [AvaloniaFact]
    public void A_whole_editing_session_is_one_undo_step()
    {
        var s = new Session();
        s.Press(Key.Insert);
        s.Type("Ideas");
        s.Press(Key.Enter);

        Assert.Equal("Ideas", Assert.Single(s.Central.Children).Text);

        s.Press(Key.Z, RawInputModifiers.Control);

        Assert.Empty(s.Central.Children);
        Assert.False(s.Main.History.CanUndo);
    }

    [AvaloniaFact]
    public void Enter_adds_a_sibling_and_on_the_central_topic_a_main_topic()
    {
        var s = new Session();
        s.Press(Key.Enter);
        s.Type("One");
        s.Press(Key.Enter);
        s.Press(Key.Enter);
        s.Type("Two");
        s.Press(Key.Enter);

        Assert.Equal(["One", "Two"], s.Central.Children.Select(child => child.Text));
    }

    [AvaloniaFact]
    public void A_new_topic_left_empty_disappears_without_an_undo_step()
    {
        var s = new Session();

        s.Press(Key.Insert);
        s.Press(Key.Enter);

        Assert.Empty(s.Central.Children);
        Assert.False(s.Main.History.CanUndo);
        Assert.Equal(s.Central.Id, s.Main.SelectedTopic);
    }

    [AvaloniaFact]
    public void Esc_puts_the_previous_text_back()
    {
        var s = new Session();

        s.Press(Key.F2);
        s.Type(" changed");
        Assert.Equal("Central Topic changed", s.Canvas.Editor.Text);
        s.Press(Key.Escape);

        Assert.Equal("Central Topic", s.Central.Text);
        Assert.False(s.Main.History.CanUndo);
    }

    [AvaloniaFact]
    public void The_map_stays_still_while_typing_and_makes_room_after_enter()
    {
        var s = new Session();
        s.Press(Key.Insert);
        s.Type("Main");
        s.Press(Key.Enter);
        s.Press(Key.Insert);
        s.Type("First");
        s.Press(Key.Enter);
        s.Press(Key.Enter);
        s.Type("Second");
        s.Press(Key.Enter);
        Topic main = Assert.Single(s.Central.Children);
        TopicId first = main.Children[0].Id;
        TopicId second = main.Children[1].Id;
        LayoutRect firstBefore = s.Canvas.Layout!.Find(first)!.Bounds;
        LayoutRect secondBefore = s.Canvas.Layout.Find(second)!.Bounds;

        s.Main.Select(first);
        s.Press(Key.F2);
        s.Type(" with a much longer text that wraps onto more lines");
        Session.Pump();

        // Nothing moved and nothing changed in the document: only the editor grew, over its neighbours.
        Assert.Equal("First", s.Map.Find(first)!.Text);
        Assert.Equal(firstBefore, s.Canvas.Layout.Find(first)!.Bounds);
        Assert.Equal(secondBefore, s.Canvas.Layout.Find(second)!.Bounds);

        // All of the typed text is visible: the editor is wider than the topic was, starts where the topic starts
        // (the right side grows away from the parent) and is as tall as its lines.
        Rect editor = s.Canvas.Editor.Bounds;
        Point topic = s.Canvas.Viewport.ToScreen(new LayoutPoint(firstBefore.X, firstBefore.Y));
        Assert.True(editor.Width > firstBefore.Width * s.Canvas.Viewport.Zoom, "the editor grows with the text");
        Assert.InRange(editor.X, topic.X - 1, topic.X + 1);
        Assert.True(editor.Height > firstBefore.Height * s.Canvas.Viewport.Zoom, "the wrapped text takes more lines");

        s.Press(Key.Enter);

        // The topic the map lays out for the text is no taller than the editor was: no line was cut off.
        LayoutRect firstAfter = s.Canvas.Layout.Find(first)!.Bounds;
        Assert.Equal("First with a much longer text that wraps onto more lines", s.Map.Find(first)!.Text);
        Assert.True(firstAfter.Height > firstBefore.Height);
        Assert.True(editor.Height >= (firstAfter.Height * s.Canvas.Viewport.Zoom) - 1, "every typed line was shown");
        Assert.NotEqual(secondBefore, s.Canvas.Layout.Find(second)!.Bounds);
        Assert.Equal(CommandKind.EditText, s.Main.History.UndoKind);
    }

    /// <summary>The editor over a wrapped subtopic of the Lisbon sample, mid-typing, in both themes.</summary>
    [AvaloniaFact]
    public void The_editor_while_typing_renders_in_both_themes()
    {
        var frames = new List<string>();
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            s.Main.Execute(CommandId.NewSample);
            s.Main.ActivateMap(s.Main.Document.Maps[1].Id);
            Session.Pump();
            s.Main.Select(s.Map.Root.Children[0].Children[0].Id);
            s.Press(Key.F2);
            s.Type(" and a few more words that wrap onto more lines");
            frames.Add(Render.Save(s.Window, theme, "editor-typing"));
        }

        Assert.NotEqual(File.ReadAllBytes(frames[0]), File.ReadAllBytes(frames[1]));
    }

    [AvaloniaFact]
    public void Switching_maps_while_typing_keeps_the_typed_text()
    {
        var s = new Session();
        s.Main.Execute(CommandId.NewMap);
        s.Main.ActivateMap(s.Main.Document.Maps[0].Id);
        s.Press(Key.F2);
        s.Type(" typed");

        s.Main.ActivateMap(s.Main.Document.Maps[1].Id);

        Assert.Equal("Central Topic typed", s.Main.Document.Maps[0].Root.Text);
    }

    [AvaloniaFact]
    public void Shift_enter_inserts_a_line_break_in_the_topic()
    {
        var s = new Session();

        s.Type("Line");
        s.Press(Key.Enter, RawInputModifiers.Shift);
        s.Type("two");
        s.Press(Key.Enter);

        Assert.Equal("Line\ntwo", s.Central.Text.ReplaceLineEndings("\n"));
    }

    [AvaloniaFact]
    public void Spaces_and_empty_lines_around_the_text_are_trimmed_when_it_is_kept()
    {
        var s = new Session();

        s.Type("  Plan");
        s.Press(Key.Enter, RawInputModifiers.Shift);
        s.Type("next   ");
        s.Press(Key.Enter, RawInputModifiers.Shift);
        s.Press(Key.Enter, RawInputModifiers.Shift);
        s.Type("   ");
        s.Press(Key.Enter);

        Assert.Equal("Plan\nnext", s.Central.Text.ReplaceLineEndings("\n"));
    }

    [AvaloniaFact]
    public void Canvas_keys_do_not_fire_while_typing_in_the_editor()
    {
        var s = new Session();
        s.Press(Key.Insert);
        s.Type("Keep me");
        s.Press(Key.Enter);

        s.Press(Key.F2);
        s.Press(Key.Delete);
        s.Press(Key.Back);
        s.Press(Key.Enter);

        Topic kept = Assert.Single(s.Central.Children);
        Assert.Equal("Keep m", kept.Text);
    }

    [AvaloniaFact]
    public void Arrow_keys_move_the_selection_and_delete_removes_the_selected_topic()
    {
        var s = new Session();
        s.Press(Key.Insert);
        s.Type("Right");
        s.Press(Key.Enter);
        s.Press(Key.Home, RawInputModifiers.Control);
        Assert.Equal(s.Central.Id, s.Main.SelectedTopic);

        s.Press(Key.Right);
        Assert.Equal("Right", s.Selected.Text);

        s.Press(Key.Delete);
        Assert.Empty(s.Central.Children);
        Assert.Equal(s.Central.Id, s.Main.SelectedTopic);
    }

    [AvaloniaFact]
    public void The_central_topic_cannot_be_deleted_from_the_keyboard()
    {
        var s = new Session();

        s.Press(Key.Delete);

        Assert.Equal("Central Topic", s.Central.Text);
        Assert.False(s.Main.Commands[Commands.CommandId.DeleteTopic].Command.CanExecute(null));
    }

    [AvaloniaFact]
    public void Undo_and_redo_restore_the_selection_too()
    {
        var s = new Session();
        s.Press(Key.Insert);
        s.Type("A");
        s.Press(Key.Enter);
        TopicId a = s.Main.SelectedTopic!.Value;
        s.Press(Key.Delete);

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal(a, s.Main.SelectedTopic);

        s.Press(Key.Y, RawInputModifiers.Control);
        Assert.Equal(s.Central.Id, s.Main.SelectedTopic);
    }

    /// <summary>
    /// On macOS, ⌘↩ is a menu-bar key equivalent, which the headless platform does not have (it is on the manual
    /// checklist). What the canvas binds itself is tested here: ⌫ deletes.
    /// </summary>
    [AvaloniaFact]
    public void On_macOS_backspace_deletes_the_selected_topic()
    {
        var s = new Session(HostPlatform.MacOS);
        s.Main.Execute(Commands.CommandId.NewSubtopic);
        Session.Pump();
        s.Type("Sub");
        s.Press(Key.Enter);
        Assert.Equal("Sub", Assert.Single(s.Central.Children).Text);

        s.Press(Key.Back);

        Assert.Empty(s.Central.Children);
    }

    /// <summary>The editor over a topic in both themes, for review: it must look like the topic it edits.</summary>
    [AvaloniaFact]
    public void The_editor_renders_over_its_topic_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            s.Press(Key.Insert);
            s.Type("Being typed");
            string path = Render.Save(s.Window, theme, "editing");
            Assert.True(new FileInfo(path).Length > 0);
        }
    }

    /// <summary>Shift+Space has no command; the space it types must not replace the text.</summary>
    [AvaloniaFact]
    public void Shift_space_does_not_replace_the_topic_text_with_a_blank()
    {
        var s = new Session();
        string before = s.Central.Text;

        s.Press(Key.Space, RawInputModifiers.Shift);
        s.Type(" ");

        Assert.False(s.Main.IsEditing);
        Assert.Equal(before, s.Central.Text);
    }
}
