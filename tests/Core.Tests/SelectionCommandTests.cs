using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

/// <summary>Delete and move for a multi-selection. The map: Center → A (A1, A2), B, C (C1).</summary>
public sealed class SelectionCommandTests
{
    private readonly Document _doc = Small();

    [Fact]
    public void Several_topics_are_deleted_in_one_step_and_the_selection_goes_to_a_neighbour()
    {
        CommandResult result = new DeleteTopics(M1, [Id("A1"), Id("C")], Primary: Id("C")).Apply(_doc);

        Assert.True(result.IsApplied);
        Assert.Equal("A2", result.After!.ChildrenOf("A"));
        Assert.Equal("A,B", result.After!.ChildrenOf("CENTER"));
        Assert.Equal(Id("B"), result.Target!.Value.Topic);
    }

    [Fact]
    public void A_topic_inside_another_deleted_one_goes_with_it_and_the_central_topic_stays()
    {
        Document after = _doc.Applied(new DeleteTopics(M1, [Id("CENTER"), Id("A"), Id("A1")], Primary: Id("A1")));

        Assert.Equal("B,C", after.ChildrenOf("CENTER"));
        Assert.Equal(Refusal.CentralTopic, _doc.RefusalOf(new DeleteTopics(M1, [Id("CENTER")], Primary: Id("CENTER"))));
    }

    [Fact]
    public void Several_topics_move_side_by_side_in_map_order()
    {
        // C1 was dragged; A1 comes along. Into B, which has no children.
        Document after = _doc.Applied(new MoveTopics(M1, [Id("C1"), Id("A1")], Primary: Id("C1"), NewParent: Id("B"), Index: 0));

        Assert.Equal("A1,C1", after.ChildrenOf("B"));
        Assert.Equal("A2", after.ChildrenOf("A"));
        Assert.Equal(string.Empty, after.ChildrenOf("C"));
    }

    [Fact]
    public void Moved_main_topics_land_where_the_dragged_one_would_and_share_its_side()
    {
        // B dragged to the front of the main topics, C with it.
        Document after = _doc.Applied(new MoveTopics(M1, [Id("B"), Id("C")], Primary: Id("B"), NewParent: Id("CENTER"), Index: 0));

        Assert.Equal("B,C,A", after.ChildrenOf("CENTER"));
        Assert.Equal(after.Get("B").Side, after.Get("C").Side);
    }

    [Fact]
    public void A_group_cannot_move_into_one_of_its_own_subtrees()
    {
        Assert.Equal(Refusal.IntoOwnSubtree, _doc.RefusalOf(new MoveTopics(M1, [Id("A"), Id("B")], Primary: Id("B"), NewParent: Id("A1"), Index: 0)));
    }
}
