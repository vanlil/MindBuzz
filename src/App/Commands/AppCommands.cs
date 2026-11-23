using Avalonia.Input;
using MindBuzz.App.Localization;

namespace MindBuzz.App.Commands;

/// <summary>
/// What the commands act on: one entry point per direction. The main view model implements it; tests and the
/// macOS application menu (built before the view model exists) supply their own.
/// </summary>
internal interface ICommandHost
{
    void Execute(CommandId command);

    bool CanExecute(CommandId command);

    /// <summary>True when a command's menu entry shows its alternate label now (Encrypt File → Change
    /// Password).</summary>
    bool UsesAlternateLabel(CommandId command) => false;
}

/// <summary>
/// The command table: every command with its label, gestures per platform, contexts, key scope and source. One
/// place, so a gesture is decided once and the guard tests check the whole table.
/// </summary>
internal static class AppCommands
{
    /// <summary>A shortcut that established mind-mapping apps use for the same action.</summary>
    private const string Common = "Common mind-mapping shortcut";

    private const string Ours = "MindBuzz";

    private const CommandContexts TopicContexts = CommandContexts.MainMenu | CommandContexts.TopicMenu;

    public static CommandRegistry Create(ICommandHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        AppCommand Command(
            CommandId id,
            string group,
            string label,
            CommandGestures gestures,
            CommandContexts contexts,
            string? source = Common,
            KeyScope scope = KeyScope.Window,
            bool actsOnTopic = false,
            string? icon = null) =>
            new(id, group, label, gestures, contexts, () => host.Execute(id), () => host.CanExecute(id))
            {
                Source = source,
                Scope = scope,
                ActsOnTopic = actsOnTopic,
                IconKey = icon,
            };

        AppCommand TopicCommand(CommandId id, string label, CommandGestures gestures, CommandContexts contexts = TopicContexts, string? source = Common, string? icon = null) =>
            Command(id, Strings.Menu_Topic, label, gestures, contexts, source, KeyScope.Canvas, actsOnTopic: true, icon);

        AppCommand Navigation(CommandId id, string label, CommandGestures gestures, string? source = Common) =>
            Command(id, Strings.Group_Navigation, label, gestures, CommandContexts.None, source, KeyScope.Canvas);

        Chord Key(Key key, ChordModifiers modifiers = ChordModifiers.None) => new(key, modifiers);

        CommandGestures PriorityGestures(Key digit) => new(
            Windows: [Key(digit, ChordModifiers.Command | ChordModifiers.Shift)],
            MacOS: [Key(digit, ChordModifiers.Command | ChordModifiers.Control)],
            Linux: [Key(digit, ChordModifiers.Command | ChordModifiers.Shift)]);

        return new CommandRegistry(
        [
            // File. F12 is the common Save As; Ctrl+W closes the document, not the window, and
            // starts a new one. On macOS ⌘W does the same.
            Command(CommandId.New, Strings.Menu_File, Strings.Command_New, CommandGestures.Everywhere(Key(Avalonia.Input.Key.N, ChordModifiers.Command)), CommandContexts.MainMenu),
            // Two small maps for a first look. Menu only.
            Command(CommandId.NewSample, Strings.Menu_File, Strings.Command_NewSample, CommandGestures.None, CommandContexts.MainMenu, source: null),
            Command(CommandId.Open, Strings.Menu_File, Strings.Command_Open, CommandGestures.Everywhere(Key(Avalonia.Input.Key.O, ChordModifiers.Command)), CommandContexts.MainMenu | CommandContexts.Toolbar, icon: "IconOpen"),
            Command(CommandId.Save, Strings.Menu_File, Strings.Command_Save, CommandGestures.Everywhere(Key(Avalonia.Input.Key.S, ChordModifiers.Command)), CommandContexts.MainMenu | CommandContexts.Toolbar, icon: "IconSave"),
            Command(
                CommandId.SaveAs,
                Strings.Menu_File,
                Strings.Command_SaveAs,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.S, ChordModifiers.Command | ChordModifiers.Shift), Key(Avalonia.Input.Key.F12)],
                    MacOS: [Key(Avalonia.Input.Key.S, ChordModifiers.Command | ChordModifiers.Shift)],
                    Linux: [Key(Avalonia.Input.Key.S, ChordModifiers.Command | ChordModifiers.Shift), Key(Avalonia.Input.Key.F12)]),
                CommandContexts.MainMenu),
            Command(CommandId.CloseDocument, Strings.Menu_File, Strings.Command_CloseDocument, CommandGestures.Everywhere(Key(Avalonia.Input.Key.W, ChordModifiers.Command)), CommandContexts.MainMenu),

            // Export: menu only.
            Command(CommandId.ExportPng, Strings.Menu_File, Strings.Command_ExportPng, CommandGestures.None, CommandContexts.MainMenu, source: null),

            // The PNG export's picture, on the clipboard instead of in a file. No shortcut (the owner's choice).
            Command(CommandId.CopyMapImage, Strings.Menu_Edit, Strings.Command_CopyMapImage, CommandGestures.None, CommandContexts.MainMenu | CommandContexts.Toolbar, source: null, icon: "IconCopyImage"),

            // Encryption. On an encrypted file, Encrypt File becomes Change Password.
            new AppCommand(CommandId.EncryptFile, Strings.Menu_File, Strings.Command_EncryptFile, CommandGestures.None, CommandContexts.MainMenu, () => host.Execute(CommandId.EncryptFile), () => host.CanExecute(CommandId.EncryptFile))
            {
                AlternateLabel = Strings.Command_ChangePassword,
                UsesAlternateLabel = () => host.UsesAlternateLabel(CommandId.EncryptFile),
            },
            Command(CommandId.RemoveEncryption, Strings.Menu_File, Strings.Command_RemoveEncryption, CommandGestures.None, CommandContexts.MainMenu, source: null),

            // Windows has Alt+F4 from the OS and shows no gesture of its own; macOS gets Quit from the application
            // menu the platform builds (⌘Q), so this command is not in a macOS menu at all; Linux expects Ctrl+Q.
            Command(
                CommandId.Quit,
                Strings.Menu_File,
                Strings.Command_Quit,
                new CommandGestures(Windows: [], MacOS: [], Linux: [Key(Avalonia.Input.Key.Q, ChordModifiers.Command)]),
                CommandContexts.MainMenu,
                source: "Linux desktop convention (KDE, GNOME)"),

            // Undo and redo. Alt+Backspace is the second undo on Windows; ⇧⌘Z is the macOS convention.
            Command(
                CommandId.Undo,
                Strings.Menu_Edit,
                Strings.Command_Undo,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.Z, ChordModifiers.Command), Key(Avalonia.Input.Key.Back, ChordModifiers.Alt)],
                    MacOS: [Key(Avalonia.Input.Key.Z, ChordModifiers.Command)],
                    Linux: [Key(Avalonia.Input.Key.Z, ChordModifiers.Command), Key(Avalonia.Input.Key.Back, ChordModifiers.Alt)]),
                CommandContexts.MainMenu | CommandContexts.Toolbar,
                icon: "IconUndo"),
            Command(
                CommandId.Redo,
                Strings.Menu_Edit,
                Strings.Command_Redo,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.Y, ChordModifiers.Command), Key(Avalonia.Input.Key.Z, ChordModifiers.Command | ChordModifiers.Shift)],
                    MacOS: [Key(Avalonia.Input.Key.Z, ChordModifiers.Command | ChordModifiers.Shift), Key(Avalonia.Input.Key.Y, ChordModifiers.Command)],
                    Linux: [Key(Avalonia.Input.Key.Y, ChordModifiers.Command), Key(Avalonia.Input.Key.Z, ChordModifiers.Command | ChordModifiers.Shift)]),
                CommandContexts.MainMenu | CommandContexts.Toolbar,
                icon: "IconRedo"),

            // Search. Ctrl+F everywhere; F3 and Shift+F3 step through the hits, and on macOS ⌘G and
            // ⇧⌘G, listed first because the menu bar shows the first gesture and cannot show an F-key alone.
            Command(CommandId.Find, Strings.Menu_Edit, Strings.Command_Find, CommandGestures.Everywhere(Key(Avalonia.Input.Key.F, ChordModifiers.Command)), CommandContexts.MainMenu, source: Ours, icon: "IconSearch"),

            // The toolbar's search button opens and closes the panel, like the detail view's. Ctrl+F
            // only opens it, or brings the keyboard back to the box, as in every editor.
            Command(CommandId.ToggleSearch, Strings.Menu_View, Strings.Command_ToggleSearch, CommandGestures.None, CommandContexts.MainMenu | CommandContexts.Toolbar, source: null, icon: "IconSearch"),
            Command(
                CommandId.FindNext,
                Strings.Menu_Edit,
                Strings.Command_FindNext,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.F3)],
                    MacOS: [Key(Avalonia.Input.Key.G, ChordModifiers.Command), Key(Avalonia.Input.Key.F3)],
                    Linux: [Key(Avalonia.Input.Key.F3)]),
                CommandContexts.MainMenu,
                source: "MindBuzz; macOS convention for ⌘G"),
            Command(
                CommandId.FindPrevious,
                Strings.Menu_Edit,
                Strings.Command_FindPrevious,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.F3, ChordModifiers.Shift)],
                    MacOS: [Key(Avalonia.Input.Key.G, ChordModifiers.Command | ChordModifiers.Shift), Key(Avalonia.Input.Key.F3, ChordModifiers.Shift)],
                    Linux: [Key(Avalonia.Input.Key.F3, ChordModifiers.Shift)]),
                CommandContexts.MainMenu,
                source: "MindBuzz; macOS convention for ⇧⌘G"),

            // Topics. macOS has no Insert key, so ⌘↩ adds a subtopic there and ⇧⌘↩ a parent.
            TopicCommand(
                CommandId.NewSubtopic,
                Strings.Command_NewSubtopic,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.Insert), Key(Avalonia.Input.Key.Enter, ChordModifiers.Command)],
                    MacOS: [Key(Avalonia.Input.Key.Enter, ChordModifiers.Command)],
                    Linux: [Key(Avalonia.Input.Key.Insert), Key(Avalonia.Input.Key.Enter, ChordModifiers.Command)]),
                TopicContexts | CommandContexts.Toolbar,
                icon: "IconSubtopic"),
            TopicCommand(
                CommandId.NewSiblingAfter,
                Strings.Command_NewSiblingAfter,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.Enter)),
                TopicContexts | CommandContexts.Toolbar,
                icon: "IconSibling"),
            TopicCommand(
                CommandId.NewSiblingBefore,
                Strings.Command_NewSiblingBefore,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.Enter, ChordModifiers.Shift))),
            TopicCommand(
                CommandId.NewParent,
                Strings.Command_NewParent,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.Insert, ChordModifiers.Command | ChordModifiers.Shift)],
                    MacOS: [Key(Avalonia.Input.Key.Enter, ChordModifiers.Command | ChordModifiers.Shift)],
                    Linux: [Key(Avalonia.Input.Key.Insert, ChordModifiers.Command | ChordModifiers.Shift)])),
            TopicCommand(
                CommandId.EditText,
                Strings.Command_EditText,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.F2), Key(Avalonia.Input.Key.Space))),
            TopicCommand(
                CommandId.DeleteTopic,
                Strings.Command_DeleteTopic,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.Delete)],
                    MacOS: [Key(Avalonia.Input.Key.Back), Key(Avalonia.Input.Key.Delete)],
                    Linux: [Key(Avalonia.Input.Key.Delete)]),
                TopicContexts | CommandContexts.Toolbar,
                icon: "IconDelete"),

            // Clipboard. Shift+Delete, Ctrl+Insert and Shift+Insert are Windows'
            // second set; macOS has no Insert key.
            TopicCommand(
                CommandId.Cut,
                Strings.Command_Cut,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.X, ChordModifiers.Command), Key(Avalonia.Input.Key.Delete, ChordModifiers.Shift)],
                    MacOS: [Key(Avalonia.Input.Key.X, ChordModifiers.Command)],
                    Linux: [Key(Avalonia.Input.Key.X, ChordModifiers.Command), Key(Avalonia.Input.Key.Delete, ChordModifiers.Shift)])),
            TopicCommand(
                CommandId.Copy,
                Strings.Command_Copy,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.C, ChordModifiers.Command), Key(Avalonia.Input.Key.Insert, ChordModifiers.Command)],
                    MacOS: [Key(Avalonia.Input.Key.C, ChordModifiers.Command)],
                    Linux: [Key(Avalonia.Input.Key.C, ChordModifiers.Command), Key(Avalonia.Input.Key.Insert, ChordModifiers.Command)])),
            TopicCommand(
                CommandId.Paste,
                Strings.Command_Paste,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.V, ChordModifiers.Command), Key(Avalonia.Input.Key.Insert, ChordModifiers.Shift)],
                    MacOS: [Key(Avalonia.Input.Key.V, ChordModifiers.Command)],
                    Linux: [Key(Avalonia.Input.Key.V, ChordModifiers.Command), Key(Avalonia.Input.Key.Insert, ChordModifiers.Shift)]),
                TopicContexts | CommandContexts.CanvasMenu),

            // Moving. Alt+Ctrl+arrows are the common move keys; promote and demote use the same family.
            // On the left half of the map the view model swaps promote and demote, so Left always points outwards.
            TopicCommand(CommandId.MoveUp, Strings.Command_MoveUp, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Up, ChordModifiers.Command | ChordModifiers.Alt))),
            TopicCommand(CommandId.MoveDown, Strings.Command_MoveDown, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Down, ChordModifiers.Command | ChordModifiers.Alt))),
            TopicCommand(CommandId.MoveToFirst, Strings.Command_MoveToFirst, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Home, ChordModifiers.Command | ChordModifiers.Alt))),
            TopicCommand(CommandId.MoveToLast, Strings.Command_MoveToLast, CommandGestures.Everywhere(Key(Avalonia.Input.Key.End, ChordModifiers.Command | ChordModifiers.Alt))),
            TopicCommand(CommandId.Promote, Strings.Command_Promote, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Left, ChordModifiers.Command | ChordModifiers.Alt)), source: Ours),
            TopicCommand(CommandId.Demote, Strings.Command_Demote, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Right, ChordModifiers.Command | ChordModifiers.Alt)), source: Ours),

            // Main topics stay on their side: these two change it on purpose. Menu only.
            TopicCommand(CommandId.MoveToOtherSide, Strings.Command_MoveToOtherSide, CommandGestures.None, source: null),
            Command(CommandId.BalanceMap, Strings.Menu_Map, Strings.Command_BalanceMap, CommandGestures.None, CommandContexts.MainMenu | CommandContexts.CanvasMenu, source: null),

            // Collapse and expand: - and + on both keyboards, the common
            // Alt+Shift+0 and Ctrl+D, and Alt+Shift+1…9 and Alt+Shift+> for the whole map.
            TopicCommand(
                CommandId.Collapse,
                Strings.Command_Collapse,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.OemMinus), Key(Avalonia.Input.Key.Subtract), Key(Avalonia.Input.Key.D0, ChordModifiers.Alt | ChordModifiers.Shift)),
                source: "MindBuzz (- and +); common mind-mapping shortcut for Alt+Shift+0"),
            TopicCommand(
                CommandId.Expand,
                Strings.Command_Expand,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.OemPlus), Key(Avalonia.Input.Key.OemPlus, ChordModifiers.Shift), Key(Avalonia.Input.Key.Add)),
                source: "MindBuzz (- and +)"),
            TopicCommand(CommandId.ShowNextLevel, Strings.Command_ShowNextLevel, CommandGestures.Everywhere(Key(Avalonia.Input.Key.D, ChordModifiers.Command))),
            .. Enumerable.Range(1, 9).Select(level => Command(
                CommandId.ShowLevel1 + (level - 1),
                Strings.Menu_View,
                string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Command_ShowLevelFormat, level),
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.D0 + level, ChordModifiers.Alt | ChordModifiers.Shift)),
                CommandContexts.MainMenu | CommandContexts.CanvasMenu,
                scope: KeyScope.Canvas)),
            Command(
                CommandId.ShowAllLevels,
                Strings.Menu_View,
                Strings.Command_ShowAllLevels,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.OemPeriod, ChordModifiers.Alt | ChordModifiers.Shift)),
                CommandContexts.MainMenu | CommandContexts.CanvasMenu,
                scope: KeyScope.Canvas),

            // Markers. Ctrl+Shift+1…5 and 0 are the common marker keys; macOS keeps ⇧⌘3/4/5 for screenshots,
            // so the Mac uses ⌃⌘ plus the digit for the whole family.
            .. Enumerable.Range(1, 5).Select(n => TopicCommand(
                CommandId.Priority1 + (n - 1),
                string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Command_PriorityFormat, n),
                PriorityGestures(Avalonia.Input.Key.D0 + n))),
            TopicCommand(CommandId.RemovePriority, Strings.Command_RemovePriority, PriorityGestures(Avalonia.Input.Key.D0)),
            TopicCommand(CommandId.Progress0, Strings.Command_Progress0, CommandGestures.None, source: null),
            TopicCommand(CommandId.Progress25, Strings.Command_Progress25, CommandGestures.None, source: null),
            TopicCommand(CommandId.Progress50, Strings.Command_Progress50, CommandGestures.None, source: null),
            TopicCommand(CommandId.Progress75, Strings.Command_Progress75, CommandGestures.None, source: null),
            TopicCommand(CommandId.ProgressDone, Strings.Command_ProgressDone, CommandGestures.None, source: null),
            TopicCommand(CommandId.RemoveProgress, Strings.Command_RemoveProgress, CommandGestures.None, source: null),
            TopicCommand(CommandId.ProgressForward, Strings.Command_ProgressForward, CommandGestures.Everywhere(Key(Avalonia.Input.Key.P, ChordModifiers.Command | ChordModifiers.Alt))),
            TopicCommand(CommandId.ProgressBackward, Strings.Command_ProgressBackward, CommandGestures.Everywhere(Key(Avalonia.Input.Key.P, ChordModifiers.Command | ChordModifiers.Alt | ChordModifiers.Shift))),

            // Topic icons on the left border: menus only, each entry with the icon's picture.
            .. Core.Model.MarkerGroups.IconValues.Select((value, i) => new AppCommand(
                CommandId.TopicIconAlert + i,
                Strings.Menu_Topic,
                Strings.ResourceManager.GetString($"Icon_{value}", Strings.Culture)!,
                CommandGestures.None,
                TopicContexts,
                () => host.Execute(CommandId.TopicIconAlert + i),
                () => host.CanExecute(CommandId.TopicIconAlert + i))
            {
                Scope = KeyScope.Canvas,
                ActsOnTopic = true,
                Swatch = new MenuSwatch(Canvas.TopicIcons.BrushKey(value), TopicIcon: value),
            }),
            TopicCommand(CommandId.RemoveTopicIcon, Strings.Command_RemoveTopicIcon, CommandGestures.None, source: null),

            // Internet links. Ctrl+K is the common "Add hyperlink"; the list is ours.
            TopicCommand(CommandId.AddLink, Strings.Command_AddLink, CommandGestures.Everywhere(Key(Avalonia.Input.Key.K, ChordModifiers.Command))),
            // Keyboard only: the detail view shows the links; this jumps into its list.
            TopicCommand(CommandId.ShowLinks, Strings.Command_ShowLinks, CommandGestures.Everywhere(Key(Avalonia.Input.Key.K, ChordModifiers.Command | ChordModifiers.Shift)), CommandContexts.None, source: Ours),

            // Notes: "Open Detail View" opens the topic panel with the keyboard in the note editor.
            // Ctrl+Shift+N, N for notes, beside Ctrl+N; Ctrl+T toggles the panel itself.
            TopicCommand(CommandId.OpenNotes, Strings.Command_OpenNotes, CommandGestures.Everywhere(Key(Avalonia.Input.Key.N, ChordModifiers.Command | ChordModifiers.Shift)), source: Ours),

            // Relationships. Ctrl+Shift+R is the common "Insert relationship"; the rest are
            // ours. B and R act on a selected relationship only; Delete removes it through the Delete command.
            TopicCommand(CommandId.StartLinkMode, Strings.Command_StartLinkMode, CommandGestures.Everywhere(Key(Avalonia.Input.Key.R, ChordModifiers.Command | ChordModifiers.Shift))),
            TopicCommand(CommandId.FollowRelationship, Strings.Command_FollowRelationship, CommandGestures.Everywhere(Key(Avalonia.Input.Key.J, ChordModifiers.Command)), source: Ours),
            TopicCommand(CommandId.SelectNextRelationship, Strings.Command_SelectNextRelationship, CommandGestures.Everywhere(Key(Avalonia.Input.Key.J, ChordModifiers.Command | ChordModifiers.Shift)), source: Ours),
            // B switches from the keyboard; the menus show One-Way and Two-Way as two items instead, the current one
            // greyed out, so B is listed in the shortcuts dialog only.
            Command(
                CommandId.ToggleTwoWay,
                Strings.Group_Relationship,
                Strings.Command_ToggleTwoWay,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.B)),
                CommandContexts.None,
                source: Ours,
                scope: KeyScope.Canvas),
            Command(
                CommandId.MakeOneWay,
                Strings.Group_Relationship,
                Strings.Command_MakeOneWay,
                CommandGestures.None,
                CommandContexts.MainMenu | CommandContexts.RelationshipMenu,
                source: null,
                scope: KeyScope.Canvas),
            Command(
                CommandId.MakeTwoWay,
                Strings.Group_Relationship,
                Strings.Command_MakeTwoWay,
                CommandGestures.None,
                CommandContexts.MainMenu | CommandContexts.RelationshipMenu,
                source: null,
                scope: KeyScope.Canvas),
            Command(
                CommandId.ReverseRelationship,
                Strings.Group_Relationship,
                Strings.Command_ReverseRelationship,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.R)),
                CommandContexts.MainMenu | CommandContexts.RelationshipMenu,
                source: Ours,
                scope: KeyScope.Canvas),
            Command(
                CommandId.ResetCurve,
                Strings.Group_Relationship,
                Strings.Command_ResetCurve,
                CommandGestures.None,
                CommandContexts.MainMenu | CommandContexts.RelationshipMenu,
                source: null,
                scope: KeyScope.Canvas),
            Command(
                CommandId.DeleteRelationship,
                Strings.Group_Relationship,
                Strings.Command_DeleteRelationship,
                CommandGestures.None,
                CommandContexts.MainMenu | CommandContexts.RelationshipMenu,
                source: null,
                scope: KeyScope.Canvas),

            // A relationship's colour and line pattern: menu only, in its context menu and Topic >
            // Relationships, with the current value greyed out like One-Way and Two-Way.
            .. Enum.GetValues<Core.Model.RelationshipColor>().Select(color => new AppCommand(
                CommandId.RelationshipColorDefault + (int)color,
                Strings.Group_Relationship,
                Strings.ResourceManager.GetString($"Color_{color}", Strings.Culture)!,
                CommandGestures.None,
                CommandContexts.MainMenu | CommandContexts.RelationshipMenu,
                () => host.Execute(CommandId.RelationshipColorDefault + (int)color),
                () => host.CanExecute(CommandId.RelationshipColorDefault + (int)color))
            {
                Scope = KeyScope.Canvas,
                Swatch = new MenuSwatch(color == Core.Model.RelationshipColor.Default ? "TopicRelationshipBrush" : $"Relationship{color}Brush"),
            }),
            .. Enum.GetValues<Core.Model.RelationshipLine>().Select(line => new AppCommand(
                CommandId.RelationshipLineSolid + (int)line,
                Strings.Group_Relationship,
                Strings.ResourceManager.GetString($"Line_{line}", Strings.Culture)!,
                CommandGestures.None,
                CommandContexts.MainMenu | CommandContexts.RelationshipMenu,
                () => host.Execute(CommandId.RelationshipLineSolid + (int)line),
                () => host.CanExecute(CommandId.RelationshipLineSolid + (int)line))
            {
                Scope = KeyScope.Canvas,
                Swatch = new MenuSwatch("AppTextBrush", line),
            }),

            // Navigation: keyboard only, listed in the shortcuts dialog.
            Navigation(CommandId.SelectUp, Strings.Command_SelectUp, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Up))),
            Navigation(CommandId.SelectDown, Strings.Command_SelectDown, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Down))),
            Navigation(CommandId.SelectLeft, Strings.Command_SelectLeft, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Left))),
            Navigation(CommandId.SelectRight, Strings.Command_SelectRight, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Right))),
            Navigation(CommandId.SelectFirstSibling, Strings.Command_SelectFirstSibling, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Home))),
            Navigation(CommandId.SelectLastSibling, Strings.Command_SelectLastSibling, CommandGestures.Everywhere(Key(Avalonia.Input.Key.End))),
            Navigation(CommandId.SelectCentral, Strings.Command_SelectCentral, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Home, ChordModifiers.Command)), Ours),

            // Ctrl+Backspace on every OS, including ⌃⌫ on macOS: ⌘⌫ is "delete" there.
            Navigation(CommandId.SelectParent, Strings.Command_SelectParent, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Back, ChordModifiers.Control))),
            Navigation(CommandId.SelectNext, Strings.Command_SelectNext, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Tab))),
            Navigation(CommandId.SelectPrevious, Strings.Command_SelectPrevious, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Tab, ChordModifiers.Shift))),

            // Multi-select: Shift plus an arrow adds the next topic in that direction, as in
            // MindManager and XMind.
            Navigation(CommandId.ExtendUp, Strings.Command_ExtendUp, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Up, ChordModifiers.Shift))),
            Navigation(CommandId.ExtendDown, Strings.Command_ExtendDown, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Down, ChordModifiers.Shift))),
            Navigation(CommandId.ExtendLeft, Strings.Command_ExtendLeft, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Left, ChordModifiers.Shift))),
            Navigation(CommandId.ExtendRight, Strings.Command_ExtendRight, CommandGestures.Everywhere(Key(Avalonia.Input.Key.Right, ChordModifiers.Shift))),

            // Maps, the sheet tabs. Next and previous use the literal Control key, so macOS gets
            // ⌃Tab and never ⌘Tab, which belongs to the OS. Ctrl+Tab and Ctrl+F6 are the common sheet keys;
            // Ctrl+PageDown and
            // Ctrl+PageUp are Excel's.
            Command(CommandId.NewMap, Strings.Menu_Map, Strings.Command_NewMap, CommandGestures.Everywhere(Key(Avalonia.Input.Key.M, ChordModifiers.Command | ChordModifiers.Shift)), CommandContexts.MainMenu | CommandContexts.TabMenu | CommandContexts.CanvasMenu, source: Ours),
            Command(CommandId.RenameMap, Strings.Menu_Map, Strings.Command_RenameMap, CommandGestures.Everywhere(Key(Avalonia.Input.Key.F2, ChordModifiers.Command | ChordModifiers.Shift)), CommandContexts.MainMenu | CommandContexts.TabMenu, source: Ours),
            Command(CommandId.DeleteMap, Strings.Menu_Map, Strings.Command_DeleteMap, CommandGestures.None, CommandContexts.MainMenu | CommandContexts.TabMenu, source: null),
            Command(CommandId.MoveMapLeft, Strings.Menu_Map, Strings.Command_MoveMapLeft, CommandGestures.Everywhere(Key(Avalonia.Input.Key.PageUp, ChordModifiers.Command | ChordModifiers.Shift)), CommandContexts.MainMenu | CommandContexts.TabMenu, source: Ours),
            Command(CommandId.MoveMapRight, Strings.Menu_Map, Strings.Command_MoveMapRight, CommandGestures.Everywhere(Key(Avalonia.Input.Key.PageDown, ChordModifiers.Command | ChordModifiers.Shift)), CommandContexts.MainMenu | CommandContexts.TabMenu, source: Ours),
            Command(
                CommandId.NextMap,
                Strings.Menu_Map,
                Strings.Command_NextMap,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.Tab, ChordModifiers.Control), Key(Avalonia.Input.Key.F6, ChordModifiers.Control), Key(Avalonia.Input.Key.PageDown, ChordModifiers.Command)),
                CommandContexts.MainMenu,
                source: "Common mind-mapping shortcut (Ctrl+Tab, Ctrl+F6); Excel (Ctrl+PageDown)"),
            Command(
                CommandId.PreviousMap,
                Strings.Menu_Map,
                Strings.Command_PreviousMap,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.Tab, ChordModifiers.Control | ChordModifiers.Shift), Key(Avalonia.Input.Key.F6, ChordModifiers.Control | ChordModifiers.Shift), Key(Avalonia.Input.Key.PageUp, ChordModifiers.Command)),
                CommandContexts.MainMenu,
                source: "Common mind-mapping shortcut (Ctrl+Shift+Tab, Ctrl+Shift+F6); Excel (Ctrl+PageUp)"),

            // Zoom. The numeric keypad's + and - work too. Fit has an F-key on Windows; macOS laptops need fn for
            // F-keys, so ⌥⌘0 is added there.
            Command(
                CommandId.ZoomIn,
                Strings.Menu_View,
                Strings.Command_ZoomIn,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.OemPlus, ChordModifiers.Command), Key(Avalonia.Input.Key.Add, ChordModifiers.Command)),
                CommandContexts.MainMenu | CommandContexts.CanvasMenu),
            Command(
                CommandId.ZoomOut,
                Strings.Menu_View,
                Strings.Command_ZoomOut,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.OemMinus, ChordModifiers.Command), Key(Avalonia.Input.Key.Subtract, ChordModifiers.Command)),
                CommandContexts.MainMenu | CommandContexts.CanvasMenu),
            Command(
                CommandId.ZoomActualSize,
                Strings.Menu_View,
                Strings.Command_ZoomActualSize,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.D0, ChordModifiers.Command)),
                CommandContexts.MainMenu | CommandContexts.CanvasMenu),
            Command(
                CommandId.ZoomFit,
                Strings.Menu_View,
                Strings.Command_ZoomFit,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.F5, ChordModifiers.Command)],
                    MacOS: [Key(Avalonia.Input.Key.D0, ChordModifiers.Command | ChordModifiers.Alt), Key(Avalonia.Input.Key.F5, ChordModifiers.Command)],
                    Linux: [Key(Avalonia.Input.Key.F5, ChordModifiers.Command)]),
                CommandContexts.MainMenu | CommandContexts.CanvasMenu | CommandContexts.Toolbar,
                icon: "IconFit"),

            // The topic panel. Ctrl+T is the common "Show or hide Topic Notes". F6 is the standard
            // pane cycle; macOS laptops need fn for F-keys, so ⌥⌘T is added there and listed first, because the macOS
            // menu bar shows the first gesture and cannot show F6 (a plain key there would be caught app-wide).
            Command(
                CommandId.ToggleTopicPanel,
                Strings.Menu_View,
                Strings.Command_ToggleTopicPanel,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.T, ChordModifiers.Command)),
                CommandContexts.MainMenu | CommandContexts.Toolbar,
                source: "Common mind-mapping shortcut (\"Show or hide Topic Notes\")",
                icon: "IconPanel"),
            Command(
                CommandId.ToggleMaximizePanel,
                Strings.Menu_View,
                Strings.Command_ToggleMaximizePanel,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.T, ChordModifiers.Command | ChordModifiers.Shift)),
                CommandContexts.MainMenu,
                source: Ours),
            Command(
                CommandId.NextPane,
                Strings.Menu_View,
                Strings.Command_NextPane,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.F6)],
                    MacOS: [Key(Avalonia.Input.Key.T, ChordModifiers.Command | ChordModifiers.Alt), Key(Avalonia.Input.Key.F6)],
                    Linux: [Key(Avalonia.Input.Key.F6)]),
                CommandContexts.MainMenu,
                source: "Windows pane cycle (F6); MindBuzz for ⌥⌘T"),
            Command(
                CommandId.PreviousPane,
                Strings.Menu_View,
                Strings.Command_PreviousPane,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.F6, ChordModifiers.Shift)],
                    MacOS: [Key(Avalonia.Input.Key.T, ChordModifiers.Command | ChordModifiers.Alt | ChordModifiers.Shift), Key(Avalonia.Input.Key.F6, ChordModifiers.Shift)],
                    Linux: [Key(Avalonia.Input.Key.F6, ChordModifiers.Shift)]),
                CommandContexts.MainMenu,
                source: "Windows pane cycle (Shift+F6); MindBuzz for ⌥⇧⌘T"),

            // The theme for this session; the one in use is greyed out. Never saved, so the next start follows the OS
            // again.
            Command(CommandId.LightTheme, Strings.Menu_View, Strings.Command_LightTheme, CommandGestures.None, CommandContexts.MainMenu, source: null),
            Command(CommandId.DarkTheme, Strings.Menu_View, Strings.Command_DarkTheme, CommandGestures.None, CommandContexts.MainMenu, source: null),

            // Only the map, full screen; search and the detail view still open inside it. F11 is the
            // full-screen key of Windows and Linux desktops, ⌃⌘F that of macOS.
            Command(
                CommandId.ToggleFullScreen,
                Strings.Menu_View,
                Strings.Command_ToggleFullScreen,
                new CommandGestures(
                    Windows: [Key(Avalonia.Input.Key.F11)],
                    MacOS: [Key(Avalonia.Input.Key.F, ChordModifiers.Command | ChordModifiers.Control)],
                    Linux: [Key(Avalonia.Input.Key.F11)]),
                CommandContexts.MainMenu | CommandContexts.Toolbar,
                source: "Full-screen key of Windows and Linux desktops (F11) and of macOS (⌃⌘F)",
                icon: "IconFullScreen"),

            // Keys inside the topic panel only. They are in no menu: the panel has its own Edit/Preview control, and
            // a macOS menu-bar key would be caught outside the panel too. The shortcuts dialog lists them.
            Command(
                CommandId.ToggleNotesMode,
                Strings.Group_Notes,
                Strings.Command_ToggleNotesMode,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.E, ChordModifiers.Command)),
                CommandContexts.None,
                source: Ours,
                scope: KeyScope.Panel),
            Command(
                CommandId.Bold,
                Strings.Group_Formatting,
                Strings.Command_Bold,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.B, ChordModifiers.Command)),
                CommandContexts.None,
                source: "Common editor convention",
                scope: KeyScope.Text),
            Command(
                CommandId.Italic,
                Strings.Group_Formatting,
                Strings.Command_Italic,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.I, ChordModifiers.Command)),
                CommandContexts.None,
                source: "Common editor convention",
                scope: KeyScope.Text),
            Command(
                CommandId.Underline,
                Strings.Group_Formatting,
                Strings.Command_Underline,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.U, ChordModifiers.Command)),
                CommandContexts.None,
                source: "Common editor convention",
                scope: KeyScope.Text),

            // F1 is the usual Help key. MindBuzz's help in v1 is the shortcut list, so F1 opens it.
            Command(
                CommandId.KeyboardShortcuts,
                Strings.Menu_Help,
                Strings.Command_KeyboardShortcuts,
                CommandGestures.Everywhere(Key(Avalonia.Input.Key.F1)),
                CommandContexts.MainMenu,
                icon: "IconKeyboard"),

            // A sheet to print and keep beside the keyboard, for people new to computers.
            Command(
                CommandId.PrintShortcuts,
                Strings.Menu_Help,
                Strings.Command_PrintShortcuts,
                CommandGestures.None,
                CommandContexts.MainMenu,
                source: null),

            // The Markdown syntax for notes on one page; also the Help button beside Edit and Preview.
            Command(
                CommandId.PrintMarkdownHelp,
                Strings.Menu_Help,
                Strings.Command_PrintMarkdownHelp,
                CommandGestures.None,
                CommandContexts.MainMenu,
                source: null),
            // Asks GitHub for a newer release and answers in a dialog.
            Command(
                CommandId.CheckForUpdate,
                Strings.Menu_Help,
                Strings.Command_CheckForUpdate,
                CommandGestures.None,
                CommandContexts.MainMenu,
                source: null),
            Command(
                CommandId.About,
                Strings.Menu_Help,
                Strings.Command_About,
                CommandGestures.None,
                CommandContexts.MainMenu,
                source: null),
        ]);
    }
}
