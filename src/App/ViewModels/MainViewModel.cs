using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MindBuzz.App.Commands;
using MindBuzz.App.Diagnostics;
using MindBuzz.App.Localization;
using MindBuzz.App.Platform;
using MindBuzz.App.Printing;
using MindBuzz.App.Services;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;
using MindBuzz.Core.Outline;
using MindBuzz.Core.Search;
using MindBuzz.Core.Serialization;

namespace MindBuzz.App.ViewModels;

/// <summary>How an editing session starts (F2 and Space).</summary>
public enum EditStart
{
    CaretAtEnd,

    /// <summary>Typing a character on a selected topic replaces its text with that character.</summary>
    Replace,
}

/// <summary>
/// The main window's view model. It owns the document's undo history, the active map, the selection and the
/// editing session; the canvas and every other view only observe them and send changes through commands. Every command
/// reaches it through <see cref="Execute"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, ICommandHost, IDisposable
{
    private readonly IUiServices _ui;
    private readonly IPlatformShell _shell;
    private readonly UndoHistory _history;

    /// <summary>The editing session's coalescing key: everything typed in one session is one undo step.</summary>
    private object? _session;

    /// <summary>True when the session began by adding the topic, which then disappears if the session ends
    /// empty.</summary>
    private bool _sessionAddedTopic;

    /// <summary>Where the selection was before the session, to go back to when it is abandoned.</summary>
    private TopicId? _selectionBeforeSession;

    private readonly RecoveryWriter _recovery;

    /// <summary>Set once the user agreed to close: the window and the app may then close without asking
    /// again.</summary>
    private bool _closeConfirmed;

    /// <summary>True while the crash dialog is open, so a second error does not stack another one.</summary>
    private bool _crashShown;

    private readonly TimeProvider _time;

    /// <summary>
    /// The open document's password, or null when it is not encrypted. Held in memory for the session
    /// only; saving and the recovery copy use it.
    /// </summary>
    private string? _password;

    /// <summary>
    /// A pause this long ends a burst of note typing, so the next keystroke starts a new undo step. Leaving the editor
    /// or selecting another topic ends it too.
    /// </summary>
    internal static readonly TimeSpan NoteBurstPause = TimeSpan.FromSeconds(2);

    /// <summary>The coalescing key of the current burst of note typing, and when its last keystroke was.</summary>
    private object? _noteBurst;

    private DateTimeOffset _lastNoteEdit;

    internal MainViewModel(IUiServices ui, IPlatformShell shell, HostPlatform platform, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(shell);

        _ui = ui;
        _shell = shell;
        Platform = platform;
        _time = time ?? TimeProvider.System;
        _recovery = new RecoveryWriter(
            _time,
            Generator,
            hide: platform == HostPlatform.Windows,
            report: exception => GlobalExceptionHandler.Report("Recovery file", exception, isFatal: false));
        Commands = AppCommands.Create(this);
        Menus = MenuLayout.Build(Commands, platform);
        TopicMenu = MenuLayout.TopicContextMenu(Commands, platform);
        TabMenu = MenuLayout.TabContextMenu(Commands, platform);
        RelationshipMenu = MenuLayout.RelationshipContextMenu(Commands, platform);
        CanvasMenu = MenuLayout.CanvasContextMenu(Commands, platform);
        Toolbar = MenuLayout.Toolbar(Commands, platform);
        Panel = new TopicPanelViewModel(this);
        Search = new SearchViewModel(this);

        _history = new UndoHistory(NewDocument());
        _history.Changed += (_, _) => OnHistoryChanged();
        Document = _history.Current;
        ActiveMap = Document.Maps[0];
        SelectedTopic = ActiveMap.Root.Id;
        Panel.Refresh();
    }

    /// <summary>Stops the recovery timer. The window disposes its view model when it closes.</summary>
    public void Dispose() => _recovery.Dispose();

    /// <summary>Written into every saved file's <c>format.json</c>, for diagnostics.</summary>
    private static string Generator => $"MindBuzz {AppInfo.Version}";

    internal HostPlatform Platform { get; }

    internal CommandRegistry Commands { get; }

    internal UndoHistory History => _history;

    /// <summary>The menus; built again when a label changes with the document (Encrypt File → Change
    /// Password).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MenuEntries))]
    public partial MenuModel Menus { get; private set; }

    /// <summary>The window's menus, for the in-window <c>Menu</c> on Windows and Linux.</summary>
    public IReadOnlyList<MenuEntry> MenuEntries => Menus.TopLevel;

    /// <summary>The topic context menu (right-click, Shift+F10, Menu key).</summary>
    public IReadOnlyList<MenuEntry> TopicMenu { get; }

    /// <summary>The selected relationship's context menu.</summary>
    public IReadOnlyList<MenuEntry> RelationshipMenu { get; }

    /// <summary>The sheet tab's context menu.</summary>
    public IReadOnlyList<MenuEntry> TabMenu { get; }

    /// <summary>The empty canvas's context menu.</summary>
    public IReadOnlyList<MenuEntry> CanvasMenu { get; }

    public IReadOnlyList<ToolbarItem> Toolbar { get; }

    /// <summary>True while the open document is encrypted: the status bar shows a lock.</summary>
    [ObservableProperty]
    public partial bool IsEncrypted { get; private set; }

    /// <summary>
    /// A newer release GitHub reported at start, or null: the status bar then shows a quiet button for it until
    /// MindBuzz quits, never a dialog, because a user who starts the app ten times a day must not be
    /// interrupted ten times.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText), nameof(UpdateTooltip))]
    public partial Version? AvailableUpdate { get; private set; }

    public string UpdateText => AvailableUpdate is { } version
        ? string.Format(CultureInfo.CurrentCulture, Strings.Status_UpdateAvailable, version.ToString(3))
        : string.Empty;

    public string UpdateTooltip => AvailableUpdate is { } version
        ? string.Format(CultureInfo.CurrentCulture, Strings.Status_UpdateTooltip, version.ToString(3))
        : string.Empty;

    /// <summary>
    /// The update check: GitHub, asked with this build's version. Tests replace it, so no
    /// request leaves the machine.
    /// </summary>
    internal Func<Task<UpdateAnswer>> UpdateSource { get; set; } = static () =>
        Version.TryParse(AppInfo.Version, out Version? current)
            ? UpdateCheck.CheckAsync(current)
            : Task.FromResult(UpdateAnswer.Unknown);

    /// <summary>True while Help → Check for Updates waits for GitHub; the command is greyed out meanwhile.</summary>
    private bool _checkingForUpdate;

    /// <summary>The check at start: quiet, only the status-bar button when there is news. Never throws.</summary>
    internal async Task CheckForUpdateAtStartAsync()
    {
        if ((await UpdateSource().ConfigureAwait(true)).Newer is { } newer)
        {
            AvailableUpdate = newer;
        }
    }

    /// <summary>
    /// Help → Check for Updates: the same check, answered in a dialog whatever the outcome, since the user asked.
    /// A newer release also gets the status-bar button.
    /// </summary>
    private async Task CheckForUpdateOnRequestAsync()
    {
        _checkingForUpdate = true;
        StatusText = Strings.Status_CheckingForUpdate;
        Commands.RefreshAll();
        UpdateAnswer answer;
        try
        {
            answer = await UpdateSource().ConfigureAwait(true);
        }
        finally
        {
            _checkingForUpdate = false;
            StatusText = Strings.Status_Ready;
        }

        string title = MenuText.WithoutAccessKey(Strings.Command_CheckForUpdate);
        string version = string.Format(CultureInfo.CurrentCulture, Strings.About_VersionFormat, AppInfo.Version);
        switch (answer.Status)
        {
            case UpdateStatus.Newer:
                AvailableUpdate = answer.Newer;
                int choice = await _ui.AskAsync(new ChoiceRequest(
                    title,
                    string.Format(CultureInfo.CurrentCulture, Strings.Update_NewerFormat, answer.Newer!.ToString(3)),
                    string.Format(CultureInfo.CurrentCulture, Strings.Update_NewerDetailFormat, AppInfo.Version),
                    [Strings.Dialog_Close, Strings.Answer_OpenDownloadPage],
                    DefaultAnswer: 1,
                    CancelAnswer: 0)).ConfigureAwait(true);
                if (choice == 1)
                {
                    OpenUpdatePage();
                }

                break;
            case UpdateStatus.UpToDate:
                await _ui.ShowMessageAsync(new MessageRequest(title, Strings.Update_UpToDate, Strings.Dialog_Close) { Version = version })
                    .ConfigureAwait(true);
                break;
            default:
                await _ui.ShowMessageAsync(new MessageRequest(title, Strings.Update_Unknown, Strings.Dialog_Close) { Version = version })
                    .ConfigureAwait(true);
                break;
        }
    }

    /// <summary>When the release page was last opened; a second click within <see cref="UpdatePageRepeatGap"/> is
    /// ignored.</summary>
    private DateTimeOffset _updatePageOpened = DateTimeOffset.MinValue;

    /// <summary>A double-click on a button is two clicks; out of habit from the map's topics it would open two
    /// tabs.</summary>
    internal static readonly TimeSpan UpdatePageRepeatGap = TimeSpan.FromSeconds(1);

    [RelayCommand]
    private void OpenUpdatePage()
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (AvailableUpdate is { } version && now - _updatePageOpened >= UpdatePageRepeatGap)
        {
            _updatePageOpened = now;
            _shell.OpenUrl(UpdateCheck.ReleasePage(version));
        }
    }

    /// <summary>The topic panel on the right: the selected topic's markers, links and note.</summary>
    public TopicPanelViewModel Panel { get; }

    /// <summary>The search panel, left of the detail view.</summary>
    public SearchViewModel Search { get; }

    /// <summary>
    /// Per map, the collapsed topics that going to a search hit opened for the eye only: the document
    /// keeps them collapsed, nothing is undoable, and closing the search closes them again, except above the
    /// selected topic.
    /// </summary>
    private readonly Dictionary<MapId, System.Collections.Immutable.ImmutableHashSet<TopicId>> _revealed = [];

    private (Map Source, System.Collections.Immutable.ImmutableHashSet<TopicId> Revealed, Map Shown)? _shownCache;

    /// <summary>
    /// The map as the canvas shows it: <see cref="ActiveMap"/> with the branches search opened. Equal to it while
    /// search opened nothing.
    /// </summary>
    public Map DisplayMap
    {
        get
        {
            if (!_revealed.TryGetValue(ActiveMap.Id, out var revealed) || revealed.IsEmpty)
            {
                return ActiveMap;
            }

            if (_shownCache is { } cache && ReferenceEquals(cache.Source, ActiveMap) && ReferenceEquals(cache.Revealed, revealed))
            {
                return cache.Shown;
            }

            Map shown = Reveal.Apply(ActiveMap, revealed);
            _shownCache = (ActiveMap, revealed, shown);
            return shown;
        }
    }

    /// <summary>The topics with search hits in the map on screen, for the canvas to highlight while search is
    /// open.</summary>
    [ObservableProperty]
    public partial IReadOnlySet<TopicId>? SearchHighlights { get; private set; }

    /// <summary>Draws and saves a map as a picture: the window, which has the place to draw it.</summary>
    internal IMapExport? MapExport { get; set; }

    /// <summary>The window's panes, attached by the window: focus moves and the notes editor's selection need
    /// it.</summary>
    internal IPanelView? PanelView { get; set; }

    /// <summary>
    /// True while the notes editor has the keyboard. Topic commands are disabled then: on macOS their menu-bar keys
    /// (⌘C, ⌘V, ⌘K…) would otherwise act on the topic instead of the text being typed (key scopes).
    /// Undo and redo stay on, and undo the note's typing bursts.
    /// </summary>
    internal bool NotesFocused { get; private set; }

    /// <summary>True while the panel's title box has the keyboard: topic commands are off then too, as for
    /// notes.</summary>
    internal bool TitleFocused { get; private set; }

    /// <summary>The document's file, or null while it is untitled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentName), nameof(WindowTitle))]
    public partial string? FilePath { get; private set; }

    public string DocumentName => FilePath is null ? Strings.Document_Untitled : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>True when the document differs from what is on disk.</summary>
    public bool IsDirty => _history.IsDirty;

    /// <summary>
    /// The window title. macOS: the document name, with "*" while there are unsaved changes; the menu
    /// bar names the app. Windows and Linux, which have no menu bar of their own: "MindBuzz 0.9.0 | Plan.mbz", and
    /// "untitled*" for a document never saved.
    /// </summary>
    public string WindowTitle
    {
        get
        {
            if (Platform == HostPlatform.MacOS)
            {
                return IsDirty ? string.Format(CultureInfo.CurrentCulture, Strings.Title_EditedFormat, DocumentName) : DocumentName;
            }

            string file = FilePath is null
                ? string.Format(CultureInfo.CurrentCulture, Strings.Title_EditedFormat, Strings.Title_UntitledFile)
                : IsDirty ? string.Format(CultureInfo.CurrentCulture, Strings.Title_EditedFormat, Path.GetFileName(FilePath)) : Path.GetFileName(FilePath);
            string product = string.IsNullOrEmpty(AppInfo.Version) ? Strings.AppTitle : $"{Strings.AppTitle} {AppInfo.Version}";
            return string.Format(CultureInfo.CurrentCulture, Strings.Title_ProductFormat, product, file);
        }
    }


    /// <summary>The open document: always the undo history's current state.</summary>
    [ObservableProperty]
    public partial Document Document { get; private set; }

    /// <summary>The map on screen, chosen with the sheet tabs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tabs), nameof(DisplayMap))]
    public partial Map ActiveMap { get; private set; }

    /// <summary>The map whose tab is being renamed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tabs))]
    public partial MapId? RenamingMap { get; private set; }

    /// <summary>
    /// The sheet tabs, in document order. Every document change asks again, a keystroke in a note
    /// too; while the tabs read the same, the same list comes back, so the bar does not rebuild its tabs.
    /// </summary>
    public IReadOnlyList<MapTab> Tabs
    {
        get
        {
            MapTab[] tabs = [.. Document.Maps.Select(map => new MapTab(map.Id, map.Name, MapName.OneLine(map.Root.Text), map.Id == ActiveMap?.Id, map.Id == RenamingMap))];
            if (!tabs.SequenceEqual(_tabs))
            {
                _tabs = tabs;
            }

            return _tabs;
        }
    }

    private MapTab[] _tabs = [];

    /// <summary>
    /// The selected topic of every map that is not on screen: each map keeps its own selection while the document
    /// is open. Zoom and scroll are kept by the canvas the same way.
    /// </summary>
    private readonly Dictionary<MapId, TopicId> _selectionByMap = [];

    /// <summary>The selected topic in the active map. The one owner of the selection.</summary>
    [ObservableProperty]
    public partial TopicId? SelectedTopic { get; set; }

    /// <summary>
    /// Distraction-free full screen: only the map, and search and the detail view when open. Session
    /// only, like everything else. The window shows and hides its bars.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFullScreen { get; set; }

    /// <summary>
    /// Every selected topic, <see cref="SelectedTopic"/> last (multi-select). Usually just that one.
    /// Delete and drag and drop act on all of them; every other command on <see cref="SelectedTopic"/>.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TopicId> SelectedTopics { get; private set; } = [];

    /// <summary>Set while the selection grows or shrinks, so the change of <see cref="SelectedTopic"/> keeps the
    /// others.</summary>
    private bool _keepingSelection;

    /// <summary>
    /// The selected relationship, or null. While one is selected, <see cref="SelectedTopic"/> is its
    /// source, where Esc returns to.
    /// </summary>
    [ObservableProperty]
    public partial RelationshipId? SelectedRelationship { get; set; }

    /// <summary>The topic link mode started from, or null outside link mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinkMode))]
    public partial TopicId? LinkSource { get; private set; }

    /// <summary>The highlighted target in link mode.</summary>
    [ObservableProperty]
    public partial TopicId? LinkTarget { get; private set; }

    public bool IsLinkMode => LinkSource is not null;

    /// <summary>What has been typed in link mode to find a target.</summary>
    private string _linkFind = string.Empty;

    /// <summary>For Follow Link's cycling: where it started, and how many links it has followed from there.</summary>
    private (TopicId Origin, TopicId Last, int Count)? _following;

    /// <summary>Set while the selection is changed together with the relationship, so one does not clear the
    /// other.</summary>
    private bool _selectingRelationship;

    /// <summary>The topic being edited in place, or null. The canvas shows its text box while this is set.</summary>
    [ObservableProperty]
    public partial TopicId? EditingTopic { get; private set; }

    /// <summary>The text the editor starts with, and where its caret goes.</summary>
    public string EditText { get; private set; } = string.Empty;

    public EditStart EditStartMode { get; private set; }

    public bool IsEditing => EditingTopic is not null;

    /// <summary>The zoom factor, shared by the canvas (two-way) and the status bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText), nameof(ZoomSlider))]
    public partial double Zoom { get; set; } = 1;

    public string ZoomText => string.Format(CultureInfo.CurrentCulture, Strings.Status_ZoomFormat, Zoom * 100);

    /// <summary>
    /// The status bar slider works on a log2 scale: equal slider distances are equal zoom ratios, so 50 % to
    /// 100 % feels the same as 100 % to 200 %.
    /// </summary>
    public double ZoomSlider
    {
        get => Math.Log2(Zoom);
        set => Zoom = Math.Clamp(Math.Pow(2, value), Canvas.Viewport.MinZoom, Canvas.Viewport.MaxZoom);
    }

    public static double ZoomSliderMinimum => Math.Log2(Canvas.Viewport.MinZoom);

    public static double ZoomSliderMaximum => Math.Log2(Canvas.Viewport.MaxZoom);

    public ICommand ZoomInCommand => Commands[CommandId.ZoomIn].Command;

    public ICommand ZoomOutCommand => Commands[CommandId.ZoomOut].Command;

    public ICommand NewMapCommand => Commands[CommandId.NewMap].Command;

    public ICommand ZoomActualSizeCommand => Commands[CommandId.ZoomActualSize].Command;

    /// <summary>The 100 % button's label, written the way the zoom level is written.</summary>
    public static string ZoomResetText => string.Format(CultureInfo.CurrentCulture, Strings.Status_ZoomFormat, 100);

    /// <summary>The 100 % button's tooltip: the command's label and shortcut.</summary>
    public string ZoomActualSizeTooltip => TooltipOf(CommandId.ZoomActualSize);

    /// <summary>A button's tooltip: the command's label and its first shortcut on this platform.</summary>
    internal string TooltipOf(CommandId id)
    {
        AppCommand command = Commands[id];
        IReadOnlyList<Avalonia.Input.KeyGesture> gestures = command.Gestures.KeyGestures(Platform);
        string label = MenuText.WithoutAccessKey(command.Label);
        return gestures.Count == 0 ? label : $"{label}  ({GestureText.Format(gestures[0], Platform)})";
    }

    /// <summary>The canvas, attached by the window: navigation, zoom steps and fitting need its layout and
    /// size.</summary>
    internal IMapView? MapView { get; set; }

    /// <summary>The status area's mode hint or the last message.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = Strings.Status_Ready;

    private EditTarget Here => new(ActiveMap.Id, SelectedTopic);

    // --- commands ----------------------------------------------------------------------------------------------------

    public bool UsesAlternateLabel(CommandId command) => command == CommandId.EncryptFile && IsEncrypted;

    public bool CanExecute(CommandId command)
    {
        bool idle = !IsEditing && !IsLinkMode;
        bool typingInPanel = NotesFocused || TitleFocused;
        bool topic = idle && !typingInPanel && SelectedTopic is not null;
        bool relationship = idle && SelectedRelationship is not null;
        bool central = SelectedTopic == ActiveMap.Root.Id;

        return command switch
        {
            CommandId.Save => true,
            CommandId.CheckForUpdate => !_checkingForUpdate,
            CommandId.EncryptFile => idle,
            CommandId.ExportPng => idle,
            CommandId.CopyMapImage => idle,
            CommandId.RemoveEncryption => idle && IsEncrypted,
            // Off in the title box, so ⌘Z from the macOS menu bar reaches its own undo; the notes editor has none and
            // runs the document's.
            CommandId.Undo => idle && !TitleFocused && _history.CanUndo,
            CommandId.Redo => idle && !TitleFocused && _history.CanRedo,
            // With several selected, the central topic among them is skipped, so the others can still go.
            CommandId.DeleteTopic or CommandId.Cut => (relationship && command == CommandId.DeleteTopic) || (topic && (!central || SelectedTopics.Count > 1)),
            CommandId.ToggleTwoWay or CommandId.DeleteRelationship => relationship,
            CommandId.RemovePriority => topic && SelectedTopicModel?.MarkerOf(MarkerGroups.Priority) is not null,
            CommandId.RemoveProgress => topic && SelectedTopicModel?.MarkerOf(MarkerGroups.Progress) is not null,
            CommandId.RemoveTopicIcon => topic && SelectedTopicModel?.MarkerOf(MarkerGroups.Icon) is not null,
            >= CommandId.TopicIconAlert and <= CommandId.TopicIconPin => topic,
            CommandId.ShowLinks => topic && SelectedTopicModel is { Links.IsEmpty: false },
            (>= CommandId.Priority1 and <= CommandId.Priority5) or (>= CommandId.Progress0 and <= CommandId.ProgressDone)
                or CommandId.ProgressForward or CommandId.ProgressBackward or CommandId.AddLink => topic,
            CommandId.MakeOneWay => relationship && CurrentRelationship?.Direction == RelationshipDirection.TwoWay,
            CommandId.MakeTwoWay or CommandId.ReverseRelationship => relationship && CurrentRelationship?.Direction == RelationshipDirection.OneWay,
            CommandId.ResetCurve => relationship && CurrentRelationship?.Bend is not null,
            >= CommandId.RelationshipColorDefault and <= CommandId.RelationshipColorPurple =>
                relationship && CurrentRelationship?.Color != (RelationshipColor)(command - CommandId.RelationshipColorDefault),
            >= CommandId.RelationshipLineSolid and <= CommandId.RelationshipLineDotted =>
                relationship && CurrentRelationship?.Line != (RelationshipLine)(command - CommandId.RelationshipLineSolid),
            CommandId.StartLinkMode or CommandId.FollowRelationship or CommandId.SelectNextRelationship => topic,
            CommandId.NewParent
                or (>= CommandId.MoveUp and <= CommandId.Demote) => topic && !central,
            CommandId.Collapse => topic && !central,
            CommandId.Copy or CommandId.Expand or CommandId.ShowNextLevel => topic,
            CommandId.Paste => idle && !typingInPanel,
            >= CommandId.ShowLevel1 and <= CommandId.ShowAllLevels => idle,
            CommandId.NewMap or CommandId.RenameMap => idle && RenamingMap is null,
            CommandId.DeleteMap or CommandId.NextMap or CommandId.PreviousMap => idle && RenamingMap is null && Document.Maps.Length > 1,
            CommandId.MoveMapLeft => idle && RenamingMap is null && Document.IndexOfMap(ActiveMap.Id) > 0,
            CommandId.MoveMapRight => idle && RenamingMap is null && Document.IndexOfMap(ActiveMap.Id) < Document.Maps.Length - 1,
            CommandId.NewSubtopic or CommandId.NewSiblingAfter or CommandId.NewSiblingBefore
                or CommandId.EditText or CommandId.OpenNotes => topic,
            CommandId.ToggleTopicPanel or CommandId.NextPane or CommandId.PreviousPane => idle,
            CommandId.ToggleMaximizePanel => idle && Panel.IsOpen,
            CommandId.MoveToOtherSide => topic && SelectedTopic is { } main && ActiveMap.Root.Children.Any(child => child.Id == main),
            CommandId.BalanceMap => idle && ActiveMap.Root.Children.Length > 1,
            CommandId.Find or CommandId.ToggleSearch => idle,
            CommandId.LightTheme => _ui.ShowsDarkTheme(),
            CommandId.DarkTheme => !_ui.ShowsDarkTheme(),
            CommandId.FindNext or CommandId.FindPrevious => idle && Search.IsOpen && Search.HasHits,
            CommandId.ToggleNotesMode => Panel.IsOpen && Panel.HasTopic,
            // A topic's title, on the map or in the title box, or the note being edited; notes have no underline in
            // Markdown, so Underline is for titles only.
            CommandId.Bold or CommandId.Italic => IsEditing || TitleFocused || (Panel.IsOpen && Panel.IsEditMode && NotesFocused),
            CommandId.Underline => IsEditing || TitleFocused,
            // With nothing selected (a click on the empty map), any navigation key selects the central topic.
            >= CommandId.SelectUp and <= CommandId.SelectPrevious => idle && !typingInPanel,
            >= CommandId.ExtendUp and <= CommandId.ExtendRight => idle && !typingInPanel && SelectedTopic is not null && SelectedRelationship is null,
            _ => true,
        };
    }

    public void Execute(CommandId command)
    {
        if (!CanExecute(command))
        {
            return;
        }

        switch (command)
        {
            case CommandId.Quit: _ui.RequestExit(); break;
            case CommandId.New: _ = RunGuarded(NewAsync); break;
            case CommandId.NewSample: _ = RunGuarded(NewSampleAsync); break;
            case CommandId.Open: _ = RunGuarded(OpenAsync); break;
            case CommandId.Save: _ = RunGuarded(() => SaveAsync(saveAs: false)); break;
            case CommandId.SaveAs: _ = RunGuarded(() => SaveAsync(saveAs: true)); break;
            case CommandId.CloseDocument: _ = RunGuarded(NewAsync); break;
            case CommandId.EncryptFile: _ = RunGuarded(EncryptAsync); break;
            case CommandId.ExportPng: _ = RunGuarded(ExportPngAsync); break;
            case CommandId.CopyMapImage: _ = RunGuarded(CopyMapImageAsync); break;
            case CommandId.ToggleFullScreen: IsFullScreen = !IsFullScreen; break;
            case CommandId.RemoveEncryption: _ = RunGuarded(RemoveEncryptionAsync); break;
            case CommandId.Undo: EndNoteBurst(); ShowStep(_history.Undo(), Strings.Status_UndoneFormat); break;
            case CommandId.Redo: EndNoteBurst(); ShowStep(_history.Redo(), Strings.Status_RedoneFormat); break;
            case CommandId.NewSubtopic: AddTopic(NewTopicPlacement.Subtopic); break;
            case CommandId.NewSiblingAfter: AddTopic(NewTopicPlacement.SiblingAfter); break;
            case CommandId.NewSiblingBefore: AddTopic(NewTopicPlacement.SiblingBefore); break;
            case CommandId.NewParent: AddParent(); break;
            case CommandId.EditText: BeginEdit(EditStart.CaretAtEnd); break;
            case CommandId.DeleteTopic when SelectedRelationship is { } selected: Apply(new DeleteRelationship(ActiveMap.Id, selected)); break;
            case CommandId.DeleteTopic when SelectedTopics.Count > 1: Apply(new DeleteTopics(ActiveMap.Id, [.. SelectedTopics], SelectedTopic!.Value)); break;
            case CommandId.DeleteTopic: Apply(new DeleteTopic(ActiveMap.Id, SelectedTopic!.Value)); break;
            case CommandId.DeleteRelationship: Apply(new DeleteRelationship(ActiveMap.Id, SelectedRelationship!.Value)); break;
            case CommandId.ToggleTwoWay: Apply(new ChangeRelationship(ActiveMap.Id, SelectedRelationship!.Value, RelationshipChange.ToggleTwoWay)); break;
            case CommandId.ReverseRelationship: Apply(new ChangeRelationship(ActiveMap.Id, SelectedRelationship!.Value, RelationshipChange.Reverse)); break;
            case CommandId.MakeOneWay: Apply(new ChangeRelationship(ActiveMap.Id, SelectedRelationship!.Value, RelationshipChange.MakeOneWay)); break;
            case CommandId.MakeTwoWay: Apply(new ChangeRelationship(ActiveMap.Id, SelectedRelationship!.Value, RelationshipChange.MakeTwoWay)); break;
            case CommandId.ResetCurve: Apply(new SetRelationshipBend(ActiveMap.Id, SelectedRelationship!.Value, null)); break;
            case >= CommandId.RelationshipColorDefault and <= CommandId.RelationshipColorPurple:
                Apply(new SetRelationshipStyle(ActiveMap.Id, SelectedRelationship!.Value, Color: (RelationshipColor)(command - CommandId.RelationshipColorDefault)));
                break;
            case >= CommandId.RelationshipLineSolid and <= CommandId.RelationshipLineDotted:
                Apply(new SetRelationshipStyle(ActiveMap.Id, SelectedRelationship!.Value, Line: (RelationshipLine)(command - CommandId.RelationshipLineSolid)));
                break;
            case >= CommandId.Priority1 and <= CommandId.Priority5:
                Apply(new SetMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Priority, MarkerGroups.PriorityValues[command - CommandId.Priority1]));
                break;
            case CommandId.RemovePriority: Apply(new SetMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Priority, null)); break;
            case >= CommandId.Progress0 and <= CommandId.ProgressDone:
                Apply(new SetMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Progress, MarkerGroups.ProgressValues[command - CommandId.Progress0]));
                break;
            case CommandId.RemoveProgress: Apply(new SetMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Progress, null)); break;
            case >= CommandId.TopicIconAlert and <= CommandId.TopicIconPin:
                Apply(new SetMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Icon, MarkerGroups.IconValues[command - CommandId.TopicIconAlert]));
                break;
            case CommandId.RemoveTopicIcon: Apply(new SetMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Icon, null)); break;
            case CommandId.ProgressForward: Apply(new CycleMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Progress, Forward: true)); break;
            case CommandId.ProgressBackward: Apply(new CycleMarker(ActiveMap.Id, SelectedTopic!.Value, MarkerGroups.Progress, Forward: false)); break;
            case CommandId.AddLink: _ = RunGuarded(() => AddLinkAsync(SelectedTopic!.Value)); break;
            case CommandId.ShowLinks: ShowLinks(SelectedTopic!.Value); break;
            case CommandId.OpenNotes: OpenNotes(edit: true); break;
            case CommandId.ToggleTopicPanel: ToggleTopicPanel(); break;
            case CommandId.MoveToOtherSide:
                ApplyAnimated(new SetMainSide(
                    ActiveMap.Id,
                    SelectedTopic!.Value,
                    MainSides.Of(ActiveMap, ActiveMap.Find(SelectedTopic!.Value)!) == MainSide.Right ? MainSide.Left : MainSide.Right));
                break;
            case CommandId.BalanceMap: ApplyAnimated(new BalanceMap(ActiveMap.Id, MainHeights())); break;
            case CommandId.Find: OpenSearch(); break;
            case CommandId.ToggleSearch when Search.IsOpen: CloseSearch(); break;
            case CommandId.ToggleSearch: OpenSearch(); break;
            case CommandId.FindNext: Search.Step(forward: true); break;
            case CommandId.FindPrevious: Search.Step(forward: false); break;
            case CommandId.ToggleMaximizePanel:
                Panel.IsMaximized = !Panel.IsMaximized;
                if (Panel.IsMaximized)
                {
                    // The canvas is hidden now, so the keyboard cannot stay there.
                    PanelView?.FocusNotes();
                }

                break;
            case CommandId.NextPane: CyclePane(forward: true); break;
            case CommandId.PreviousPane: CyclePane(forward: false); break;
            case CommandId.ToggleNotesMode: Panel.IsPreview = !Panel.IsPreview; PanelView?.FocusNotes(); break;
            case CommandId.Bold: Format(TitleStyle.Bold); break;
            case CommandId.Italic: Format(TitleStyle.Italic); break;
            case CommandId.Underline: Format(TitleStyle.Underline); break;
            case CommandId.StartLinkMode: StartLinkMode(); break;
            case CommandId.FollowRelationship: FollowRelationship(); break;
            case CommandId.SelectNextRelationship: SelectNextRelationship(); break;
            case CommandId.Cut: _ = RunGuarded(() => CopyAsync(cut: true)); break;
            case CommandId.Copy: _ = RunGuarded(() => CopyAsync(cut: false)); break;
            case CommandId.Paste: _ = RunGuarded(PasteAsync); break;
            case CommandId.MoveUp: ApplyAnimated(new ReorderTopic(ActiveMap.Id, SelectedTopic!.Value, ReorderDirection.Up)); break;
            case CommandId.MoveDown: ApplyAnimated(new ReorderTopic(ActiveMap.Id, SelectedTopic!.Value, ReorderDirection.Down)); break;
            case CommandId.MoveToFirst: ApplyAnimated(new ReorderTopic(ActiveMap.Id, SelectedTopic!.Value, ReorderDirection.First)); break;
            case CommandId.MoveToLast: ApplyAnimated(new ReorderTopic(ActiveMap.Id, SelectedTopic!.Value, ReorderDirection.Last)); break;
            case CommandId.Promote: PromoteOrDemote(outwards: false); break;
            case CommandId.Demote: PromoteOrDemote(outwards: true); break;
            case CommandId.Collapse when Unreveal(SelectedTopic!.Value): break;
            case CommandId.Collapse: ApplyAnimated(new SetCollapsed(ActiveMap.Id, SelectedTopic!.Value, true)); break;
            case CommandId.Expand: ApplyAnimated(new SetCollapsed(ActiveMap.Id, SelectedTopic!.Value, false)); break;
            case CommandId.ShowNextLevel: ApplyAnimated(new ShowNextLevel(ActiveMap.Id, SelectedTopic!.Value)); break;
            case >= CommandId.ShowLevel1 and <= CommandId.ShowLevel9:
                ApplyAnimated(new ShowLevels(ActiveMap.Id, command - CommandId.ShowLevel1 + 1, SelectedTopic));
                break;
            case CommandId.ShowAllLevels: ApplyAnimated(new ShowLevels(ActiveMap.Id, int.MaxValue, SelectedTopic)); break;
            case CommandId.NewMap:
                Apply(new AddMap(RandomIdSource.Instance.NextMap(), RandomIdSource.Instance.NextTopic(), NextCentralText()));
                break;
            case CommandId.RenameMap: BeginRenameMap(ActiveMap.Id); break;
            case CommandId.DeleteMap: _ = RunGuarded(DeleteMapAsync); break;
            case CommandId.MoveMapLeft: Apply(new MoveMap(ActiveMap.Id, Document.IndexOfMap(ActiveMap.Id) - 1)); break;
            case CommandId.MoveMapRight: Apply(new MoveMap(ActiveMap.Id, Document.IndexOfMap(ActiveMap.Id) + 1)); break;
            case CommandId.NextMap: SwitchMap(+1); break;
            case CommandId.PreviousMap: SwitchMap(-1); break;
            case CommandId.SelectUp: Navigate(NavigationMove.Up); break;
            case CommandId.SelectDown: Navigate(NavigationMove.Down); break;
            case CommandId.SelectLeft: Navigate(NavigationMove.Left); break;
            case CommandId.SelectRight: Navigate(NavigationMove.Right); break;
            case CommandId.ExtendUp: ExtendSelection(NavigationMove.Up); break;
            case CommandId.ExtendDown: ExtendSelection(NavigationMove.Down); break;
            case CommandId.ExtendLeft: ExtendSelection(NavigationMove.Left); break;
            case CommandId.ExtendRight: ExtendSelection(NavigationMove.Right); break;
            case CommandId.SelectFirstSibling: Navigate(NavigationMove.FirstSibling); break;
            case CommandId.SelectLastSibling: Navigate(NavigationMove.LastSibling); break;
            case CommandId.SelectCentral: Navigate(NavigationMove.Central); break;
            case CommandId.SelectParent: Navigate(NavigationMove.Parent); break;
            case CommandId.SelectNext: Navigate(NavigationMove.Next); break;
            case CommandId.SelectPrevious: Navigate(NavigationMove.Previous); break;
            case CommandId.ZoomIn: MapView?.ZoomStep(zoomIn: true); break;
            case CommandId.ZoomOut: MapView?.ZoomStep(zoomIn: false); break;
            case CommandId.ZoomActualSize: MapView?.ZoomTo(1); break;
            case CommandId.ZoomFit: MapView?.FitToWindow(); break;
            case CommandId.KeyboardShortcuts: _ = RunGuarded(() => _ui.ShowShortcutsAsync(BuildShortcutRows())); break;
            case CommandId.PrintShortcuts: PrintShortcuts(); break;
            case CommandId.PrintMarkdownHelp: PrintMarkdownHelp(); break;
            case CommandId.LightTheme: _ui.UseTheme(dark: false); Commands.RefreshAll(); break;
            case CommandId.DarkTheme: _ui.UseTheme(dark: true); Commands.RefreshAll(); break;
            case CommandId.CheckForUpdate: _ = RunGuarded(CheckForUpdateOnRequestAsync); break;
            case CommandId.About: _ = RunGuarded(ShowAboutAsync); break;
        }
    }

    /// <summary>Runs a document command through the history and shows the result. The one way the document
    /// changes.</summary>
    internal CommandResult Apply(IDocumentCommand command)
    {
        CommandResult result = _history.Execute(command, Here);

        // Showing levels sets the whole map's flags anew: they are the ones to show again, and search's openings end.
        if (result.IsApplied && command is ShowLevels or ShowNextLevel)
        {
            _revealed.Remove(result.Target!.Value.Map);
        }

        if (result.IsApplied)
        {
            Show(result.After!, result.Target!.Value);
            StatusText = SelectedRelationship is null ? Strings.Status_Ready : Strings.Status_RelationshipSelected;
        }
        else if (RefusalText(result.Refusal) is { } message)
        {
            StatusText = message;
        }

        Commands.RefreshAll();
        return result;
    }

    /// <summary>
    /// A structure change the canvas animates from the old layout to the new one (expand,
    /// collapse, the settle after a drop). Typing is never animated.
    /// </summary>
    internal CommandResult ApplyAnimated(IDocumentCommand command)
    {
        MapView?.AnimateNextLayout();
        return Apply(command);
    }

    // --- structure ---------------------------------------------------------------------------------------------------

    /// <summary>A click on a topic's collapse handle: toggles it, without moving the selection.</summary>
    public void ToggleCollapsed(TopicId topic)
    {
        if (IsEditing || DisplayMap.Find(topic) is not { HasChildren: true } current)
        {
            return;
        }

        TopicId? selection = SelectedTopic;
        if (!Unreveal(topic))
        {
            ApplyAnimated(new SetCollapsed(ActiveMap.Id, topic, !current.IsCollapsed));
        }

        // Keep the selection unless it just disappeared into the collapsed branch.
        if (selection is { } kept && ActiveMap.Index.Locate(kept) is not null && !IsHidden(kept))
        {
            SelectedTopic = kept;
        }
    }

    /// <summary>A drop from drag and drop: one undo step, settled with an animation.</summary>
    /// <param name="side">For a drop that makes or keeps a main topic: the side it was dropped on.</param>
    public void MoveByDrag(TopicId topic, TopicId newParent, int index, MainSide? side = null)
    {
        if (IsEditing)
        {
            return;
        }

        // A topic of a multi-selection takes the others along.
        ApplyAnimated(SelectedTopics.Count > 1 && SelectedTopics.Contains(topic)
            ? new MoveTopics(ActiveMap.Id, [.. SelectedTopics], topic, newParent, index, side)
            : new MoveTopic(ActiveMap.Id, topic, newParent, index, Side: side));
    }

    // --- the sides of main topics ------------------------------------------------------------------------------------

    /// <summary>
    /// How tall each main topic's branch is as laid out: what Balance Map and "the shorter side" go by. Collapsed
    /// branches count as they show.
    /// </summary>
    private Dictionary<TopicId, double> MainHeights()
    {
        var extents = new Dictionary<TopicId, (double Top, double Bottom)>();
        if (MapView?.Layout is not { } layout)
        {
            return [];
        }

        foreach (TopicBox box in layout.Boxes)
        {
            if (box.Depth == 0)
            {
                continue;
            }

            TopicBox main = box;
            while (main.Depth > 1 && main.Parent is { } parent && layout.Find(parent) is { } up)
            {
                main = up;
            }

            extents[main.Id] = extents.TryGetValue(main.Id, out var e)
                ? (Math.Min(e.Top, box.Bounds.Y), Math.Max(e.Bottom, box.Bounds.Bottom))
                : (box.Bounds.Y, box.Bounds.Bottom);
        }

        return extents.ToDictionary(pair => pair.Key, pair => pair.Value.Bottom - pair.Value.Top);
    }

    /// <summary>The side whose main topics take less height: where a new main topic goes.</summary>
    private MainSide ShorterSide()
    {
        Dictionary<TopicId, double> heights = MainHeights();
        double right = 0, left = 0;
        foreach (Topic main in ActiveMap.Root.Children)
        {
            double height = heights.TryGetValue(main.Id, out double h) ? h : 0;
            if (MainSides.Of(ActiveMap, main) == MainSide.Right)
            {
                right += height;
            }
            else
            {
                left += height;
            }
        }

        return right <= left ? MainSide.Right : MainSide.Left;
    }

    /// <summary>
    /// Promote and demote, side-aware: Alt+Ctrl+Right demotes on the right half of the map, where
    /// children are to the right; on the left half children are to the left, so the keys swap meaning.
    /// </summary>
    private void PromoteOrDemote(bool outwards)
    {
        TopicId topic = SelectedTopic!.Value;
        bool leftSide = MapView?.Layout?.Find(topic)?.Side == Side.Left;
        bool demote = outwards != leftSide;
        ApplyAnimated(demote ? new DemoteTopic(ActiveMap.Id, topic) : new PromoteTopic(ActiveMap.Id, topic));
    }

    private bool IsHidden(TopicId topic) =>
        DisplayMap.Index.PathTo(topic).SkipLast(1).Any(ancestor => DisplayMap.Find(ancestor)!.IsCollapsed);

    // --- search ------------------------------------------------------------------------------------------------------

    /// <summary>Ctrl/⌘+F: opens the search panel with the keyboard in its box, the text selected.</summary>
    private void OpenSearch()
    {
        Search.IsOpen = true;
        Search.Refresh();
        PanelView?.FocusSearch();
        SearchResultsChanged();
        Commands.RefreshAll();
    }

    /// <summary>
    /// Esc or the close button: the panel closes, the keyboard goes back to the map, the selected hit stays selected,
    /// and the branches search opened close again, except the one holding the selection.
    /// </summary>
    internal void CloseSearch()
    {
        Search.IsOpen = false;
        foreach (MapId map in _revealed.Keys.ToList())
        {
            _revealed[map] = map == ActiveMap.Id && SelectedTopic is { } selected
                ? [.. _revealed[map].Intersect(ActiveMap.Index.PathTo(selected))]
                : [];
        }

        MapView?.AnimateNextLayout();
        OnPropertyChanged(nameof(DisplayMap));
        SearchResultsChanged();
        PanelView?.FocusCanvas();
        Commands.RefreshAll();
    }

    /// <summary>The results changed: the canvas highlights and the next and previous commands follow.</summary>
    internal void SearchResultsChanged()
    {
        SearchHighlights = Search.IsOpen && Search.HasHits
            ? Search.Hits.Where(hit => hit.Map == ActiveMap.Id).Select(hit => hit.Topic).ToHashSet()
            : null;
        Commands.RefreshAll();
    }

    /// <summary>
    /// Goes to a hit: its map's tab, the collapsed branches above it opened for the eye, the topic selected and
    /// scrolled into view; for a note, the detail view with the match selected in the editor. The
    /// keyboard stays in the search panel, so Enter and F3 go on stepping through the hits.
    /// </summary>
    internal void GoToHit(SearchHit hit)
    {
        if (Document.FindMap(hit.Map) is not { } map || !map.Contains(hit.Topic) || IsEditing || IsLinkMode)
        {
            return;
        }

        ActivateMap(hit.Map);
        var opened = Reveal.CollapsedAncestors(ActiveMap, hit.Topic);
        if (!opened.IsEmpty)
        {
            var revealed = _revealed.TryGetValue(hit.Map, out var known) ? known : [];
            _revealed[hit.Map] = revealed.Union(opened);
            MapView?.AnimateNextLayout();
            OnPropertyChanged(nameof(DisplayMap));
        }

        SelectedTopic = hit.Topic;

        if (hit.Field == HitField.Note)
        {
            Panel.IsOpen = true;
            Panel.IsPreview = false;
            PanelView?.ShowNoteMatch(hit.Start, hit.Length);
        }
        else if (hit.Field is HitField.LinkTitle or HitField.LinkUrl && Panel.IsOpen && Panel.Links is { } links)
        {
            links.SelectedIndex = hit.LinkIndex;
        }

        SearchResultsChanged();
    }

    /// <summary>
    /// Collapsing a branch search opened only takes the opening back: the document has it collapsed already.
    /// </summary>
    /// <returns>True when <paramref name="topic"/> was one search opened.</returns>
    private bool Unreveal(TopicId topic)
    {
        if (!_revealed.TryGetValue(ActiveMap.Id, out var revealed) || !revealed.Contains(topic))
        {
            return false;
        }

        _revealed[ActiveMap.Id] = revealed.Remove(topic);
        MapView?.AnimateNextLayout();
        OnPropertyChanged(nameof(DisplayMap));
        if (SelectedTopic is { } selected && IsHidden(selected))
        {
            SelectedTopic = topic;
        }

        return true;
    }

    /// <summary>
    /// Copy puts the private format and a plain-text outline on the clipboard; cut also deletes
    /// the subtree, as one undo step.
    /// </summary>
    private async Task CopyAsync(bool cut)
    {
        TopicId topic = SelectedTopic!.Value;

        // Several selected topics are copied, and cut, together; pasted, they come back side by side.
        bool several = SelectedTopics.Count > 1;
        if ((several ? Clip.Copy(ActiveMap, SelectedTopics, withCentral: !cut) : Clip.Copy(ActiveMap, topic)) is not { } clip)
        {
            return;
        }

        // Cut is copy, then delete: with nothing copied, nothing is deleted.
        if (!await _ui.SetClipboardAsync(ClipFormat.Write(clip), OutlineText.Write(clip.Roots)).ConfigureAwait(true))
        {
            StatusText = Strings.Status_NoClipboard;
            return;
        }

        if (cut)
        {
            ApplyAnimated(several ? new DeleteTopics(ActiveMap.Id, [.. SelectedTopics], topic) : new DeleteTopic(ActiveMap.Id, topic));
        }
    }

    /// <summary>
    /// Pastes as the last subtopics of the selection (the central topic when nothing is selected). MindBuzz's own
    /// format keeps everything; plain text from other apps becomes topics, one per line, by indentation.
    /// </summary>
    private async Task PasteAsync()
    {
        ClipboardContent content = await _ui.GetClipboardAsync().ConfigureAwait(true);
        var problems = new List<LoadProblem>();
        Clip? clip = ClipFormat.Read(content.Clip, problems);
        if (clip is null && !string.IsNullOrWhiteSpace(content.Text))
        {
            var roots = OutlineText.Read(content.Text, RandomIdSource.Instance);
            clip = roots.IsEmpty ? null : new Clip(roots, []);
        }

        if (clip is null || IsEditing)
        {
            return;
        }

        TopicId parent = SelectedTopic ?? ActiveMap.Root.Id;
        ApplyAnimated(new PasteClip(ActiveMap.Id, parent, clip.WithFreshIds(RandomIdSource.Instance), parent == ActiveMap.Root.Id ? ShorterSide() : null));

        // Never silently: the clipboard still holds the original, so a note in the status bar is enough.
        if (problems.Count > 0)
        {
            StatusText = Strings.Status_PastePartlyRead;
        }
    }

    // --- editing -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Starts editing the selected topic in place. <paramref name="typed"/> is the character that started a
    /// <see cref="EditStart.Replace"/> session; it replaces the text in the editor at once.
    /// </summary>
    public void BeginEdit(EditStart start, string? typed = null)
    {
        if (IsEditing || SelectedTopic is not { } topic || ActiveMap.Find(topic) is not { } current)
        {
            return;
        }

        StartSession(addedTopic: false);
        EditStartMode = start;
        EditText = start == EditStart.Replace ? typed ?? string.Empty : current.Text;
        EditingTopic = topic;
        Commands.RefreshAll();
    }

    /// <summary>
    /// Live text while typing. It stays in the session, not in the document: the map is laid out again only when the
    /// edit ends, so it does not shift under the text being typed. The editor grows meanwhile.
    /// </summary>
    public void UpdateEditText(string text)
    {
        if (IsEditing)
        {
            EditText = text;
        }
    }

    /// <summary>
    /// Ends the session and keeps the text. Empty text deletes a topic the session added, and is refused on an
    /// existing topic, which keeps its previous text.
    /// </summary>
    public void CommitEdit(string text)
    {
        if (!IsEditing)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TitleMarkup.Plain(text)))
        {
            bool added = _sessionAddedTopic;
            AbandonSession();
            if (!added)
            {
                StatusText = Strings.Refusal_EmptyText;
            }

            return;
        }

        // Leading and trailing spaces and empty lines are trimmed when the text is kept: they were never meant to
        // be part of the topic. Line breaks inside the text stay. A topic the session added joins this step.
        if (EditingTopic is { } topic
            && _history.Execute(new SetTopicText(ActiveMap.Id, topic, text.Trim(), _session), Here) is { IsApplied: true } result)
        {
            // The neighbours make room for the new size now, eased like any other structure change.
            MapView?.AnimateNextLayout();
            Show(result.After!, result.Target!.Value);
        }

        _history.Seal();
        EndSession();
    }

    /// <summary>Esc: ends the session and puts everything back as it was before it (a new topic disappears).</summary>
    public void CancelEdit()
    {
        if (IsEditing)
        {
            AbandonSession();
        }
    }

    /// <summary>Bold, italic or underline where the keyboard is: the title on the map, the title box, or the
    /// note.</summary>
    private void Format(TitleStyle style)
    {
        if (IsEditing)
        {
            MapView?.ToggleTitleStyle(style);
        }
        else if (TitleFocused)
        {
            PanelView?.ToggleTitleStyle(style);
        }
        else if (style != TitleStyle.Underline)
        {
            PanelView?.WrapSelection(TitleMarkup.Marker(style));
        }
    }

    /// <summary>A click on a topic, or on the empty canvas (null).</summary>
    public void Select(TopicId? topic)
    {
        if (!IsEditing)
        {
            SelectedTopic = topic;
        }
    }

    partial void OnSelectedTopicChanged(TopicId? value)
    {
        if (!_selectingRelationship)
        {
            SelectedRelationship = null;
        }

        // A plain selection (click, arrow, search, undo) is one topic again.
        if (!_keepingSelection)
        {
            SelectedTopics = value is { } one ? [one] : [];
        }

        Panel?.Refresh();

        Commands?.RefreshAll();
    }

    /// <summary>
    /// A relationship was selected (a click, or the canvas): the selection moves to its source, where Esc returns
    /// to, and the status area says what can be done with it.
    /// </summary>
    partial void OnSelectedRelationshipChanged(RelationshipId? value)
    {
        if (value is { } id && ActiveMap.Relationships.FirstOrDefault(r => r.Id == id) is { } relationship)
        {
            _selectingRelationship = true;
            SelectedTopic = relationship.From;
            _selectingRelationship = false;
            StatusText = Strings.Status_RelationshipSelected;
        }
        else if (StatusText == Strings.Status_RelationshipSelected)
        {
            StatusText = Strings.Status_Ready;
        }

        Commands?.RefreshAll();
    }

    // --- markers and internet links ----------------------------------------------------------------------------------

    private Topic? SelectedTopicModel => SelectedTopic is { } id ? ActiveMap.Find(id) : null;

    /// <summary>
    /// Ctrl+K: the link dialog, with the address prefilled when the clipboard holds one that is allowed. The topic
    /// is the one selected when the command started, even if the dialog takes a while.
    /// </summary>
    private async Task AddLinkAsync(TopicId topic)
    {
        MapId map = ActiveMap.Id;
        InternetLink? link = await AskForLinkAsync(null, adding: true).ConfigureAwait(true);
        if (link is not null)
        {
            Apply(new AddLink(map, topic, link));
        }
    }

    /// <summary>The link dialog, for a new link (prefilled from the clipboard) or to edit <paramref
    /// name="link"/>.</summary>
    internal async Task<InternetLink?> AskForLinkAsync(InternetLink? link, bool adding)
    {
        string url = link?.Url ?? string.Empty;
        if (adding)
        {
            ClipboardContent clipboard = await _ui.GetClipboardAsync().ConfigureAwait(true);
            string? text = clipboard.Text?.Trim();
            url = LinkUrl.IsAllowed(text) ? text! : string.Empty;
        }

        return await _ui.EditLinkAsync(new LinkEditRequest(
            adding ? Strings.LinkEdit_AddTitle : Strings.LinkEdit_EditTitle,
            url,
            link?.Title ?? string.Empty,
            adding ? Strings.LinkEdit_Add : Strings.LinkEdit_Ok)).ConfigureAwait(true);
    }

    /// <summary>
    /// Ctrl+Shift+K, the link indicator, or the menu: the detail view opens with the keyboard in the topic's links.
    /// </summary>
    private void ShowLinks(TopicId topic)
    {
        SelectedTopic = topic;
        Panel.IsOpen = true;
        PanelView?.FocusLinks();
        Commands.RefreshAll();
    }

    /// <summary>A change made in the link list: applied like any other, without leaving the list.</summary>
    internal void ApplyFromList(IDocumentCommand command) => Apply(command);

    /// <summary>Hands an address to the browser or mail program; says so when it could not.</summary>
    internal void OpenUrl(string url)
    {
        if (!_shell.OpenUrl(url))
        {
            StatusText = Strings.Status_LinkNotOpened;
        }
    }

    /// <summary>
    /// A click on a topic's link indicator: the link list, or with Ctrl (⌘ on macOS) and exactly one link, that
    /// link at once.
    /// </summary>
    public void LinkIndicatorClicked(TopicId topic, bool openDirectly)
    {
        if (ActiveMap.Find(topic) is not { Links.IsEmpty: false } model || IsEditing || IsLinkMode)
        {
            return;
        }

        SelectedTopic = topic;
        if (openDirectly && model.Links.Length == 1)
        {
            OpenUrl(model.Links[0].Url);
            return;
        }

        ShowLinks(topic);
    }

    /// <summary>A click on the note indicator: the topic panel opens on the note.</summary>
    public void NoteIndicatorClicked(TopicId topic)
    {
        if (IsEditing || IsLinkMode || !ActiveMap.Contains(topic))
        {
            return;
        }

        SelectedTopic = topic;
        OpenNotes();
    }

    // --- the topic panel and notes -----------------------------------------------------------------------------------

    /// <summary>
    /// Opens the panel and puts the keyboard in the notes section. With <paramref name="edit"/>, as for "Open Detail
    /// View", always in the editor with the caret at the end, ready to type; otherwise in the
    /// note's default mode.
    /// </summary>
    internal void OpenNotes(bool edit = false)
    {
        Panel.IsOpen = true;
        if (edit)
        {
            Panel.IsPreview = false;
        }

        PanelView?.FocusNotes(caretAtEnd: edit);
        Commands.RefreshAll();
    }

    /// <summary>Ctrl+T: opens the panel on the notes, or closes it and gives the keyboard back to the map.</summary>
    private void ToggleTopicPanel()
    {
        if (Panel.IsOpen)
        {
            Panel.IsOpen = false;
            PanelView?.FocusCanvas();
            Commands.RefreshAll();
        }
        else
        {
            OpenNotes();
        }
    }

    /// <summary>F6 and Shift+F6: between the canvas and the panel, opening the panel if needed.</summary>
    /// <summary>F6: the window knows the panes; it opens a closed detail view when the cycle reaches it.</summary>
    private void CyclePane(bool forward) => PanelView?.CyclePane(forward);

    /// <summary>
    /// The notes editor's text changed: applies the keystroke's note; false when it was refused, such as a note grown
    /// too long. Keystrokes less than <see cref="NoteBurstPause"/> apart become one undo step, which undoes to the
    /// note as it was before the burst.
    /// </summary>
    internal bool UpdateNote(MapId map, TopicId topic, string note)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_noteBurst is null || now - _lastNoteEdit > NoteBurstPause)
        {
            _noteBurst = new object();
        }

        _lastNoteEdit = now;
        return Apply(new SetNote(map, topic, note, _noteBurst)).IsApplied;
    }

    /// <summary>Ends the current burst of note typing: the next keystroke starts a new undo step.</summary>
    internal void EndNoteBurst() => _noteBurst = null;

    internal void SetNotesFocused(bool focused)
    {
        if (NotesFocused == focused)
        {
            return;
        }

        NotesFocused = focused;
        if (!focused)
        {
            EndNoteBurst();
        }

        Commands.RefreshAll();
    }

    internal void SetTitleFocused(bool focused)
    {
        if (TitleFocused != focused)
        {
            TitleFocused = focused;
            Commands.RefreshAll();
        }
    }

    // --- relationships -----------------------------------------------------------------------------------------------

    /// <summary>The selected relationship itself, or null.</summary>
    private Relationship? CurrentRelationship =>
        SelectedRelationship is { } id ? ActiveMap.Relationships.FirstOrDefault(r => r.Id == id) : null;

    /// <summary>The relationship's handle was dragged and let go: the new shape, as one undo step.</summary>
    public void BendRelationship(RelationshipId relationship, RelationshipBend bend) =>
        Apply(new SetRelationshipBend(ActiveMap.Id, relationship, bend));

    /// <summary>Esc on a selected relationship: back to its source topic.</summary>
    public void LeaveRelationship() => SelectedRelationship = null;

    private void StartLinkMode()
    {
        LinkSource = SelectedTopic;
        LinkTarget = SelectedTopic;
        _linkFind = string.Empty;
        ShowLinkStatus();
        Commands.RefreshAll();
    }

    /// <summary>Arrow keys in link mode move the target highlight, with the same rules as the selection.</summary>
    public void LinkMove(NavigationMove move)
    {
        if (LinkTarget is { } from && MapView?.Layout is { } layout && Navigator.Move(layout, DisplayMap, from, move) is { } target)
        {
            LinkTarget = target;
            _linkFind = string.Empty;
            ShowLinkStatus();
        }
    }

    /// <summary>
    /// Typing in link mode finds a target by its text (type-to-find): the next visible topic, in
    /// outline order after the current target, whose text contains what was typed, ignoring case.
    /// </summary>
    public void LinkType(string text)
    {
        _linkFind += text;
        FindLinkTarget(fromNext: false);
        ShowLinkStatus();
    }

    public void LinkBackspace()
    {
        if (_linkFind.Length > 0)
        {
            _linkFind = _linkFind[..^1];
            ShowLinkStatus();
        }
    }

    /// <summary>
    /// Enter (one-way) or Shift+Enter (two-way). A link to the topic itself or a second link between the same two
    /// topics is refused with a short message, and link mode stays on so another target can be picked.
    /// </summary>
    public void LinkCommit(bool twoWay)
    {
        if (LinkSource is not { } source || LinkTarget is not { } target)
        {
            return;
        }

        CommandResult result = _history.Execute(
            new AddRelationship(ActiveMap.Id, RandomIdSource.Instance.NextRelationship(), source, target, twoWay ? RelationshipDirection.TwoWay : RelationshipDirection.OneWay),
            Here);

        if (!result.IsApplied)
        {
            StatusText = RefusalText(result.Refusal) ?? StatusText;
            return;
        }

        EndLinkMode();
        Show(result.After!, result.Target!.Value);
        Commands.RefreshAll();
    }

    /// <summary>A click on a target in link mode: one-way, or two-way with Shift.</summary>
    public void LinkPick(TopicId target, bool twoWay)
    {
        if (IsLinkMode)
        {
            LinkTarget = target;
            LinkCommit(twoWay);
        }
    }

    public void LinkCancel()
    {
        EndLinkMode();
        StatusText = Strings.Status_Ready;
        Commands.RefreshAll();
    }

    private void EndLinkMode()
    {
        LinkSource = null;
        LinkTarget = null;
        _linkFind = string.Empty;
    }

    private void ShowLinkStatus() => StatusText = string.Format(
        CultureInfo.CurrentCulture,
        Strings.Status_LinkModeFormat,
        _linkFind.Length == 0 ? string.Empty : string.Format(CultureInfo.CurrentCulture, Strings.Status_LinkFindFormat, _linkFind));

    private void FindLinkTarget(bool fromNext)
    {
        if (MapView?.Layout is not { } layout || _linkFind.Length == 0)
        {
            return;
        }

        List<Topic> visible = [.. ActiveMap.Root.DescendantsAndSelf().Where(topic => layout.Find(topic.Id) is not null)];
        int start = LinkTarget is { } current ? visible.FindIndex(topic => topic.Id == current) + (fromNext ? 1 : 0) : 0;
        CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;

        for (int i = 0; i < visible.Count; i++)
        {
            Topic candidate = visible[(Math.Max(0, start) + i) % visible.Count];
            if (compare.IndexOf(TitleMarkup.Plain(candidate.Text), _linkFind, CompareOptions.IgnoreCase) >= 0)
            {
                LinkTarget = candidate.Id;
                return;
            }
        }
    }

    /// <summary>
    /// Ctrl/⌘+J: jumps to the topic at the other end of a relationship. Pressed again it jumps back, and back on the
    /// topic it started from it takes that topic's next relationship: there and back through all of them. A hidden end
    /// selects its nearest visible ancestor.
    /// </summary>
    private void FollowRelationship()
    {
        TopicId here = SelectedTopic!.Value;
        if (_following is { } following && following.Last == here && here != following.Origin)
        {
            SelectedTopic = following.Origin;
            _following = (following.Origin, following.Origin, following.Count);
            return;
        }

        (TopicId origin, int count) = _following is { } back && back.Last == here
            ? (back.Origin, back.Count + 1)
            : (here, 0);

        List<Relationship> links = [.. ActiveMap.Relationships.Where(r => r.Touches(origin))];
        if (links.Count == 0)
        {
            StatusText = Strings.Status_NoRelationships;
            return;
        }

        Relationship link = links[count % links.Count];
        TopicId other = link.From == origin ? link.To : link.From;
        TopicId target = MapView?.Layout is { } layout
            ? ActiveMap.Index.PathTo(other).Reverse().FirstOrDefault(id => layout.Find(id) is not null, other)
            : other;

        SelectedTopic = target;
        _following = (origin, target, count);
    }

    /// <summary>Ctrl+Shift+J: selects the topic's relationships one after another.</summary>
    private void SelectNextRelationship()
    {
        TopicId topic = SelectedTopic!.Value;
        List<Relationship> links = [.. ActiveMap.Relationships.Where(r => r.Touches(topic))];
        if (links.Count == 0)
        {
            StatusText = Strings.Status_NoRelationships;
            return;
        }

        int current = SelectedRelationship is { } selected ? links.FindIndex(r => r.Id == selected) : -1;
        Relationship next = links[(current + 1) % links.Count];

        // Keep the topic it was asked from as the place Esc returns to, not the relationship's source.
        SelectedRelationship = next.Id;
        _selectingRelationship = true;
        SelectedTopic = topic;
        _selectingRelationship = false;
    }

    partial void OnDocumentChanged(Document value)
    {
        OnPropertyChanged(nameof(Tabs));
        Search?.DocumentChanged();
    }

    /// <summary>
    /// Another map on screen: search in the current map follows it, and the highlights too. Only for another map: an
    /// edit replaces the map with a new version of itself, and <see cref="Show"/> refreshes the search once for that;
    /// refreshing here too ran the whole search twice per keystroke.
    /// </summary>
    partial void OnActiveMapChanged(Map oldValue, Map newValue)
    {
        if (oldValue?.Id == newValue.Id)
        {
            return;
        }

        if (Search is { IsOpen: true, AllMaps: false })
        {
            Search.Refresh();
        }
        else
        {
            Search?.SearchResultsChangedIfOpen();
        }
    }

    /// <summary>Leaving a map remembers its selection, for when it is shown again.</summary>
    partial void OnActiveMapChanging(Map oldValue, Map newValue)
    {
        if (oldValue is not null && oldValue.Id != newValue.Id && SelectedTopic is { } selected)
        {
            _selectionByMap[oldValue.Id] = selected;
        }
    }

    // --- maps, the sheet tabs ----------------------------------------------------------------------------------------

    /// <summary>Shows a map, with the selection it had when it was last shown.</summary>
    public void ActivateMap(MapId id)
    {
        if (id == ActiveMap.Id || Document.FindMap(id) is not { } map)
        {
            return;
        }

        if (IsEditing)
        {
            CommitEdit(EditText);
        }

        ActiveMap = map;
        SelectedTopic = _selectionByMap.TryGetValue(id, out TopicId kept) && map.Contains(kept) ? kept : map.Root.Id;
        Commands.RefreshAll();
    }

    /// <summary>Starts renaming a tab in place: double-click, context menu, or Ctrl+Shift+F2.</summary>
    public void BeginRenameMap(MapId id)
    {
        ActivateMap(id);
        if (!IsEditing)
        {
            RenamingMap = id;
            Commands.RefreshAll();
        }
    }

    /// <summary>
    /// Enter, or focus leaving the tab's text box: the central topic gets the new text. An empty
    /// name is refused and the old one kept. The box shows the text on one line, so an unchanged commit leaves a
    /// multi-line central topic as it is.
    /// </summary>
    public void CommitRenameMap(string name)
    {
        if (RenamingMap is not { } id)
        {
            return;
        }

        RenamingMap = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = Strings.Refusal_EmptyName;
        }
        else if (Document.FindMap(id) is { } map && name.Trim() != MapName.OneLine(map.Root.Text))
        {
            Apply(new RenameMap(id, name.Trim()));
        }

        Commands.RefreshAll();
    }

    /// <summary>Esc while renaming: the name stays as it was.</summary>
    public void CancelRenameMap()
    {
        RenamingMap = null;
        Commands.RefreshAll();
    }

    /// <summary>A tab dragged to a new position.</summary>
    public void MoveMapTo(MapId id, int index)
    {
        if (!IsEditing)
        {
            Apply(new MoveMap(id, index));
        }
    }

    /// <summary>
    /// A new map's central topic, which is also its tab's name: "Central Topic", or "Central Topic 2" and so on when
    /// that is taken, so the tabs can be told apart.
    /// </summary>
    private string NextCentralText()
    {
        HashSet<string> taken = [.. Document.Maps.Select(map => map.Root.Text)];
        if (!taken.Contains(Strings.Topic_DefaultCentral))
        {
            return Strings.Topic_DefaultCentral;
        }

        for (int number = 2; ; number++)
        {
            string text = string.Format(CultureInfo.CurrentCulture, Strings.Topic_DefaultCentralNumberedFormat, Strings.Topic_DefaultCentral, number);
            if (!taken.Contains(text))
            {
                return text;
            }
        }
    }

    private void SwitchMap(int step)
    {
        int count = Document.Maps.Length;
        int index = Document.IndexOfMap(ActiveMap.Id);
        ActivateMap(Document.Maps[(index + step + count) % count].Id);
    }

    /// <summary>Delete asks first, although it can be undone: a whole map is a lot to lose by a slip.</summary>
    private async Task DeleteMapAsync()
    {
        Map map = ActiveMap;
        int answer = await _ui.AskAsync(new ChoiceRequest(
            Strings.DeleteMap_Title,
            string.Format(CultureInfo.CurrentCulture, Strings.DeleteMap_MessageFormat, map.Name),
            Strings.DeleteMap_Detail,
            [Strings.Answer_Cancel, Strings.Answer_Delete],
            DefaultAnswer: 1,
            CancelAnswer: 0)).ConfigureAwait(true);

        if (answer == 1)
        {
            Apply(new DeleteMap(map.Id));
        }
    }

    private void AddTopic(NewTopicPlacement placement)
    {
        TopicId reference = SelectedTopic!.Value;

        // The central topic has no siblings: Enter on it adds a main topic.
        if (reference == ActiveMap.Root.Id)
        {
            placement = NewTopicPlacement.Subtopic;
        }

        StartSession(addedTopic: true);
        TopicId id = RandomIdSource.Instance.NextTopic();
        MainSide? side = reference == ActiveMap.Root.Id ? ShorterSide() : null;
        if (_history.Execute(new AddTopic(ActiveMap.Id, reference, placement, id, string.Empty, _session, side), Here) is { IsApplied: true } result)
        {
            Show(result.After!, result.Target!.Value);
            OpenEditor(id);
        }
        else
        {
            EndSession();
        }
    }

    private void AddParent()
    {
        StartSession(addedTopic: true);
        TopicId id = RandomIdSource.Instance.NextTopic();
        if (_history.Execute(new InsertParent(ActiveMap.Id, SelectedTopic!.Value, id, string.Empty, _session), Here) is { IsApplied: true } result)
        {
            Show(result.After!, result.Target!.Value);
            OpenEditor(id);
        }
        else
        {
            EndSession();
        }
    }

    private void OpenEditor(TopicId topic)
    {
        EditStartMode = EditStart.CaretAtEnd;
        EditText = string.Empty;
        EditingTopic = topic;
        Commands.RefreshAll();
    }

    private void StartSession(bool addedTopic)
    {
        _history.Seal();
        _session = new object();
        _sessionAddedTopic = addedTopic;
        _selectionBeforeSession = SelectedTopic;
    }

    private void AbandonSession()
    {
        if (_session is not null && _history.Abandon(_session) is { } before)
        {
            Show(before, new EditTarget(ActiveMap.Id, _selectionBeforeSession));
        }

        EndSession();
    }

    private void EndSession()
    {
        _session = null;
        _sessionAddedTopic = false;
        EditingTopic = null;
        Commands.RefreshAll();
    }

    // --- multi-select ------------------------------------------------------------------------------------------------

    /// <summary>⌘-click (Ctrl-click on Windows and Linux): adds the topic to the selection, or takes it out.</summary>
    public void ToggleSelected(TopicId topic)
    {
        if (IsEditing || IsLinkMode || !ActiveMap.Contains(topic))
        {
            return;
        }

        var topics = SelectedTopics.ToList();
        if (!topics.Remove(topic))
        {
            topics.Add(topic);
        }

        // The last topic stays selected: a selection is never emptied by ⌘-click.
        if (topics.Count > 0)
        {
            SetSelection(topics);
        }
    }

    /// <summary>A plain click on one topic of several selected: that one alone.</summary>
    public void SelectOnly(TopicId topic)
    {
        if (!IsEditing && ActiveMap.Contains(topic))
        {
            SetSelection([topic]);
        }
    }

    /// <summary>Shift plus an arrow: the next topic in that direction joins the selection and becomes the last
    /// one.</summary>
    private void ExtendSelection(NavigationMove move)
    {
        if (SelectedTopic is not { } from || MapView?.Layout is not { } layout || Navigator.Move(layout, DisplayMap, from, move) is not { } target)
        {
            Navigate(move);
            return;
        }

        SetSelection([.. SelectedTopics.Where(id => id != target), target]);
    }

    private void SetSelection(List<TopicId> topics)
    {
        _keepingSelection = true;
        try
        {
            SelectedTopic = topics[^1];
        }
        finally
        {
            _keepingSelection = false;
        }

        SelectedTopics = topics;
        Panel.Refresh();
        Commands.RefreshAll();
    }

    // --- showing changes ---------------------------------------------------------------------------------------------

    private void Navigate(NavigationMove move)
    {
        if (SelectedTopic is null)
        {
            SelectedTopic = ActiveMap.Root.Id;
            return;
        }

        if (MapView?.Layout is { } layout && Navigator.Move(layout, DisplayMap, SelectedTopic!.Value, move) is { } target)
        {
            SelectedTopic = target;
        }
    }

    private void ShowStep(HistoryStep? step, string format)
    {
        if (step is null)
        {
            return;
        }

        if (step.Kind is CommandKind.Collapse or CommandKind.MoveTopic or CommandKind.Paste or CommandKind.DeleteTopic)
        {
            MapView?.AnimateNextLayout();
        }

        Show(step.Document, step.Target);
        StatusText = string.Format(CultureInfo.CurrentCulture, format, KindText(step.Kind));
        Commands.RefreshAll();
    }

    /// <summary>
    /// Shows a document state: switches to the target's map (an undo in another map switches tabs) and selects the
    /// target, or the central topic when the target is gone.
    /// </summary>
    private void Show(Document document, EditTarget target)
    {
        Document = document;
        Map map = document.FindMap(target.Map) ?? document.FindMap(ActiveMap.Id) ?? document.Maps[0];
        ActiveMap = map;
        SelectedTopic = target.Topic is { } topic && map.Contains(topic) ? topic : map.Root.Id;
        SelectedRelationship = target.Relationship is { } relationship && map.Relationships.Any(r => r.Id == relationship) ? relationship : null;

        // An edit that kept the primary selected keeps the others too, as far as they are still there.
        if (SelectedTopics.Count > 1 && SelectedTopic is { } primary)
        {
            SelectedTopics = [.. SelectedTopics.Where(id => id != primary && map.Contains(id)), primary];
        }

        // The selection may be unchanged while its text, links or note changed.
        Panel.Refresh();
        Search.Refresh();
    }

    private static string? RefusalText(Refusal refusal) => refusal switch
    {
        Refusal.CentralTopic => Strings.Refusal_CentralTopic,
        Refusal.EmptyText => Strings.Refusal_EmptyText,
        Refusal.SelfRelationship => Strings.Refusal_SelfRelationship,
        Refusal.DuplicateRelationship => Strings.Refusal_DuplicateRelationship,
        Refusal.UrlNotAllowed => Strings.Refusal_UrlNotAllowed,
        Refusal.TooDeep => Strings.Refusal_TooDeep,
        Refusal.NoteTooLong => string.Format(CultureInfo.CurrentCulture, Strings.Refusal_NoteTooLong, Topic.MaxNoteLength),
        _ => null,
    };

    internal static string KindText(CommandKind kind) => kind switch
    {
        CommandKind.AddTopic => MenuText.WithoutAccessKey(Strings.Command_NewSiblingAfter),
        CommandKind.InsertParent => MenuText.WithoutAccessKey(Strings.Command_NewParent),
        CommandKind.DeleteTopic => MenuText.WithoutAccessKey(Strings.Command_DeleteTopic),
        CommandKind.EditText => MenuText.WithoutAccessKey(Strings.Command_EditText),
        CommandKind.MoveTopic => Strings.Kind_Move,
        CommandKind.Collapse => Strings.Kind_Collapse,
        CommandKind.Paste => MenuText.WithoutAccessKey(Strings.Command_Paste),
        CommandKind.AddMap or CommandKind.RenameMap or CommandKind.DeleteMap or CommandKind.MoveMap => Strings.Kind_Map,
        CommandKind.AddRelationship or CommandKind.DeleteRelationship or CommandKind.ChangeRelationship => Strings.Kind_Relationship,
        CommandKind.SetMarker => Strings.Kind_Marker,
        CommandKind.AddLink or CommandKind.EditLink or CommandKind.RemoveLink or CommandKind.MoveLink => Strings.Kind_Link,
        CommandKind.EditNote => Strings.Kind_Note,
        _ => Strings.Kind_Change,
    };

    // --- files -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// True when the window may close now. Otherwise the caller cancels the close and calls
    /// <see cref="ConfirmCloseAsync"/>, which asks "Save changes?" and, if the answer allows it, closes again.
    /// </summary>
    public bool CanCloseWithoutAsking => _closeConfirmed || (!IsDirty && !IsEditing);

    /// <summary>Asks to save before closing the window or quitting. True when closing may go ahead.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        _closeConfirmed = await ConfirmDiscardAsync().ConfigureAwait(true);
        return _closeConfirmed;
    }

    /// <summary>The emergency save: the recovery file, never the document. Never throws.</summary>
    public void EmergencySave() => _recovery.WriteNow();

    /// <summary>
    /// The crash dialog after a caught UI-thread error: the recovery file first, then what happened
    /// and where the work is, then Continue or Quit. One dialog at a time: errors while it is open are only reported.
    /// </summary>
    public async Task ShowCrashAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (_crashShown)
        {
            return;
        }

        _crashShown = true;
        try
        {
            string technical = string.Create(
                CultureInfo.InvariantCulture,
                $"MindBuzz {AppInfo.Version}\n{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n\n{exception}");
            var request = new CrashRequest(Strings.Crash_Title, Strings.Crash_Message, SaveForCrash(), technical);
            if (await _ui.ShowCrashAsync(request).ConfigureAwait(true))
            {
                _ui.RequestExit();
            }
        }
        finally
        {
            _crashShown = false;
        }
    }

    /// <summary>Writes the recovery file where there is one to write, and says where the user's work is.</summary>
    private string SaveForCrash()
    {
        if (!IsDirty)
        {
            return Strings.Crash_NothingUnsaved;
        }

        if (FilePath is null)
        {
            return Strings.Crash_Untitled;
        }

        try
        {
            EmergencySave();
            return Strings.Crash_RecoveryWritten;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            GlobalExceptionHandler.Report("Recovery file", failure, isFatal: false);
            return Strings.Crash_RecoveryFailed;
        }
    }

    /// <summary>Opens a file handed to the app from outside: the start-up argument, or Finder's "Open With" on
    /// macOS.</summary>
    public async Task OpenFromOutsideAsync(string path, bool askFirst)
    {
        if (askFirst && !await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        await OpenPathAsync(path).ConfigureAwait(true);
    }

    private async Task NewAsync()
    {
        if (await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            Load(NewDocument(), path: null, unsaved: false);
        }
    }

    /// <summary>
    /// New Sample Mind Maps: an untitled document with the two sample maps, after the usual "Save changes?". It counts
    /// as unsaved, so New, Open or Quit ask before it is thrown away.
    /// </summary>
    private async Task NewSampleAsync()
    {
        if (await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            Load(SampleMaps.Create(RandomIdSource.Instance, Commands, Platform), path: null, unsaved: true);
        }
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync().ConfigureAwait(true))
        {
            return;
        }

        if (await _ui.PickOpenPathAsync().ConfigureAwait(true) is { } path)
        {
            await OpenPathAsync(path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Saves to the document's file, or asks for one when it is untitled or for Save As. Atomic: a failure leaves the
    /// file on disk as it was, and says so. True when saved.
    /// </summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        CommitPendingEdits();
        string? path = FilePath;
        if (saveAs || path is null)
        {
            // The name without extension: the save panel adds ".mbz" from the file type itself, so suggesting
            // "Plan.mbz" came out as "Plan.mbz.mbz" on macOS.
            path = await _ui.PickSavePathAsync(DocumentName).ConfigureAwait(true);
            if (path is null)
            {
                return false;
            }

            if (!path.EndsWith(".mbz", StringComparison.OrdinalIgnoreCase))
            {
                path += ".mbz";
            }
        }

        try
        {
            MbzFile.Save(path, _history.Current, Generator, _password);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _ui.ShowMessageAsync(new MessageRequest(
                Strings.SaveError_Title,
                string.Format(CultureInfo.CurrentCulture, Strings.SaveError_MessageFormat, Path.GetFileName(path), exception.Message),
                Strings.Dialog_Close)
            {
                Legal = Strings.SaveError_Detail,
            }).ConfigureAwait(true);
            return false;
        }

        // Saved under a new name: the old file's recovery belongs to changes that are now in the new file.
        if (FilePath is { } previous && previous != path)
        {
            _recovery.Discard(previous);
        }

        FilePath = path;
        _history.MarkClean();
        _recovery.Discard(path);
        StatusText = Strings.Status_Saved;
        return true;
    }

    /// <summary>
    /// The "Save changes?" prompt, before New, Open, Close and Quit. True when the caller may go
    /// on: nothing to save, saved, or "Don't Save" (which also deletes the recovery file).
    /// </summary>
    // --- export ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Export as PNG: the background (white or transparent), then where to save; the map on screen goes into the
    /// picture as it is expanded, in the light theme's colours.
    /// </summary>
    /// <summary>
    /// The PNG export's picture of the current map, on the clipboard (white background: a transparent one pastes
    /// badly in many apps).
    /// </summary>
    private async Task CopyMapImageAsync()
    {
        if (MapExport is not { } export)
        {
            return;
        }

        StatusText = await export.CopyImageAsync(ActiveMap).ConfigureAwait(true) ? Strings.Status_MapImageCopied : Strings.Status_NoClipboard;
    }

    private async Task ExportPngAsync()
    {
        int answer = await _ui.AskAsync(new ChoiceRequest(
            Strings.Export_Title,
            string.Format(CultureInfo.CurrentCulture, Strings.Export_MessageFormat, ActiveMap.Name),
            Strings.Export_Detail,
            [Strings.Answer_Cancel, Strings.Export_Transparent, Strings.Export_White],
            DefaultAnswer: 2,
            CancelAnswer: 0)).ConfigureAwait(true);
        if (answer == 0 || MapExport is null)
        {
            return;
        }

        string suggested = Path.GetFileNameWithoutExtension(DocumentName) is { Length: > 0 } document && Document.Maps.Length > 1
            ? $"{document} – {MapName.OneLine(ActiveMap.Root.Text)}"
            : MapName.OneLine(ActiveMap.Root.Text);
        string? path = await _ui.PickExportPathAsync(suggested).ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            path += ".png";
        }

        try
        {
            MapExport.SavePng(DisplayMap, path, whiteBackground: answer == 2);
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Status_ExportedFormat, Path.GetFileName(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _ui.ShowMessageAsync(new MessageRequest(
                Strings.Export_ErrorTitle,
                string.Format(CultureInfo.CurrentCulture, Strings.SaveError_MessageFormat, Path.GetFileName(path), exception.Message),
                Strings.Dialog_Close)).ConfigureAwait(true);
        }
    }

    // --- encryption --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The document's password changed: the recovery copy, the lock in the status bar and the File menu's
    /// "Encrypt File…" / "Change Password…" follow.
    /// </summary>
    private void SetPassword(string? password)
    {
        _password = password;
        _recovery.Password = password;
        if (IsEncrypted != password is not null)
        {
            IsEncrypted = password is not null;
            Menus = MenuLayout.Build(Commands, Platform);
        }

        Commands.RefreshAll();
    }

    /// <summary>
    /// Encrypt File: a new password typed twice, then the file is saved encrypted; a document never saved gets the
    /// save dialog first. On an encrypted file this is Change Password: the current password
    /// first. Cancelling anything, or a failed save, leaves the encryption as it was.
    /// </summary>
    private async Task EncryptAsync()
    {
        bool changing = IsEncrypted;
        if (changing && !await AskCurrentPasswordAsync(Strings.Password_ChangeTitle).ConfigureAwait(true))
        {
            return;
        }

        string? password = await _ui.AskPasswordAsync(new PasswordRequest(
            changing ? Strings.Password_ChangeTitle : Strings.Password_EncryptTitle,
            string.Format(CultureInfo.CurrentCulture, changing ? Strings.Password_NewMessageFormat : Strings.Password_EncryptMessageFormat, DocumentName),
            IsNew: true,
            changing ? Strings.Password_Change : Strings.Password_Encrypt)).ConfigureAwait(true);
        if (password is null)
        {
            return;
        }

        await SaveWithPasswordAsync(password, changing ? Strings.Status_PasswordChanged : Strings.Status_Encrypted).ConfigureAwait(true);
    }

    /// <summary>Remove Encryption: the current password, then the file is saved without encryption.</summary>
    private async Task RemoveEncryptionAsync()
    {
        if (await AskCurrentPasswordAsync(Strings.Password_RemoveTitle).ConfigureAwait(true))
        {
            await SaveWithPasswordAsync(null, Strings.Status_EncryptionRemoved).ConfigureAwait(true);
        }
    }

    /// <summary>Asks for the current password until it is right; false when cancelled.</summary>
    private async Task<bool> AskCurrentPasswordAsync(string title)
    {
        string? error = null;
        while (true)
        {
            string? typed = await _ui.AskPasswordAsync(new PasswordRequest(
                title,
                string.Format(CultureInfo.CurrentCulture, Strings.Password_CurrentMessageFormat, DocumentName),
                IsNew: false,
                Strings.Password_Continue,
                error)).ConfigureAwait(true);
            if (typed is null)
            {
                return false;
            }

            if (typed == _password)
            {
                return true;
            }

            error = Strings.Password_Wrong;
        }
    }

    /// <summary>
    /// Saves under <paramref name="password"/>; on a cancelled save dialog or a failed write, also an unexpected
    /// exception, the old one stays, so the password in effect always matches the file on disk.
    /// </summary>
    private async Task SaveWithPasswordAsync(string? password, string done)
    {
        string? previous = _password;
        SetPassword(password);
        bool saved = false;
        try
        {
            saved = await SaveAsync(saveAs: FilePath is null).ConfigureAwait(true);
        }
        finally
        {
            if (!saved)
            {
                SetPassword(previous);
            }
        }

        if (saved)
        {
            StatusText = done;
            if (password is not null && password != previous)
            {
                await RemoveOldRecoveryAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// After encrypting or changing the password, the recovery file written before holds the document unencrypted, or
    /// under the old password. The save queued its delete; this waits for it and, should it have failed, tries once
    /// more and otherwise says where the copy is, because recovery failures otherwise only reach standard error.
    /// </summary>
    private async Task RemoveOldRecoveryAsync()
    {
        await _recovery.Idle.ConfigureAwait(true);
        if (FilePath is not { } path)
        {
            return;
        }

        string recovery = RecoveryFile.PathFor(path);
        try
        {
            RecoveryFile.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _ui.ShowMessageAsync(new MessageRequest(
                Strings.RecoveryLeftover_Title,
                string.Format(CultureInfo.CurrentCulture, Strings.RecoveryLeftover_MessageFormat, recovery, exception.Message),
                Strings.Dialog_Close)).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Text typed but not yet confirmed joins the document: the map's editor, the detail view's title box, a tab
    /// being renamed. Save, Close, New and Open run this first, so they see the document as the user sees it.
    /// </summary>
    internal void CommitPendingEdits()
    {
        if (IsEditing)
        {
            CommitEdit(EditText);
        }

        PanelView?.CommitPendingText();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        CommitPendingEdits();

        if (!IsDirty)
        {
            return true;
        }

        int answer = await _ui.AskAsync(new ChoiceRequest(
            Strings.SavePrompt_Title,
            string.Format(CultureInfo.CurrentCulture, Strings.SavePrompt_MessageFormat, DocumentName),
            Strings.SavePrompt_Detail,
            [Strings.Answer_DontSave, Strings.Answer_Cancel, Strings.Answer_Save],
            DefaultAnswer: 2,
            CancelAnswer: 1)).ConfigureAwait(true);

        switch (answer)
        {
            case 2:
                return await SaveAsync(saveAs: false).ConfigureAwait(true);
            case 0:
                _recovery.Discard(FilePath);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Opens a file: refuses it with a clear message when it cannot be loaded, reports what could not be loaded
    /// when it partly can, and offers a newer recovery file.
    /// </summary>
    private async Task OpenPathAsync(string path)
    {
        string name = Path.GetFileName(path);
        LoadResult result;
        string? password = null;
        try
        {
            result = MbzFile.Load(path);

            // An encrypted file asks for its password until it is right or the user cancels.
            string? error = null;
            while (result.Failure is LoadFailure.PasswordRequired or LoadFailure.WrongPassword)
            {
                password = await _ui.AskPasswordAsync(new PasswordRequest(
                    Strings.Password_OpenTitle,
                    string.Format(CultureInfo.CurrentCulture, Strings.Password_OpenMessageFormat, name),
                    IsNew: false,
                    Strings.Password_Open,
                    error)).ConfigureAwait(true);
                if (password is null)
                {
                    return;
                }

                result = MbzFile.Load(path, password);
                error = Strings.Password_Wrong;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowOpenErrorAsync(string.Format(CultureInfo.CurrentCulture, Strings.OpenError_IoFormat, name, exception.Message)).ConfigureAwait(true);
            return;
        }

        if (result.Document is not { } document)
        {
            await ShowOpenErrorAsync(OpenErrorText(result, name)).ConfigureAwait(true);
            return;
        }

        if (RecoveryFile.HasNewerRecovery(path) && await OfferRecoveryAsync(path, name, password).ConfigureAwait(true))
        {
            return;
        }

        Load(document, path, unsaved: false, password);
        await ShowLoadProblemsAsync(name, result.Problems).ConfigureAwait(true);
    }

    /// <summary>What could not be loaded, if anything: never dropped silently.</summary>
    private async Task ShowLoadProblemsAsync(string name, IReadOnlyList<LoadProblem> problems)
    {
        if (problems.Count > 0)
        {
            await _ui.ShowMessageAsync(new MessageRequest(
                Strings.LoadProblems_Title,
                string.Format(CultureInfo.CurrentCulture, Strings.LoadProblems_MessageFormat, name),
                Strings.Dialog_Close)
            {
                Legal = ProblemSummary(problems),
            }).ConfigureAwait(true);
        }
    }

    /// <summary>The recovery question's answer when it is dismissed rather than answered (none of its
    /// buttons).</summary>
    private const int Dismissed = 3;

    /// <summary>The recovery offer. True when it loaded something itself (restore or copy); false to open the
    /// document.</summary>
    /// <param name="password">The document's password: an encrypted document's recovery copy is encrypted with
    /// it.</param>
    private async Task<bool> OfferRecoveryAsync(string path, string name, string? password)
    {
        string recoveryPath = RecoveryFile.PathFor(path);
        int answer = await _ui.AskAsync(new ChoiceRequest(
            Strings.Recovery_Title,
            string.Format(CultureInfo.CurrentCulture, Strings.Recovery_MessageFormat, name, File.GetLastWriteTime(recoveryPath)),
            Strings.Recovery_Detail,
            [Strings.Answer_OpenCopy, Strings.Answer_KeepDocument, Strings.Answer_Restore],
            DefaultAnswer: 2,
            CancelAnswer: Dismissed)).ConfigureAwait(true);

        if (answer == 1)
        {
            // Keep the document: the recovery is not wanted.
            _recovery.Discard(path);
            return false;
        }

        if (answer == Dismissed)
        {
            // Esc or the close button, perhaps by accident: the document opens, and the recovery file, the only copy
            // of that work, stays and is offered again next time.
            return false;
        }

        LoadResult recovered;
        try
        {
            recovered = MbzFile.Load(recoveryPath, password);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowOpenErrorAsync(string.Format(CultureInfo.CurrentCulture, Strings.OpenError_IoFormat, Path.GetFileName(recoveryPath), exception.Message)).ConfigureAwait(true);
            return false;
        }

        if (recovered.Document is not { } document)
        {
            await ShowOpenErrorAsync(OpenErrorText(recovered, Path.GetFileName(recoveryPath))).ConfigureAwait(true);
            return false;
        }

        // Restore: the recovered state, as unsaved changes to the document; the recovery file stays until it is
        // saved or discarded. Open as copy: an untitled document; the file and its recovery stay untouched.
        Load(document, answer == 2 ? path : null, unsaved: true, password);
        await ShowLoadProblemsAsync(Path.GetFileName(recoveryPath), recovered.Problems).ConfigureAwait(true);
        return true;
    }

    /// <summary>Puts a document on screen with a fresh history (cleared on open and new).</summary>
    /// <param name="password">The password it was opened with, or null: it stays the document's (an "Open as Copy" of
    /// an encrypted file stays encrypted, so its first save cannot leak the content).</param>
    private void Load(Document document, string? path, bool unsaved, string? password = null)
    {
        // The keyboard goes to the map: a title box that kept it would go on showing the old document's title.
        PanelView?.FocusCanvas();
        _closeConfirmed = false;
        FilePath = path;
        SetPassword(password);
        _history.Reset(document, unsaved);
        _selectionByMap.Clear();
        _revealed.Clear();
        RenamingMap = null;

        // A newly opened map opens fitted to the window: zoom and scroll are not part of the file.
        MapView?.ForgetViews();
        Show(document, new EditTarget(document.Maps[0].Id, document.Maps[0].Root.Id));
        StatusText = Strings.Status_Ready;
        Commands.RefreshAll();
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(WindowTitle));
        _recovery.Changed(FilePath, _history.Current, _history.IsDirty);
    }

    private Task ShowOpenErrorAsync(string message) =>
        _ui.ShowMessageAsync(new MessageRequest(Strings.OpenError_Title, message, Strings.Dialog_Close));

    private static string OpenErrorText(LoadResult result, string name) => result.Failure switch
    {
        LoadFailure.NewerFormat => string.Format(CultureInfo.CurrentCulture, Strings.OpenError_NewerFormat, name, result.FoundVersion, MbzFormat.CurrentVersion),
        LoadFailure.UnsafeArchive => string.Format(CultureInfo.CurrentCulture, Strings.OpenError_Unsafe, name),
        LoadFailure.DamagedDocument => string.Format(CultureInfo.CurrentCulture, Strings.OpenError_Damaged, name),
        _ => string.Format(CultureInfo.CurrentCulture, Strings.OpenError_NotAMap, name),
    };

    /// <summary>One line per kind of problem, with a count: "3 links were left out".</summary>
    internal static string ProblemSummary(IEnumerable<LoadProblem> problems) => string.Join(
        Environment.NewLine,
        problems.GroupBy(problem => problem.Kind).Select(group => string.Format(
            CultureInfo.CurrentCulture,
            group.Key switch
            {
                LoadProblemKind.MapSkipped => Strings.Problem_MapSkipped,
                LoadProblemKind.TopicSkipped => Strings.Problem_TopicSkipped,
                LoadProblemKind.TextMissing => Strings.Problem_TextMissing,
                LoadProblemKind.IdReplaced => Strings.Problem_IdReplaced,
                LoadProblemKind.LinkSkipped => Strings.Problem_LinkSkipped,
                LoadProblemKind.MarkerSkipped => Strings.Problem_MarkerSkipped,
                LoadProblemKind.RelationshipSkipped => Strings.Problem_RelationshipSkipped,
                LoadProblemKind.TooDeep => Strings.Problem_TooDeep,
                LoadProblemKind.BendIgnored => Strings.Problem_BendIgnored,
                _ => Strings.Problem_UnknownEntry,
            },
            group.Count())));

    /// <summary>
    /// Commands that run asynchronously (dialogs, the clipboard), whose callers do not wait. The expected failures (a
    /// locked file, a wrong password) are answered inside each command with their own message; anything that still gets
    /// here is unexpected and gets the crash dialog, so a failed Open, Save or Paste is never silent.
    /// </summary>
    private async Task RunGuarded(Func<Task> command)
    {
        try
        {
            await command().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GlobalExceptionHandler.Report("Command", exception, isFatal: false);
            await ShowCrashAsync(exception).ConfigureAwait(true);
        }
        finally
        {
            Commands.RefreshAll();
        }
    }

    /// <summary>A new document: "Map 1" with a central topic, selected and ready to type into.</summary>
    private static Document NewDocument() => Document.CreateNew(RandomIdSource.Instance, Strings.Topic_DefaultCentral);

    internal Task ShowAboutAsync() =>
        _ui.ShowMessageAsync(new MessageRequest(
            MenuText.WithoutAccessKey(Strings.Command_About),
            Strings.About_Text,
            Strings.Dialog_Close)
        {
            Version = string.IsNullOrEmpty(AppInfo.Version)
                ? null
                : string.Format(CultureInfo.CurrentCulture, Strings.About_VersionFormat, AppInfo.Version),
            LinkText = AppInfo.RepositoryUrl,
            OpenLink = () => _shell.OpenUrl(AppInfo.RepositoryUrl),
            Legal = Strings.About_Legal,
            Notices = string.IsNullOrEmpty(AppInfo.ThirdPartyNotices) ? null : AppInfo.ThirdPartyNotices,
            NoticesLabel = Strings.About_Notices,
        });

    /// <summary>
    /// Help → Print Keyboard Shortcuts: this platform's sheet as a PDF, opened in the PDF viewer, which prints it.
    /// </summary>
    private void PrintShortcuts() =>
        OpenSheet(ShortcutSheet.Build(Commands, Platform), Strings.Sheet_Title, Strings.Sheet_FileName, Strings.Status_PrintShortcutsFailed);

    /// <summary>
    /// Help → Print Markdown Help, and the Help button beside Edit and Preview: the Markdown syntax for notes as a
    /// one-page PDF in the keyboard sheet's layout.
    /// </summary>
    private void PrintMarkdownHelp() =>
        OpenSheet(MarkdownSheet.Build(Commands, Platform), Strings.MarkdownSheet_Title, Strings.MarkdownSheet_FileName, Strings.Status_PrintMarkdownHelpFailed);

    private void OpenSheet(IReadOnlyList<SheetSection> sheet, string title, string fileName, string failed)
    {
        byte[] pdf = ShortcutSheetPdf.Render(sheet, ShortcutSheet.Legend(sheet, Platform), title);
        if (!_shell.OpenTemporaryPdf(pdf, fileName))
        {
            StatusText = failed;
        }
    }

    /// <summary>
    /// The shortcuts dialog's rows: every command with at least one gesture on this platform, in registry
    /// order, all gestures formatted the platform's way.
    /// </summary>
    internal IReadOnlyList<ShortcutRow> BuildShortcutRows() =>
    [
        .. Commands.All
            .Select(command => (command, gestures: command.Gestures.KeyGestures(Platform)))
            .Where(pair => pair.gestures.Count > 0)
            .Select(pair => new ShortcutRow(
                MenuText.WithoutAccessKey(pair.command.Group),
                MenuText.WithoutAccessKey(pair.command.Label),
                string.Join(
                    Strings.Shortcuts_AlternativeSeparator,
                    pair.gestures.Select(gesture => GestureText.Format(gesture, Platform))))),
    ];
}

/// <summary>What the view model asks of the canvas.</summary>
/// <summary>
/// The window's panes as the view model needs them: focus moves between the canvas and the topic
/// panel, and the notes editor's selection for bold and italic. The main window implements it.
/// </summary>
internal interface IPanelView
{
    /// <summary>
    /// Puts the keyboard in the notes section: the editor in Edit mode, the preview otherwise. With
    /// <paramref name="caretAtEnd"/> the editor's caret goes to the end of the note.
    /// </summary>
    void FocusNotes(bool caretAtEnd = false);

    void FocusCanvas();

    /// <summary>Puts the keyboard in the links list, on its selected row.</summary>
    void FocusLinks();

    /// <summary>F6: from the canvas to the panel, or from the panel back to the canvas.</summary>
    void CyclePane(bool forward);

    /// <summary>Wraps the editor's selection in a Markdown marker, or unwraps it (Ctrl/⌘+B, I).</summary>
    void WrapSelection(string marker);

    /// <summary>Bold, italic or underline in the title box.</summary>
    void ToggleTitleStyle(TitleStyle style);

    /// <summary>Commits text typed into the title box or a tab's rename box and not yet confirmed.</summary>
    void CommitPendingText();

    /// <summary>Puts the keyboard in the search box, its text selected.</summary>
    void FocusSearch();

    /// <summary>Selects a search match in the note editor without taking the keyboard from the search box.</summary>
    void ShowNoteMatch(int start, int length);
}

/// <summary>Saves a map as a PNG. Throws <see cref="IOException"/> or <see
/// cref="UnauthorizedAccessException"/>.</summary>
internal interface IMapExport
{
    void SavePng(Map map, string path, bool whiteBackground);

    /// <summary>The same picture on a white background, on the clipboard. False when there is no clipboard.</summary>
    Task<bool> CopyImageAsync(Map map);
}

internal interface IMapView
{
    /// <summary>The current layout, for keyboard navigation; null before the first layout.</summary>
    MapLayout? Layout { get; }

    void ZoomStep(bool zoomIn);

    void ZoomTo(double zoom);

    void FitToWindow();

    /// <summary>Animates the next layout change from the current one (structure changes, never typing).</summary>
    void AnimateNextLayout();

    /// <summary>Forgets every map's zoom and scroll: a new or opened document starts fitted.</summary>
    void ForgetViews();

    /// <summary>Bold, italic or underline in the in-place editor.</summary>
    void ToggleTitleStyle(TitleStyle style);
}

/// <summary>One sheet tab.</summary>
/// <param name="Name">The label: the central topic's text, shortened.</param>
/// <param name="FullName">The central topic's whole text on one line, for the tooltip and the rename box.</param>
public sealed record MapTab(MapId Id, string Name, string FullName, bool IsActive, bool IsRenaming);
