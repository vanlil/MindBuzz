using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MindBuzz.App.Canvas;
using MindBuzz.App.Commands;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The map for screen readers: the canvas's automation name says what is selected (MapCanvas.Automation.cs).</summary>
public sealed class AccessibilityTests
{
    [AvaloniaFact]
    public void The_canvas_names_the_selected_topic_with_its_level_place_and_markers()
    {
        var s = new Session();
        s.Main.Execute(CommandId.NewSample);
        Session.Pump();
        s.Main.ActivateMap(s.Main.Document.Maps[1].Id);
        Topic gettingThere = s.Map.Root.Children[0];
        s.Main.Select(gettingThere.Children[0].Id);
        Session.Pump();

        AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(s.Canvas);

        Assert.Equal("Book flights, subtopic, level 2, 1 of 2, priority 1, done", peer.GetName());
        Assert.Equal("mind map", peer.GetLocalizedControlType());
    }

    [AvaloniaFact]
    public void The_central_topic_says_how_many_main_topics_and_that_it_has_a_note_and_a_link()
    {
        var s = new Session();
        s.Main.Execute(CommandId.NewSample);
        Session.Pump();
        s.Main.ActivateMap(s.Main.Document.Maps[1].Id);
        s.Main.Select(s.Map.Root.Id);
        Session.Pump();

        Assert.Equal("Weekend in Lisbon, central topic, 5 subtopics, has a note, 1 links", s.Canvas.SpokenName);
    }

    [AvaloniaFact]
    public void A_collapsed_topic_says_its_subtopics_are_hidden_and_a_relationship_names_its_ends()
    {
        var s = new Session();
        s.Main.Execute(CommandId.NewSample);
        Session.Pump();
        s.Main.ActivateMap(s.Main.Document.Maps[1].Id);
        Topic budget = s.Map.Root.Children[4];
        s.Main.Apply(new SetCollapsed(s.Map.Id, budget.Id, true));
        s.Main.Select(budget.Id);
        Session.Pump();
        Assert.Equal("Budget, main topic, 5 of 5, collapsed, 3 subtopics hidden, icon: Money", s.Canvas.SpokenName);

        Assert.Equal(
            "Relationship from Book flights to Flights: €250",
            MapCanvas.Describe(s.Map, null, s.Map.Relationships[0].Id));
    }

    [AvaloniaFact]
    public void With_nothing_selected_the_canvas_says_so()
    {
        var s = new Session();
        s.Main.Select(null);
        Session.Pump();

        Assert.Equal("Mind map, nothing selected", s.Canvas.SpokenName);
        s.Press(Key.Right);
        Assert.StartsWith("Central Topic, central topic", s.Canvas.SpokenName, StringComparison.Ordinal);
    }
}
