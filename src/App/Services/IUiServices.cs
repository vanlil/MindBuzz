namespace MindBuzz.App.Services;

/// <summary>
/// What a view model needs from the window it lives in, and nothing more. View models never see an Avalonia
/// <c>Window</c>; tests substitute a fake. A guard test pins the member list.
/// </summary>
public interface IUiServices
{
    Task ShowMessageAsync(MessageRequest request);

    Task ShowShortcutsAsync(IReadOnlyList<ShortcutRow> rows);

    /// <summary>Asks a question with a few answers; returns the index of the chosen one (the cancel answer on
    /// Esc).</summary>
    Task<int> AskAsync(ChoiceRequest request);

    /// <summary>The system's open dialog, filtered to <c>*.mbz</c>. Null when cancelled.</summary>
    Task<string?> PickOpenPathAsync();

    /// <summary>The system's save dialog, filtered to <c>*.mbz</c>. Null when cancelled.</summary>
    Task<string?> PickSavePathAsync(string suggestedName);

    /// <summary>The system's save dialog for an exported picture, filtered to <c>*.png</c>. Null when
    /// cancelled.</summary>
    Task<string?> PickExportPathAsync(string suggestedName);

    /// <summary>
    /// Puts copied topics on the system clipboard: the private format (JSON, full fidelity)
    /// and plain text as an indented outline for other apps, together in one clipboard item. False when there is no
    /// clipboard to put them on (a session without one), so Cut knows not to delete.
    /// </summary>
    Task<bool> SetClipboardAsync(string clip, string text);

    /// <summary>What the clipboard holds: the private format if another MindBuzz put it there, and plain
    /// text.</summary>
    Task<ClipboardContent> GetClipboardAsync();

    /// <summary>
    /// The add or edit dialog for an internet link. It accepts only an address <c>LinkUrl</c>
    /// allows. Null when cancelled.
    /// </summary>
    Task<Core.Model.InternetLink?> EditLinkAsync(LinkEditRequest request);

    /// <summary>
    /// The password dialog: one field to open or unlock, or a new password twice when
    /// <see cref="PasswordRequest.IsNew"/>. Null when cancelled.
    /// </summary>
    Task<string?> AskPasswordAsync(PasswordRequest request);

    /// <summary>
    /// The crash dialog: what happened, where the work is, the technical details to copy. True when
    /// the user chose Quit, false for Continue (also Esc and the close button).
    /// </summary>
    Task<bool> ShowCrashAsync(CrashRequest request);

    /// <summary>Closes the main window; the close goes through the save prompt like any other.</summary>
    void RequestExit();

    /// <summary>True while the dark theme shows: the OS's choice, or the one picked in the View menu.</summary>
    bool ShowsDarkTheme();

    /// <summary>
    /// Shows the light or the dark theme until MindBuzz quits, whatever the OS uses. Never saved:
    /// the next start follows the OS again.
    /// </summary>
    void UseTheme(bool dark);
}

/// <summary>What the password dialog shows. Every word is in the request.</summary>
/// <param name="WindowTitle">"Encrypt File", "Open Encrypted File"…</param>
/// <param name="Message">What the password is for, e.g. naming the file.</param>
/// <param name="IsNew">True: a new password, typed twice, at least <c>MbzCrypto.MinPasswordLength</c>
/// characters.</param>
/// <param name="OkLabel">The confirming button.</param>
/// <param name="Error">Shown in the warning colour, e.g. after a wrong password; null for none.</param>
public sealed record PasswordRequest(string WindowTitle, string Message, bool IsNew, string OkLabel, string? Error = null);

/// <summary>What the link dialog starts with. Every word is in the request, like <see cref="ChoiceRequest"/>.</summary>
/// <param name="WindowTitle">"Add Internet Link" or "Edit Internet Link".</param>
/// <param name="Url">The address to start with: the link's, or a valid one from the clipboard, or empty.</param>
/// <param name="LinkTitle">The title to start with.</param>
/// <param name="OkLabel">The confirming button: "Add" or "OK".</param>
public sealed record LinkEditRequest(string WindowTitle, string Url, string LinkTitle, string OkLabel);

/// <summary>The clipboard's content as far as MindBuzz cares: its own format, and plain text. Either may be
/// null.</summary>
public sealed record ClipboardContent(string? Clip, string? Text);

/// <summary>A question with a few answers, pre-formatted like <see cref="MessageRequest"/>.</summary>
/// <param name="Title">The window title.</param>
/// <param name="Message">The question.</param>
/// <param name="Detail">A muted second line, or null.</param>
/// <param name="Answers">The buttons, left to right.</param>
/// <param name="DefaultAnswer">The button Enter presses.</param>
/// <param name="CancelAnswer">The button Esc and closing the window press; or <c>Answers.Count</c>, an answer of its
/// own
/// that no button gives, when every button does something the user should choose on purpose.</param>
public sealed record ChoiceRequest(string Title, string Message, string? Detail, IReadOnlyList<string> Answers, int DefaultAnswer, int CancelAnswer);

/// <summary>
/// A message for the user to read and dismiss. Everything is pre-formatted by the view model, so the dialog
/// itself holds no strings and no formatting rules.
/// </summary>
/// <param name="Title">The window title.</param>
/// <param name="Message">The body text.</param>
/// <param name="CloseLabel">The only button's label.</param>
public sealed record MessageRequest(string Title, string Message, string CloseLabel)
{
    /// <summary>A version line under the message, or null to leave it out.</summary>
    public string? Version { get; init; }

    /// <summary>
    /// A link shown under the version. <see cref="LinkText"/> is what the user reads and
    /// <see cref="OpenLink"/> is what a click runs: the dialog has no idea how a link is opened, which keeps
    /// the platform out of the view.
    /// </summary>
    public string? LinkText { get; init; }

    public Action? OpenLink { get; init; }

    /// <summary>Licence and warranty text, last and muted, or null to leave it out.</summary>
    public string? Legal { get; init; }

    /// <summary>
    /// The bundled components' notices, shown in a short scrolling box, or null to leave it out. Long,
    /// verbatim and untranslated, which is why it is a scroll box and not a line.
    /// </summary>
    public string? Notices { get; init; }

    /// <summary>The heading above <see cref="Notices"/>. Ignored when there are no notices.</summary>
    public string? NoticesLabel { get; init; }
}

/// <summary>One line of the keyboard shortcuts dialog, already formatted for the platform.</summary>
/// <param name="Group">The menu or area the command belongs to, for the section heading.</param>
/// <param name="Command">The command's label, without access-key markers.</param>
/// <param name="Gestures">Every gesture for the command on this platform, formatted the platform's way.</param>
public sealed record ShortcutRow(string Group, string Command, string Gestures);

/// <summary>What the crash dialog shows. Every word is in the request, like <see cref="ChoiceRequest"/>.</summary>
/// <param name="Title">The window title.</param>
/// <param name="Message">What happened.</param>
/// <param name="Detail">Where the user's work is and what to do.</param>
/// <param name="TechnicalDetails">The version, the OS and the exception, for copying into a report; not
/// localised.</param>
public sealed record CrashRequest(string Title, string Message, string Detail, string TechnicalDetails);
