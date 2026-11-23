using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using MindBuzz.SyntheticMap;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

public sealed class ClipFormatTests
{
    [Fact]
    public void A_clip_round_trips_with_notes_links_markers_and_internal_relationships()
    {
        Document document = MapGenerator.Generate(new MapSpec(Topics: 120, NoteRatio: 0.5, LinkRatio: 0.5, MarkerRatio: 0.5, Relationships: 40, Seed: 8));
        Map map = document.Maps[0];
        Topic branch = map.Root.Children[0];
        Clip clip = Clip.Copy(map, branch.Id)!;

        Clip read = ClipFormat.Read(ClipFormat.Write(clip))!;

        Assert.True(DocumentEquality.AreEqual(clip.Roots[0], read.Roots[0]));
        Assert.Equal(
            clip.Relationships.Select(r => (r.From, r.To, r.Direction)),
            read.Relationships.Select(r => (r.From, r.To, r.Direction)));
    }

    [Theory]
    [InlineData("plain text from another app")]
    [InlineData("""{ "something": "else" }""")]
    [InlineData("""{ "mindBuzzClip": 99, "roots": [] }""")]
    [InlineData("{ not json")]
    [InlineData("")]
    public void Anything_that_is_not_a_clip_reads_as_null(string text)
    {
        Assert.Null(ClipFormat.Read(text));
    }

    [Fact]
    public void A_clip_with_a_disallowed_link_loses_that_link_only()
    {
        const string json = """{ "mindBuzzClip": 1, "roots": [ { "id": "A", "text": "kept", "links": [ { "url": "javascript:x" } ] } ] }""";

        Clip clip = ClipFormat.Read(json)!;

        Assert.Equal("kept", clip.Roots[0].Text);
        Assert.Empty(clip.Roots[0].Links);
    }
}

public sealed class ShowNextLevelTests
{
    [Fact]
    public void A_collapsed_topic_opens_itself_first_then_the_collapsed_edge_below_it()
    {
        Document collapsed = Small()
            .Applied(new AddTopic(M1, Id("A1"), NewTopicPlacement.Subtopic, Id("X"), "X"))
            .Applied(new SetCollapsed(M1, Id("A1"), true))
            .Applied(new SetCollapsed(M1, Id("A"), true));

        Document once = collapsed.Applied(new ShowNextLevel(M1, Id("A")));
        Assert.False(once.Get("A").IsCollapsed);
        Assert.True(once.Get("A1").IsCollapsed);

        Document twice = once.Applied(new ShowNextLevel(M1, Id("A")));
        Assert.False(twice.Get("A1").IsCollapsed);
        Assert.Equal(Refusal.NoChange, twice.RefusalOf(new ShowNextLevel(M1, Id("A"))));
    }
}

/// <summary>Drop targets on the small map, laid out with the fake measurer (A, B on the right; C on the left).</summary>
public sealed class DropTargetTests
{
    private readonly Map _map = Small().Maps[0];
    private readonly MapLayout _layout;

    public DropTargetTests()
    {
        _layout = new LayoutEngine(new FakeMeasurer()).Layout(_map);
    }

    private LayoutPoint On(string id, double fy = 0.5)
    {
        LayoutRect bounds = _layout.Find(Id(id))!.Bounds;
        return new LayoutPoint(bounds.CenterX, bounds.Y + (bounds.Height * fy));
    }

    [Fact]
    public void The_middle_of_a_topic_drops_into_it_as_its_last_subtopic()
    {
        DropPlan plan = DropTargets.Find(_layout, _map, Id("B"), On("C1"))!;

        Assert.Equal((DropKind.Into, Id("C1"), 0), (plan.Kind, plan.NewParent, plan.Index));
    }

    [Fact]
    public void The_upper_and_lower_edges_drop_before_and_after()
    {
        DropPlan before = DropTargets.Find(_layout, _map, Id("C1"), On("A2", 0.1))!;
        DropPlan after = DropTargets.Find(_layout, _map, Id("C1"), On("A2", 0.9))!;

        Assert.Equal((DropKind.Before, Id("A"), 1), (before.Kind, before.NewParent, before.Index));
        Assert.Equal((DropKind.After, Id("A"), 2), (after.Kind, after.NewParent, after.Index));
    }

    [Fact]
    public void Reordering_within_the_same_parent_accounts_for_the_topic_leaving_its_place()
    {
        DropPlan plan = DropTargets.Find(_layout, _map, Id("A1"), On("A2", 0.9))!;

        Assert.Equal((Id("A"), 1), (plan.NewParent, plan.Index));
        Assert.Equal("A2,A1", Small().Applied(new MoveTopic(M1, Id("A1"), plan.NewParent, plan.Index)).ChildrenOf("A"));
    }

    [Fact]
    public void Nothing_drops_into_its_own_subtree_onto_itself_or_where_it_already_is()
    {
        Assert.Null(DropTargets.Find(_layout, _map, Id("A"), On("A1")));
        Assert.Null(DropTargets.Find(_layout, _map, Id("A"), On("A")));
        Assert.Null(DropTargets.Find(_layout, _map, Id("A1"), On("A2", 0.1)));
    }

    [Fact]
    public void The_central_topic_only_takes_topics_into_it()
    {
        DropPlan plan = DropTargets.Find(_layout, _map, Id("A1"), On("CENTER", 0.05))!;

        Assert.Equal(DropKind.Into, plan.Kind);
        Assert.Equal(Id("CENTER"), plan.NewParent);
    }

    [Fact]
    public void Empty_canvas_is_no_drop()
    {
        Assert.Null(DropTargets.Find(_layout, _map, Id("A1"), new LayoutPoint(5000, 5000)));
    }
}
