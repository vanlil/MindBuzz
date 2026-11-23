using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using MindBuzz.Core.Search;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The search panel end to end.</summary>
public sealed class SearchTests
{
    private static void Run(Session s, CommandId id) => s.Main.Commands[id].Command.Execute(null);

    private static TextBox QueryBox(Session s) => s.Window.FindControl<SearchPanel>("SearchPanelView")!.FindControl<TextBox>("QueryBox")!;

    /// <summary>Central topic with "Alpha plan", "Beta" (collapsed, holding "Hidden plan") and "Gamma" with a
    /// note.</summary>
    private static Session Sample()
    {
        var s = new Session();
        TopicId root = s.Central.Id;
        MapId map = s.Map.Id;
        s.Main.Apply(new SetTopicText(map, root, "Launch"));
        foreach (string text in (string[])["Alpha plan", "Beta", "Gamma"])
        {
            s.Main.Apply(new AddTopic(map, root, NewTopicPlacement.Subtopic, new TopicId(text[..1]), text));
            s.Main.Apply(new SetTopicText(map, new TopicId(text[..1]), text));
        }

        s.Main.Apply(new AddTopic(map, new TopicId("B"), NewTopicPlacement.Subtopic, new TopicId("H"), "Hidden plan"));
        s.Main.Apply(new SetTopicText(map, new TopicId("H"), "Hidden plan"));
        s.Main.Apply(new SetCollapsed(map, new TopicId("B"), true));
        s.Main.Apply(new SetNote(map, new TopicId("G"), "The plan for the gamma release"));
        s.Main.Select(root);
        Session.Pump();
        return s;
    }

    private static void Find(Session s, string query)
    {
        s.Press(Key.F, RawInputModifiers.Control);
        QueryBox(s).Text = query;
        Session.Pump();
    }

    [AvaloniaFact]
    public void Ctrl_f_opens_the_panel_with_the_keyboard_in_the_box_and_typing_lists_the_hits()
    {
        Session s = Sample();
        bool dirty = s.Main.IsDirty;
        bool canUndo = s.Main.History.CanUndo;

        Find(s, "PLAN");

        Assert.True(s.Main.Search.IsOpen);
        Assert.True(QueryBox(s).IsKeyboardFocusWithin);
        Assert.Equal("3 hits", s.Main.Search.CountText);
        Assert.Equal(["A", "H", "G"], s.Main.Search.Rows.OfType<SearchHitRow>().Select(r => r.Hit.Topic.Value));
        Assert.Equal("plan", s.Main.Search.Rows.OfType<SearchHitRow>().Last().Match);
        Assert.Equal(new HashSet<TopicId> { new("A"), new("H"), new("G") }, s.Main.SearchHighlights);
        Assert.Equal(dirty, s.Main.IsDirty);
        Assert.Equal(canUndo, s.Main.History.CanUndo);
    }

    [AvaloniaFact]
    public void The_toolbar_button_opens_and_closes_the_panel_while_ctrl_f_only_opens_it()
    {
        Session s = Sample();

        Run(s, CommandId.ToggleSearch);
        Assert.True(s.Main.Search.IsOpen);
        Run(s, CommandId.ToggleSearch);
        Assert.False(s.Main.Search.IsOpen);
        Assert.True(s.Canvas.IsFocused);

        s.Press(Key.F, RawInputModifiers.Control);
        s.Press(Key.F, RawInputModifiers.Control);
        Assert.True(s.Main.Search.IsOpen);
        Assert.Contains(s.Main.Toolbar, item => item.Command == s.Main.Commands[CommandId.ToggleSearch].Command);
    }

    [AvaloniaFact]
    public void Enter_steps_through_the_hits_and_shift_enter_and_f3_go_both_ways()
    {
        Session s = Sample();
        Find(s, "plan");

        s.Press(Key.Enter);
        Assert.Equal(new TopicId("A"), s.Main.SelectedTopic);
        s.Press(Key.Enter);
        Assert.Equal(new TopicId("H"), s.Main.SelectedTopic);
        s.Press(Key.Enter, RawInputModifiers.Shift);
        Assert.Equal(new TopicId("A"), s.Main.SelectedTopic);
        s.Press(Key.F3, RawInputModifiers.Shift);
        Assert.Equal(new TopicId("G"), s.Main.SelectedTopic);
        s.Press(Key.F3);
        Assert.Equal(new TopicId("A"), s.Main.SelectedTopic);
        Assert.True(QueryBox(s).IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void A_hit_in_a_collapsed_branch_is_shown_without_changing_the_document_and_closing_keeps_only_its_branch()
    {
        Session s = Sample();
        bool canUndo = s.Main.History.CanUndo;
        bool dirty = s.Main.IsDirty;
        Find(s, "hidden");

        s.Press(Key.Enter);

        Assert.Equal(new TopicId("H"), s.Main.SelectedTopic);
        Assert.True(s.Main.ActiveMap.Find(new TopicId("B"))!.IsCollapsed);
        Assert.False(s.Main.DisplayMap.Find(new TopicId("B"))!.IsCollapsed);
        Assert.NotNull(s.Canvas.Layout!.Find(new TopicId("H")));
        Assert.Equal(canUndo, s.Main.History.CanUndo);
        Assert.Equal(dirty, s.Main.IsDirty);

        s.Press(Key.Escape);
        Assert.False(s.Main.Search.IsOpen);
        Assert.True(s.Canvas.IsFocused);
        Assert.Equal(new TopicId("H"), s.Main.SelectedTopic);
        Assert.False(s.Main.DisplayMap.Find(new TopicId("B"))!.IsCollapsed);

        s.Main.Select(new TopicId("A"));
        Run(s, CommandId.Find);
        QueryBox(s).Text = "alpha";
        Session.Pump();
        s.Press(Key.Enter);
        s.Press(Key.Escape);
        Assert.True(s.Main.DisplayMap.Find(new TopicId("B"))!.IsCollapsed);
    }

    [AvaloniaFact]
    public void Collapsing_a_branch_search_opened_takes_the_opening_back_without_an_undo_step()
    {
        Session s = Sample();
        Find(s, "hidden");
        s.Press(Key.Enter);
        bool canUndo = s.Main.History.CanUndo;

        s.Main.ToggleCollapsed(new TopicId("B"));

        Assert.True(s.Main.DisplayMap.Find(new TopicId("B"))!.IsCollapsed);
        Assert.Equal(new TopicId("B"), s.Main.SelectedTopic);
        Assert.Equal(canUndo, s.Main.History.CanUndo);
    }

    [AvaloniaFact]
    public void A_note_hit_opens_the_detail_view_with_the_match_selected_and_the_keyboard_stays_in_the_box()
    {
        Session s = Sample();
        Find(s, "gamma release");

        s.Press(Key.Enter);

        Assert.True(s.Main.Panel.IsOpen);
        Assert.True(s.Main.Panel.IsEditMode);
        TopicPanel panel = s.Window.FindControl<TopicPanel>("TopicPanelView")!;
        Assert.Equal("gamma release", panel.NoteEditor.SelectedText);
        Assert.True(QueryBox(s).IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void All_maps_groups_the_hits_by_map_and_going_to_one_switches_the_tab()
    {
        Session s = Sample();
        Run(s, CommandId.NewMap);
        s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "Second plan"));
        MapId second = s.Map.Id;
        s.Main.ActivateMap(s.Main.Document.Maps[0].Id);

        Find(s, "plan");
        Assert.Equal(3, s.Main.Search.Hits.Count);
        s.Main.Search.AllMaps = true;

        Assert.Equal(2, s.Main.Search.Rows.OfType<SearchMapRow>().Count());
        Assert.Equal(4, s.Main.Search.Hits.Count);

        s.Press(Key.Enter, RawInputModifiers.Shift);
        Assert.Equal(second, s.Main.ActiveMap.Id);
    }

    [AvaloniaFact]
    public void All_maps_is_greyed_out_with_one_map_and_falls_back_when_the_second_map_goes()
    {
        Session s = Sample();
        Find(s, "plan");
        Assert.False(s.Main.Search.EveryMapCommand.CanExecute(null));

        Run(s, CommandId.NewMap);
        Assert.True(s.Main.Search.EveryMapCommand.CanExecute(null));
        s.Main.Search.EveryMapCommand.Execute(null);
        Assert.True(s.Main.Search.AllMaps);

        Run(s, CommandId.Undo);
        Assert.False(s.Main.Search.AllMaps);
        Assert.False(s.Main.Search.EveryMapCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Editing_while_searching_updates_the_results()
    {
        Session s = Sample();
        Find(s, "plan");

        s.Main.Apply(new SetTopicText(s.Map.Id, new TopicId("B"), "Beta plan"));

        Assert.Equal("4 hits", s.Main.Search.CountText);
    }

    [AvaloniaFact]
    public void F6_goes_from_the_map_to_search_to_the_detail_view_and_back()
    {
        Session s = Sample();
        Find(s, "plan");
        Run(s, CommandId.ToggleTopicPanel);
        Session.Pump();
        s.Canvas.Focus();
        Session.Pump();

        s.Press(Key.F6);
        Assert.True(QueryBox(s).IsKeyboardFocusWithin);
        s.Press(Key.F6);
        Assert.True(s.Window.FindControl<TopicPanel>("TopicPanelView")!.IsKeyboardFocusWithin);
        s.Press(Key.F6);
        Assert.True(s.Canvas.IsFocused);
        s.Press(Key.F6, RawInputModifiers.Shift);
        Assert.True(s.Window.FindControl<TopicPanel>("TopicPanelView")!.IsKeyboardFocusWithin);
    }

    /// <summary>With the detail view closed, F6 still visits search first, and opens the detail view after
    /// it.</summary>
    [AvaloniaFact]
    public void F6_visits_search_before_opening_a_closed_detail_view()
    {
        Session s = Sample();
        Find(s, "plan");
        s.Canvas.Focus();
        Session.Pump();
        Assert.False(s.Main.Panel.IsOpen);

        s.Press(Key.F6);
        Assert.True(QueryBox(s).IsKeyboardFocusWithin);
        Assert.False(s.Main.Panel.IsOpen);

        s.Press(Key.F6);
        Assert.True(s.Main.Panel.IsOpen);
        Assert.True(s.Window.FindControl<TopicPanel>("TopicPanelView")!.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void Renders_search_beside_the_detail_view_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            Session s = Sample();
            Find(s, "plan");
            s.Press(Key.Enter);
            s.Press(Key.Enter);
            Run(s, CommandId.ToggleTopicPanel);
            Session.Pump();
            Render.Save(s.Window, theme, "search");
        }
    }
}

/// <summary>The dividers between the map, search and the detail view.</summary>
public sealed class PaneDividerTests
{
    private static (Session S, Grid Grid) Open()
    {
        var s = new Session();
        s.Main.Commands[CommandId.Find].Command.Execute(null);
        s.Main.Commands[CommandId.ToggleTopicPanel].Command.Execute(null);
        Session.Pump();
        return (s, s.Window.FindControl<Grid>("Workspace")!);
    }

    private static Point Middle(Session s, string divider) =>
        s.Window.FindControl<PaneDivider>(divider)!.TranslatePoint(new Point(2, 200), s.Window)!.Value;

    [AvaloniaFact]
    public void The_divider_between_search_and_details_moves_with_the_pointer_and_the_map_stays()
    {
        (Session s, Grid grid) = Open();
        double map = grid.ColumnDefinitions[0].ActualWidth;
        double search = grid.ColumnDefinitions[2].ActualWidth;
        double details = grid.ColumnDefinitions[4].ActualWidth;
        Point at = Middle(s, "PanelSplitter");

        s.Drag(at, at + new Vector(-40, 0));
        Assert.Equal(search - 40, grid.ColumnDefinitions[2].ActualWidth, 1);
        Assert.Equal(details + 40, grid.ColumnDefinitions[4].ActualWidth, 1);
        Assert.Equal(map, grid.ColumnDefinitions[0].ActualWidth, 1);

        // Back part of the way: the detail view opens at its narrowest, so it cannot get narrower than at the start.
        s.Drag(at + new Vector(-40, 0), at + new Vector(-20, 0));
        Assert.Equal(search - 20, grid.ColumnDefinitions[2].ActualWidth, 1);
        Assert.Equal(details + 20, grid.ColumnDefinitions[4].ActualWidth, 1);
        Assert.Equal(map, grid.ColumnDefinitions[0].ActualWidth, 1);
    }

    [AvaloniaFact]
    public void The_divider_left_of_search_trades_with_the_map_and_stops_at_the_minimum()
    {
        (Session s, Grid grid) = Open();
        double search = grid.ColumnDefinitions[2].ActualWidth;
        double details = grid.ColumnDefinitions[4].ActualWidth;
        Point at = Middle(s, "SearchSplitter");

        s.Drag(at, at + new Vector(-50, 0));
        Assert.Equal(search + 50, grid.ColumnDefinitions[2].ActualWidth, 1);
        Assert.Equal(details, grid.ColumnDefinitions[4].ActualWidth, 1);

        s.Drag(at + new Vector(-50, 0), at + new Vector(400, 0));
        Assert.Equal(220, grid.ColumnDefinitions[2].ActualWidth, 1);
    }
}
