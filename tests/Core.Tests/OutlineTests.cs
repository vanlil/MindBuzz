using MindBuzz.Core.Model;
using MindBuzz.Core.Outline;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.Core.Tests;

public sealed class OutlineTests
{
    private static string Shape(IEnumerable<Topic> topics, int depth = 0) =>
        string.Concat(topics.Select(topic => new string(' ', depth * 2) + topic.Text + "\n" + Shape(topic.Children, depth + 1)));

    [Fact]
    public void Topics_are_written_one_per_line_with_one_tab_per_level()
    {
        Topic root = TestMaps.Small().Maps[0].Root;

        Assert.Equal("Center\n\tA\n\t\tA1\n\t\tA2\n\tB\n\tC\n\t\tC1\n", OutlineText.Write([root]));
    }

    [Fact]
    public void Line_breaks_inside_a_topic_become_spaces()
    {
        Assert.Equal("two\nlines\n".Replace("\n", " ", StringComparison.Ordinal).TrimEnd() + "\n",
            OutlineText.Write([new Topic(new TopicId("T"), "two\nlines")]));
    }

    [Fact]
    public void What_is_written_reads_back_as_the_same_tree()
    {
        Topic root = TestMaps.Small().Maps[0].Root;

        var read = OutlineText.Read(OutlineText.Write([root]), new SeededIdSource(1));

        Assert.Equal(Shape([root]), Shape(read));
    }

    [Fact]
    public void Indentation_is_relative_and_tabs_spaces_bullets_and_blank_lines_are_handled()
    {
        const string text = "Groceries\r\n    - Milk\r\n    - Fruit\r\n\r\n          * Apples\r\n\t\t• Pears\r\nWork\n  Mail";

        var topics = OutlineText.Read(text, new SeededIdSource(1));

        Assert.Equal("Groceries\n  Milk\n  Fruit\n    Apples\n    Pears\nWork\n  Mail\n", Shape(topics));
    }

    [Fact]
    public void A_staircase_of_indents_stops_at_the_deepest_level_the_file_format_keeps()
    {
        // Each line one space deeper: 3,000 levels without the cap, far deeper than a file keeps.
        string text = string.Concat(Enumerable.Range(0, 3_000).Select(i => new string(' ', i) + "x\n"));

        Topic root = Assert.Single(OutlineText.Read(text, new SeededIdSource(1)));

        int depth = 0;
        for (Topic topic = root; topic.HasChildren; topic = topic.Children[^1])
        {
            depth++;
        }

        Assert.Equal(MbzLimits.MaxTopicDepth - 1, depth);
        Assert.Equal(3_000, root.DescendantsAndSelf().Count());
    }

    [Fact]
    public void Every_read_topic_gets_a_fresh_id()
    {
        var topics = OutlineText.Read("a\n\tb\n\tc", new SeededIdSource(1));

        Assert.Equal(3, topics.SelectMany(topic => topic.DescendantsAndSelf()).Select(topic => topic.Id).Distinct().Count());
    }
}
