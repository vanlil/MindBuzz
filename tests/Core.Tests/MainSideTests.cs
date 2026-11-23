using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

/// <summary>Main topics stay on their side.</summary>
public sealed class MainSideTests
{
    private static readonly MapId M = M1;

    /// <summary>Central with R1, R2 on the right and L1, L2 on the left, sides written down.</summary>
    private static Document Four()
    {
        Topic Main(string name, MainSide side) => new(Id(name), name) { Side = side };
        var root = new Topic(Id("CENTER"), "Center")
        {
            Children = [Main("R1", MainSide.Right), Main("R2", MainSide.Right), Main("L1", MainSide.Left), Main("L2", MainSide.Left)],
        };
        return new Document([new Map(M, root)]);
    }

    private static Dictionary<string, MainSide> Sides(Document document) =>
        document.Map().Root.Children.ToDictionary(child => child.Text, child => MainSides.Of(document.Map(), child));

    private static MapLayout Layout(Document document) => new LayoutEngine(new FakeMeasurer()).Layout(document.Map());

    [Fact]
    public void Adding_or_deleting_a_main_topic_never_moves_another_to_the_other_side()
    {
        Document before = Four();

        Document added = before.Applied(new AddTopic(M, Id("CENTER"), NewTopicPlacement.Subtopic, Id("NEW"), "New"));
        Document deleted = before.Applied(new DeleteTopic(M, Id("R1")));

        Assert.All(Sides(before), pair => Assert.Equal(pair.Value, Sides(added)[pair.Key]));
        Assert.All(Sides(deleted), pair => Assert.Equal(Sides(before)[pair.Key], pair.Value));
    }

    [Fact]
    public void A_new_main_topic_takes_the_side_it_is_given_or_the_one_with_fewer_topics()
    {
        Document threeRight = Four().Applied(new SetMainSide(M, Id("L1"), MainSide.Right));

        Document plain = threeRight.Applied(new AddTopic(M, Id("CENTER"), NewTopicPlacement.Subtopic, Id("NEW"), "New"));
        Document told = threeRight.Applied(new AddTopic(M, Id("CENTER"), NewTopicPlacement.Subtopic, Id("NEW"), "New", Side: MainSide.Right));

        Assert.Equal(MainSide.Left, Sides(plain)["New"]);
        Assert.Equal(MainSide.Right, Sides(told)["New"]);
    }

    [Fact]
    public void A_sibling_of_a_left_main_topic_goes_left_and_below_it_on_screen()
    {
        Document added = Four().Applied(new AddTopic(M, Id("L2"), NewTopicPlacement.SiblingAfter, Id("NEW"), "New"));

        Assert.Equal(MainSide.Left, Sides(added)["New"]);
        List<string> leftTopToBottom = [.. MainSides.TopToBottom(added.Map(), MainSide.Left).Select(t => t.Text)];
        Assert.Equal(leftTopToBottom.IndexOf("L2") + 1, leftTopToBottom.IndexOf("New"));
    }

    [Fact]
    public void A_promoted_subtopic_becomes_a_main_topic_on_its_old_parent_side()
    {
        Document document = Four().Applied(new AddTopic(M, Id("L1"), NewTopicPlacement.Subtopic, Id("SUB"), "Sub"));

        Document promoted = document.Applied(new PromoteTopic(M, Id("SUB")));

        Assert.Equal(MainSide.Left, Sides(promoted)["Sub"]);
    }

    [Fact]
    public void Balance_map_splits_by_height_keeping_the_clockwise_order()
    {
        Document document = Four().Applied(new SetMainSide(M, Id("L1"), MainSide.Right)).Applied(new SetMainSide(M, Id("L2"), MainSide.Right));
        var heights = new Dictionary<TopicId, double> { [Id("R1")] = 100, [Id("R2")] = 100, [Id("L1")] = 100, [Id("L2")] = 100 };

        Document balanced = document.Applied(new BalanceMap(M, heights));

        Assert.Equal(["R1", "R2"], MainSides.TopToBottom(balanced.Map(), MainSide.Right).Select(t => t.Text));
        Assert.Equal(["L2", "L1"], MainSides.TopToBottom(balanced.Map(), MainSide.Left).Select(t => t.Text));
        Assert.Equal(Refusal.NoChange, balanced.RefusalOf(new BalanceMap(M, heights)));
    }

    [Fact]
    public void A_file_without_sides_loads_looking_as_before_and_is_saved_with_them()
    {
        Document old = Small();
        MapLayout before = Layout(old);

        Document loaded = MbzReader.Read(new MemoryStream(MbzWriter.ToBytes(old, "test"))).Document!;

        Assert.All(loaded.Map().Root.Children, child => Assert.NotNull(child.Side));
        Assert.Equal(
            before.Boxes.Select(box => (box.Id, box.Side)),
            Layout(loaded).Boxes.Select(box => (box.Id, box.Side)));
    }

    [Fact]
    public void Dropping_on_the_left_half_of_the_central_topic_moves_a_main_topic_to_the_left()
    {
        Document document = Four();
        MapLayout layout = Layout(document);
        TopicBox central = layout.Boxes[0];

        DropPlan? plan = DropTargets.Find(layout, document.Map(), Id("R1"), new LayoutPoint(central.Bounds.X + 4, central.Bounds.CenterY));

        Assert.NotNull(plan);
        Assert.Equal(MainSide.Left, plan.Side);
        Document moved = document.Applied(new MoveTopic(M, Id("R1"), plan.NewParent, plan.Index, Side: plan.Side));
        Assert.Equal(MainSide.Left, Sides(moved)["R1"]);
    }

    [Fact]
    public void Dropping_above_a_left_main_topic_lands_above_it_on_screen()
    {
        Document document = Four();
        MapLayout layout = Layout(document);
        TopicBox l1 = layout.Find(Id("L1"))!;

        DropPlan plan = DropTargets.Find(layout, document.Map(), Id("R2"), new LayoutPoint(l1.Bounds.CenterX, l1.Bounds.Y + 1))!;
        Document moved = document.Applied(new MoveTopic(M, Id("R2"), plan.NewParent, plan.Index, Side: plan.Side));

        List<string> left = [.. MainSides.TopToBottom(moved.Map(), MainSide.Left).Select(t => t.Text)];
        Assert.Equal(left.IndexOf("L1") - 1, left.IndexOf("R2"));
    }
}
