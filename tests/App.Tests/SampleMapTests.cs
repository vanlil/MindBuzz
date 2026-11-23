using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>File > New Sample Mind Maps.</summary>
public sealed class SampleMapTests
{
    [AvaloniaFact]
    public async Task New_sample_mind_maps_opens_two_maps_with_markers_notes_links_and_styled_relationships()
    {
        var s = new Session();

        s.Main.Execute(CommandId.NewSample);
        await Task.Yield();
        Session.Pump();

        Document document = s.Main.Document;
        Assert.Equal(2, document.Maps.Length);
        Assert.True(s.Main.IsDirty);
        Assert.Null(s.Main.FilePath);
        Assert.False(s.Main.CanCloseWithoutAsking);

        Topic[] topics = [.. document.Maps.SelectMany(map => map.Root.DescendantsAndSelf())];
        Assert.Contains(topics, t => t.MarkerOf(MarkerGroups.Priority) is not null);
        Assert.Contains(topics, t => t.MarkerOf(MarkerGroups.Progress) is not null);
        Assert.Contains(topics, t => t.Note.Length > 0);
        Assert.Contains(topics, t => !t.Links.IsEmpty);
        Assert.All(document.Maps, map => Assert.All(map.Root.Children, main => Assert.NotNull(main.Side)));

        Relationship[] trip = [.. document.Maps[1].Relationships];
        Assert.Equal(2, trip.Length);
        Assert.NotEqual(trip[0].Color, trip[1].Color);
        Assert.NotEqual(trip[0].Line, trip[1].Line);

        // Saved and loaded, it is the same document: nothing in it the loader would repair or drop.
        LoadResult loaded = MbzReader.Read(new MemoryStream(MbzWriter.ToBytes(document, "test")));
        Assert.Empty(loaded.Problems);
        Assert.True(DocumentEquality.AreEqual(document, loaded.Document!));
    }

    [AvaloniaFact]
    public void The_keys_in_the_sample_are_this_platforms()
    {
        var mac = new Session(HostPlatform.MacOS);
        mac.Main.Execute(CommandId.NewSample);
        Session.Pump();

        Assert.Contains(mac.Map.Root.DescendantsAndSelf(), t => t.Text.StartsWith('⌘'));
    }

    /// <summary>The newer features are in the sample too: formatting (shown, not just named), several topics, full screen, copying the map as a picture.</summary>
    [AvaloniaTheory]
    [InlineData(HostPlatform.MacOS, "⌘B", "⌘-click", "⌃⌘F")]
    [InlineData(HostPlatform.Windows, "Ctrl+B", "Ctrl-click", "F11")]
    public void The_sample_shows_the_newer_features_with_this_platforms_keys(HostPlatform platform, string bold, string click, string fullScreen)
    {
        var s = new Session(platform);
        s.Main.Execute(CommandId.NewSample);
        Session.Pump();
        Topic more = Assert.Single(s.Map.Root.Children, main => main.Text == Localization.Strings.Sample_MoreToTry);
        string[] texts = [.. more.Children.Select(child => child.Text)];

        Assert.Contains(texts, text => text.Contains(bold, StringComparison.Ordinal) && TitleMarkup.Parse(text).Runs.Any(run => run.Style == TitleStyle.Underline));
        Assert.Contains(texts, text => text.StartsWith(click, StringComparison.Ordinal));
        Assert.Contains(texts, text => text.StartsWith(fullScreen, StringComparison.Ordinal));
        Assert.Contains(texts, text => text.Contains(MenuText.WithoutAccessKey(Localization.Strings.Command_CopyMapImage), StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_sample_maps_render_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            foreach (int map in (int[])[0, 1])
            {
                var s = new Session();
                s.Main.Execute(CommandId.NewSample);
                Session.Pump();
                s.Main.ActivateMap(s.Main.Document.Maps[map].Id);
                s.Main.Select(null);
                Session.Pump();
                Render.Save(s.Window, theme, map == 0 ? "sample-intro" : "sample-trip");
            }
        }
    }
}
