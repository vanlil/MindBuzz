using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MindBuzz.App.Commands;
using MindBuzz.App.Localization;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;

namespace MindBuzz.App.ViewModels;

/// <summary>One marker button in the topic panel: a priority or a progress value, highlighted when the topic has
/// it.</summary>
/// <remarks>
/// The buttons apply the marker themselves rather than run the menu command: the menu commands are off while the notes
/// editor has the keyboard (see <see cref="MainViewModel.NotesFocused"/>), and these buttons sit right beside it.
/// </remarks>
public sealed record MarkerChoice(string Label, string Tooltip, ICommand Command, bool IsActive);

/// <summary>
/// The topic panel docked on the right. It always shows the selected topic: its title, markers,
/// internet links and Markdown note. It changes the document only through the main view model, so every change is
/// a command and an undo step. Whether it is open, and its width, last for the session only.
/// </summary>
public sealed partial class TopicPanelViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    /// <summary>The topic on show, to tell a new selection from a change to the same topic.</summary>
    private (MapId Map, TopicId Topic)? _shown;

    /// <summary>
    /// The topic the title box was entered for. A window key (next map, a search step, undo from the menu bar) can
    /// move the selection while the box keeps the text typed for this one, so a commit goes here, not to the topic
    /// now shown.
    /// </summary>
    private (MapId Map, TopicId Topic)? _titleTarget;

    /// <summary>
    /// What the marker buttons and the link rows show, so typing in the note (one document change per keystroke)
    /// does not rebuild them.
    /// </summary>
    private (string? Priority, string? Progress)? _markersShown;

    private System.Collections.Immutable.ImmutableArray<InternetLink> _linksShown;

    internal TopicPanelViewModel(MainViewModel owner)
    {
        _owner = owner;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Opening the panel starts in the default mode for the note on show, like selecting a topic
    /// does.</summary>
    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            IsPreview = !IsNoteEmpty;
        }
        else
        {
            IsMaximized = false;
        }
    }

    /// <summary>
    /// The panel fills the area beside the toolbar instead of the canvas, for writing longer notes.
    /// Session only, like the width. Going back to the map restores the normal size.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMaximized { get; set; }

    public ICommand MaximizeCommand => _owner.Commands[CommandId.ToggleMaximizePanel].Command;

    public string MaximizeTooltip => Tooltip(CommandId.ToggleMaximizePanel);

    public string RestoreTooltip
    {
        get
        {
            IReadOnlyList<Avalonia.Input.KeyGesture> gestures = _owner.Commands[CommandId.ToggleMaximizePanel].Gestures.KeyGestures(_owner.Platform);
            return gestures.Count == 0 ? Strings.Panel_RestoreSize : $"{Strings.Panel_RestoreSize}  ({GestureText.Format(gestures[0], _owner.Platform)})";
        }
    }

    /// <summary>
    /// The title box was committed (Enter, or leaving it): the topic's text changes as one undo step, as after
    /// editing it on the map. Empty text is refused and the box shows the text again.
    /// </summary>
    /// <returns>False when the text was refused.</returns>
    internal bool CommitTitle(string text)
    {
        if ((_titleTarget ?? _shown) is not { } target
            || _owner.Document.Maps.FirstOrDefault(map => map.Id == target.Map)?.Find(target.Topic) is not { } topic
            || text == topic.Text)
        {
            return true;
        }

        if (!_owner.Apply(new MindBuzz.Core.Editing.SetTopicText(target.Map, target.Topic, text)).IsApplied)
        {
            // The box now shows the selected topic's text again, so further typing is for that one.
            _titleTarget = _shown;
            OnPropertyChanged(nameof(TopicTitle));
            return false;
        }

        return true;
    }

    /// <summary>Esc in the title box: the typing is thrown away, so leaving the box commits nothing.</summary>
    internal void CancelTitle() => _titleTarget = null;

    internal void SetTitleFocused(bool focused)
    {
        // Remembered on entering only: leaving the box commits after this, still to the topic it was entered for.
        if (focused)
        {
            _titleTarget = _shown;
        }

        _owner.SetTitleFocused(focused);
    }

    /// <summary>
    /// After Enter in the title box the keyboard leaves it, so the field frame goes away: back to
    /// the map, or to the notes while the panel is maximized, so it keeps its size.
    /// </summary>
    internal void LeaveTitle()
    {
        if (IsMaximized)
        {
            _owner.PanelView?.FocusNotes();
        }
        else
        {
            _owner.PanelView?.FocusCanvas();
        }
    }

    /// <summary>True when a topic is selected; otherwise the panel says how to get one.</summary>
    [ObservableProperty]
    public partial bool HasTopic { get; private set; }

    [ObservableProperty]
    public partial string TopicTitle { get; private set; } = string.Empty;

    /// <summary>The shown topic's level, for the title's line limit; null without a topic.</summary>
    internal Core.Layout.TopicLevel? TopicLevel { get; private set; }

    /// <summary>The note, verbatim. The editor and the preview both follow it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoteEmpty))]
    public partial string NoteText { get; private set; } = string.Empty;

    public bool IsNoteEmpty => NoteText.Length == 0;

    /// <summary>
    /// Preview or Edit. A newly selected topic, or the panel opening, starts in Preview when the note has content and
    /// in Edit when it is empty; the toggle then holds until the selection moves on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode))]
    public partial bool IsPreview { get; set; }

    public bool IsEditMode => !IsPreview;

    /// <summary>The selected topic's links, with the same operations as the link list popup.</summary>
    [ObservableProperty]
    public partial LinkListViewModel? Links { get; private set; }

    public ObservableCollection<MarkerChoice> Priorities { get; } = [];

    public ObservableCollection<MarkerChoice> Progress { get; } = [];

    /// <summary>The panel's close button: the same as View > Topic Panel.</summary>
    public ICommand CloseCommand => _owner.Commands[CommandId.ToggleTopicPanel].Command;

    public ICommand AddLinkCommand => _owner.Commands[CommandId.AddLink].Command;

    /// <summary>The Help button beside Edit and Preview: the Markdown sheet as a PDF.</summary>
    public ICommand MarkdownHelpCommand => _owner.Commands[CommandId.PrintMarkdownHelp].Command;

    public string CloseTooltip => Tooltip(CommandId.ToggleTopicPanel);

    public string ModeTooltip => Tooltip(CommandId.ToggleNotesMode);

    /// <summary>The notes toolbar's Bold and Italic: their names with their shortcuts.</summary>
    public string BoldTooltip => Tooltip(CommandId.Bold);

    public string ItalicTooltip => Tooltip(CommandId.Italic);

    [RelayCommand]
    private void ShowEdit() => SetMode(preview: false);

    [RelayCommand]
    private void ShowPreview() => SetMode(preview: true);

    private void SetMode(bool preview)
    {
        IsPreview = preview;
        _owner.PanelView?.FocusNotes();
    }

    /// <summary>The editor's text changed: a burst of typing becomes one undo step.</summary>
    /// <summary>A keystroke in the notes editor. False when the document refused it; the editor then puts back <see
    /// cref="NoteText"/>.</summary>
    internal bool EditNote(string text)
    {
        if (_shown is not { } shown || text == NoteText)
        {
            return true;
        }

        if (!_owner.UpdateNote(shown.Map, shown.Topic, text))
        {
            return false;
        }

        NoteText = text;
        return true;
    }

    /// <summary>A link in the preview was clicked: opened through the one guarded gate.</summary>
    internal void OpenLink(string url)
    {
        if (LinkUrl.IsAllowed(url))
        {
            _owner.OpenUrl(url);
        }
    }

    /// <summary>Esc in the panel: the keyboard goes back to the map, and the panel stays open.</summary>
    internal void ReturnToCanvas() => _owner.PanelView?.FocusCanvas();

    /// <summary>Undo or Redo when <paramref name="e"/> is one of their keys on this platform, otherwise null.</summary>
    internal ICommand? HistoryCommandFor(Avalonia.Input.KeyEventArgs e)
    {
        foreach (CommandId id in (CommandId[])[CommandId.Undo, CommandId.Redo])
        {
            AppCommand command = _owner.Commands[id];
            if (command.Gestures.KeyGestures(_owner.Platform).Any(gesture => gesture.Matches(e)))
            {
                return command.Command;
            }
        }

        return null;
    }

    /// <summary>The notes editor gained or lost the keyboard; see <see cref="MainViewModel.NotesFocused"/>.</summary>
    internal void SetNotesFocused(bool focused) => _owner.SetNotesFocused(focused);

    /// <summary>Reads the selected topic again: after the selection, the map or the document changed.</summary>
    /// <summary>"3 topics selected" while several are; empty otherwise. The panel shows the last one.</summary>
    [ObservableProperty]
    public partial string SelectionNote { get; private set; } = string.Empty;

    internal void Refresh()
    {
        SelectionNote = _owner.SelectedTopics.Count > 1
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Panel_SelectionFormat, _owner.SelectedTopics.Count)
            : string.Empty;
        Map map = _owner.ActiveMap;
        Topic? topic = _owner.SelectedTopic is { } id ? map.Find(id) : null;
        (MapId, TopicId)? now = topic is null ? null : (map.Id, topic.Id);

        if (now != _shown)
        {
            _owner.EndNoteBurst();
            _shown = now;
            IsPreview = topic is { Note.Length: > 0 };
            Links = topic is null ? null : new LinkListViewModel(_owner, map.Id, topic.Id);
            _linksShown = topic?.Links ?? default;
            _markersShown = null;
        }
        else if (topic is not null && topic.Links != _linksShown)
        {
            Links?.Reload();
            _linksShown = topic.Links;
        }

        HasTopic = topic is not null;
        TopicLevel = topic is not null && map.Index.Locate(topic.Id) is { } location
            ? location.Depth switch { 0 => Core.Layout.TopicLevel.Central, 1 => Core.Layout.TopicLevel.Main, _ => Core.Layout.TopicLevel.Sub }
            : null;
        TopicTitle = topic?.Text ?? string.Empty;
        NoteText = topic?.Note ?? string.Empty;
        RefreshMarkers(topic);
    }

    private void RefreshMarkers(Topic? topic)
    {
        string? priority = topic?.MarkerOf(MarkerGroups.Priority)?.Value;
        string? progress = topic?.MarkerOf(MarkerGroups.Progress)?.Value;
        if (_markersShown == (priority, progress) && Priorities.Count > 0)
        {
            return;
        }

        _markersShown = (priority, progress);

        Priorities.Clear();
        for (int i = 0; i < MarkerGroups.PriorityValues.Count; i++)
        {
            CommandId id = CommandId.Priority1 + i;
            Priorities.Add(new MarkerChoice(
                (i + 1).ToString(CultureInfo.CurrentCulture),
                Tooltip(id),
                SetMarkerCommand(MarkerGroups.Priority, MarkerGroups.PriorityValues[i]),
                priority == MarkerGroups.PriorityValues[i]));
        }

        Progress.Clear();
        for (int i = 0; i < MarkerGroups.ProgressValues.Count; i++)
        {
            CommandId id = CommandId.Progress0 + i;
            string label = id == CommandId.ProgressDone
                ? Strings.Panel_ProgressDone
                : string.Format(CultureInfo.CurrentCulture, Strings.Panel_ProgressFormat, i * 25);
            Progress.Add(new MarkerChoice(label, Tooltip(id), SetMarkerCommand(MarkerGroups.Progress, MarkerGroups.ProgressValues[i]), progress == MarkerGroups.ProgressValues[i]));
        }
    }

    /// <summary>Sets the marker on the topic on show, or removes it when the topic already has it.</summary>
    private RelayCommand SetMarkerCommand(string group, string value) => new(() =>
    {
        if (_shown is { } shown)
        {
            _owner.Apply(new SetMarker(shown.Map, shown.Topic, group, value));
        }
    });

    private string Tooltip(CommandId id) => _owner.TooltipOf(id);
}
