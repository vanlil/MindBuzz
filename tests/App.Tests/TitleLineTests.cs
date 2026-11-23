using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using MindBuzz.App.Canvas;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Topic titles have at most four lines on the map, wrapped or typed (TopicText.MaxLines).</summary>
public sealed class TitleLineTests
{
    private static int Lines(string text) => TitleLineGuard.Lines(text, TopicLevel.Central);

    [AvaloniaFact]
    public void Typing_stops_at_four_lines_however_they_come()
    {
        var s = new Session();
        s.Press(Key.F2);
        for (int i = 0; i < 8; i++)
        {
            s.Press(Key.Enter, RawInputModifiers.Shift);
            s.Type("line");
        }

        Assert.Equal(TopicText.MaxLines, Lines(s.Canvas.Editor.Text!));
        s.Type(" and a lot more words that would wrap onto a fifth line if they were allowed in");
        Assert.Equal(TopicText.MaxLines, Lines(s.Canvas.Editor.Text!));
        s.Press(Key.Enter);

        Assert.Equal(TopicText.MaxLines, Lines(s.Central.Text));
    }

    [AvaloniaFact]
    public void A_longer_title_from_elsewhere_can_still_be_shortened()
    {
        var s = new Session();
        s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "one\ntwo\nthree\nfour\nfive\nsix"));
        Assert.Equal(6, Lines(s.Central.Text));

        s.Press(Key.F2);
        s.Press(Key.Back);
        s.Press(Key.Back);
        s.Press(Key.Enter);

        Assert.Equal("one\ntwo\nthree\nfour\nfive\ns", s.Central.Text);
    }

    [AvaloniaFact]
    public void The_map_shows_a_longer_title_cut_at_four_lines()
    {
        var s = new Session();
        s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "one\ntwo\nthree\nfour\nfive\nsix"));
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(s.Window)?.Dispose();
        Session.Pump();
        double tall = s.Canvas.Layout!.Find(s.Central.Id)!.Bounds.Height;

        s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "one\ntwo\nthree\nfour"));
        Session.Pump();
        Assert.Equal(s.Canvas.Layout!.Find(s.Central.Id)!.Bounds.Height, tall);

        s.Main.Apply(new SetTopicText(s.Map.Id, s.Central.Id, "A long central topic that wraps on and on across many lines, far more than four lines would ever hold on the map, so the map cuts it after the fourth line with an ellipsis"));
        Session.Pump();
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            Render.Save(s.Window, theme, "title-four-lines");
            break;
        }
    }
}
