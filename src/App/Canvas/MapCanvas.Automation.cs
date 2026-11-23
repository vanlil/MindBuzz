using System.Globalization;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using MindBuzz.App.Localization;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The map for screen readers. The canvas draws its topics, so there are no controls to read: its automation name
/// says what is selected, and changes with the selection, so VoiceOver and Narrator announce each step.
/// </summary>
internal sealed partial class MapCanvas
{
    private string? _spokenName;

    protected override AutomationPeer OnCreateAutomationPeer() => new MapCanvasAutomationPeer(this);

    /// <summary>What a screen reader says for the canvas now.</summary>
    internal string SpokenName => Describe(Map, SelectedTopic, SelectedRelationship);

    /// <summary>Tells a listening screen reader the selection changed; called after the properties it reads change.</summary>
    private void AnnounceSelection()
    {
        string name = SpokenName;
        if (name == _spokenName)
        {
            return;
        }

        string? previous = _spokenName;
        _spokenName = name;
        if (ControlAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, previous, name);
        }
    }

    /// <summary>
    /// The selected relationship, or the selected topic with its level, its place among its siblings, whether it is
    /// collapsed, its markers, note and links. Words from Strings.resx; the topic's own text first.
    /// </summary>
    internal static string Describe(Map? map, TopicId? topic, RelationshipId? relationship)
    {
        if (map is null)
        {
            return Strings.A11y_Map;
        }

        if (relationship is { } relationshipId && map.Relationships.FirstOrDefault(r => r.Id == relationshipId) is { } line)
        {
            string from = map.Find(line.From)?.Text ?? string.Empty;
            string to = map.Find(line.To)?.Text ?? string.Empty;
            string format = line.Direction == RelationshipDirection.TwoWay ? Strings.A11y_RelationshipTwoWay : Strings.A11y_RelationshipOneWay;
            return string.Format(CultureInfo.CurrentCulture, format, OneLine(from), OneLine(to));
        }

        if (topic is not { } id || map.Index.Locate(id) is not { } location)
        {
            return Strings.A11y_MapNothingSelected;
        }

        Topic selected = location.Topic;
        var parts = new List<string> { OneLine(selected.Text) };
        parts.Add(location.Depth switch
        {
            0 => Strings.A11y_LevelCentral,
            1 => Strings.A11y_LevelMain,
            _ => string.Format(CultureInfo.CurrentCulture, Strings.A11y_LevelSubFormat, location.Depth),
        });

        if (location.Parent is { } parent && map.Find(parent) is { } parentTopic)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.A11y_PositionFormat, location.IndexInParent + 1, parentTopic.Children.Length));
        }

        if (selected.Children.Length > 0)
        {
            string format = selected.IsCollapsed ? Strings.A11y_CollapsedFormat : Strings.A11y_SubtopicsFormat;
            parts.Add(string.Format(CultureInfo.CurrentCulture, format, selected.Children.Length));
        }

        if (selected.MarkerOf(MarkerGroups.Priority) is { } priority)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.A11y_PriorityFormat, priority.Value));
        }

        if (selected.MarkerOf(MarkerGroups.Progress) is { } progress)
        {
            parts.Add(progress.Value == "100" ? Strings.A11y_Done : string.Format(CultureInfo.CurrentCulture, Strings.A11y_ProgressFormat, progress.Value));
        }

        if (selected.MarkerOf(MarkerGroups.Icon) is { } icon && Strings.ResourceManager.GetString($"Icon_{icon.Value}", Strings.Culture) is { } iconName)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.A11y_IconFormat, iconName));
        }

        if (!string.IsNullOrEmpty(selected.Note))
        {
            parts.Add(Strings.A11y_HasNote);
        }

        if (selected.Links.Length > 0)
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.A11y_LinksFormat, selected.Links.Length));
        }

        return string.Join(Strings.A11y_Separator, parts);
    }

    /// <summary>The title as shown, on one line: a screen reader says the words, not the Markdown markers.</summary>
    private static string OneLine(string text) => TitleMarkup.Plain(text).ReplaceLineEndings(" ");

    /// <summary>The canvas as a group named after its selection, so the name is read whenever it changes.</summary>
    private sealed class MapCanvasAutomationPeer(MapCanvas owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetLocalizedControlTypeCore() => Strings.A11y_MapType;

        protected override string? GetNameCore() => owner.SpokenName;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
