using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.Platform;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Several topics selected: ⌘/Ctrl-click, Shift+arrows, Delete and drag for all of them.</summary>
public sealed class MultiSelectTests
{
    /// <summary>⌘ on macOS, Ctrl elsewhere: the canvas follows the machine it runs on.</summary>
    private static RawInputModifiers ToggleKey => Host.Current == HostPlatform.MacOS ? RawInputModifiers.Meta : RawInputModifiers.Control;

    /// <summary>A central topic with main topics One, Two, Three; One has a subtopic One-a.</summary>
    private static (Session S, TopicId One, TopicId Two, TopicId Three, TopicId OneA) Map()
    {
        var s = new Session();
        TopicId Add(TopicId parent, string text)
        {
            s.Main.Select(parent);
            s.Main.Execute(CommandId.NewSubtopic);
            Session.Pump();
            s.Type(text);
            s.Press(Key.Enter);
            return s.Main.SelectedTopic!.Value;
        }

        TopicId one = Add(s.Central.Id, "One");
        TopicId oneA = Add(one, "One-a");
        TopicId two = Add(s.Central.Id, "Two");
        TopicId three = Add(s.Central.Id, "Three");
        s.Main.Select(one);
        Session.Pump();
        return (s, one, two, three, oneA);
    }

    private static void ToggleClick(Session s, TopicId topic)
    {
        Point at = s.On(topic);
        s.Window.MouseDown(at, MouseButton.Left, ToggleKey);
        s.Window.MouseUp(at, MouseButton.Left, ToggleKey);
        Session.Pump();
    }

    [AvaloniaFact]
    public void Command_click_adds_and_removes_topics_and_the_last_one_leads()
    {
        (Session s, TopicId one, TopicId two, TopicId three, _) = Map();

        ToggleClick(s, two);
        ToggleClick(s, three);
        Assert.Equal([one, two, three], s.Main.SelectedTopics);
        Assert.Equal(three, s.Main.SelectedTopic);

        ToggleClick(s, three);
        Assert.Equal([one, two], s.Main.SelectedTopics);
        Assert.Equal(two, s.Main.SelectedTopic);
    }

    [AvaloniaFact]
    public void Delete_takes_all_selected_topics_in_one_undo_step()
    {
        (Session s, TopicId one, TopicId two, TopicId three, _) = Map();
        ToggleClick(s, three);

        s.Press(Key.Delete);

        Assert.Equal([two], s.Central.Children.Select(child => child.Id));
        Assert.Single(s.Main.SelectedTopics);

        s.Main.Execute(CommandId.Undo);
        Assert.Equal([one, two, three], s.Central.Children.Select(child => child.Id));
    }

    [AvaloniaFact]
    public void Shift_and_an_arrow_add_the_next_topic_and_esc_keeps_only_the_last()
    {
        (Session s, TopicId one, _, _, _) = Map();
        TopicId below = s.Main.SelectedTopic!.Value;

        s.Press(Key.Down, RawInputModifiers.Shift);

        Assert.Equal(2, s.Main.SelectedTopics.Count);
        Assert.Equal(one, s.Main.SelectedTopics[0]);
        TopicId last = s.Main.SelectedTopic!.Value;
        Assert.NotEqual(below, last);

        s.Press(Key.Escape);
        Assert.Equal([last], s.Main.SelectedTopics);
    }

    [AvaloniaFact]
    public void A_plain_click_on_one_of_several_selects_only_it()
    {
        (Session s, TopicId one, TopicId two, _, _) = Map();
        ToggleClick(s, two);

        s.Click(s.On(one));

        Assert.Equal([one], s.Main.SelectedTopics);
    }

    [AvaloniaFact]
    public void Dragging_one_of_several_selected_topics_moves_them_all()
    {
        (Session s, TopicId one, TopicId two, TopicId three, TopicId oneA) = Map();
        // Three selected first, Two ⌘-clicked: then Three is dragged (a press right after a click on the same topic
        // would be a double-click).
        s.Main.Select(three);
        ToggleClick(s, two);

        // Onto One-a: both become its subtopics, in map order.
        s.Drag(s.On(three), s.On(oneA));

        Assert.Equal([two, three], s.Map.Find(oneA)!.Children.Select(child => child.Id));
        Assert.Equal([one], s.Central.Children.Select(child => child.Id));
    }

    /// <summary>The central topic in the selection stays where it is and does not stop the others: every drop
    /// target lies inside it, so the canvas refused them all.</summary>
    [AvaloniaFact]
    public void Dragging_several_topics_with_the_central_topic_among_them_moves_the_others()
    {
        (Session s, TopicId one, TopicId two, TopicId three, TopicId oneA) = Map();
        s.Main.Select(three);
        ToggleClick(s, s.Central.Id);
        ToggleClick(s, two);

        s.Drag(s.On(three), s.On(oneA));

        Assert.Equal([two, three], s.Map.Find(oneA)!.Children.Select(child => child.Id));
        Assert.Equal([one], s.Central.Children.Select(child => child.Id));
    }

    [AvaloniaFact]
    public void Other_commands_act_on_the_last_selected_topic_and_the_detail_view_says_how_many()
    {
        (Session s, TopicId one, TopicId two, _, _) = Map();
        ToggleClick(s, two);
        s.Main.Execute(CommandId.Priority1);
        s.Main.Execute(CommandId.ToggleTopicPanel);
        Session.Pump();

        Assert.Null(s.Map.Find(one)!.MarkerOf(MarkerGroups.Priority));
        Assert.NotNull(s.Map.Find(two)!.MarkerOf(MarkerGroups.Priority));
        TextBlock note = s.Window.FindControl<TopicPanel>("TopicPanelView")!.FindControl<TextBlock>("SelectionNote")!;
        Assert.True(note.IsVisible);
        Assert.StartsWith("2", note.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Copy_and_paste_take_all_selected_topics_side_by_side()
    {
        var ui = new FakeUiServices();
        var main = new ViewModels.MainViewModel(ui, new FakePlatformShell(), HostPlatform.Windows);
        TopicId Add(string text)
        {
            main.Select(main.ActiveMap.Root.Id);
            main.Execute(CommandId.NewSubtopic);
            main.CommitEdit(text);
            return main.SelectedTopic!.Value;
        }

        TopicId one = Add("One");
        TopicId two = Add("Two");
        TopicId target = Add("Target");
        main.Select(one);
        main.ToggleSelected(two);

        main.Execute(CommandId.Copy);
        await Task.Yield();
        Assert.Equal("One\nTwo\n", ui.Clipboard.Text);

        main.Select(target);
        main.Execute(CommandId.Paste);
        await Task.Yield();

        Assert.Equal(["One", "Two"], main.ActiveMap.Find(target)!.Children.Select(child => child.Text));
    }

    [AvaloniaFact]
    public async Task Cut_takes_all_selected_topics_and_leaves_the_central_topic()
    {
        var ui = new FakeUiServices();
        var main = new ViewModels.MainViewModel(ui, new FakePlatformShell(), HostPlatform.Windows);
        main.Select(main.ActiveMap.Root.Id);
        main.Execute(CommandId.NewSubtopic);
        main.CommitEdit("One");
        TopicId one = main.SelectedTopic!.Value;
        main.ToggleSelected(main.ActiveMap.Root.Id);

        // The central topic leads, yet Cut works on the others.
        Assert.True(main.CanExecute(CommandId.Cut));
        main.Execute(CommandId.Cut);
        await Task.Yield();

        Assert.Null(main.ActiveMap.Find(one));
        Assert.Equal("One\n", ui.Clipboard.Text);
    }

    [AvaloniaFact]
    public void Several_selected_topics_render_in_both_themes()
    {
        var frames = new List<string>();
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            (Session s, _, TopicId two, TopicId three, _) = Map();
            ToggleClick(s, two);
            ToggleClick(s, three);
            frames.Add(Render.Save(s.Window, theme, "multi-select"));
        }

        Assert.NotEqual(File.ReadAllBytes(frames[0]), File.ReadAllBytes(frames[1]));
    }
}
