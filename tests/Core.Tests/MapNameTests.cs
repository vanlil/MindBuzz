using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.Core.Tests;

/// <summary>A map's name is its central topic's text, shortened for the tab.</summary>
public sealed class MapNameTests
{
    [Theory]
    [InlineData("Launch plan", "Launch plan")]
    [InlineData("  Launch   plan  ", "Launch plan")]
    [InlineData("Launch plan\nwith details", "Launch plan…")]
    [InlineData("\n\nSecond line first", "Second line first")]
    [InlineData("Quarterly planning for the infrastructure team", "Quarterly planning for the…")]
    [InlineData("Supercalifragilisticexpialidocious-and-more", "Supercalifragilisticexpialidoci…")]
    [InlineData("", "")]
    public void The_label_is_the_first_line_cut_at_a_word(string text, string expected)
    {
        string label = MapName.For(text);

        Assert.Equal(expected, label);
        Assert.True(label.Length <= MapName.MaxLength);
    }

    [Fact]
    public void An_emoji_is_never_cut_in_half()
    {
        string text = new string('x', MapName.MaxLength - 2) + "🐝🐝🐝";

        string label = MapName.For(text);

        Assert.False(label.Any(char.IsHighSurrogate) && !label.Any(char.IsLowSurrogate));
        Assert.EndsWith("…", label, StringComparison.Ordinal);
    }

    [Fact]
    public void Renaming_a_map_sets_its_central_topic_and_editing_the_topic_renames_the_map()
    {
        var document = new Document([new Map(new MapId("M"), new Topic(new TopicId("T"), "Old"))]);

        Document renamed = document.Applied(new RenameMap(new MapId("M"), "New name"));
        Assert.Equal("New name", renamed.Maps[0].Root.Text);
        Assert.Equal("New name", renamed.Maps[0].Name);

        Document edited = renamed.Applied(new SetTopicText(new MapId("M"), new TopicId("T"), "Edited"));
        Assert.Equal("Edited", edited.Maps[0].Name);
    }

    [Fact]
    public void One_line_joins_the_lines_for_the_rename_box()
    {
        Assert.Equal("Launch plan with details", MapName.OneLine("Launch plan\n  with   details\n"));
    }
}
