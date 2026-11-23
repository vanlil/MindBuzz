using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using MindBuzz.App.Canvas;
using MindBuzz.App.Commands;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Topic icons on the left border: one per topic, set from the context menu.</summary>
public sealed class TopicIconTests
{
    private static TopicId Add(Session s, TopicId parent, string text)
    {
        TopicId id = RandomIdSource.Instance.NextTopic();
        s.Main.Apply(new AddTopic(s.Map.Id, parent, NewTopicPlacement.Subtopic, id, text));
        return id;
    }

    [AvaloniaFact]
    public void An_icon_is_set_replaced_and_removed_from_the_menu_commands()
    {
        var s = new Session();
        s.Main.Select(s.Central.Id);
        Assert.False(s.Main.CanExecute(CommandId.RemoveTopicIcon));

        s.Main.Execute(CommandId.TopicIconQuestion);
        Assert.Equal("question", s.Central.MarkerOf(MarkerGroups.Icon)?.Value);

        s.Main.Execute(CommandId.TopicIconMoney);
        Assert.Equal("money", s.Central.MarkerOf(MarkerGroups.Icon)?.Value);
        Assert.Single(s.Central.Markers);

        s.Main.Execute(CommandId.RemoveTopicIcon);
        Assert.Null(s.Central.MarkerOf(MarkerGroups.Icon));
    }

    [AvaloniaFact]
    public void The_icon_sits_on_the_left_border_three_quarters_of_a_one_line_topic_high_and_moves_the_content_right()
    {
        var s = new Session();
        TopicId main = Add(s, s.Central.Id, "Budget");
        Session.Pump();
        TopicBox before = s.Canvas.Layout!.Find(main)!;

        s.Main.Apply(new SetMarker(s.Map.Id, main, MarkerGroups.Icon, "money"));
        Session.Pump();
        TopicBox box = s.Canvas.Layout!.Find(main)!;
        (Marker marker, LayoutRect bounds) = TopicParts.Icon(box)!.Value;

        Assert.Equal("money", marker.Value);
        Assert.Equal(TopicParts.IconOnRight(box) ? box.Bounds.Right : box.Bounds.X, bounds.CenterX, 3);
        Assert.Equal(box.Bounds.CenterY, bounds.CenterY, 3);
        Assert.Equal(Math.Round(0.75 * before.Bounds.Height), bounds.Width, 0);
        Assert.True(box.Bounds.Width > before.Bounds.Width, "the topic grows by the half of the icon inside it");
    }

    [AvaloniaFact]
    public void The_central_topic_shows_its_icon_too()
    {
        var s = new Session();
        s.Main.Apply(new SetMarker(s.Map.Id, s.Central.Id, MarkerGroups.Icon, "question"));
        Session.Pump();

        Assert.NotNull(TopicParts.Icon(s.Canvas.Layout!.Find(s.Central.Id)!));
    }

    [AvaloniaFact]
    public void Icons_render_on_every_level_and_in_the_menu_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            s.Main.Apply(new SetMarker(s.Map.Id, s.Central.Id, MarkerGroups.Icon, "question"));
            int i = 0;
            foreach (string main in new[] { "Budget", "Trip", "Ideas" })
            {
                TopicId id = Add(s, s.Central.Id, main);
                s.Main.Apply(new SetMarker(s.Map.Id, id, MarkerGroups.Icon, MarkerGroups.IconValues[i++ % 10]));
                foreach (string sub in new[] { "First thing", "Second thing with a longer text that wraps onto a second line" })
                {
                    TopicId child = Add(s, id, sub);
                    s.Main.Apply(new SetMarker(s.Map.Id, child, MarkerGroups.Icon, MarkerGroups.IconValues[i++ % 10]));
                    s.Main.Apply(new SetMarker(s.Map.Id, child, MarkerGroups.Priority, "1"));
                }
            }

            s.Main.Select(null);
            s.Main.Execute(CommandId.ZoomFit);
            Session.Pump();
            Render.Save(s.Window, theme, "topic-icons");

            MenuEntry iconMenu = s.Main.TopicMenu.Single(entry => entry.Children?.Any(child => child.Swatch?.TopicIcon is not null) == true);
            var presenter = new MenuFlyoutPresenter
            {
                ItemsSource = iconMenu.Children!.Select(entry => entry.IsSeparator
                    ? (object)new Separator()
                    : new MenuItem { Header = entry.Header, Icon = entry.Swatch is { } swatch ? MenuSwatches.Create(swatch) : null }).ToList(),
            };
            Render.Save(new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = presenter }, theme, "menu-topic-icons");
        }
    }
}
