using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

/// <summary>
/// The keyboard moves on the small map. Main topics split by count: A and B on the right, C on the left.
/// <code>
///            Center ── A ── A1
///   C1 ── C ─┘     │     └─ A2
///                  └─ B
/// </code>
/// </summary>
public sealed class NavigatorTests
{
    private readonly Document _doc = Small();

    private TopicId? Move(string from, NavigationMove move, Document? document = null)
    {
        Map map = (document ?? _doc).Maps[0];
        return Navigator.Move(new LayoutEngine(new FakeMeasurer()).Layout(map), map, Id(from), move);
    }

    [Fact]
    public void From_the_central_topic_right_and_left_go_to_the_main_topic_nearest_the_centre_on_that_side()
    {
        // Right side has A above and B below the centre; with two topics of equal size, A is first in order.
        Assert.Contains(Move("CENTER", NavigationMove.Right), new TopicId?[] { Id("A"), Id("B") });
        Assert.Equal(Id("C"), Move("CENTER", NavigationMove.Left));
    }

    [Fact]
    public void On_the_right_side_right_goes_outwards_and_left_goes_back()
    {
        Assert.Contains(Move("A", NavigationMove.Right), new TopicId?[] { Id("A1"), Id("A2") });
        Assert.Equal(Id("A"), Move("A1", NavigationMove.Left));
        Assert.Equal(Id("CENTER"), Move("A", NavigationMove.Left));
        Assert.Null(Move("B", NavigationMove.Right));
    }

    [Fact]
    public void On_the_left_side_left_goes_outwards_and_right_goes_back()
    {
        Assert.Equal(Id("C1"), Move("C", NavigationMove.Left));
        Assert.Equal(Id("C"), Move("C1", NavigationMove.Right));
        Assert.Equal(Id("CENTER"), Move("C", NavigationMove.Right));
    }

    [Fact]
    public void Up_and_down_are_spatial_across_branches_and_stay_on_their_side()
    {
        Assert.Equal(Id("A2"), Move("A1", NavigationMove.Down));
        Assert.Equal(Id("A1"), Move("A2", NavigationMove.Up));

        // Below A2 the nearest topic on the right side is in another branch.
        Assert.Equal(Id("B"), Move("A2", NavigationMove.Down));

        // C is alone on the left: nothing above or below, and never a jump to the right side.
        Assert.Null(Move("C", NavigationMove.Down));
        Assert.Null(Move("CENTER", NavigationMove.Up));
    }

    [Fact]
    public void Home_and_end_go_to_the_first_and_last_sibling()
    {
        Assert.Equal(Id("A1"), Move("A2", NavigationMove.FirstSibling));
        Assert.Equal(Id("A2"), Move("A1", NavigationMove.LastSibling));
        Assert.Null(Move("A1", NavigationMove.FirstSibling));
    }

    [Fact]
    public void The_central_topic_and_the_parent_are_one_key_away()
    {
        Assert.Equal(Id("CENTER"), Move("A2", NavigationMove.Central));
        Assert.Equal(Id("A"), Move("A2", NavigationMove.Parent));
        Assert.Null(Move("CENTER", NavigationMove.Parent));
    }

    [Fact]
    public void Tab_order_is_the_outline_order_of_visible_topics_and_skips_collapsed_branches()
    {
        Assert.Equal(Id("A1"), Move("A", NavigationMove.Next));
        Assert.Equal(Id("B"), Move("A2", NavigationMove.Next));
        Assert.Equal(Id("A2"), Move("B", NavigationMove.Previous));
        Assert.Null(Move("C1", NavigationMove.Next));

        Document collapsed = _doc.Applied(new SetCollapsed(M1, Id("A"), true));
        Assert.Equal(Id("B"), Move("A", NavigationMove.Next, collapsed));
    }
}
