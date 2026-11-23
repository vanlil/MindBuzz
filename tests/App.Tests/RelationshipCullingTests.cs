using MindBuzz.App.Canvas;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// A long relationship is stroked only near the view (MapCanvas.VisibleRange): the cut must never lose a visible part
/// of the curve, and must leave out a curve that never comes near.
/// </summary>
public sealed class RelationshipCullingTests
{
    private static RelationshipCurve Curve(LayoutPoint start, LayoutPoint c1, LayoutPoint c2, LayoutPoint end)
    {
        var box = new TopicBox(new Topic(new TopicId("T"), "t"), TopicLevel.Sub, Side.Right, new LayoutRect(0, 0, 1, 1), new LayoutRect(0, 0, 1, 1), 2, null);
        var relationship = new Relationship(new RelationshipId("R"), new TopicId("A"), new TopicId("B"), RelationshipDirection.OneWay);
        return new RelationshipCurve(relationship, box, box, start, c1, c2, end, EndHidden: false);
    }

    [Fact]
    public void Every_point_of_the_curve_inside_the_view_lies_within_the_drawn_part()
    {
        var random = new Random(7);
        for (int run = 0; run < 2000; run++)
        {
            LayoutPoint P() => new(random.NextDouble() * 40000 - 20000, random.NextDouble() * 40000 - 20000);
            RelationshipCurve curve = Curve(P(), P(), P(), P());
            var view = new LayoutRect(random.NextDouble() * 20000 - 10000, random.NextDouble() * 20000 - 10000, 1280, 800);

            (double From, double To, double Skipped)? range = MapCanvas.VisibleRange(curve, 0, 1, view);
            for (int i = 0; i <= 20000; i++)
            {
                double t = i / 20000.0;
                if (view.Contains(curve.At(t)))
                {
                    Assert.True(range is { } r && t >= r.From - 1e-9 && t <= r.To + 1e-9, $"run {run}: t={t} visible but not drawn");
                }
            }
        }
    }

    [Fact]
    public void A_curve_far_from_the_view_is_not_drawn_and_one_wholly_crossing_it_is_cut_on_both_sides()
    {
        RelationshipCurve far = Curve(new(50000, 0), new(52000, 1000), new(54000, 1000), new(56000, 0));
        Assert.Null(MapCanvas.VisibleRange(far, 0, 1, new LayoutRect(0, 0, 1280, 800)));

        RelationshipCurve across = Curve(new(-30000, 400), new(-10000, 400), new(10000, 400), new(30000, 400));
        (double from, double to, double skipped) = MapCanvas.VisibleRange(across, 0, 1, new LayoutRect(0, 0, 1280, 800))!.Value;
        Assert.InRange(from, 0.3, 0.5);
        Assert.InRange(to, 0.5, 0.7);
        Assert.InRange(skipped, 0.9 * 60000 * from, 1.1 * 60000 * from);
    }
}

/// <summary>Relationships stay on screen while the layout animates, without flicker.</summary>
public sealed class RelationshipTransitionTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Relationships_are_drawn_in_every_frame_of_a_collapse_animation()
    {
        var s = new Session();
        s.Main.Execute(Commands.CommandId.NewSample);
        Session.Pump();
        s.Main.ActivateMap(s.Main.Document.Maps[1].Id);
        Session.Pump();
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(s.Window)?.Dispose();
        Assert.Equal(2, s.Canvas.RelationshipsDrawn);

        // "Things to do" holds the end of one relationship; collapsing it animates the layout, held halfway so the
        // frame below is a transition frame however slow the machine.
        s.Canvas.HoldTransitionAt = 0.5;
        s.Main.Select(s.Map.Root.Children[2].Id);
        s.Main.Execute(Commands.CommandId.Collapse);
        Assert.True(s.Canvas.IsTransitioning);
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(s.Window)?.Dispose();

        Assert.True(s.Canvas.IsTransitioning);
        Assert.Equal(2, s.Canvas.RelationshipsDrawn);
    }
}
