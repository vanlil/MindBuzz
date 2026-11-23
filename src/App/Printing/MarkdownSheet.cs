using MindBuzz.App.Commands;
using MindBuzz.App.Localization;

namespace MindBuzz.App.Printing;

/// <summary>
/// The printable Markdown sheet (the Help button beside Edit and Preview, and Help → Print Markdown Help): the syntax
/// the notes preview shows, on one page in the keyboard sheet's layout, what to type and then what
/// it gives. Drawn by <see cref="ShortcutSheetPdf"/>, so both sheets look alike. The editor's own keys come from the
/// command table, like on the keyboard sheet, so they cannot drift.
/// </summary>
internal static class MarkdownSheet
{
    /// <summary>The sheet, with the editor's keys written the way <paramref name="platform"/> writes them.</summary>
    internal static IReadOnlyList<SheetSection> Build(CommandRegistry commands, HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(commands);

        return
        [
            new(Strings.MarkdownSheet_Text,
            [
                new(Strings.MarkdownSheet_BoldSyntax, Strings.MarkdownSheet_Bold),
                new(Strings.MarkdownSheet_ItalicSyntax, Strings.MarkdownSheet_Italic),
                new(Strings.MarkdownSheet_BoldItalicSyntax, Strings.MarkdownSheet_BoldItalic),
                new(Strings.MarkdownSheet_StrikeSyntax, Strings.MarkdownSheet_Strike),
                new(Strings.MarkdownSheet_CodeSyntax, Strings.MarkdownSheet_Code),
                new(Strings.MarkdownSheet_EscapeSyntax, Strings.MarkdownSheet_Escape),
            ]),
            new(Strings.MarkdownSheet_Headings,
            [
                new(Strings.MarkdownSheet_Heading1Syntax, Strings.MarkdownSheet_Heading1),
                new(Strings.MarkdownSheet_Heading2Syntax, Strings.MarkdownSheet_Heading2),
                new(Strings.MarkdownSheet_Heading3Syntax, Strings.MarkdownSheet_Heading3),
            ]),
            new(Strings.MarkdownSheet_Paragraphs,
            [
                new(Strings.MarkdownSheet_ParagraphSyntax, Strings.MarkdownSheet_Paragraph),
                new(Strings.MarkdownSheet_LineBreakSyntax, Strings.MarkdownSheet_LineBreak),
                new(Strings.MarkdownSheet_QuoteSyntax, Strings.MarkdownSheet_Quote),
                new(Strings.MarkdownSheet_RuleSyntax, Strings.MarkdownSheet_Rule),
                new(Strings.MarkdownSheet_CodeBlockSyntax, Strings.MarkdownSheet_CodeBlock),
            ]),
            new(Strings.MarkdownSheet_Lists,
            [
                new(Strings.MarkdownSheet_BulletSyntax, Strings.MarkdownSheet_Bullet),
                new(Strings.MarkdownSheet_NumberedSyntax, Strings.MarkdownSheet_Numbered),
                new(Strings.MarkdownSheet_NestedSyntax, Strings.MarkdownSheet_Nested),
                new(Strings.MarkdownSheet_TaskSyntax, Strings.MarkdownSheet_Task),
                new(Strings.MarkdownSheet_TaskDoneSyntax, Strings.MarkdownSheet_TaskDone),
            ]),
            new(Strings.MarkdownSheet_Links,
            [
                new(Strings.MarkdownSheet_LinkSyntax, Strings.MarkdownSheet_Link),
                new(Strings.MarkdownSheet_AutoLinkSyntax, Strings.MarkdownSheet_AutoLink),
                new(Strings.MarkdownSheet_ImageSyntax, Strings.MarkdownSheet_Image),
            ]),
            new(Strings.MarkdownSheet_Tables,
            [
                new(Strings.MarkdownSheet_TableRowSyntax, Strings.MarkdownSheet_TableRow),
                new(Strings.MarkdownSheet_TableRuleSyntax, Strings.MarkdownSheet_TableRule),
            ]),
            new(Strings.MarkdownSheet_Editor, [.. EditorKeys(commands, platform)]),
        ];
    }

    /// <summary>The notes editor's keys: bold, italic and the Edit/Preview switch, where the platform has
    /// them.</summary>
    private static IEnumerable<SheetRow> EditorKeys(CommandRegistry commands, HostPlatform platform)
    {
        (CommandId Id, string Text)[] keys =
        [
            (CommandId.Bold, Strings.MarkdownSheet_EditorBold),
            (CommandId.Italic, Strings.MarkdownSheet_EditorItalic),
            (CommandId.ToggleNotesMode, Strings.MarkdownSheet_EditorMode),
        ];
        foreach ((CommandId id, string text) in keys)
        {
            if (commands[id].Gestures.KeyGestures(platform) is [var gesture, ..])
            {
                yield return new SheetRow(GestureText.Format(gesture, platform), text);
            }
        }
    }
}
