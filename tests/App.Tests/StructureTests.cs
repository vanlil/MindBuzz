using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.Services;
using MindBuzz.App.ViewModels;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// Reshaping a map with the keyboard and the mouse, end to end, and the clipboard through
/// the view model with a fake clipboard.
/// </summary>
public sealed class StructureTests
{
    private const RawInputModifiers CtrlAlt = RawInputModifiers.Control | RawInputModifiers.Alt;

    /// <summary>Central with main topics A, B, C (A, B right; C left) and A1, A2 under A.</summary>
    private static Session Build()
    {
        var s = new Session();
        foreach (string main in new[] { "A", "B", "C" })
        {
            s.Main.Select(s.Central.Id);
            s.Press(Key.Insert);
            s.Type(main);
            s.Press(Key.Enter);
        }

        // New main topics go to the shorter side; fix the sides this fixture describes.
        s.Main.Apply(new SetMainSide(s.Map.Id, Find(s, "A").Id, MainSide.Right));
        s.Main.Apply(new SetMainSide(s.Map.Id, Find(s, "B").Id, MainSide.Right));
        s.Main.Apply(new SetMainSide(s.Map.Id, Find(s, "C").Id, MainSide.Left));

        s.Main.Select(Find(s, "A").Id);
        foreach (string sub in new[] { "A1", "A2" })
        {
            s.Main.Select(Find(s, "A").Id);
            s.Press(Key.Insert);
            s.Type(sub);
            s.Press(Key.Enter);
        }

        return s;
    }

    private static Topic Find(Session s, string text) => s.Map.Root.DescendantsAndSelf().Single(t => t.Text == text);

    private static string Children(Session s, string text) => string.Join(',', Find(s, text).Children.Select(c => c.Text));

    [AvaloniaFact]
    public void Alt_ctrl_up_and_down_reorder_and_undo_restores_the_order()
    {
        Session s = Build();
        s.Main.Select(Find(s, "B").Id);

        s.Press(Key.Up, CtrlAlt);
        Assert.Equal("B,A,C", Children(s, s.Central.Text));

        // End: the last place on its own side; C stays alone on the left.
        s.Press(Key.End, CtrlAlt);
        Assert.Equal("A,B,C", Children(s, s.Central.Text));

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal("B,A,C", Children(s, s.Central.Text));
        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal("A,B,C", Children(s, s.Central.Text));
    }

    [AvaloniaFact]
    public void On_the_right_alt_ctrl_right_demotes_and_alt_ctrl_left_promotes()
    {
        Session s = Build();
        s.Main.Select(Find(s, "A2").Id);

        s.Press(Key.Right, CtrlAlt);
        Assert.Equal("A2", Children(s, "A1"));

        s.Press(Key.Left, CtrlAlt);
        Assert.Equal("A1,A2", Children(s, "A"));
    }

    [AvaloniaFact]
    public void On_the_left_side_the_keys_mirror_so_left_points_outwards()
    {
        Session s = Build();

        // C is the only main topic on the left. Give it two subtopics, then demote the second with Left.
        foreach (string sub in new[] { "C1", "C2" })
        {
            s.Main.Select(Find(s, "C").Id);
            s.Press(Key.Insert);
            s.Type(sub);
            s.Press(Key.Enter);
        }

        s.Main.Select(Find(s, "C2").Id);
        s.Press(Key.Left, CtrlAlt);
        Assert.Equal("C2", Children(s, "C1"));

        s.Press(Key.Right, CtrlAlt);
        Assert.Equal("C1,C2", Children(s, "C"));
    }

    /// <summary>
    /// A filled map gets a new main topic, and no branch jumps to the other side. The new
    /// one goes to the shorter side.
    /// </summary>
    [AvaloniaFact]
    public void A_new_main_topic_on_a_full_map_leaves_every_branch_on_its_side_and_takes_the_shorter_one()
    {
        var s = new Session();
        Document generated = MindBuzz.SyntheticMap.MapGenerator.Generate(new MindBuzz.SyntheticMap.MapSpec(Topics: 80, MainTopics: 7, Seed: 4));
        s.Main.Apply(new PasteClip(s.Map.Id, s.Central.Id, new Clip([.. generated.Maps[0].Root.Children], [])));
        Session.Pump();
        Dictionary<TopicId, Core.Layout.Side> before = s.Canvas.Layout!.Boxes.Where(b => b.Depth >= 1).ToDictionary(b => b.Id, b => b.Side);
        double Height(Core.Layout.Side side)
        {
            var boxes = s.Canvas.Layout!.Boxes.Where(b => b.Side == side).ToList();
            return boxes.Count == 0 ? 0 : boxes.Max(b => b.Bounds.Bottom) - boxes.Min(b => b.Bounds.Y);
        }

        Core.Layout.Side shorter = Height(Core.Layout.Side.Right) <= Height(Core.Layout.Side.Left) ? Core.Layout.Side.Right : Core.Layout.Side.Left;

        s.Main.Select(s.Central.Id);
        s.Press(Key.Insert);
        s.Type("New branch");
        s.Press(Key.Enter);

        Assert.All(before, pair => Assert.Equal(pair.Value, s.Canvas.Layout!.Find(pair.Key)!.Side));
        Assert.Equal(shorter, s.Canvas.Layout!.Boxes.Single(b => b.Topic.Text == "New branch").Side);
    }

    [AvaloniaFact]
    public void Move_to_other_side_and_balance_map_change_sides_and_undo_in_one_step()
    {
        Session s = Build();
        s.Main.Select(Find(s, "A").Id);

        s.Main.Commands[CommandId.MoveToOtherSide].Command.Execute(null);
        Assert.Equal(MainSide.Left, Find(s, "A").Side);

        s.Main.Commands[CommandId.BalanceMap].Command.Execute(null);
        Assert.Contains(s.Map.Root.Children, child => child.Side == MainSide.Right);
        Assert.Contains(s.Map.Root.Children, child => child.Side == MainSide.Left);

        s.Press(Key.Z, RawInputModifiers.Control);
        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal(MainSide.Right, Find(s, "A").Side);
        Assert.False(s.Main.Commands[CommandId.MoveToOtherSide].Command.CanExecute(null) && s.Main.SelectedTopic == s.Central.Id);
    }

    [AvaloniaFact]
    public void Minus_and_plus_collapse_and_expand_and_show_levels_works_for_the_map()
    {
        Session s = Build();
        s.Main.Select(Find(s, "A").Id);

        s.Press(Key.OemMinus);
        Assert.True(Find(s, "A").IsCollapsed);
        Assert.Null(s.Canvas.Layout!.Find(Find(s, "A1").Id));

        s.Press(Key.Add);
        Assert.False(Find(s, "A").IsCollapsed);

        s.Press(Key.D1, RawInputModifiers.Alt | RawInputModifiers.Shift);
        Assert.True(Find(s, "A").IsCollapsed);

        s.Press(Key.OemPeriod, RawInputModifiers.Alt | RawInputModifiers.Shift);
        Assert.False(Find(s, "A").IsCollapsed);
    }

    [AvaloniaFact]
    public void A_structure_change_animates_and_typing_does_not()
    {
        Session s = Build();
        s.Main.Select(Find(s, "A").Id);

        // Held halfway: on a slow machine the key press alone can outlast the 160 ms.
        s.Canvas.HoldTransitionAt = 0.5;
        s.Press(Key.OemMinus);
        Assert.True(s.Canvas.IsTransitioning);

        var t = new Session();
        t.Type("typed");
        Assert.False(t.Canvas.IsTransitioning);
    }

    [AvaloniaFact]
    public void Clicking_the_collapse_handle_toggles_without_moving_the_selection()
    {
        Session s = Build();
        TopicId a = Find(s, "A").Id;
        s.Main.Select(a);
        Session.Pump();

        var box = s.Canvas.Layout!.Find(a)!;
        var handle = new Avalonia.Point(box.Bounds.Right + 10, box.Bounds.CenterY);
        var onScreen = s.Canvas.TranslatePoint(s.Canvas.Viewport.ToScreen(new Core.Layout.LayoutPoint(handle.X, handle.Y)), s.Window)!.Value;

        s.Click(onScreen);

        Assert.True(Find(s, "A").IsCollapsed);
        Assert.Equal(a, s.Main.SelectedTopic);
    }

    [AvaloniaFact]
    public void Dragging_a_topic_onto_another_makes_it_a_subtopic_in_one_undo_step()
    {
        Session s = Build();

        s.Drag(s.On(Find(s, "B").Id), s.On(Find(s, "A1").Id));

        Assert.Equal("B", Children(s, "A1"));
        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal("A,B,C", Children(s, s.Central.Text));
    }

    [AvaloniaFact]
    public void Dragging_onto_the_lower_edge_of_a_sibling_reorders()
    {
        Session s = Build();

        s.Drag(s.On(Find(s, "A1").Id), s.On(Find(s, "A2").Id, 0.92));

        Assert.Equal("A2,A1", Children(s, "A"));
    }

    [AvaloniaFact]
    public void Esc_cancels_a_drag()
    {
        Session s = Build();

        s.Drag(s.On(Find(s, "B").Id), s.On(Find(s, "A1").Id), cancelWithEscape: true);

        Assert.Equal("A,B,C", Children(s, s.Central.Text));
        Assert.False(s.Canvas.IsDragging);
    }

    [AvaloniaFact]
    public void The_central_topic_cannot_be_dragged()
    {
        Session s = Build();

        s.Drag(s.On(s.Central.Id), s.On(Find(s, "A1").Id));

        Assert.Equal("A,B,C", Children(s, s.Central.Text));
    }

    /// <summary>A drag frame in both themes, for review: the dimmed branch, the ghost and the drop outline.</summary>
    [AvaloniaFact]
    public void A_drag_in_progress_renders_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            Session s = Build();
            s.Window.MouseDown(s.On(Find(s, "B").Id), MouseButton.Left);
            s.Window.MouseMove(s.On(Find(s, "A1").Id), RawInputModifiers.LeftMouseButton);
            Session.Pump();
            Assert.True(s.Canvas.IsDragging);
            Assert.NotNull(s.Canvas.CurrentDropPlan);

            Render.Save(s.Window, theme, "drag");
        }
    }
}

public sealed class ClipboardTests : IDisposable
{
    private readonly FakeUiServices _ui = new();
    private readonly MainViewModel _main;

    public ClipboardTests()
    {
        _main = new MainViewModel(_ui, new FakePlatformShell(), HostPlatform.Windows);
    }

    public void Dispose() => _main.Dispose();

    private TopicId Add(TopicId parent, string text)
    {
        TopicId id = RandomIdSource.Instance.NextTopic();
        _main.Apply(new Core.Editing.AddTopic(_main.ActiveMap.Id, parent, Core.Editing.NewTopicPlacement.Subtopic, id, text));
        return id;
    }

    private static async Task Run(MainViewModel main, CommandId command)
    {
        main.Execute(command);
        await Task.Yield();
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_private_format_and_an_outline_on_the_clipboard()
    {
        TopicId a = Add(_main.ActiveMap.Root.Id, "A");
        Add(a, "A1");
        _main.Select(a);

        await Run(_main, CommandId.Copy);

        Assert.NotNull(ClipFormat.Read(_ui.Clipboard.Clip));
        Assert.Equal("A\n\tA1\n", _ui.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task Paste_adds_a_copy_with_fresh_ids_as_the_last_subtopic_and_keeps_internal_relationships()
    {
        TopicId a = Add(_main.ActiveMap.Root.Id, "A");
        TopicId a1 = Add(a, "A1");
        TopicId a2 = Add(a, "A2");
        TopicId b = Add(_main.ActiveMap.Root.Id, "B");
        _main.Apply(new Core.Editing.AddRelationship(_main.ActiveMap.Id, RandomIdSource.Instance.NextRelationship(), a1, a2, RelationshipDirection.OneWay));
        _main.Select(a);
        await Run(_main, CommandId.Copy);

        _main.Select(b);
        await Run(_main, CommandId.Paste);

        Topic pasted = Assert.Single(_main.ActiveMap.Find(b)!.Children);
        Assert.Equal("A", pasted.Text);
        Assert.NotEqual(a, pasted.Id);
        Assert.Equal(2, _main.ActiveMap.Relationships.Length);
    }

    [AvaloniaFact]
    public async Task Cut_removes_the_branch_and_paste_brings_it_back_elsewhere()
    {
        TopicId a = Add(_main.ActiveMap.Root.Id, "A");
        TopicId b = Add(_main.ActiveMap.Root.Id, "B");
        _main.Select(a);

        await Run(_main, CommandId.Cut);
        Assert.Null(_main.ActiveMap.Find(a));

        _main.Select(b);
        await Run(_main, CommandId.Paste);
        Assert.Equal("A", Assert.Single(_main.ActiveMap.Find(b)!.Children).Text);
    }

    /// <summary>Cut is copy, then delete: without a clipboard to copy to, the branch stays.</summary>
    [AvaloniaFact]
    public async Task Cut_without_a_clipboard_keeps_the_branch_and_says_so()
    {
        TopicId a = Add(_main.ActiveMap.Root.Id, "A");
        _main.Select(a);
        _ui.HasClipboard = false;

        await Run(_main, CommandId.Cut);

        Assert.NotNull(_main.ActiveMap.Find(a));
        Assert.Equal(Localization.Strings.Status_NoClipboard, _main.StatusText);
    }

    /// <summary>A clip from a newer MindBuzz, or a damaged one, still pastes its plain text.</summary>
    [AvaloniaFact]
    public async Task A_clip_this_version_cannot_read_pastes_its_plain_text()
    {
        _ui.Clipboard = new ClipboardContent("""{ "mindBuzzClip": 99, "roots": [] }""", "From a newer version\n\tChild");
        _main.Select(_main.ActiveMap.Root.Id);

        await Run(_main, CommandId.Paste);

        Topic pasted = Assert.Single(_main.ActiveMap.Root.Children);
        Assert.Equal("From a newer version", pasted.Text);
        Assert.Equal("Child", Assert.Single(pasted.Children).Text);
    }

    /// <summary>The real clipboard service hands over the plain text also when a clip is there (the review's
    /// R8).</summary>
    [AvaloniaFact]
    public async Task The_clipboard_service_reads_the_text_beside_a_clip()
    {
        var s = new Session();
        var services = new Services.AvaloniaUiServices(s.Window);
        Assert.True(await services.SetClipboardAsync("""{ "mindBuzzClip": 99 }""", "Outline"));

        ClipboardContent content = await services.GetClipboardAsync();

        Assert.NotNull(content.Clip);
        Assert.Equal("Outline", content.Text);
    }

    [AvaloniaFact]
    public async Task Indented_text_from_another_app_pastes_as_a_subtree()
    {
        _ui.Clipboard = new ClipboardContent(null, "Shopping\n\t- Milk\n\t- Bread\nWork");
        _main.Select(_main.ActiveMap.Root.Id);

        await Run(_main, CommandId.Paste);

        Topic root = _main.ActiveMap.Root;
        Assert.Equal(["Shopping", "Work"], root.Children.Select(child => child.Text));
        Assert.Equal(["Milk", "Bread"], root.Children[0].Children.Select(child => child.Text));
    }

    [AvaloniaFact]
    public async Task A_paste_that_left_something_out_says_so()
    {
        _ui.Clipboard = new ClipboardContent("""{ "mindBuzzClip": 1, "roots": [ { "id": "A", "text": "kept", "links": [ { "url": "javascript:x" } ] } ] }""", "kept");
        _main.Select(_main.ActiveMap.Root.Id);

        await Run(_main, CommandId.Paste);

        Assert.Equal("kept", Assert.Single(_main.ActiveMap.Root.Children).Text);
        Assert.Equal(Localization.Strings.Status_PastePartlyRead, _main.StatusText);
    }

    [AvaloniaFact]
    public void The_central_topic_can_be_copied_but_not_cut()
    {
        _main.Select(_main.ActiveMap.Root.Id);

        Assert.True(_main.CanExecute(CommandId.Copy));
        Assert.False(_main.CanExecute(CommandId.Cut));
    }

    [AvaloniaFact]
    public async Task Save_as_suggests_the_name_without_the_extension()
    {
        _ui.PickedPaths.Enqueue(null);

        await Run(_main, CommandId.SaveAs);

        Assert.Equal("Untitled", Assert.Single(_ui.SuggestedNames));
    }
}
