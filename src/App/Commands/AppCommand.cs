using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace MindBuzz.App.Commands;

/// <summary>Every command the app has. The id is what the menu layout, tests and docs refer to.</summary>
public enum CommandId
{
    New,
    NewSample,
    Open,
    Save,
    SaveAs,
    CloseDocument,
    ExportPng,
    CopyMapImage,
    EncryptFile,
    RemoveEncryption,
    Quit,

    Undo,
    Redo,

    NewSubtopic,
    NewSiblingAfter,
    NewSiblingBefore,
    NewParent,
    EditText,
    DeleteTopic,

    Cut,
    Copy,
    Paste,

    Find,
    ToggleSearch,
    FindNext,
    FindPrevious,

    MoveUp,
    MoveDown,
    MoveToFirst,
    MoveToLast,
    Promote,
    Demote,
    MoveToOtherSide,
    BalanceMap,

    Collapse,
    Expand,
    ShowNextLevel,
    ShowLevel1,
    ShowLevel2,
    ShowLevel3,
    ShowLevel4,
    ShowLevel5,
    ShowLevel6,
    ShowLevel7,
    ShowLevel8,
    ShowLevel9,
    ShowAllLevels,

    Priority1,
    Priority2,
    Priority3,
    Priority4,
    Priority5,
    RemovePriority,
    Progress0,
    Progress25,
    Progress50,
    Progress75,
    ProgressDone,
    RemoveProgress,
    ProgressForward,
    ProgressBackward,
    TopicIconAlert,
    TopicIconExclamation,
    TopicIconQuestion,
    TopicIconMoney,
    TopicIconIdea,
    TopicIconStar,
    TopicIconFlag,
    TopicIconHeart,
    TopicIconClock,
    TopicIconPin,
    RemoveTopicIcon,
    AddLink,
    ShowLinks,
    OpenNotes,

    StartLinkMode,
    FollowRelationship,
    SelectNextRelationship,
    ToggleTwoWay,
    MakeOneWay,
    MakeTwoWay,
    ReverseRelationship,
    ResetCurve,
    DeleteRelationship,
    RelationshipColorDefault,
    RelationshipColorRed,
    RelationshipColorOrange,
    RelationshipColorYellow,
    RelationshipColorGreen,
    RelationshipColorTeal,
    RelationshipColorBlue,
    RelationshipColorPurple,
    RelationshipLineSolid,
    RelationshipLineDashed,
    RelationshipLineDotted,


    SelectUp,
    SelectDown,
    SelectLeft,
    SelectRight,
    SelectFirstSibling,
    SelectLastSibling,
    SelectCentral,
    SelectParent,
    SelectNext,
    SelectPrevious,
    ExtendUp,
    ExtendDown,
    ExtendLeft,
    ExtendRight,

    NewMap,
    RenameMap,
    DeleteMap,
    MoveMapLeft,
    MoveMapRight,
    NextMap,
    PreviousMap,

    ZoomIn,
    ZoomOut,
    ZoomActualSize,
    ZoomFit,

    ToggleTopicPanel,
    ToggleMaximizePanel,
    NextPane,
    PreviousPane,
    LightTheme,
    DarkTheme,
    ToggleFullScreen,
    ToggleNotesMode,
    Bold,
    Italic,
    Underline,

    KeyboardShortcuts,
    PrintShortcuts,
    PrintMarkdownHelp,
    CheckForUpdate,
    About,
}

/// <summary>
/// Where a command's gestures are listened for. Canvas commands use plain keys (Enter, Delete, arrows, Tab) that
/// mean something else in a text box or a dialog, so only the canvas binds them, and only while it has focus.
/// </summary>
internal enum KeyScope
{
    Window,
    Canvas,

    /// <summary>
    /// The topic panel: keys that mean something only there, such as Ctrl+E for Edit/Preview and Ctrl+B in the
    /// notes editor. Bound on the panel, so they never fire on the canvas.
    /// </summary>
    Panel,

    /// <summary>
    /// Text formatting (Ctrl/⌘+B, I, U): bound on the panel, for the notes editor and the title box, and on the canvas,
    /// for the in-place editor. Never on the window, so a dialog's text box never gets them.
    /// </summary>
    Text,
}

/// <summary>
/// Where a command appears. The main menu, the context menus and the toolbar are all generated from the
/// registry by these flags, so they cannot drift apart.
/// </summary>
[Flags]
internal enum CommandContexts
{
    None = 0,
    MainMenu = 1,
    Toolbar = 2,
    TopicMenu = 4,
    CanvasMenu = 8,
    RelationshipMenu = 16,
    TabMenu = 32,
}

/// <summary>
/// The gestures of one command on each platform. Most commands write the same chords for every platform and
/// let <see cref="ChordModifiers.Command"/> become Ctrl or ⌘; the few that differ (no Insert key on a Mac,
/// OS-reserved shortcuts) spell out each platform.
/// </summary>
internal sealed record CommandGestures(
    IReadOnlyList<Chord> Windows,
    IReadOnlyList<Chord> MacOS,
    IReadOnlyList<Chord> Linux)
{
    internal static CommandGestures None { get; } = new([], [], []);

    internal static CommandGestures Everywhere(params Chord[] chords) => new(chords, chords, chords);

    internal IReadOnlyList<Chord> For(HostPlatform platform) => platform switch
    {
        HostPlatform.MacOS => MacOS,
        HostPlatform.Linux => Linux,
        _ => Windows,
    };

    /// <summary>The Avalonia gestures for <paramref name="platform"/>, primary first.</summary>
    internal IReadOnlyList<KeyGesture> KeyGestures(HostPlatform platform) =>
        [.. For(platform).Select(chord => chord.ToKeyGesture(platform))];
}

/// <summary>
/// One entry of the command registry: everything the menus, context menus, toolbar, key handler and the
/// shortcuts dialog need to know about a command.
/// </summary>
internal sealed class AppCommand
{
    private readonly RelayCommand _command;

    public AppCommand(
        CommandId id,
        string group,
        string label,
        CommandGestures gestures,
        CommandContexts contexts,
        Action execute,
        Func<bool>? canExecute = null)
    {
        Id = id;
        Group = group;
        Label = label;
        Gestures = gestures;
        Contexts = contexts;
        _command = new RelayCommand(execute, canExecute ?? (static () => true));
    }

    public CommandId Id { get; }

    /// <summary>The section the command is listed under in the shortcuts dialog, from <c>Strings.resx</c>.</summary>
    public string Group { get; }

    /// <summary>The label from <c>Strings.resx</c>. May carry an access-key underscore for in-window menus.</summary>
    public string Label { get; }

    /// <summary>A second label the menus show while <see cref="UsesAlternateLabel"/> says so, e.g. "Change
    /// Password…".</summary>
    public string? AlternateLabel { get; init; }

    public Func<bool>? UsesAlternateLabel { get; init; }

    /// <summary>The label the menus show now.</summary>
    public string CurrentLabel => AlternateLabel is not null && UsesAlternateLabel?.Invoke() == true ? AlternateLabel : Label;

    public CommandGestures Gestures { get; }

    public CommandContexts Contexts { get; }

    /// <summary>
    /// Where the gestures come from, e.g. "Common mind-mapping shortcut" or "MindBuzz". Null for
    /// commands without a gesture.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>A colour dot or line sample shown beside the label in menus, or null for none.</summary>
    public MenuSwatch? Swatch { get; init; }

    /// <summary>The drawn icon's resource key in <c>Themes/Icons.axaml</c>, or null for none.</summary>
    public string? IconKey { get; init; }

    /// <summary>Where the gestures are listened for. Window by default.</summary>
    public KeyScope Scope { get; init; }

    /// <summary>
    /// True for commands that act on the selected topic. Every such command with a gesture must be in the topic
    /// context menu too (mouse parity for every keyboard action); a guard test checks it.
    /// </summary>
    public bool ActsOnTopic { get; init; }

    public ICommand Command => _command;

    /// <summary>Asks menus and buttons to re-read whether the command can run now.</summary>
    public void Refresh() => _command.NotifyCanExecuteChanged();
}
