using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MindBuzz.App.Canvas;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The tooltip over a topic sums up its markers, links and note.</summary>
public sealed class TopicTipTests
{
    [Fact]
    public void The_summary_lists_progress_priority_links_note_and_icon_one_per_line()
    {
        var topic = new Topic(new TopicId("T"), "Plan")
        {
            Markers = [new Marker(MarkerGroups.Priority, "5"), new Marker(MarkerGroups.Progress, "50"), new Marker(MarkerGroups.Icon, "money")],
            Links = [new InternetLink("https://a.example", string.Empty), new InternetLink("https://b.example", string.Empty)],
            Note = "\n# Budget\nDetails follow.",
        };

        Assert.Equal("Progress: 50 %\nPriority: 5 (lowest)\n2 links\nNote: Budget\nIcon: Money", TopicSummary.For(topic));
        Assert.Equal("Progress: done\nPriority: 1 (highest)", TopicSummary.For(topic with
        {
            Markers = [new Marker(MarkerGroups.Priority, "1"), new Marker(MarkerGroups.Progress, "100")],
            Links = [],
            Note = string.Empty,
        }));
        Assert.Null(TopicSummary.For(new Topic(new TopicId("U"), "Plain")));
    }

    [AvaloniaFact]
    public void Hovering_a_topic_with_markers_opens_its_summary_and_a_click_closes_it()
    {
        var s = new Session();
        s.Main.Apply(new SetMarker(s.Map.Id, s.Central.Id, MarkerGroups.Priority, "3"));
        Session.Pump();
        var box = s.Canvas.Layout!.Find(s.Central.Id)!;
        Point centre = s.Canvas.TranslatePoint(s.Canvas.Viewport.ToScreen(new Core.Layout.LayoutPoint(box.Bounds.CenterX, box.Bounds.CenterY)), s.Window)!.Value;

        s.Window.MouseMove(centre);
        Session.Pump();
        s.Canvas.ShowTip();
        Assert.Equal("Priority: 3", s.Canvas.OpenTip);

        s.Window.MouseDown(centre, MouseButton.Left);
        s.Window.MouseUp(centre, MouseButton.Left);
        Session.Pump();
        Assert.Null(s.Canvas.OpenTip);
    }

    [AvaloniaFact]
    public void A_topic_without_anything_to_sum_up_shows_no_tip()
    {
        var s = new Session();
        Session.Pump();
        var box = s.Canvas.Layout!.Find(s.Central.Id)!;
        Point centre = s.Canvas.TranslatePoint(s.Canvas.Viewport.ToScreen(new Core.Layout.LayoutPoint(box.Bounds.CenterX, box.Bounds.CenterY)), s.Window)!.Value;

        s.Window.MouseMove(centre);
        Session.Pump();
        s.Canvas.ShowTip();

        Assert.Null(s.Canvas.OpenTip);
    }
}
