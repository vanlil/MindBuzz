using System.Windows.Input;
using Avalonia.Input;
using MindBuzz.App.Localization;

namespace MindBuzz.App.Commands;

/// <summary>
/// One entry of the shared menu model. The in-window <c>Menu</c> on Windows and Linux and the
/// <c>NativeMenu</c> on macOS are both generated from this, so the two can never drift apart.
/// </summary>
public sealed record MenuEntry(
    string Header,
    ICommand? Command = null,
    KeyGesture? Gesture = null,
    IReadOnlyList<MenuEntry>? Children = null,
    bool IsSeparator = false,
    MenuSwatch? Swatch = null)
{
    public static MenuEntry Separator { get; } = new("-", IsSeparator: true);

    public bool HasChildren => Children is { Count: > 0 };

    /// <summary>
    /// The header without access-key underscores, for the macOS menu bar, which has no access keys and would
    /// show the underscore literally.
    /// </summary>
    public string PlainHeader => MenuText.WithoutAccessKey(Header);
}

/// <summary>
/// A small picture beside a menu entry: a dot in a theme brush, or with <paramref name="Line"/> a
/// short line in that pattern. Used for a relationship's colours and line types.
/// </summary>
/// <param name="BrushKey">The theme brush the picture is drawn in.</param>
/// <param name="Line">A line pattern to draw instead of a dot.</param>
/// <param name="TopicIcon">A topic icon to draw instead of a dot (Canvas.TopicIcons).</param>
public sealed record MenuSwatch(string BrushKey, Core.Model.RelationshipLine? Line = null, string? TopicIcon = null);

/// <summary>The menus for one platform.</summary>
/// <param name="TopLevel">The window's menus: in the window on Windows and Linux, in the menu bar on macOS.</param>
/// <param name="MacApplicationItems">
/// The macOS application menu's own items (the leftmost menu, titled with the app's name). They are set on the
/// <c>Application</c>, flat, in <c>Initialize()</c>; the platform appends Services, Hide and Quit below them.
/// Empty on other platforms.
/// </param>
public sealed record MenuModel(IReadOnlyList<MenuEntry> TopLevel, IReadOnlyList<MenuEntry> MacApplicationItems);

/// <summary>One toolbar button, or a gap between groups.</summary>
public sealed record ToolbarItem(System.Windows.Input.ICommand? Command, string? IconKey, string Tooltip, bool IsSeparator = false);

/// <summary>A submenu in a menu definition.</summary>
internal sealed record SubMenu(string Header, object?[] Slots);

/// <summary>
/// The arrangement of the registry's commands into menus, context menus and the toolbar, per platform. Pure, so
/// every platform's arrangement is asserted from any machine. A definition is a list of slots: a
/// <see cref="CommandId"/>, a <see cref="SubMenu"/>, or null for a separator.
/// </summary>
internal static class MenuLayout
{
    private static readonly object?[] ClipboardSlots = [CommandId.Cut, CommandId.Copy, CommandId.Paste];

    private static readonly object?[] MoveSlots =
    [
        CommandId.MoveUp, CommandId.MoveDown, CommandId.MoveToFirst, CommandId.MoveToLast, null, CommandId.Promote, CommandId.Demote, null, CommandId.MoveToOtherSide,
    ];

    private static readonly object?[] RelationshipSlots =
    [
        CommandId.StartLinkMode, CommandId.FollowRelationship, CommandId.SelectNextRelationship,
    ];

    private static object?[] SelectedRelationshipSlots =>
    [
        CommandId.MakeOneWay, CommandId.MakeTwoWay, CommandId.ReverseRelationship, null,
        new SubMenu(Strings.Menu_RelationshipColor, [.. Enumerable.Range(0, 8).Select(i => (object?)(CommandId.RelationshipColorDefault + i))]),
        new SubMenu(Strings.Menu_RelationshipLine, [CommandId.RelationshipLineSolid, CommandId.RelationshipLineDashed, CommandId.RelationshipLineDotted]),
        null,
        CommandId.ResetCurve, null,
        CommandId.DeleteRelationship,
    ];

    private static object?[] MarkerSlots =>
    [
        new SubMenu(Strings.Menu_Priority, [CommandId.Priority1, CommandId.Priority2, CommandId.Priority3, CommandId.Priority4, CommandId.Priority5, null, CommandId.RemovePriority]),
        new SubMenu(
            Strings.Menu_Progress,
            [
                CommandId.Progress0, CommandId.Progress25, CommandId.Progress50, CommandId.Progress75, CommandId.ProgressDone, null,
                CommandId.RemoveProgress, null, CommandId.ProgressForward, CommandId.ProgressBackward,
            ]),
        new SubMenu(
            Strings.Menu_TopicIcon,
            [
                CommandId.TopicIconAlert, CommandId.TopicIconExclamation, CommandId.TopicIconQuestion, CommandId.TopicIconMoney,
                CommandId.TopicIconIdea, CommandId.TopicIconStar, CommandId.TopicIconFlag, CommandId.TopicIconHeart,
                CommandId.TopicIconClock, CommandId.TopicIconPin, null, CommandId.RemoveTopicIcon,
            ]),
    ];

    /// <summary>Show Links is in no menu: the detail view shows the links; its key stays.</summary>
    private static readonly object?[] LinkSlots = [CommandId.AddLink, CommandId.OpenNotes];

    /// <summary>
    /// Topic commands with a shortcut that are left out of the topic context menu.
    /// The mouse reaches them in the detail view's links section and
    /// through the link icon on the topic, and they stay in the Topic menu.
    /// </summary>
    internal static readonly IReadOnlySet<CommandId> NotInTopicContextMenu = new HashSet<CommandId> { CommandId.AddLink, CommandId.ShowLinks };

    private static readonly object?[] ZoomSlots =
    [
        CommandId.ZoomIn, CommandId.ZoomOut, null, CommandId.ZoomActualSize, CommandId.ZoomFit,
    ];

    private static object?[] LevelSlots =>
    [
        .. Enumerable.Range(0, 9).Select(i => (object?)(CommandId.ShowLevel1 + i)), null, CommandId.ShowAllLevels,
    ];

    /// <summary>The topic commands, grouped so the context menu stays scannable.</summary>
    private static object?[] TopicSlots =>
    [
        CommandId.NewSubtopic, CommandId.NewSiblingAfter, CommandId.NewSiblingBefore, CommandId.NewParent,
        null,
        CommandId.EditText,
        null,
        .. ClipboardSlots,
        null,
        new SubMenu(Strings.Menu_Move, MoveSlots),
        CommandId.Collapse, CommandId.Expand, CommandId.ShowNextLevel,
        null,
        .. MarkerSlots,
        null,
        CommandId.OpenNotes,
        null,
        .. RelationshipSlots,
        null,
        CommandId.DeleteTopic,
    ];

    internal static MenuModel Build(CommandRegistry registry, HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(registry);

        bool mac = platform == HostPlatform.MacOS;

        // About lives in the application menu on macOS and in Help elsewhere, never both. Quit is the
        // platform's own application-menu item on macOS, so File has no Exit there.
        var topLevel = new List<MenuEntry>();
        object?[] file = [CommandId.New, CommandId.NewSample, CommandId.Open, null, CommandId.Save, CommandId.SaveAs, null, CommandId.ExportPng, null, CommandId.EncryptFile, CommandId.RemoveEncryption, null, CommandId.CloseDocument];
        AddMenu(topLevel, registry, platform, Strings.Menu_File, mac ? file : [.. file, null, CommandId.Quit]);
        AddMenu(topLevel, registry, platform, Strings.Menu_Edit, [CommandId.Undo, CommandId.Redo, null, .. ClipboardSlots, CommandId.CopyMapImage, null, CommandId.Find, CommandId.FindNext, CommandId.FindPrevious]);
        AddMenu(
            topLevel,
            registry,
            platform,
            Strings.Menu_Topic,
            [
                CommandId.NewSubtopic, CommandId.NewSiblingAfter, CommandId.NewSiblingBefore, CommandId.NewParent,
                null,
                CommandId.EditText,
                null,
                .. MoveSlots,
                null,
                CommandId.Collapse, CommandId.Expand, CommandId.ShowNextLevel,
                null,
                .. MarkerSlots,
                null,
                .. LinkSlots,
                null,
                new SubMenu(Strings.Menu_Relationships, [.. RelationshipSlots, null, .. SelectedRelationshipSlots]),
                null,
                CommandId.DeleteTopic,
            ]);
        AddMenu(topLevel, registry, platform, Strings.Menu_Map, [CommandId.NewMap, CommandId.RenameMap, CommandId.DeleteMap, null, CommandId.MoveMapLeft, CommandId.MoveMapRight, null, CommandId.NextMap, CommandId.PreviousMap, null, CommandId.BalanceMap]);
        AddMenu(
            topLevel,
            registry,
            platform,
            Strings.Menu_View,
            [.. ZoomSlots, null, new SubMenu(Strings.Menu_ShowLevels, LevelSlots), null, CommandId.ToggleSearch, CommandId.ToggleTopicPanel, CommandId.ToggleMaximizePanel, CommandId.NextPane, CommandId.PreviousPane, null, CommandId.ToggleFullScreen, null, CommandId.LightTheme, CommandId.DarkTheme]);
        AddMenu(
            topLevel,
            registry,
            platform,
            Strings.Menu_Help,
            mac
                ? [CommandId.KeyboardShortcuts, CommandId.PrintShortcuts, CommandId.PrintMarkdownHelp, null, CommandId.CheckForUpdate]
                : [CommandId.KeyboardShortcuts, CommandId.PrintShortcuts, CommandId.PrintMarkdownHelp, null, CommandId.CheckForUpdate, CommandId.About]);

        IReadOnlyList<MenuEntry> appItems = mac ? Items(registry, platform, [CommandId.About]) : [];

        return new MenuModel(topLevel, appItems);
    }

    /// <summary>The topic context menu: every topic command, with its shortcut shown.</summary>
    internal static IReadOnlyList<MenuEntry> TopicContextMenu(CommandRegistry registry, HostPlatform platform) =>
        Items(registry, platform, TopicSlots, forContextMenu: true);

    /// <summary>The menu for the empty canvas: paste, new map, levels, zoom.</summary>
    internal static IReadOnlyList<MenuEntry> CanvasContextMenu(CommandRegistry registry, HostPlatform platform) =>
        Items(registry, platform, [CommandId.Paste, null, CommandId.NewMap, CommandId.BalanceMap, null, new SubMenu(Strings.Menu_ShowLevels, LevelSlots), null, .. ZoomSlots, null, CommandId.ToggleFullScreen], forContextMenu: true);

    /// <summary>The menu of a selected relationship. Right-click selects it first.</summary>
    internal static IReadOnlyList<MenuEntry> RelationshipContextMenu(CommandRegistry registry, HostPlatform platform) =>
        Items(registry, platform, SelectedRelationshipSlots, forContextMenu: true);

    /// <summary>The sheet tab's context menu. Right-click activates the tab first.</summary>
    internal static IReadOnlyList<MenuEntry> TabContextMenu(CommandRegistry registry, HostPlatform platform) =>
        Items(registry, platform, [CommandId.NewMap, null, CommandId.RenameMap, CommandId.MoveMapLeft, CommandId.MoveMapRight, null, CommandId.DeleteMap], forContextMenu: true);

    /// <summary>The toolbar: drawn icons with the label and shortcut as tooltip.</summary>
    internal static IReadOnlyList<ToolbarItem> Toolbar(CommandRegistry registry, HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(registry);

        CommandId?[] slots =
        [
            CommandId.Open, CommandId.Save, CommandId.CopyMapImage, null,
            CommandId.Undo, CommandId.Redo, null,
            CommandId.NewSubtopic, CommandId.NewSiblingAfter, CommandId.DeleteTopic, null,
            CommandId.ZoomFit, CommandId.ToggleFullScreen, null,
            CommandId.ToggleSearch, CommandId.ToggleTopicPanel,
        ];

        return
        [
            .. slots.Select(slot =>
            {
                if (slot is not { } id)
                {
                    return new ToolbarItem(null, null, string.Empty, IsSeparator: true);
                }

                AppCommand command = registry[id];
                IReadOnlyList<KeyGesture> gestures = command.Gestures.KeyGestures(platform);
                string label = MenuText.WithoutAccessKey(command.Label);
                string tooltip = gestures.Count == 0 ? label : $"{label}  ({GestureText.Format(gestures[0], platform)})";
                return new ToolbarItem(command.Command, command.IconKey, tooltip);
            }),
        ];
    }

    /// <summary>Adds a menu unless it would be empty: an empty menu is dropped, not shown.</summary>
    private static void AddMenu(
        List<MenuEntry> menus,
        CommandRegistry registry,
        HostPlatform platform,
        string header,
        object?[] slots)
    {
        List<MenuEntry> items = Items(registry, platform, slots);
        if (items.Count > 0)
        {
            menus.Add(new MenuEntry(header, Children: items));
        }
    }

    /// <summary>
    /// The entries for a list of slots. Separators never lead, trail or double up, so a menu stays tidy when a
    /// platform leaves some of its commands out.
    /// </summary>
    private static List<MenuEntry> Items(CommandRegistry registry, HostPlatform platform, object?[] slots, bool forContextMenu = false)
    {
        var items = new List<MenuEntry>();

        foreach (object? slot in slots)
        {
            switch (slot)
            {
                case null:
                    if (items.Count > 0 && !items[^1].IsSeparator)
                    {
                        items.Add(MenuEntry.Separator);
                    }

                    break;

                case SubMenu sub:
                    List<MenuEntry> children = Items(registry, platform, sub.Slots, forContextMenu);
                    if (children.Count > 0)
                    {
                        items.Add(new MenuEntry(sub.Header, Children: children));
                    }

                    break;

                case CommandId id:
                    items.Add(Entry(registry[id], platform, forContextMenu));
                    break;
            }
        }

        if (items.Count > 0 && items[^1].IsSeparator)
        {
            items.RemoveAt(items.Count - 1);
        }

        return items;
    }

    private static MenuEntry Entry(AppCommand command, HostPlatform platform, bool forContextMenu)
    {
        IReadOnlyList<KeyGesture> gestures = command.Gestures.KeyGestures(platform);
        KeyGesture? gesture = gestures.Count > 0 ? gestures[0] : null;

        // A macOS menu-bar item with a key equivalent catches that key everywhere in the app, also while the user
        // types in a text box or a dialog. Plain keys (Enter, Delete, arrows, -, +, F2) must not be caught there, so
        // the macOS menu bar shows the first gesture with ⌘ or ⌃, else an F-key of a window command (F1 Keyboard
        // Shortcuts), which means nothing in a text box; the canvas binds the others.
        if (platform == HostPlatform.MacOS && !forContextMenu)
        {
            gesture = gestures.FirstOrDefault(g => (g.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0)
                ?? gestures.FirstOrDefault(g => command.Scope == KeyScope.Window && g.Key is >= Key.F1 and <= Key.F24);
        }

        return new MenuEntry(command.CurrentLabel, command.Command, gesture, Swatch: command.Swatch);
    }
}

internal static class MenuText
{
    /// <summary>"_File" becomes "File"; a doubled "__" stays as one literal underscore.</summary>
    internal static string WithoutAccessKey(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        const string Placeholder = "\u0000";
        return header
            .Replace("__", Placeholder, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(Placeholder, "_", StringComparison.Ordinal);
    }
}
