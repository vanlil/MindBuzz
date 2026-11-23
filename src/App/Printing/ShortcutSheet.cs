using System.Globalization;
using Avalonia.Input;
using MindBuzz.App.Commands;
using MindBuzz.App.Localization;

namespace MindBuzz.App.Printing;

/// <summary>One line of the printed sheet: the keys, then what they do.</summary>
internal sealed record SheetRow(string Keys, string Text);

/// <summary>A titled group of lines.</summary>
internal sealed record SheetSection(string Title, IReadOnlyList<SheetRow> Rows);

/// <summary>
/// The printable keyboard sheet (Help → Print Keyboard Shortcuts): every shortcut of this platform, grouped by what
/// people want to do rather than by menu, for people who keep a sheet beside the keyboard,
/// often people new to computers. The keys come from the command table, so the sheet cannot drift; a guard test
/// checks that every command with a shortcut is on it. Only the keys of the in-place editor and of link mode, which
/// are not commands, are written here.
/// </summary>
internal static class ShortcutSheet
{
    /// <summary>
    /// A second way is shown only while both fit this many characters ("F2 or Space", "⌫ or ⌦"): the sheet shows
    /// how, not every way, and long keys would squeeze the text.
    /// </summary>
    private const int MaxKeysLength = 20;

    private abstract record Entry;

    private sealed record One(CommandId Id) : Entry;

    /// <summary>A run of numbered commands on one line, such as Priority 1 to 5.</summary>
    private sealed record Range(CommandId First, CommandId Last, string Text) : Entry;

    /// <summary>Keys that are not commands, such as Enter while typing.</summary>
    private sealed record Fixed(KeyGesture[] Gestures, string Text) : Entry;

    /// <summary>The sections in reading order: from moving around and typing, the first things a beginner needs.</summary>
    private static (string Title, Entry[] Entries)[] Layout() =>
    [
        (Strings.Sheet_Moving,
        [
            new One(CommandId.SelectUp), new One(CommandId.SelectDown), new One(CommandId.SelectLeft), new One(CommandId.SelectRight),
            new One(CommandId.SelectFirstSibling), new One(CommandId.SelectLastSibling), new One(CommandId.SelectCentral),
            new One(CommandId.SelectParent), new One(CommandId.SelectNext), new One(CommandId.SelectPrevious),
            new Fixed([new KeyGesture(Key.F10, KeyModifiers.Shift)], Strings.Sheet_ContextMenu),
            new One(CommandId.ExtendUp), new One(CommandId.ExtendDown), new One(CommandId.ExtendLeft), new One(CommandId.ExtendRight),
        ]),
        (Strings.Sheet_Typing,
        [
            new One(CommandId.EditText),
            new Fixed([new KeyGesture(Key.Enter)], Strings.Sheet_TypingDone),
            new Fixed([new KeyGesture(Key.Enter, KeyModifiers.Shift)], Strings.Sheet_TypingNewLine),
            new Fixed([new KeyGesture(Key.Escape)], Strings.Sheet_TypingCancel),
            new One(CommandId.Bold), new One(CommandId.Italic), new One(CommandId.Underline),
        ]),
        (Strings.Sheet_Topics,
        [
            new One(CommandId.NewSiblingAfter), new One(CommandId.NewSiblingBefore), new One(CommandId.NewSubtopic),
            new One(CommandId.NewParent), new One(CommandId.DeleteTopic), new One(CommandId.Undo), new One(CommandId.Redo),
            new One(CommandId.Cut), new One(CommandId.Copy), new One(CommandId.Paste),
        ]),
        (Strings.Sheet_Arranging,
        [
            new One(CommandId.MoveUp), new One(CommandId.MoveDown), new One(CommandId.MoveToFirst), new One(CommandId.MoveToLast),
            new One(CommandId.Promote), new One(CommandId.Demote),
        ]),
        (Strings.Sheet_ShowHide,
        [
            new One(CommandId.Collapse), new One(CommandId.Expand), new One(CommandId.ShowNextLevel),
            new Range(CommandId.ShowLevel1, CommandId.ShowLevel9, Strings.Sheet_Levels), new One(CommandId.ShowAllLevels),
        ]),
        (Strings.Sheet_Markers,
        [
            new Range(CommandId.Priority1, CommandId.Priority5, Strings.Sheet_Priorities), new One(CommandId.RemovePriority),
            new One(CommandId.ProgressForward), new One(CommandId.ProgressBackward),
        ]),
        (Strings.Sheet_Details,
        [
            new One(CommandId.ToggleTopicPanel), new One(CommandId.OpenNotes), new One(CommandId.ToggleMaximizePanel),
            new One(CommandId.AddLink), new One(CommandId.ShowLinks),
            new One(CommandId.ToggleNotesMode),
        ]),
        (Strings.Sheet_Relationships,
        [
            new One(CommandId.StartLinkMode),
            new Fixed([new KeyGesture(Key.Enter), new KeyGesture(Key.Enter, KeyModifiers.Shift)], Strings.Sheet_LinkFinish),
            new One(CommandId.FollowRelationship), new One(CommandId.SelectNextRelationship),
            new One(CommandId.ToggleTwoWay), new One(CommandId.ReverseRelationship), new One(CommandId.DeleteRelationship),
        ]),
        (Strings.Sheet_Search,
        [
            new One(CommandId.Find), new One(CommandId.FindNext), new One(CommandId.FindPrevious),
        ]),
        (Strings.Sheet_View,
        [
            new One(CommandId.ZoomIn), new One(CommandId.ZoomOut), new One(CommandId.ZoomActualSize), new One(CommandId.ZoomFit),
            new One(CommandId.NextPane), new One(CommandId.PreviousPane), new One(CommandId.ToggleFullScreen),
        ]),
        (Strings.Sheet_Maps,
        [
            new One(CommandId.NewMap), new One(CommandId.RenameMap), new One(CommandId.NextMap), new One(CommandId.PreviousMap),
            new One(CommandId.MoveMapLeft), new One(CommandId.MoveMapRight), new One(CommandId.DeleteMap),
        ]),
        (Strings.Sheet_Files,
        [
            new One(CommandId.New), new One(CommandId.Open), new One(CommandId.Save), new One(CommandId.SaveAs),
            new One(CommandId.CloseDocument), new One(CommandId.Quit), new One(CommandId.KeyboardShortcuts),
        ]),
    ];

    /// <summary>The sheet for <paramref name="platform"/>. Commands without a shortcut there are left out.</summary>
    internal static IReadOnlyList<SheetSection> Build(CommandRegistry commands, HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var sections = new List<SheetSection>();
        foreach ((string title, Entry[] entries) in Layout())
        {
            List<SheetRow> rows = [.. entries.Select(entry => Row(commands, platform, entry)).OfType<SheetRow>()];
            if (rows.Count > 0)
            {
                sections.Add(new SheetSection(title, rows));
            }
        }

        return sections;
    }

    /// <summary>The commands the sheet shows: a test checks that every command with a shortcut is one of them.</summary>
    internal static IEnumerable<CommandId> Covered() =>
        Layout().SelectMany(section => section.Entries).SelectMany(entry => entry switch
        {
            One one => [one.Id],
            Range range => Enumerable.Range((int)range.First, range.Last - range.First + 1).Select(id => (CommandId)id),
            _ => [],
        });

    /// <summary>
    /// The macOS key symbols used on the sheet, each with its name, for a line at the top: the symbols are what the
    /// menus show, but not everyone knows them. Empty on Windows and
    /// Linux, which write keys as words.
    /// </summary>
    internal static IReadOnlyList<(string Symbol, string Name)> Legend(IReadOnlyList<SheetSection> sheet, HostPlatform platform)
    {
        if (platform != HostPlatform.MacOS)
        {
            return [];
        }

        string used = string.Concat(sheet.SelectMany(section => section.Rows).Select(row => row.Keys));
        (string Symbol, string Name)[] all =
        [
            ("⌘", Strings.Sheet_KeyCommand), ("⌥", Strings.Sheet_KeyOption), ("⇧", Strings.Sheet_KeyShift),
            ("⌃", Strings.Sheet_KeyControl), ("↩", Strings.Sheet_KeyReturn), ("⎋", Strings.Sheet_KeyEscape),
            ("⇥", Strings.Sheet_KeyTab), ("⌫", Strings.Sheet_KeyDelete), ("⌦", Strings.Sheet_KeyForwardDelete),
            ("↖", Strings.Sheet_KeyHome), ("↘", Strings.Sheet_KeyEnd), ("⇞", Strings.Sheet_KeyPageUp),
            ("⇟", Strings.Sheet_KeyPageDown),
        ];
        return [.. all.Where(key => used.Contains(key.Symbol, StringComparison.Ordinal))];
    }

    private static SheetRow? Row(CommandRegistry commands, HostPlatform platform, Entry entry)
    {
        switch (entry)
        {
            case One one when commands.Contains(one.Id):
                AppCommand command = commands[one.Id];
                string? keys = Keys(command.Gestures.KeyGestures(platform), platform);
                return keys is null ? null : new SheetRow(keys, Label(command.Label));

            case Range range:
                IReadOnlyList<KeyGesture> first = commands[range.First].Gestures.KeyGestures(platform);
                IReadOnlyList<KeyGesture> last = commands[range.Last].Gestures.KeyGestures(platform);
                if (first.Count == 0 || last.Count == 0)
                {
                    return null;
                }

                string to = GestureText.KeyName(last[0].Key, platform);
                return new SheetRow(
                    string.Format(CultureInfo.CurrentCulture, Strings.Sheet_RangeFormat, GestureText.Format(first[0], platform), to),
                    range.Text);

            case Fixed fixedKeys:
                return new SheetRow(Keys(fixedKeys.Gestures, platform)!, fixedKeys.Text);

            default:
                return null;
        }
    }

    /// <summary>The first way, and a second when both are short, written the platform's way; null when there is none.</summary>
    private static string? Keys(IReadOnlyList<KeyGesture> gestures, HostPlatform platform)
    {
        string[] texts = [.. gestures.Select(gesture => GestureText.Format(gesture, platform)).Distinct()];
        if (texts.Length == 0)
        {
            return null;
        }

        string both = texts.Length > 1 ? texts[0] + Strings.Sheet_Or + texts[1] : texts[0];
        return both.Length <= MaxKeysLength ? both : texts[0];
    }

    /// <summary>A menu label as sheet text: without its access key and without the "…" that promises a dialog.</summary>
    private static string Label(string label) => MenuText.WithoutAccessKey(label).TrimEnd('…', '.').Trim();
}
