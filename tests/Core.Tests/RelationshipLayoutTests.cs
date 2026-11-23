using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;
using static MindBuzz.Core.Tests.TestMaps;

namespace MindBuzz.Core.Tests;

public sealed class RelationshipLayoutTests
{
    private static readonly RelationshipId R1 = new("R1");

    private static (MapLayout Layout, IReadOnlyList<RelationshipCurve> Curves) Lay(Document document)
    {
        Map map = document.Maps[0];
        MapLayout layout = new LayoutEngine(new FakeMeasurer()).Layout(map);
        return (layout, RelationshipLayout.Compute(layout, map));
    }

    private static Document Linked(string from, string to) =>
        Small().Applied(new AddRelationship(M1, R1, Id(from), Id(to), RelationshipDirection.OneWay));

    /// <summary>The ends keep <see cref="RelationshipLayout.Clearance"/> from the outlines.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, 0.6)]
    [InlineData(0.3, -1.2)]
    public void A_curve_starts_and_ends_just_outside_the_outlines_of_its_topics(double? along, double? across)
    {
        Document linked = Linked("A1", "C1");
        if (along is { } a && across is { } c)
        {
            linked = linked.Applied(new SetRelationshipBend(M1, R1, new RelationshipBend(a, c)));
        }

        (MapLayout layout, IReadOnlyList<RelationshipCurve> curves) = Lay(linked);
        RelationshipCurve curve = Assert.Single(curves);

        LayoutRect from = layout.Find(Id("A1"))!.Bounds;
        LayoutRect to = layout.Find(Id("C1"))!.Bounds;
        double gap = RelationshipLayout.Clearance;

        Assert.True(from.Inflate(gap + 0.01).Contains(curve.Start) && !from.Inflate(gap - 0.5).Contains(curve.Start), "The start is 3 px outside A1.");
        Assert.True(to.Inflate(gap + 0.01).Contains(curve.End) && !to.Inflate(gap - 0.5).Contains(curve.End), "The end is 3 px outside C1.");
        Assert.False(curve.EndHidden);
    }

    [Fact]
    public void A_hidden_end_goes_to_its_nearest_visible_ancestor_and_is_dashed()
    {
        Document collapsed = Linked("A1", "C1").Applied(new SetCollapsed(M1, Id("C"), true));

        RelationshipCurve curve = Assert.Single(Lay(collapsed).Curves);

        Assert.Equal(Id("C"), curve.ToBox.Id);
        Assert.True(curve.EndHidden);
    }

    [Fact]
    public void Both_ends_inside_one_collapsed_branch_draw_nothing()
    {
        Document collapsed = Linked("A1", "A2").Applied(new SetCollapsed(M1, Id("A"), true));

        Assert.Empty(Lay(collapsed).Curves);
    }

    [Fact]
    public void The_curve_bows_away_from_the_central_topic()
    {
        (MapLayout layout, IReadOnlyList<RelationshipCurve> curves) = Lay(Linked("A1", "A2"));
        RelationshipCurve curve = Assert.Single(curves);

        LayoutRect central = layout.Boxes[0].Bounds;
        LayoutPoint middle = curve.At(0.5);
        LayoutPoint chord = new((curve.Start.X + curve.End.X) / 2, (curve.Start.Y + curve.End.Y) / 2);

        double Distance(LayoutPoint p) => Math.Abs(p.X - central.CenterX) + Math.Abs(p.Y - central.CenterY);
        Assert.True(Distance(middle) > Distance(chord), "The arc's middle is further from the centre than its chord.");
    }

    [Fact]
    public void A_point_on_the_curve_is_near_it_and_one_far_away_is_not()
    {
        RelationshipCurve curve = Assert.Single(Lay(Linked("A1", "C1")).Curves);

        Assert.True(curve.DistanceTo(curve.At(0.37)) < 0.5);
        Assert.True(curve.DistanceTo(new LayoutPoint(curve.Bounds.Right + 50, curve.Bounds.Bottom + 50)) > 40);
    }

    [Fact]
    public void A_bend_makes_the_curve_pass_through_the_dragged_point()
    {
        (MapLayout layout, IReadOnlyList<RelationshipCurve> curves) = Lay(Linked("A1", "C1"));
        RelationshipCurve automatic = Assert.Single(curves);
        var point = new LayoutPoint(automatic.At(0.5).X + 37, automatic.At(0.5).Y - 120);

        RelationshipCurve bent = RelationshipLayout.Reshape(layout, automatic, RelationshipLayout.BendThrough(automatic, point));

        Assert.True(Math.Abs(bent.At(0.5).X - point.X) < 0.001 && Math.Abs(bent.At(0.5).Y - point.Y) < 0.001);
    }

    [Fact]
    public void Reversing_a_bent_relationship_keeps_its_shape()
    {
        (MapLayout layout, IReadOnlyList<RelationshipCurve> curves) = Lay(Linked("A1", "C1"));
        RelationshipCurve automatic = Assert.Single(curves);
        RelationshipBend bend = RelationshipLayout.BendThrough(automatic, new LayoutPoint(automatic.At(0.5).X, automatic.At(0.5).Y + 90));
        Document bent = Linked("A1", "C1").Applied(new SetRelationshipBend(M1, R1, bend));

        LayoutPoint before = Assert.Single(Lay(bent).Curves).At(0.5);
        LayoutPoint after = Assert.Single(Lay(bent.Applied(new ChangeRelationship(M1, R1, RelationshipChange.Reverse))).Curves).At(0.5);

        Assert.True(Math.Abs(before.X - after.X) < 0.001 && Math.Abs(before.Y - after.Y) < 0.001);
    }

    [Fact]
    public void A_bend_keeps_its_shape_relative_to_the_topics_and_survives_a_round_trip()
    {
        Document bent = Linked("A1", "C1").Applied(new SetRelationshipBend(M1, R1, new RelationshipBend(0.125, -0.5)));

        using var stream = new MemoryStream();
        Serialization.MbzWriter.Write(stream, bent, "test");
        stream.Position = 0;
        Document loaded = Serialization.MbzReader.Read(stream).Document!;

        Assert.Equal(new RelationshipBend(0.125, -0.5), loaded.Maps[0].Relationships[0].Bend);
        Assert.Equal(Refusal.NoChange, bent.RefusalOf(new SetRelationshipBend(M1, R1, new RelationshipBend(0.125, -0.5))));
    }
}
