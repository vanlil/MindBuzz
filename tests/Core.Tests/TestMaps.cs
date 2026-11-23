using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.Core.Tests;

/// <summary>
/// A small, readable map for command tests:
/// <code>
/// Center
///   A
///     A1
///     A2
///   B
///   C
///     C1
/// </code>
/// Topic ids are their names, so assertions read naturally.
/// </summary>
internal static class TestMaps
{
    internal static readonly MapId M1 = new("MAP1");

    internal static TopicId Id(string name) => new(name.ToUpperInvariant());

    internal static Document Small()
    {
        Topic Leaf(string name) => new(Id(name), name);

        var root = new Topic(Id("CENTER"), "Center")
        {
            Children =
            [
                new Topic(Id("A"), "A") { Children = [Leaf("A1"), Leaf("A2")] },
                Leaf("B"),
                new Topic(Id("C"), "C") { Children = [Leaf("C1")] },
            ],
        };

        return new Document([new Map(M1, root)]);
    }

    internal static Map Map(this Document document) => document.Maps[0];

    internal static Topic Get(this Document document, string name) => document.Map().Find(Id(name))!;

    /// <summary>The children's names of a topic, e.g. "A1,A2".</summary>
    internal static string ChildrenOf(this Document document, string name) =>
        string.Join(',', document.Get(name).Children.Select(child => child.Text));

    internal static Document Applied(this Document document, IDocumentCommand command)
    {
        CommandResult result = command.Apply(document);
        Assert.True(result.IsApplied, $"{command} was refused: {result.Refusal}");
        return result.After!;
    }

    internal static Refusal RefusalOf(this Document document, IDocumentCommand command)
    {
        CommandResult result = command.Apply(document);
        Assert.False(result.IsApplied, $"{command} was applied but should have been refused");
        return result.Refusal;
    }
}
