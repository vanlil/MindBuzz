using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MindBuzz.App.Commands;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Relationships end to end, from the keyboard and the mouse.</summary>
public sealed class RelationshipTests
{
    private const RawInputModifiers CtrlShift = RawInputModifiers.Control | RawInputModifiers.Shift;

    /// <summary>Central with A, B (right) and C (left); A1 under A, C1 under C.</summary>
    private static Session Build()
    {
        var s = new Session();
        void Add(string parent, string text)
        {
            s.Main.Select(s.Map.Root.DescendantsAndSelf().Single(t => t.Text == parent).Id);
            s.Press(Key.Insert);
            s.Type(text);
            s.Press(Key.Enter);
        }

        Add("Central Topic", "A");
        Add("Central Topic", "B");
        Add("Central Topic", "C");
        Add("A", "A1");
        Add("C", "C1");
        return s;
    }

    private static TopicId Id(Session s, string text) => s.Map.Root.DescendantsAndSelf().Single(t => t.Text == text).Id;

    private static Relationship Only(Session s) => Assert.Single(s.Map.Relationships);

    [AvaloniaFact]
    public void Link_mode_from_the_keyboard_creates_a_one_way_relationship_and_selects_it()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));

        s.Press(Key.R, CtrlShift);
        Assert.True(s.Main.IsLinkMode);
        s.Press(Key.Left);
        s.Press(Key.Enter);

        Relationship relationship = Only(s);
        Assert.Equal((Id(s, "A1"), Id(s, "A")), (relationship.From, relationship.To));
        Assert.Equal(RelationshipDirection.OneWay, relationship.Direction);
        Assert.Equal(relationship.Id, s.Main.SelectedRelationship);
        Assert.False(s.Main.IsLinkMode);
    }

    [AvaloniaFact]
    public void Typing_in_link_mode_finds_the_target_and_shift_enter_links_both_ways()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));

        s.Press(Key.R, CtrlShift);
        s.Type("c1");
        Assert.Equal(Id(s, "C1"), s.Main.LinkTarget);
        s.Press(Key.Enter, RawInputModifiers.Shift);

        Assert.Equal(RelationshipDirection.TwoWay, Only(s).Direction);
    }

    [AvaloniaFact]
    public void Linking_a_topic_to_itself_is_refused_and_link_mode_stays_on()
    {
        Session s = Build();
        s.Main.Select(Id(s, "B"));

        s.Press(Key.R, CtrlShift);
        s.Press(Key.Enter);

        Assert.Empty(s.Map.Relationships);
        Assert.True(s.Main.IsLinkMode);
        Assert.Equal(Localization.Strings.Refusal_SelfRelationship, s.Main.StatusText);

        s.Press(Key.Escape);
        Assert.False(s.Main.IsLinkMode);
    }

    [AvaloniaFact]
    public void B_toggles_two_way_R_reverses_delete_removes_and_esc_returns_to_the_topic()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);

        s.Press(Key.R);
        Assert.Equal(Id(s, "C1"), Only(s).From);

        s.Press(Key.B);
        Assert.Equal(RelationshipDirection.TwoWay, Only(s).Direction);

        s.Press(Key.Escape);
        Assert.Null(s.Main.SelectedRelationship);
        Assert.NotNull(s.Main.SelectedTopic);

        s.Press(Key.J, CtrlShift);
        Assert.NotNull(s.Main.SelectedRelationship);
        s.Press(Key.Delete);
        Assert.Empty(s.Map.Relationships);
        Assert.Equal(5, s.Map.Index.Count - 1);
    }

    [AvaloniaFact]
    public void Letters_on_a_selected_relationship_never_start_editing_the_topic()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);

        s.Type("x");

        Assert.False(s.Main.IsEditing);
        Assert.Equal("A1", s.Map.Find(Id(s, "A1"))!.Text);
    }

    [AvaloniaFact]
    public void Follow_link_jumps_to_the_other_end_and_back_and_cycles_through_several()
    {
        Session s = Build();
        foreach (string target in new[] { "C1", "B" })
        {
            s.Main.Select(Id(s, "A1"));
            s.Press(Key.R, CtrlShift);
            s.Type(target);
            s.Press(Key.Enter);
        }

        s.Main.Select(Id(s, "A1"));
        s.Press(Key.J, RawInputModifiers.Control);
        TopicId first = s.Main.SelectedTopic!.Value;
        s.Press(Key.J, RawInputModifiers.Control);
        Assert.Equal(Id(s, "A1"), s.Main.SelectedTopic);
        s.Press(Key.J, RawInputModifiers.Control);
        TopicId second = s.Main.SelectedTopic!.Value;

        Assert.Equal(new HashSet<TopicId> { Id(s, "B"), Id(s, "C1") }, new HashSet<TopicId> { first, second });
    }

    [AvaloniaFact]
    public void A_collapsed_end_is_drawn_dashed_to_its_ancestor()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);

        s.Main.Select(Id(s, "C"));
        s.Press(Key.OemMinus);

        RelationshipCurve curve = Assert.Single(s.Canvas.Curves);
        Assert.True(curve.EndHidden);
        Assert.Equal(Id(s, "C"), curve.ToBox.Id);
    }

    [AvaloniaFact]
    public void Clicking_a_curve_selects_its_relationship_and_clicking_a_target_in_link_mode_links()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Click(s.On(Id(s, "C1")));
        Relationship relationship = Only(s);
        s.Press(Key.Escape);
        Assert.Null(s.Main.SelectedRelationship);

        RelationshipCurve curve = Assert.Single(s.Canvas.Curves);
        LayoutPoint middle = curve.At(0.5);
        Point onScreen = s.Canvas.TranslatePoint(s.Canvas.Viewport.ToScreen(middle), s.Window)!.Value;
        s.Click(onScreen);

        Assert.Equal(relationship.Id, s.Main.SelectedRelationship);
    }

    [AvaloniaFact]
    public void Deleting_a_topic_removes_its_relationships_and_undo_brings_them_back()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);

        s.Main.Select(Id(s, "C"));
        s.Press(Key.Delete);
        Assert.Empty(s.Map.Relationships);

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Single(s.Map.Relationships);
    }

    [AvaloniaFact]
    public void The_relationship_commands_are_in_the_topic_and_relationship_menus()
    {
        Session s = Build();

        Assert.Contains(s.Main.TopicMenu, entry => entry.Command == s.Main.Commands[CommandId.StartLinkMode].Command);
        Assert.Contains(s.Main.RelationshipMenu, entry => entry.Command == s.Main.Commands[CommandId.ReverseRelationship].Command);
    }

    [AvaloniaFact]
    public void The_menu_greys_out_the_current_direction_and_offers_reverse_only_for_one_way()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);
        bool Can(CommandId id) => s.Main.Commands[id].Command.CanExecute(null);

        Assert.False(Can(CommandId.MakeOneWay));
        Assert.True(Can(CommandId.MakeTwoWay));
        Assert.True(Can(CommandId.ReverseRelationship));

        s.Main.Commands[CommandId.MakeTwoWay].Command.Execute(null);

        Assert.Equal(RelationshipDirection.TwoWay, Only(s).Direction);
        Assert.True(Can(CommandId.MakeOneWay));
        Assert.False(Can(CommandId.MakeTwoWay));
        Assert.False(Can(CommandId.ReverseRelationship));

        s.Press(Key.R);
        Assert.Equal(Id(s, "A1"), Only(s).From);
    }

    [AvaloniaFact]
    public void Dragging_the_handle_bends_the_curve_in_one_undo_step_and_reset_curve_restores_it()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);
        Point handle = s.Canvas.TranslatePoint(s.Canvas.BendHandleOnScreen!.Value, s.Window)!.Value;
        Assert.False(s.Main.Commands[CommandId.ResetCurve].Command.CanExecute(null));

        byte[] before = Render.Pixels(s.Window);
        s.Window.MouseDown(handle, MouseButton.Left);
        s.Window.MouseMove(handle + new Vector(0, 40));
        s.Window.MouseMove(handle + new Vector(0, 80));
        Session.Pump();
        Assert.True(s.Canvas.IsBending);
        Assert.Null(Only(s).Bend);
        byte[] during = Render.Pixels(s.Window);
        s.Window.MouseUp(handle + new Vector(0, 80), MouseButton.Left);
        Session.Pump();

        // A point a third along the new curve, far from both handle positions, was already drawn while dragging.
        Assert.NotNull(Only(s).Bend);
        Point onNewCurve = s.Canvas.TranslatePoint(s.Canvas.Viewport.ToScreen(Assert.Single(s.Canvas.Curves).At(0.33)), s.Window)!.Value;
        Assert.True(Render.DiffersNear(before, during, s.Window, onNewCurve, radius: 2), "The whole curve follows the handle while dragging, not only the handle.");
        Point moved = s.Canvas.TranslatePoint(s.Canvas.BendHandleOnScreen!.Value, s.Window)!.Value;
        Assert.True(Math.Abs(moved.Y - (handle.Y + 80)) < 1, $"The handle follows the drop point: {moved} vs {handle}.");
        Assert.NotNull(s.Main.SelectedRelationship);

        s.Main.Commands[CommandId.ResetCurve].Command.Execute(null);
        Assert.Null(Only(s).Bend);

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.NotNull(Only(s).Bend);
        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Null(Only(s).Bend);
        Assert.Single(s.Map.Relationships);
    }

    [AvaloniaFact]
    public void The_relationship_menu_sets_colour_and_line_with_the_current_value_greyed_out()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);

        Assert.False(s.Main.Commands[CommandId.RelationshipColorDefault].Command.CanExecute(null));
        Assert.False(s.Main.Commands[CommandId.RelationshipLineSolid].Command.CanExecute(null));
        s.Main.Commands[CommandId.RelationshipColorPurple].Command.Execute(null);
        s.Main.Commands[CommandId.RelationshipLineDotted].Command.Execute(null);

        Assert.Equal((RelationshipColor.Purple, RelationshipLine.Dotted), (Only(s).Color, Only(s).Line));
        Assert.False(s.Main.Commands[CommandId.RelationshipColorPurple].Command.CanExecute(null));
        Assert.Contains(s.Main.RelationshipMenu, entry => entry.HasChildren && entry.Children!.Count == 8);
        Assert.Contains(s.Main.RelationshipMenu, entry => entry.HasChildren && entry.Children!.Count == 3);

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal(RelationshipColor.Purple, Only(s).Color);
        Assert.Equal(RelationshipLine.Solid, Only(s).Line);
    }

    /// <summary>The bend handle stays visible, and grabbable, when it lies over a topic.</summary>
    [AvaloniaFact]
    public void The_bend_handle_is_drawn_over_a_topic_and_can_be_dragged_from_there()
    {
        Session s = Build();
        s.Main.Select(Id(s, "A1"));
        s.Press(Key.R, CtrlShift);
        s.Type("C1");
        s.Press(Key.Enter);
        Point handle = s.Canvas.TranslatePoint(s.Canvas.BendHandleOnScreen!.Value, s.Window)!.Value;
        Point onTopic = s.On(Id(s, "B"));
        s.Drag(handle, onTopic);
        handle = s.Canvas.TranslatePoint(s.Canvas.BendHandleOnScreen!.Value, s.Window)!.Value;
        Assert.True(Point.Distance(handle, onTopic) < 6);

        byte[] selected = Render.Pixels(s.Window);
        RelationshipId relationship = s.Main.SelectedRelationship!.Value;
        s.Main.LeaveRelationship();
        byte[] deselected = Render.Pixels(s.Window);
        Assert.True(Render.DiffersNear(selected, deselected, s.Window, handle, 2));

        s.Main.SelectedRelationship = relationship;
        Session.Pump();
        RelationshipBend? before = Only(s).Bend;
        s.Drag(handle, handle + new Vector(0, 40));
        Assert.NotEqual(before, Only(s).Bend);
        Assert.Equal(relationship, s.Main.SelectedRelationship);
    }

    /// <summary>The relationship menu's colour dots and line samples, with the Color and Line submenus open, for
    /// review.</summary>
    [AvaloniaFact]
    public void The_colour_and_line_submenus_render_with_their_pictures()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            foreach (int submenu in (int[])[0, 1])
            {
                Session s = Build();
                s.Main.Select(Id(s, "A1"));
                s.Press(Key.R, CtrlShift);
                s.Type("C1");
                s.Press(Key.Enter);
                // The keyboard's context-menu request (Shift+F10), as the platform raises it.
                s.Canvas.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
                Session.Pump();
                var menu = s.Canvas.OpenedMenu!;
                var item = menu.Items.OfType<Avalonia.Controls.MenuItem>().Where(i => i.ItemCount > 0).ElementAt(submenu);
                item.Open();
                Session.Pump();
                Assert.All(item.Items.OfType<Avalonia.Controls.MenuItem>(), child => Assert.NotNull(child.Icon));
                Render.Save(s.Window, theme, submenu == 0 ? "relationship-colors" : "relationship-lines");
            }
        }
    }

    /// <summary>Relationships in colours and line types, a selected one, a faded one and link mode, in both
    /// themes.</summary>
    [AvaloniaFact]
    public void Relationships_render_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            Session s = Build();
            s.Main.Select(Id(s, "A1"));
            s.Press(Key.R, CtrlShift);
            s.Type("C1");
            s.Press(Key.Enter, RawInputModifiers.Shift);
            s.Main.Select(Id(s, "B"));
            s.Press(Key.R, CtrlShift);
            s.Type("C");
            s.Press(Key.Enter);
            s.Press(Key.Escape);

            foreach (Relationship relationship in s.Map.Relationships)
            {
                bool first = relationship.Id == s.Map.Relationships[0].Id;
                s.Main.Apply(new SetRelationshipStyle(
                    s.Map.Id,
                    relationship.Id,
                    first ? RelationshipColor.Teal : RelationshipColor.Purple,
                    first ? RelationshipLine.Dashed : RelationshipLine.Dotted));
            }

            s.Main.Select(Id(s, "A1"));
            s.Press(Key.R, CtrlShift);
            s.Type("B");
            Render.Save(s.Window, theme, "relationships");

            // A selected relationship, bent by its handle. Saving the PNG leaves the window without keyboard input,
            // so this part goes through the view model.
            s.Main.LinkCancel();
            s.Main.Select(Id(s, "B"));
            s.Main.Commands[CommandId.SelectNextRelationship].Command.Execute(null);
            Session.Pump();
            Point handle = s.Canvas.TranslatePoint(s.Canvas.BendHandleOnScreen!.Value, s.Window)!.Value;
            s.Drag(handle, handle + new Vector(-40, 70));
            Render.Save(s.Window, theme, "relationship-selected");
        }
    }

    [AvaloniaFact]
    public void Follow_relationship_jumps_there_and_back_then_takes_the_next_one()
    {
        Session s = Build();
        MapId map = s.Map.Id;
        s.Main.Apply(new AddRelationship(map, new RelationshipId("R1"), Id(s, "A1"), Id(s, "B"), RelationshipDirection.OneWay));
        s.Main.Apply(new AddRelationship(map, new RelationshipId("R2"), Id(s, "C1"), Id(s, "A1"), RelationshipDirection.TwoWay));
        s.Main.Select(Id(s, "A1"));

        var visited = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            s.Main.Commands[CommandId.FollowRelationship].Command.Execute(null);
            visited.Add(s.Map.Find(s.Main.SelectedTopic!.Value)!.Text);
        }

        // Its direction does not matter: A1 is the source of one and the target of the other.
        Assert.Equal(["B", "A1", "C1", "A1", "B"], visited);
    }
}
