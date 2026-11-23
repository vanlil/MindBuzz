using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

public sealed class TopicCommandTests
{
    private readonly Document _doc = Small();

    [Fact]
    public void A_subtopic_is_added_last_and_expands_a_collapsed_parent()
    {
        Document collapsed = _doc.Applied(new SetCollapsed(M1, Id("A"), true));

        Document after = collapsed.Applied(new AddTopic(M1, Id("A"), NewTopicPlacement.Subtopic, Id("A3"), "A3"));

        Assert.Equal("A1,A2,A3", after.ChildrenOf("A"));
        Assert.False(after.Get("A").IsCollapsed);
    }

    [Theory]
    [InlineData(NewTopicPlacement.SiblingAfter, "A,B,N,C")]
    [InlineData(NewTopicPlacement.SiblingBefore, "A,N,B,C")]
    public void Siblings_are_added_next_to_the_reference(NewTopicPlacement placement, string expected)
    {
        Document after = _doc.Applied(new AddTopic(M1, Id("B"), placement, Id("N"), "N"));

        Assert.Equal(expected, after.ChildrenOf("Center"));
    }

    [Fact]
    public void The_central_topic_has_no_siblings_no_parent_and_cannot_be_deleted_or_moved()
    {
        Assert.Equal(Refusal.CentralTopic, _doc.RefusalOf(new AddTopic(M1, Id("CENTER"), NewTopicPlacement.SiblingAfter, Id("N"))));
        Assert.Equal(Refusal.CentralTopic, _doc.RefusalOf(new InsertParent(M1, Id("CENTER"), Id("N"))));
        Assert.Equal(Refusal.CentralTopic, _doc.RefusalOf(new DeleteTopic(M1, Id("CENTER"))));
        Assert.Equal(Refusal.CentralTopic, _doc.RefusalOf(new MoveTopic(M1, Id("CENTER"), Id("A"), 0)));
    }

    [Fact]
    public void A_new_topic_may_start_empty_but_text_edits_may_not_be_empty()
    {
        Document after = _doc.Applied(new AddTopic(M1, Id("B"), NewTopicPlacement.Subtopic, Id("N")));

        Assert.Equal(string.Empty, after.Get("N").Text);
        Assert.Equal(Refusal.EmptyText, after.RefusalOf(new SetTopicText(M1, Id("N"), "  ")));
    }

    [Fact]
    public void An_id_already_in_the_document_is_refused()
    {
        Assert.Equal(Refusal.IdInUse, _doc.RefusalOf(new AddTopic(M1, Id("B"), NewTopicPlacement.Subtopic, Id("A1"))));
    }

    [Fact]
    public void Insert_parent_puts_a_topic_between_the_selection_and_its_parent()
    {
        Document after = _doc.Applied(new InsertParent(M1, Id("B"), Id("P"), "P"));

        Assert.Equal("A,P,C", after.ChildrenOf("Center"));
        Assert.Equal("B", after.ChildrenOf("P"));
    }

    [Theory]
    [InlineData("A1", "A2")]
    [InlineData("A2", "A1")]
    [InlineData("C1", "C")]
    public void After_delete_the_next_sibling_else_the_previous_else_the_parent_is_selected(string deleted, string selected)
    {
        CommandResult result = new DeleteTopic(M1, Id(deleted)).Apply(_doc);

        Assert.Equal(Id(selected), result.Target!.Value.Topic);
    }

    [Fact]
    public void Deleting_a_topic_deletes_its_subtree_and_every_relationship_touching_it()
    {
        Document linked = _doc
            .Applied(new AddRelationship(M1, new RelationshipId("R1"), Id("A1"), Id("B"), RelationshipDirection.OneWay))
            .Applied(new AddRelationship(M1, new RelationshipId("R2"), Id("B"), Id("C1"), RelationshipDirection.OneWay))
            .Applied(new AddRelationship(M1, new RelationshipId("R3"), Id("A1"), Id("A2"), RelationshipDirection.OneWay));

        Document after = linked.Applied(new DeleteTopic(M1, Id("A")));

        Assert.Null(after.Map().Find(Id("A1")));
        Assert.Equal(["R2"], after.Map().Relationships.Select(r => r.Id.Value));
    }

    [Fact]
    public void Unchanged_subtrees_are_shared_with_the_previous_document()
    {
        Document after = _doc.Applied(new SetTopicText(M1, Id("A1"), "changed"));

        Assert.Same(_doc.Get("C"), after.Get("C"));
        Assert.Same(_doc.Get("A2"), after.Get("A2"));
        Assert.NotSame(_doc.Get("A"), after.Get("A"));
    }

    [Fact]
    public void Setting_the_marker_a_topic_has_removes_it_and_another_value_replaces_it()
    {
        Document p2 = _doc.Applied(new SetMarker(M1, Id("B"), MarkerGroups.Priority, "2"));
        Document p3 = p2.Applied(new SetMarker(M1, Id("B"), MarkerGroups.Priority, "3"));
        Document none = p3.Applied(new SetMarker(M1, Id("B"), MarkerGroups.Priority, "3"));

        Assert.Equal([new Marker(MarkerGroups.Priority, "3")], p3.Get("B").Markers);
        Assert.Empty(none.Get("B").Markers);
        Assert.Equal(Refusal.UnknownMarker, _doc.RefusalOf(new SetMarker(M1, Id("B"), MarkerGroups.Priority, "9")));
    }

    [Fact]
    public void Markers_stay_in_group_order_whatever_order_they_are_set_in()
    {
        Document after = _doc
            .Applied(new SetMarker(M1, Id("B"), MarkerGroups.Progress, "50"))
            .Applied(new SetMarker(M1, Id("B"), MarkerGroups.Priority, "1"));

        Assert.Equal([MarkerGroups.Priority, MarkerGroups.Progress], after.Get("B").Markers.Select(m => m.Group));
    }

    [Fact]
    public void Progress_cycles_through_its_steps_and_back_to_none()
    {
        Document doc = _doc;
        var seen = new List<string?>();
        for (int i = 0; i < 6; i++)
        {
            doc = doc.Applied(new CycleMarker(M1, Id("B"), MarkerGroups.Progress));
            seen.Add(doc.Get("B").MarkerOf(MarkerGroups.Progress)?.Value);
        }

        Assert.Equal(["0", "25", "50", "75", "100", null], seen);

        Document back = _doc.Applied(new CycleMarker(M1, Id("B"), MarkerGroups.Progress, Forward: false));
        Assert.Equal("100", back.Get("B").MarkerOf(MarkerGroups.Progress)?.Value);
    }

    [Fact]
    public void A_note_is_stored_verbatim_and_an_unchanged_note_is_no_change()
    {
        const string note = "# Title\n\n<b>raw html</b> stays text\n";
        Document after = _doc.Applied(new SetNote(M1, Id("B"), note));

        Assert.Equal(note, after.Get("B").Note);
        Assert.Equal(Refusal.NoChange, after.RefusalOf(new SetNote(M1, Id("B"), note)));
    }

    [Fact]
    public void A_paste_that_would_nest_deeper_than_the_file_format_keeps_is_refused()
    {
        // A chain of topics as deep as the limit allows below a main topic (depth 1), and one more.
        Topic Chain(int levels, int start) => levels == 1
            ? new Topic(Id("D" + start), "d")
            : new Topic(Id("D" + start), "d") { Children = [Chain(levels - 1, start + 1)] };

        Assert.True(_doc.Applied(new PasteClip(M1, Id("A"), new Clip([Chain(MbzLimits.MaxTopicDepth - 1, 0)], []))).Get("D0") is not null);
        Assert.Equal(Refusal.TooDeep, _doc.RefusalOf(new PasteClip(M1, Id("A"), new Clip([Chain(MbzLimits.MaxTopicDepth, 0)], []))));
    }

    /// <summary>
    /// Every command that nests topics keeps to the loader's limit, not only Paste: a map built deeper by hand would
    /// lose its deepest topics on the next open.
    /// </summary>
    [Fact]
    public void No_command_nests_topics_deeper_than_the_file_format_keeps()
    {
        // A chain under A (depth 1) reaching exactly the limit: D0 at depth 2 … D198 at depth 200.
        Topic Chain(int levels, int start) => levels == 1
            ? new Topic(Id("D" + start), "d")
            : new Topic(Id("D" + start), "d") { Children = [Chain(levels - 1, start + 1)] };
        Document full = _doc.Applied(new PasteClip(M1, Id("A"), new Clip([Chain(MbzLimits.MaxTopicDepth - 1, 0)], [])));
        TopicId deepest = Id("D" + (MbzLimits.MaxTopicDepth - 2));
        Assert.Equal(MbzLimits.MaxTopicDepth, full.Map().Index.Locate(deepest)!.Value.Depth);

        Assert.Equal(Refusal.TooDeep, full.RefusalOf(new AddTopic(M1, deepest, NewTopicPlacement.Subtopic, Id("NEW"))));
        Assert.Equal(Refusal.TooDeep, full.RefusalOf(new InsertParent(M1, Id("D0"), Id("NEW"))));
        Assert.Equal(Refusal.TooDeep, full.RefusalOf(new MoveTopic(M1, Id("D0"), Id("A1"), 0)));
        Assert.Equal(Refusal.TooDeep, full.RefusalOf(new DemoteTopic(M1, Id("D0"))));

        // At the limit, still allowed: a sibling of the deepest topic, a move to the same depth, Promote.
        Assert.NotNull(full.Applied(new AddTopic(M1, deepest, NewTopicPlacement.SiblingAfter, Id("NEW"))).Get("NEW"));
        Assert.Equal("d", full.Applied(new MoveTopic(M1, Id("D0"), Id("B"), 0)).ChildrenOf("B"));
        Assert.NotNull(full.Applied(new PromoteTopic(M1, Id("D0"))).Get("D0"));
    }

    [Fact]
    public void A_note_cannot_grow_beyond_the_limit_but_a_longer_one_can_shrink()
    {
        string full = new('a', Topic.MaxNoteLength);
        Document atLimit = _doc.Applied(new SetNote(M1, Id("B"), full));
        Assert.Equal(Refusal.NoteTooLong, atLimit.RefusalOf(new SetNote(M1, Id("B"), full + "b")));

        // A note from a file may be longer: it may still be shortened, one character at a time.
        Document longer = TestMaps.Small().ReplaceMap(TreeEdit.Update(TestMaps.Small().Maps[0], Id("B"), topic => topic with { Note = full + "bb" }));
        Assert.Equal(full + "b", longer.Applied(new SetNote(M1, Id("B"), full + "b")).Get("B").Note);
    }
}

public sealed class StructureCommandTests
{
    private readonly Document _doc = Small();

    /// <summary>Main topics reorder on their own side only (A and B right, C left); subtopics among all siblings.</summary>
    [Theory]
    [InlineData("B", ReorderDirection.Up, "Center", "B,A,C")]
    [InlineData("A", ReorderDirection.Down, "Center", "B,A,C")]
    [InlineData("A", ReorderDirection.Last, "Center", "B,A,C")]
    [InlineData("A2", ReorderDirection.First, "A", "A2,A1")]
    public void Topics_reorder_among_their_siblings(string topic, ReorderDirection direction, string parent, string expected)
    {
        Assert.Equal(expected, _doc.Applied(new ReorderTopic(M1, Id(topic), direction)).ChildrenOf(parent));
    }

    [Theory]
    [InlineData("B", ReorderDirection.Down)]
    [InlineData("C", ReorderDirection.First)]
    public void A_main_topic_never_reorders_onto_the_other_side(string topic, ReorderDirection direction)
    {
        Assert.Equal(Refusal.NoPlaceToMove, _doc.RefusalOf(new ReorderTopic(M1, Id(topic), direction)));
    }

    /// <summary>On the left the list runs bottom to top: Down on screen is earlier in the list.</summary>
    [Fact]
    public void On_the_left_side_down_on_screen_moves_earlier_in_the_list()
    {
        Document twoLeft = _doc.Applied(new SetMainSide(M1, Id("B"), MainSide.Left));
        Assert.Equal(["C", "B"], MainSides.TopToBottom(twoLeft.Map(), MainSide.Left).Select(t => t.Text));

        Document moved = twoLeft.Applied(new ReorderTopic(M1, Id("C"), ReorderDirection.Down));

        Assert.Equal(["B", "C"], MainSides.TopToBottom(moved.Map(), MainSide.Left).Select(t => t.Text));
        Assert.Equal("A,C,B", moved.ChildrenOf("Center"));
    }

    [Fact]
    public void The_first_topic_cannot_move_up_and_the_last_cannot_move_down()
    {
        Assert.Equal(Refusal.NoPlaceToMove, _doc.RefusalOf(new ReorderTopic(M1, Id("A"), ReorderDirection.Up)));
        Assert.Equal(Refusal.NoPlaceToMove, _doc.RefusalOf(new ReorderTopic(M1, Id("C"), ReorderDirection.Down)));
    }

    [Fact]
    public void Promote_makes_a_topic_the_next_sibling_of_its_parent()
    {
        Document after = _doc.Applied(new PromoteTopic(M1, Id("A1")));

        Assert.Equal("A,A1,B,C", after.ChildrenOf("Center"));
        Assert.Equal("A2", after.ChildrenOf("A"));
        Assert.Equal(Refusal.NoPlaceToMove, _doc.RefusalOf(new PromoteTopic(M1, Id("A"))));
    }

    [Fact]
    public void Demote_makes_a_topic_the_last_child_of_its_previous_sibling()
    {
        Document after = _doc.Applied(new DemoteTopic(M1, Id("C")));

        Assert.Equal("A,B", after.ChildrenOf("Center"));
        Assert.Equal("C", after.ChildrenOf("B"));
        Assert.Equal(Refusal.NoPlaceToMove, _doc.RefusalOf(new DemoteTopic(M1, Id("A"))));
    }

    [Fact]
    public void The_central_topic_cannot_be_collapsed_but_a_collapsed_one_can_be_expanded()
    {
        Assert.Equal(Refusal.CentralTopic, _doc.RefusalOf(new SetCollapsed(M1, Id("CENTER"), true)));

        Document collapsed = _doc.ReplaceMap(_doc.Maps[0] with { Root = _doc.Maps[0].Root with { IsCollapsed = true } });
        Assert.False(collapsed.Applied(new SetCollapsed(M1, Id("CENTER"), false)).Maps[0].Root.IsCollapsed);
    }

    [Fact]
    public void A_topic_cannot_move_into_its_own_subtree()
    {
        Assert.Equal(Refusal.IntoOwnSubtree, _doc.RefusalOf(new MoveTopic(M1, Id("A"), Id("A1"), 0)));
        Assert.Equal(Refusal.IntoOwnSubtree, _doc.RefusalOf(new MoveTopic(M1, Id("A"), Id("A"), 0)));
    }

    [Fact]
    public void Moving_within_the_same_parent_counts_the_index_after_removal()
    {
        Document after = _doc.Applied(new MoveTopic(M1, Id("A"), Id("CENTER"), 2));

        Assert.Equal("B,C,A", after.ChildrenOf("Center"));
        Assert.Equal(Refusal.NoChange, _doc.RefusalOf(new MoveTopic(M1, Id("B"), Id("CENTER"), 1)));
    }

    [Fact]
    public void A_moved_topic_keeps_its_relationships()
    {
        Document linked = _doc.Applied(new AddRelationship(M1, new RelationshipId("R1"), Id("A1"), Id("C1"), RelationshipDirection.OneWay));

        Document after = linked.Applied(new MoveTopic(M1, Id("A1"), Id("B"), 0));

        Assert.Single(after.Map().Relationships);
    }

    [Fact]
    public void Collapsing_a_leaf_or_an_already_collapsed_topic_is_no_change()
    {
        Assert.Equal(Refusal.NoChange, _doc.RefusalOf(new SetCollapsed(M1, Id("B"), true)));
        Assert.Equal(Refusal.NoChange, _doc.RefusalOf(new SetCollapsed(M1, Id("A"), false)));
    }

    [Fact]
    public void Show_one_level_collapses_the_main_topics_and_show_all_expands_everything()
    {
        Document one = _doc.Applied(new ShowLevels(M1, 1, Id("A1")));

        Assert.True(one.Get("A").IsCollapsed);
        Assert.True(one.Get("C").IsCollapsed);
        Assert.False(one.Get("CENTER").IsCollapsed);
        Assert.Equal(Id("A"), new ShowLevels(M1, 1, Id("A1")).Apply(_doc).Target!.Value.Topic);

        Document all = one.Applied(new ShowLevels(M1, int.MaxValue));
        Assert.False(all.Get("A").IsCollapsed);
        Assert.Equal(Refusal.NoChange, _doc.RefusalOf(new ShowLevels(M1, int.MaxValue)));
    }
}

public sealed class RelationshipAndLinkCommandTests
{
    private readonly Document _doc = Small();
    private static readonly RelationshipId R1 = new("R1");

    [Fact]
    public void A_topic_cannot_be_linked_to_itself_or_twice_in_either_direction()
    {
        Document linked = _doc.Applied(new AddRelationship(M1, R1, Id("A"), Id("B"), RelationshipDirection.OneWay));

        Assert.Equal(Refusal.SelfRelationship, _doc.RefusalOf(new AddRelationship(M1, R1, Id("A"), Id("A"), RelationshipDirection.OneWay)));
        Assert.Equal(Refusal.DuplicateRelationship, linked.RefusalOf(new AddRelationship(M1, new RelationshipId("R2"), Id("B"), Id("A"), RelationshipDirection.TwoWay)));
    }

    [Fact]
    public void A_relationship_can_be_toggled_reversed_and_deleted()
    {
        Document linked = _doc.Applied(new AddRelationship(M1, R1, Id("A"), Id("B"), RelationshipDirection.OneWay));

        Document twoWay = linked.Applied(new ChangeRelationship(M1, R1, RelationshipChange.ToggleTwoWay));
        Document reversed = linked.Applied(new ChangeRelationship(M1, R1, RelationshipChange.Reverse));
        Document deleted = linked.Applied(new DeleteRelationship(M1, R1));

        Assert.Equal(RelationshipDirection.TwoWay, twoWay.Map().Relationships[0].Direction);
        Assert.Equal((Id("B"), Id("A")), (reversed.Map().Relationships[0].From, reversed.Map().Relationships[0].To));
        Assert.Empty(deleted.Map().Relationships);
        Assert.Equal(Refusal.NoChange, linked.RefusalOf(new ChangeRelationship(M1, R1, RelationshipChange.MakeOneWay)));
    }

    [Fact]
    public void A_relationship_takes_a_colour_and_a_line_and_the_current_value_is_no_change()
    {
        Document linked = _doc.Applied(new AddRelationship(M1, R1, Id("A"), Id("B"), RelationshipDirection.OneWay));

        Document red = linked.Applied(new SetRelationshipStyle(M1, R1, Color: RelationshipColor.Red));
        Document dotted = red.Applied(new SetRelationshipStyle(M1, R1, Line: RelationshipLine.Dotted));

        Assert.Equal((RelationshipColor.Red, RelationshipLine.Dotted), (dotted.Map().Relationships[0].Color, dotted.Map().Relationships[0].Line));
        Assert.Equal(Refusal.NoChange, red.RefusalOf(new SetRelationshipStyle(M1, R1, Color: RelationshipColor.Red)));
        Assert.Equal(Refusal.RelationshipNotFound, red.RefusalOf(new SetRelationshipStyle(M1, new RelationshipId("NONE"), Line: RelationshipLine.Dashed)));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("example.com")]
    [InlineData(" https://example.com")]
    public void Links_with_disallowed_schemes_are_refused(string url)
    {
        Assert.Equal(Refusal.UrlNotAllowed, _doc.RefusalOf(new AddLink(M1, Id("B"), new InternetLink(url))));
    }

    [Fact]
    public void Links_are_added_edited_reordered_and_removed()
    {
        Document doc = _doc
            .Applied(new AddLink(M1, Id("B"), new InternetLink("https://a.example")))
            .Applied(new AddLink(M1, Id("B"), new InternetLink("mailto:b@example.com", "Mail")))
            .Applied(new EditLink(M1, Id("B"), 0, new InternetLink("https://a.example", "A")))
            .Applied(new MoveLink(M1, Id("B"), 1, 0));

        Assert.Equal(["Mail", "A"], doc.Get("B").Links.Select(link => link.Title));

        Document removed = doc.Applied(new RemoveLink(M1, Id("B"), 0));
        Assert.Equal(["A"], removed.Get("B").Links.Select(link => link.Title));
        Assert.Equal(Refusal.LinkNotFound, removed.RefusalOf(new RemoveLink(M1, Id("B"), 3)));
    }
}

public sealed class MapCommandTests
{
    private readonly Document _doc = Small();

    [Fact]
    public void A_new_map_is_added_last_with_its_central_topic_selected()
    {
        CommandResult result = new AddMap(new MapId("MAP2"), Id("ROOT2"), "Central Topic").Apply(_doc);

        Assert.Equal([_doc.Maps[0].Root.Text, "Central Topic"], result.After!.Maps.Select(map => map.Name));
        Assert.Equal(new EditTarget(new MapId("MAP2"), Id("ROOT2")), result.Target);
    }

    [Fact]
    public void The_last_map_cannot_be_deleted_and_names_cannot_be_empty()
    {
        Assert.Equal(Refusal.LastMap, _doc.RefusalOf(new DeleteMap(M1)));
        Assert.Equal(Refusal.EmptyName, _doc.RefusalOf(new RenameMap(M1, " ")));
    }

    [Fact]
    public void Maps_are_reordered_and_deleting_one_activates_its_neighbour()
    {
        Document three = _doc
            .Applied(new AddMap(new MapId("MAP2"), Id("R2"), "2"))
            .Applied(new AddMap(new MapId("MAP3"), Id("R3"), "3"));

        Document moved = three.Applied(new MoveMap(new MapId("MAP3"), 0));
        Assert.Equal(["3", _doc.Maps[0].Name, "2"], moved.Maps.Select(map => map.Name));

        Assert.Equal(new MapId("MAP3"), new DeleteMap(new MapId("MAP2")).Apply(three).Target!.Value.Map);
        Assert.Equal(new MapId("MAP2"), new DeleteMap(new MapId("MAP3")).Apply(three).Target!.Value.Map);
    }
}

public sealed class ClipTests
{
    private readonly Document _doc = Small();

    [Fact]
    public void A_copy_keeps_relationships_with_both_ends_inside_and_drops_the_others()
    {
        Document linked = _doc
            .Applied(new AddRelationship(TestMaps.M1, new RelationshipId("R1"), Id("A1"), Id("A2"), RelationshipDirection.OneWay))
            .Applied(new AddRelationship(TestMaps.M1, new RelationshipId("R2"), Id("A1"), Id("B"), RelationshipDirection.OneWay));

        Clip clip = Clip.Copy(linked.Map(), Id("A"))!;

        Assert.Equal(["R1"], clip.Relationships.Select(r => r.Id.Value));
    }

    [Fact]
    public void Pasting_with_fresh_ids_works_twice_and_remaps_relationship_ends()
    {
        Document linked = _doc.Applied(new AddRelationship(TestMaps.M1, new RelationshipId("R1"), Id("A1"), Id("A2"), RelationshipDirection.OneWay));
        Clip clip = Clip.Copy(linked.Map(), Id("A"))!;
        var ids = new SeededIdSource(7);

        Document once = linked.Applied(new PasteClip(TestMaps.M1, Id("B"), clip.WithFreshIds(ids)));
        Document twice = once.Applied(new PasteClip(TestMaps.M1, Id("B"), clip.WithFreshIds(ids)));

        Assert.Equal("A,A", twice.ChildrenOf("B"));
        Assert.Equal(3, twice.Map().Relationships.Length);
        Assert.All(twice.Map().Relationships, r => Assert.True(twice.Map().Contains(r.From) && twice.Map().Contains(r.To)));
    }

    [Fact]
    public void Pasting_ids_that_already_exist_is_refused_rather_than_renamed()
    {
        Clip clip = Clip.Copy(_doc.Map(), Id("A"))!;

        Assert.Equal(Refusal.IdInUse, _doc.RefusalOf(new PasteClip(TestMaps.M1, Id("B"), clip)));
    }
}
