using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using MindBuzz.SyntheticMap;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

/// <summary>
/// A measurer with simple, exact rules, so layout tests do not depend on a font: 7 units per character, lines
/// wrap at the maximum width, 16 units per line.
/// </summary>
internal sealed class FakeMeasurer : ITextMeasurer
{
    public int Calls { get; private set; }

    public LayoutSize Measure(string text, TopicLevel level, double maxWidth)
    {
        Calls++;
        double width = 0;
        int lines = 0;
        foreach (string line in text.Split('\n'))
        {
            double lineWidth = Math.Max(1, line.Length) * 7;
            int wrapped = (int)Math.Ceiling(lineWidth / maxWidth);
            lines += Math.Max(1, wrapped);
            width = Math.Max(width, Math.Min(lineWidth, maxWidth));
        }

        return new LayoutSize(width, lines * 16);
    }
}

public sealed class LayoutTests
{
    private static MapLayout Layout(Map map, ITextMeasurer? measurer = null) => new LayoutEngine(measurer ?? new FakeMeasurer()).Layout(map);

    private static Map MapWithMainTopics(int count)
    {
        var root = new Topic(new TopicId("ROOT"), "Center")
        {
            Children = [.. Enumerable.Range(1, count).Select(i => new Topic(new TopicId($"M{i}"), $"Main {i}"))],
        };
        return new Map(new MapId("M"), root);
    }

    [Fact]
    public void The_central_topic_is_centred_on_the_origin()
    {
        TopicBox central = Layout(Small().Maps[0]).Boxes[0];

        Assert.Equal(TopicLevel.Central, central.Level);
        Assert.Equal(0, central.Bounds.CenterX, 6);
        Assert.Equal(0, central.Bounds.CenterY, 6);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    [InlineData(8, 4)]
    public void Main_topics_are_split_by_count_with_the_extra_one_on_the_right(int count, int right)
    {
        MapLayout layout = Layout(MapWithMainTopics(count));

        Assert.Equal(right, layout.Boxes.Count(box => box.Level == TopicLevel.Main && box.Side == Side.Right));
        Assert.Equal(count - right, layout.Boxes.Count(box => box.Level == TopicLevel.Main && box.Side == Side.Left));
    }

    [Fact]
    public void The_order_runs_clockwise_right_side_down_then_left_side_up()
    {
        MapLayout layout = Layout(MapWithMainTopics(6));
        double Y(string id) => layout.Find(new TopicId(id))!.Bounds.CenterY;

        // Right side, top to bottom: 1, 2, 3. Left side, bottom to top: 4, 5, 6.
        Assert.True(Y("M1") < Y("M2") && Y("M2") < Y("M3"));
        Assert.True(Y("M4") > Y("M5") && Y("M5") > Y("M6"));
    }

    [Fact]
    public void Subtopics_extend_outwards_on_their_side()
    {
        Document document = Small()
            .Applied(new MoveTopic(M1, Id("C"), Id("CENTER"), 0)) // C first: right side gets C and A, left gets B
            .Applied(new AddTopic(M1, Id("B"), NewTopicPlacement.Subtopic, Id("B1"), "B1"));
        MapLayout layout = Layout(document.Maps[0]);

        TopicBox b = layout.Find(Id("B"))!;
        TopicBox b1 = layout.Find(Id("B1"))!;
        TopicBox a = layout.Find(Id("A"))!;
        TopicBox a1 = layout.Find(Id("A1"))!;

        Assert.Equal(Side.Left, b.Side);
        Assert.True(b1.Bounds.Right < b.Bounds.X, "A left-side subtopic sits further left than its parent.");
        Assert.Equal(Side.Right, a.Side);
        Assert.True(a1.Bounds.X > a.Bounds.Right, "A right-side subtopic sits further right than its parent.");
    }

    [Fact]
    public void Collapsed_branches_are_not_laid_out()
    {
        Document collapsed = Small().Applied(new SetCollapsed(M1, Id("A"), true));
        MapLayout layout = Layout(collapsed.Maps[0]);

        Assert.Null(layout.Find(Id("A1")));
        Assert.Equal(2, layout.Find(Id("A"))!.HiddenChildren);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void No_two_topics_overlap_on_generated_maps(int seed)
    {
        Map map = MapGenerator.Generate(new MapSpec(Topics: 600, MaxWords: 10, CollapsedRatio: 0.05, Seed: seed)).Maps[0];
        IReadOnlyList<TopicBox> boxes = Layout(map).Boxes;

        // Sort by x so only neighbours on the x axis are compared: O(n log n) instead of O(n²).
        var byX = boxes.OrderBy(box => box.Bounds.X).ToList();
        for (int i = 0; i < byX.Count; i++)
        {
            for (int j = i + 1; j < byX.Count && byX[j].Bounds.X < byX[i].Bounds.Right; j++)
            {
                Assert.False(
                    byX[i].Bounds.Intersects(byX[j].Bounds),
                    $"'{byX[i].Topic.Text}' overlaps '{byX[j].Topic.Text}'");
            }
        }
    }

    [Fact]
    public void The_same_map_lays_out_the_same_every_time()
    {
        Map map = MapGenerator.Generate(new MapSpec(Topics: 300, Seed: 9)).Maps[0];

        IReadOnlyList<TopicBox> first = Layout(map).Boxes;
        IReadOnlyList<TopicBox> second = Layout(map).Boxes;

        Assert.Equal(first.Select(box => (box.Id, box.Bounds)), second.Select(box => (box.Id, box.Bounds)));
    }

    [Fact]
    public void After_an_edit_only_the_topics_on_the_changed_path_are_measured_again()
    {
        Document document = MapGenerator.Generate(new MapSpec(Topics: 500, CollapsedRatio: 0, Seed: 4));
        var measurer = new FakeMeasurer();
        var engine = new LayoutEngine(measurer);
        engine.Layout(document.Maps[0]);
        int first = measurer.Calls;

        Topic deep = document.Maps[0].Root.DescendantsAndSelf().Last();
        Document edited = document.Applied(new SetTopicText(document.Maps[0].Id, deep.Id, "edited"));
        engine.Layout(edited.Maps[0]);

        int depth = edited.Maps[0].Index.Locate(deep.Id)!.Value.Depth;
        // Every topic once, plus one line of text per level (at most three), measured once for placing the note and
        // link indicators level with the first line.
        Assert.InRange(first, 500, 503);
        Assert.True(measurer.Calls - first <= depth + 1, $"{measurer.Calls - first} topics were measured again; the path has {depth + 1}.");
    }

    [Fact]
    public void A_deep_edit_never_moves_a_main_topic_to_the_other_side()
    {
        Document document = MapGenerator.Generate(new MapSpec(Topics: 300, Seed: 11));
        Map map = document.Maps[0];
        var sides = Layout(map).Boxes.Where(box => box.Level == TopicLevel.Main).ToDictionary(box => box.Id, box => box.Side);

        Topic deep = map.Root.DescendantsAndSelf().Last();
        for (int i = 0; i < 20; i++)
        {
            document = document.Applied(new AddTopic(map.Id, deep.Id, NewTopicPlacement.Subtopic, new TopicId($"NEW{i}"), "A long new topic with many words in it"));
        }

        var after = Layout(document.Maps[0]).Boxes.Where(box => box.Level == TopicLevel.Main).ToDictionary(box => box.Id, box => box.Side);
        Assert.Equal(sides, after);
    }

    [Fact]
    public void Text_wraps_at_the_level_width_and_grows_the_topic_instead_of_truncating()
    {
        string longText = new('x', 200);
        Document document = Small().Applied(new SetTopicText(M1, Id("A1"), longText));

        TopicBox box = Layout(document.Maps[0]).Find(Id("A1"))!;

        Assert.True(box.Text.Width <= LayoutMetrics.Default.SubTextWidth);
        Assert.True(box.Text.Height >= 16 * 5, "200 characters at 7 units wrap to at least five lines of 16.");
    }

    [Fact]
    public void Connectors_run_from_the_parents_edge_to_the_childs_inner_edge()
    {
        MapLayout layout = Layout(Small().Maps[0]);
        TopicBox a = layout.Find(Id("A"))!;
        TopicBox a1 = layout.Find(Id("A1"))!;

        Connector connector = layout.Connectors.Single(c => c.Child == Id("A1"));

        Assert.Equal(a.Bounds.Right, connector.Start.X, 6);
        Assert.Equal(a1.Bounds.X, connector.End.X, 6);
        Assert.Equal(a1.Bounds.CenterY, connector.End.Y, 6);
    }
}
