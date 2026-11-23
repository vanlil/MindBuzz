using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Markers and internet links end to end.</summary>
public sealed class MarkerLinkTests
{
    private const RawInputModifiers CtrlShift = RawInputModifiers.Control | RawInputModifiers.Shift;
    private const RawInputModifiers CtrlAlt = RawInputModifiers.Control | RawInputModifiers.Alt;

    private static string? Priority(Session s) => s.Selected.MarkerOf(MarkerGroups.Priority)?.Value;

    private static string? Progress(Session s) => s.Selected.MarkerOf(MarkerGroups.Progress)?.Value;

    private static void Run(Session s, CommandId id) => s.Main.Commands[id].Command.Execute(null);

    [AvaloniaFact]
    public void Ctrl_shift_digit_sets_a_priority_and_the_same_again_removes_it()
    {
        var s = new Session();

        s.Press(Key.D3, CtrlShift);
        Assert.Equal("3", Priority(s));
        s.Press(Key.D1, CtrlShift);
        Assert.Equal("1", Priority(s));
        s.Press(Key.D1, CtrlShift);
        Assert.Null(Priority(s));

        s.Press(Key.D5, CtrlShift);
        s.Press(Key.D0, CtrlShift);
        Assert.Null(Priority(s));
    }

    [AvaloniaFact]
    public void On_macOS_priority_is_control_command_digit_because_shift_command_3_to_5_take_screenshots()
    {
        var main = new MainViewModel(new FakeUiServices(), new FakePlatformShell(), HostPlatform.MacOS);

        for (int n = 1; n <= 5; n++)
        {
            Chord chord = Assert.Single(main.Commands[CommandId.Priority1 + (n - 1)].Gestures.For(HostPlatform.MacOS));
            Assert.Equal((Key.D0 + n, ChordModifiers.Command | ChordModifiers.Control), (chord.Key, chord.Modifiers));
        }
    }

    [AvaloniaFact]
    public void Alt_ctrl_p_steps_through_progress_and_with_shift_steps_back()
    {
        var s = new Session();

        string?[] seen = [.. Enumerable.Range(0, 6).Select(_ =>
        {
            s.Press(Key.P, CtrlAlt);
            return Progress(s);
        })];
        Assert.Equal(new string?[] { "0", "25", "50", "75", "100", null }, seen.AsEnumerable());

        s.Press(Key.P, CtrlAlt | RawInputModifiers.Shift);
        Assert.Equal("100", Progress(s));
    }

    [AvaloniaFact]
    public void The_progress_menu_sets_a_value_toggles_it_and_both_markers_live_side_by_side()
    {
        var s = new Session();

        Run(s, CommandId.Progress50);
        Run(s, CommandId.Priority2);
        Assert.Equal(("2", "50"), (Priority(s), Progress(s)));
        Assert.True(s.Main.Commands[CommandId.RemoveProgress].Command.CanExecute(null));

        Run(s, CommandId.Progress50);
        Assert.Null(Progress(s));
        Assert.False(s.Main.Commands[CommandId.RemoveProgress].Command.CanExecute(null));

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal("50", Progress(s));
    }

    [AvaloniaFact]
    public void Priority_and_progress_hover_on_the_border_and_take_no_room_in_the_topic()
    {
        var s = new Session();
        double before = s.Canvas.Layout!.Find(s.Central.Id)!.Bounds.Width;

        Run(s, CommandId.Priority1);
        Run(s, CommandId.ProgressDone);

        // They hover on the border: the priority on the top-right corner, progress on the lower
        // border, and the topic keeps its size.
        TopicBox box = s.Canvas.Layout!.Find(s.Central.Id)!;
        Assert.Equal(before, box.Bounds.Width, 3);
        LayoutRect priority = TopicParts.Priority(box)!.Value.Bounds;
        Assert.Equal(box.Bounds.Right, priority.CenterX, 3);
        Assert.Equal(box.Bounds.Y, priority.CenterY, 3);
        LayoutRect progress = TopicParts.Progress(box)!.Value.Bounds;
        Assert.Equal(box.Bounds.Bottom, progress.CenterY, 3);
        Assert.Equal(box.Bounds.CenterX, progress.CenterX, 3);
    }

    [AvaloniaFact]
    public void Ctrl_k_prefills_an_allowed_address_from_the_clipboard_and_adds_the_link_as_one_undo_step()
    {
        var ui = new FakeUiServices { Clipboard = new(null, "  https://example.com/plan  ") };
        ui.EditedLinks.Enqueue(new InternetLink("https://example.com/plan", "Plan"));
        var s = new Session(ui: ui);

        s.Press(Key.K, RawInputModifiers.Control);
        Session.Pump();

        Assert.Equal("https://example.com/plan", Assert.Single(ui.LinkRequests).Url);
        Assert.Equal(new InternetLink("https://example.com/plan", "Plan"), Assert.Single(s.Selected.Links));

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Empty(s.Selected.Links);
    }

    [AvaloniaTheory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("example.com")]
    [InlineData("/relative/path")]
    public void A_clipboard_that_holds_no_allowed_address_prefills_nothing(string text)
    {
        var ui = new FakeUiServices { Clipboard = new(null, text) };
        var s = new Session(ui: ui);

        s.Press(Key.K, RawInputModifiers.Control);
        Session.Pump();

        Assert.Equal(string.Empty, Assert.Single(ui.LinkRequests).Url);
        Assert.Empty(s.Selected.Links);
    }

    [AvaloniaTheory]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("example.com", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData(" https://example.com ", true)]
    [InlineData("mailto:team@example.com", true)]
    public void The_link_dialog_confirms_only_allowed_addresses(string url, bool allowed)
    {
        var window = new LinkEditWindow(new Services.LinkEditRequest("Add", url, "", "Add"));

        Assert.Equal(allowed, window.Result() is not null);
        Assert.Equal(allowed, window.FindControl<Button>("OkButton")!.IsEnabled);
    }

    private static Session WithLinks(FakeUiServices ui, params string[] urls)
    {
        var s = new Session(ui: ui);
        foreach (string url in urls)
        {
            s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink(url, url.Length > 20 ? string.Empty : $"Title {url[^1]}")));
        }

        return s;
    }

    [AvaloniaFact]
    public void Ctrl_shift_k_opens_the_detail_view_on_the_links_which_move_edit_and_remove_as_undo_steps()
    {
        var ui = new FakeUiServices();
        Session s = WithLinks(ui, "https://a.example/1", "https://b.example/2", "https://c.example/3");

        s.Press(Key.K, CtrlShift);
        Assert.True(s.Main.Panel.IsOpen);
        Assert.True(s.Window.FindControl<TopicPanel>("TopicPanelView")!.FindControl<ListBox>("LinkList")!.IsKeyboardFocusWithin);
        LinkListViewModel? list = s.Main.Panel.Links;
        Assert.NotNull(list);
        Assert.Equal(3, list.Links.Count);
        Assert.Equal(0, list.SelectedIndex);

        list.SelectedIndex = 1;
        list.OpenCommand.Execute(null);
        Assert.Equal(["https://b.example/2"], s.Shell.OpenedUrls);

        list.MoveDownCommand.Execute(null);
        Assert.Equal(["https://a.example/1", "https://c.example/3", "https://b.example/2"], s.Central.Links.Select(l => l.Url));
        Assert.Equal(2, list.SelectedIndex);
        Assert.False(list.MoveDownCommand.CanExecute(null));

        ui.EditedLinks.Enqueue(new InternetLink("https://b.example/two", "Two"));
        list.EditCommand.Execute(null);
        Session.Pump();
        Assert.Equal("https://b.example/two", s.Central.Links[2].Url);
        Assert.Equal("https://b.example/2", ui.LinkRequests[^1].Url);

        list.RemoveCommand.Execute(null);
        Assert.Equal(2, list.Links.Count);
        Assert.Equal(1, list.SelectedIndex);

        s.Press(Key.Z, RawInputModifiers.Control);
        Assert.Equal(3, s.Central.Links.Length);
    }

    [AvaloniaFact]
    public void Show_links_is_only_offered_when_the_topic_has_links()
    {
        var ui = new FakeUiServices();
        var s = new Session(ui: ui);
        Assert.False(s.Main.Commands[CommandId.ShowLinks].Command.CanExecute(null));

        s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink("https://example.com")));

        Assert.True(s.Main.Commands[CommandId.ShowLinks].Command.CanExecute(null));
    }

    private static Point IndicatorOnScreen(Session s, TopicId topic, IndicatorKind kind)
    {
        TopicBox box = s.Canvas.Layout!.Find(topic)!;
        LayoutRect bounds = TopicParts.Indicators(box).Single(part => part.Kind == kind).Bounds;
        return s.Canvas.TranslatePoint(s.Canvas.Viewport.ToScreen(new LayoutPoint(bounds.CenterX, bounds.CenterY)), s.Window)!.Value;
    }

    [AvaloniaFact]
    public void Clicking_the_link_indicator_opens_the_detail_view_and_ctrl_click_opens_a_single_link_directly()
    {
        var ui = new FakeUiServices();
        Session s = WithLinks(ui, "https://example.com/only");
        Point indicator = IndicatorOnScreen(s, s.Central.Id, IndicatorKind.Link);

        s.Window.MouseDown(indicator, MouseButton.Left, RawInputModifiers.Control);
        s.Window.MouseUp(indicator, MouseButton.Left, RawInputModifiers.Control);
        Session.Pump();
        Assert.Equal(["https://example.com/only"], s.Shell.OpenedUrls);
        Assert.False(s.Main.Panel.IsOpen);

        s.Click(indicator);
        Assert.True(s.Main.Panel.IsOpen);
        Assert.Single(s.Shell.OpenedUrls);
    }

    [AvaloniaFact]
    public void The_note_indicator_sits_left_of_the_link_indicator()
    {
        var s = new Session();
        s.Main.Apply(new SetNote(s.Map.Id, s.Central.Id, "A note"));
        s.Main.Apply(new AddLink(s.Map.Id, s.Central.Id, new InternetLink("https://example.com")));

        IReadOnlyList<(IndicatorKind Kind, LayoutRect Bounds)> parts = TopicParts.Indicators(s.Canvas.Layout!.Find(s.Central.Id)!);

        Assert.Equal([IndicatorKind.Note, IndicatorKind.Link], parts.Select(p => p.Kind));
        Assert.True(parts[0].Bounds.Right < parts[1].Bounds.X);
    }

    /// <summary>
    /// A project map with every marker, links and a note, in both themes, plus a greyscale copy of the light frame:
    /// the check that markers read without colour.
    /// </summary>
    [AvaloniaFact]
    public void Markers_and_indicators_render_in_both_themes_and_in_greyscale()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "Launch plan"));
            string[] texts = ["Design", "Build", "Test", "Docs", "Release"];
            string[] progress = ["100", "75", "50", "25", "0"];
            for (int i = 0; i < texts.Length; i++)
            {
                s.Main.Select(s.Central.Id);
                s.Press(Key.Insert);
                s.Type(texts[i]);
                s.Press(Key.Enter);
                TopicId topic = s.Map.Root.Children[i].Id;
                s.Main.Apply(new SetMarker(s.Map.Id, topic, MarkerGroups.Priority, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                s.Main.Apply(new SetMarker(s.Map.Id, topic, MarkerGroups.Progress, progress[i]));
            }

            TopicId build = s.Map.Root.Children[1].Id;
            s.Main.Apply(new AddLink(s.Map.Id, build, new InternetLink("https://example.com")));
            s.Main.Apply(new SetNote(s.Map.Id, build, "Note"));
            s.Main.Apply(new AddLink(s.Map.Id, s.Map.Root.Children[3].Id, new InternetLink("https://example.com/docs")));
            s.Main.Select(s.Map.Root.Children[2].Id);
            s.Canvas.ZoomTo(1.6);
            Session.Pump();

            string path = Render.Save(s.Window, theme, "markers");
            if (theme == ThemeVariant.Light)
            {
                SaveGreyscale(path, path.Replace("markers-light", "markers-greyscale", StringComparison.Ordinal));
            }
        }
    }

    private static void SaveGreyscale(string from, string to)
    {
        using var source = new Bitmap(from);
        using var copy = new WriteableBitmap(source.PixelSize, source.Dpi, Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (Avalonia.Platform.ILockedFramebuffer buffer = copy.Lock())
        {
            int length = buffer.RowBytes * buffer.Size.Height;
            source.CopyPixels(new PixelRect(source.PixelSize), buffer.Address, length, buffer.RowBytes);
            byte[] pixels = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, length);
            for (int i = 0; i < length; i += 4)
            {
                byte grey = (byte)((0.114 * pixels[i]) + (0.587 * pixels[i + 1]) + (0.299 * pixels[i + 2]));
                pixels[i] = pixels[i + 1] = pixels[i + 2] = grey;
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, length);
        }

        copy.Save(to, new PngBitmapEncoderOptions());
    }

    /// <summary>The link dialog, for review. The links list itself is in the detail view's renders.</summary>
    [AvaloniaFact]
    public void The_link_dialog_renders_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            Render.Save(new LinkEditWindow(new Services.LinkEditRequest("Add Internet Link", "https://example.com/roadmap", "Roadmap", "Add")), theme, "link-edit");
        }
    }
}
