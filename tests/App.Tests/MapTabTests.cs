using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Several maps in one document, as sheet tabs, through the view model.</summary>
public sealed class MapTabTests : IDisposable
{
    private const int Cancel = 0;
    private const int Delete = 1;

    private readonly FakeUiServices _ui = new();
    private readonly MainViewModel _main;

    public MapTabTests()
    {
        _main = new MainViewModel(_ui, new FakePlatformShell(), HostPlatform.Windows);
    }

    public void Dispose() => _main.Dispose();

    private string Names => string.Join(',', _main.Tabs.Select(tab => tab.Name));

    [AvaloniaFact]
    public void Typing_in_a_note_keeps_the_same_tab_list_and_renaming_makes_a_new_one()
    {
        IReadOnlyList<MapTab> before = _main.Tabs;

        _main.UpdateNote(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, "a note");
        Assert.Same(before, _main.Tabs);

        _main.Apply(new SetTopicText(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, "Renamed"));
        Assert.NotSame(before, _main.Tabs);
    }

    private string Active => _main.Tabs.Single(tab => tab.IsActive).Name;

    private TopicId AddTopic(string text)
    {
        TopicId id = RandomIdSource.Instance.NextTopic();
        _main.Apply(new AddTopic(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, NewTopicPlacement.Subtopic, id, text));
        return id;
    }

    [AvaloniaFact]
    public void A_new_map_is_added_last_activated_and_ready_to_type_into()
    {
        _main.Execute(CommandId.NewMap);

        Assert.Equal("Central Topic,Central Topic 2", Names);
        Assert.Equal("Central Topic 2", Active);
        Assert.Equal(_main.ActiveMap.Root.Id, _main.SelectedTopic);
        Assert.Equal("Central Topic 2", _main.ActiveMap.Root.Text);
    }

    [AvaloniaFact]
    public void The_tab_follows_the_central_topic_and_an_unchanged_rename_keeps_its_line_breaks()
    {
        _main.Apply(new Core.Editing.SetTopicText(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, "Roadmap\n2027"));
        Assert.Equal("Roadmap…", Active);
        Assert.Equal("Roadmap 2027", _main.Tabs.Single(tab => tab.IsActive).FullName);

        _main.BeginRenameMap(_main.ActiveMap.Id);
        _main.CommitRenameMap("Roadmap 2027");
        Assert.Equal("Roadmap\n2027", _main.ActiveMap.Root.Text);

        _main.BeginRenameMap(_main.ActiveMap.Id);
        _main.CommitRenameMap("Plan");
        Assert.Equal("Plan", _main.ActiveMap.Root.Text);
        Assert.Equal("Plan", Active);
    }

    [AvaloniaFact]
    public void New_map_names_never_repeat_an_existing_one()
    {
        _main.Execute(CommandId.NewMap);
        _main.BeginRenameMap(_main.ActiveMap.Id);
        _main.CommitRenameMap("Central Topic 3");

        _main.Execute(CommandId.NewMap);
        _main.Execute(CommandId.NewMap);

        Assert.Equal("Central Topic,Central Topic 3,Central Topic 2,Central Topic 4", Names);
    }

    [AvaloniaFact]
    public void Next_and_previous_wrap_around()
    {
        _main.Execute(CommandId.NewMap);
        _main.Execute(CommandId.NewMap);

        _main.Execute(CommandId.NextMap);
        Assert.Equal("Central Topic", Active);

        _main.Execute(CommandId.PreviousMap);
        Assert.Equal("Central Topic 3", Active);
    }

    [AvaloniaFact]
    public void Every_map_keeps_its_own_selection()
    {
        TopicId onFirst = AddTopic("first");
        MapId first = _main.ActiveMap.Id;
        _main.Execute(CommandId.NewMap);
        _main.Select(AddTopic("second"));

        _main.ActivateMap(first);

        Assert.Equal(onFirst, _main.SelectedTopic);
    }

    [AvaloniaFact]
    public void Renaming_commits_a_name_refuses_an_empty_one_and_esc_cancels()
    {
        MapId id = _main.ActiveMap.Id;

        _main.BeginRenameMap(id);
        Assert.True(_main.Tabs[0].IsRenaming);
        _main.CommitRenameMap("  Ideas  ");
        Assert.Equal("Ideas", Names);

        _main.BeginRenameMap(id);
        _main.CommitRenameMap("   ");
        Assert.Equal("Ideas", Names);
        Assert.Equal(Localization.Strings.Refusal_EmptyName, _main.StatusText);

        _main.BeginRenameMap(id);
        _main.CancelRenameMap();
        Assert.Equal("Ideas", Names);
        Assert.False(_main.Tabs[0].IsRenaming);
    }

    [AvaloniaFact]
    public async Task Delete_asks_first_and_cancel_keeps_the_map()
    {
        _main.Execute(CommandId.NewMap);
        _ui.Answers.Enqueue(Cancel);

        _main.Execute(CommandId.DeleteMap);
        await Task.Yield();

        Assert.Single(_ui.Questions);
        Assert.Equal("Central Topic,Central Topic 2", Names);
    }

    [AvaloniaFact]
    public async Task A_deleted_map_comes_back_with_undo_and_is_shown_again()
    {
        _main.Execute(CommandId.NewMap);
        AddTopic("precious");
        _ui.Answers.Enqueue(Delete);

        _main.Execute(CommandId.DeleteMap);
        await Task.Yield();
        Assert.Equal("Central Topic", Names);

        _main.Execute(CommandId.Undo);

        Assert.Equal("Central Topic,Central Topic 2", Names);
        Assert.Equal("Central Topic 2", Active);
        Assert.Equal("precious", Assert.Single(_main.ActiveMap.Root.Children).Text);
    }

    [AvaloniaFact]
    public void The_last_map_cannot_be_deleted_and_the_ends_cannot_move_further()
    {
        Assert.False(_main.CanExecute(CommandId.DeleteMap));
        Assert.False(_main.CanExecute(CommandId.MoveMapLeft));
        Assert.False(_main.CanExecute(CommandId.MoveMapRight));
    }

    [AvaloniaFact]
    public void Maps_move_left_and_right()
    {
        _main.Execute(CommandId.NewMap);

        _main.Execute(CommandId.MoveMapLeft);

        Assert.Equal("Central Topic 2,Central Topic", Names);
        Assert.Equal("Central Topic 2", Active);
    }

    [AvaloniaFact]
    public void Undoing_a_change_in_another_map_switches_to_that_map_and_its_topic()
    {
        TopicId topic = AddTopic("on map 1");
        _main.Execute(CommandId.NewMap);
        Assert.Equal("Central Topic 2", Active);

        _main.Execute(CommandId.Undo); // the new map
        _main.Execute(CommandId.Undo); // the topic on map 1

        Assert.Equal("Central Topic", Active);
        Assert.Null(_main.ActiveMap.Find(topic));
    }
}

/// <summary>The tab bar end to end: keys, mouse and rendering.</summary>
public sealed class SheetTabsUiTests
{
    private static SheetTabs TabsOf(Session s) => s.Window.FindControl<SheetTabs>("Tabs")!;

    private static Point Centre(Session s, int tab)
    {
        Control control = TabsOf(s).TabControls[tab];
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), s.Window)!.Value;
    }

    [AvaloniaFact]
    public void Ctrl_shift_m_adds_a_map_and_ctrl_tab_and_ctrl_page_down_switch()
    {
        var s = new Session();

        s.Press(Key.M, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(2, s.Main.Tabs.Count);
        Assert.Equal("Central Topic 2", s.Map.Name);

        s.Press(Key.Tab, RawInputModifiers.Control);
        Assert.Equal("Central Topic", s.Map.Name);

        s.Press(Key.PageDown, RawInputModifiers.Control);
        Assert.Equal("Central Topic 2", s.Map.Name);
    }

    [AvaloniaFact]
    public void Clicking_a_tab_shows_its_map()
    {
        var s = new Session();
        s.Main.Execute(CommandId.NewMap);
        Session.Pump();

        s.Click(Centre(s, 0));

        Assert.Equal("Central Topic", s.Map.Name);
    }

    [AvaloniaFact]
    public void Double_clicking_a_tab_renames_it_in_place()
    {
        var s = new Session();
        Point tab = Centre(s, 0);

        s.Window.MouseDown(tab, MouseButton.Left);
        s.Window.MouseUp(tab, MouseButton.Left);
        s.Window.MouseDown(tab, MouseButton.Left);
        s.Window.MouseUp(tab, MouseButton.Left);
        Session.Pump();
        Assert.True(s.Main.Tabs[0].IsRenaming);

        s.Type("Plans");
        s.Press(Key.Enter);

        Assert.Equal("Plans", s.Map.Name);
        Assert.False(s.Main.Tabs[0].IsRenaming);
    }

    [AvaloniaFact]
    public void Dragging_a_tab_reorders_the_maps()
    {
        var s = new Session();
        s.Main.Execute(CommandId.NewMap);
        s.Main.Execute(CommandId.NewMap);
        Session.Pump();

        Point from = Centre(s, 2);
        Control first = TabsOf(s).TabControls[0];
        Point to = first.TranslatePoint(new Point(4, first.Bounds.Height / 2), s.Window)!.Value;
        s.Drag(from, to);

        Assert.Equal("Central Topic 3,Central Topic,Central Topic 2", string.Join(',', s.Main.Tabs.Select(tab => tab.Name)));
    }

    /// <summary>One zoom for all maps; each map keeps its own scroll position.</summary>
    [AvaloniaFact]
    public void The_zoom_is_the_same_on_every_map_and_each_keeps_its_scroll()
    {
        var s = new Session();
        s.Main.Zoom = 2;
        Session.Pump();
        s.Canvas.SetViewport(s.Canvas.Viewport.PanBy(new Avalonia.Vector(120, 0)), animate: false);
        Avalonia.Point kept = s.Canvas.Viewport.Origin;

        s.Main.Execute(CommandId.NewMap);
        Session.Pump();
        Assert.Equal(2, s.Main.Zoom, 3);

        s.Main.Zoom = 0.5;
        Session.Pump();
        s.Main.Execute(CommandId.PreviousMap);
        Session.Pump();
        Assert.Equal(0.5, s.Main.Zoom, 3);

        s.Main.Zoom = 2;
        Session.Pump();
        Assert.Equal(kept.X, s.Canvas.Viewport.Origin.X, 1);
    }

    /// <summary>Enough tabs to overflow the bar, in both themes: the scroll buttons and the active tab, for
    /// review.</summary>
    [AvaloniaFact]
    public void Many_tabs_render_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            for (int i = 0; i < 24; i++)
            {
                s.Main.Execute(CommandId.NewMap);
            }

            s.Main.ActivateMap(s.Main.Tabs[3].Id);
            Session.Pump();

            Assert.True(TabsOf(s).FindControl<StackPanel>("ScrollButtons")!.IsVisible);
            Render.Save(s.Window, theme, "tabs");
        }
    }
}
