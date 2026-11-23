using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using MindBuzz.SyntheticMap;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

public sealed class UndoHistoryTests
{
    private static readonly EditTarget Here = new(M1, Id("CENTER"));

    private readonly UndoHistory _history = new(Small());

    [Fact]
    public void Undo_and_redo_return_the_exact_snapshots_and_the_selection()
    {
        Document start = _history.Current;
        _history.Execute(new SetTopicText(M1, Id("B"), "changed"), new EditTarget(M1, Id("A")));
        Document changed = _history.Current;

        HistoryStep undo = _history.Undo()!;
        Assert.Same(start, undo.Document);
        Assert.Equal(new EditTarget(M1, Id("A")), undo.Target);

        HistoryStep redo = _history.Redo()!;
        Assert.Same(changed, redo.Document);
        Assert.Equal(new EditTarget(M1, Id("B")), redo.Target);
    }

    [Fact]
    public void A_refused_command_is_not_recorded_and_does_not_dirty_the_document()
    {
        _history.Execute(new DeleteTopic(M1, Id("CENTER")), Here);

        Assert.False(_history.CanUndo);
        Assert.False(_history.IsDirty);
    }

    [Fact]
    public void An_editing_session_is_one_undo_step()
    {
        object session = new();
        _history.Execute(new AddTopic(M1, Id("B"), NewTopicPlacement.Subtopic, Id("N"), CoalesceKey: session), Here);
        _history.Execute(new SetTopicText(M1, Id("N"), "Ne", session), Here);
        _history.Execute(new SetTopicText(M1, Id("N"), "New", session), Here);

        _history.Undo();

        Assert.False(_history.CanUndo);
        Assert.Null(_history.Current.Maps[0].Find(Id("N")));
    }

    [Fact]
    public void Different_sessions_are_different_steps()
    {
        _history.Execute(new SetTopicText(M1, Id("B"), "one", new object()), Here);
        _history.Execute(new SetTopicText(M1, Id("B"), "two", new object()), Here);

        _history.Undo();

        Assert.Equal("one", _history.Current.Get("B").Text);
    }

    [Fact]
    public void Undoing_back_to_the_saved_state_makes_the_document_clean_again()
    {
        _history.Execute(new SetTopicText(M1, Id("B"), "one"), Here);
        _history.MarkClean();
        _history.Execute(new SetTopicText(M1, Id("B"), "two"), Here);
        Assert.True(_history.IsDirty);

        _history.Undo();
        Assert.False(_history.IsDirty);

        _history.Undo();
        Assert.True(_history.IsDirty);
    }

    [Fact]
    public void Coalescing_never_merges_across_a_save()
    {
        object session = new();
        _history.Execute(new SetTopicText(M1, Id("B"), "one", session), Here);
        _history.MarkClean();
        _history.Execute(new SetTopicText(M1, Id("B"), "two", session), Here);

        _history.Undo();

        Assert.Equal("one", _history.Current.Get("B").Text);
        Assert.False(_history.IsDirty);
    }

    [Fact]
    public void A_new_change_after_undo_drops_the_redo_branch_and_a_saved_state_on_it_becomes_unreachable()
    {
        _history.Execute(new SetTopicText(M1, Id("B"), "one"), Here);
        _history.MarkClean();
        _history.Undo();

        _history.Execute(new SetTopicText(M1, Id("B"), "other"), Here);

        Assert.False(_history.CanRedo);
        _history.Undo();
        Assert.True(_history.IsDirty, "The saved text was 'one', which no undo or redo can reach any more.");
    }

    [Fact]
    public void Seal_ends_a_session_so_the_next_command_is_a_new_step()
    {
        object session = new();
        _history.Execute(new SetTopicText(M1, Id("B"), "one", session), Here);
        _history.Seal();
        _history.Execute(new SetTopicText(M1, Id("B"), "two", session), Here);

        _history.Undo();

        Assert.Equal("one", _history.Current.Get("B").Text);
    }

    [Fact]
    public void Abandoning_a_session_removes_its_step_and_leaves_no_redo()
    {
        object session = new();
        _history.Execute(new AddTopic(M1, Id("B"), NewTopicPlacement.Subtopic, Id("N"), CoalesceKey: session), Here);
        _history.Execute(new SetTopicText(M1, Id("N"), "typed", session), Here);

        Document? before = _history.Abandon(session);

        Assert.NotNull(before);
        Assert.Null(_history.Current.Maps[0].Find(Id("N")));
        Assert.False(_history.CanUndo);
        Assert.False(_history.CanRedo);
        Assert.False(_history.IsDirty);
    }

    [Fact]
    public void Abandoning_does_nothing_when_the_last_step_belongs_to_another_session()
    {
        _history.Execute(new SetTopicText(M1, Id("B"), "kept", new object()), Here);

        Assert.Null(_history.Abandon(new object()));
        Assert.Equal("kept", _history.Current.Get("B").Text);
    }

    [Fact]
    public void Reset_clears_the_history_and_starts_clean()
    {
        _history.Execute(new SetTopicText(M1, Id("B"), "one"), Here);

        _history.Reset(Small());

        Assert.False(_history.CanUndo);
        Assert.False(_history.IsDirty);
    }

    [Fact]
    public void Undo_of_a_change_in_another_map_targets_that_map()
    {
        _history.Execute(new AddMap(new MapId("MAP2"), Id("R2"), "Two"), Here);
        var onMap2 = new EditTarget(new MapId("MAP2"), Id("R2"));
        _history.Execute(new SetTopicText(M1, Id("B"), "changed"), new EditTarget(M1, Id("B")));

        // The user is on map 2 now; undo must bring map 1 back with B selected.
        HistoryStep step = _history.Undo()!;

        Assert.Equal(new EditTarget(M1, Id("B")), step.Target);
        Assert.NotEqual(onMap2, step.Target);
    }
}

/// <summary>
/// Random command sequences on generated maps: every command undoes to exactly the document
/// before it and redoes to exactly the document after it.
/// </summary>
public sealed class RandomSequenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Any_sequence_of_commands_undoes_to_the_start_and_redoes_to_the_end(int seed)
    {
        Document start = MapGenerator.Generate(new MapSpec(Topics: 150, Maps: 2, Relationships: 8, Seed: seed));
        var history = new UndoHistory(start);
        var random = new Random(seed);
        var ids = new SeededIdSource(seed + 1000);
        var snapshots = new List<Document> { start };

        for (int step = 0; step < 400; step++)
        {
            IDocumentCommand command = RandomCommand(history.Current, random, ids);
            Map map = history.Current.Maps[0];
            if (history.Execute(command, new EditTarget(map.Id, map.Root.Id)).IsApplied)
            {
                snapshots.Add(history.Current);
            }
        }

        Assert.True(snapshots.Count > 100, $"Only {snapshots.Count - 1} of 400 random commands applied; the generator is too timid.");
        Document end = history.Current;

        for (int i = snapshots.Count - 2; i >= 0; i--)
        {
            Assert.True(DocumentEquality.AreEqual(snapshots[i], history.Undo()!.Document), $"Undo step {i} differs.");
        }

        Assert.False(history.CanUndo);
        while (history.CanRedo)
        {
            history.Redo();
        }

        Assert.True(DocumentEquality.AreEqual(end, history.Current));
        AssertInvariants(end);
    }

    /// <summary>The model's invariants: unique ids, relationships inside their map, one marker per group.</summary>
    internal static void AssertInvariants(Document document)
    {
        var ids = new HashSet<string>();
        foreach (Map map in document.Maps)
        {
            Assert.True(ids.Add(map.Id.Value), $"Duplicate map id {map.Id}");
            foreach (Topic topic in map.Root.DescendantsAndSelf())
            {
                Assert.True(ids.Add(topic.Id.Value), $"Duplicate topic id {topic.Id}");
                Assert.Equal(topic.Markers.Length, topic.Markers.Select(m => m.Group).Distinct().Count());
                Assert.All(topic.Links, link => Assert.True(LinkUrl.IsAllowed(link.Url)));
            }

            foreach (Relationship relationship in map.Relationships)
            {
                Assert.True(ids.Add(relationship.Id.Value), $"Duplicate relationship id {relationship.Id}");
                Assert.True(map.Contains(relationship.From) && map.Contains(relationship.To), "A relationship points outside its map.");
                Assert.NotEqual(relationship.From, relationship.To);
            }
        }
    }

    private static IDocumentCommand RandomCommand(Document document, Random random, IIdSource ids)
    {
        Map map = document.Maps[random.Next(document.Maps.Length)];
        Topic[] topics = [.. map.Root.DescendantsAndSelf()];
        TopicId Any() => topics[random.Next(topics.Length)].Id;

        return random.Next(20) switch
        {
            0 => new AddTopic(map.Id, Any(), (NewTopicPlacement)random.Next(3), ids.NextTopic(), "new"),
            1 => new InsertParent(map.Id, Any(), ids.NextTopic(), "parent"),
            2 => new DeleteTopic(map.Id, Any()),
            3 => new SetTopicText(map.Id, Any(), $"text {random.Next(100)}"),
            4 => new MoveTopic(map.Id, Any(), Any(), random.Next(5)),
            5 => new ReorderTopic(map.Id, Any(), (ReorderDirection)random.Next(4)),
            6 => new PromoteTopic(map.Id, Any()),
            7 => new DemoteTopic(map.Id, Any()),
            8 => new SetCollapsed(map.Id, Any(), random.Next(2) == 0),
            9 => new ShowLevels(map.Id, 1 + random.Next(4)),
            10 => new SetNote(map.Id, Any(), random.Next(3) == 0 ? string.Empty : $"note {random.Next(10)}"),
            11 => new SetMarker(map.Id, Any(), MarkerGroups.Priority, MarkerGroups.PriorityValues[random.Next(5)]),
            12 => new CycleMarker(map.Id, Any(), MarkerGroups.Progress, random.Next(2) == 0),
            13 => new AddLink(map.Id, Any(), new InternetLink($"https://example.com/{random.Next(100)}")),
            14 => new AddRelationship(map.Id, ids.NextRelationship(), Any(), Any(), RelationshipDirection.OneWay),
            15 => map.Relationships.IsEmpty
                ? new AddTopic(map.Id, Any(), NewTopicPlacement.Subtopic, ids.NextTopic(), "x")
                : new ChangeRelationship(map.Id, map.Relationships[random.Next(map.Relationships.Length)].Id, (RelationshipChange)random.Next(4)),
            16 => new PasteClip(map.Id, Any(), Clip.Copy(map, Any())!.WithFreshIds(ids)),
            17 => new AddMap(ids.NextMap(), ids.NextTopic(), "Center"),
            18 => new DeleteMap(map.Id),
            _ => new MoveMap(map.Id, random.Next(document.Maps.Length)),
        };
    }
}
