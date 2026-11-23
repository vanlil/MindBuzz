using MindBuzz.App.ViewModels;
using MindBuzz.Core.Model;
using System.Diagnostics;
using MindBuzz.App.Platform;
using MindBuzz.App.Services;

namespace MindBuzz.App.Tests;

/// <summary>Records what the view model asked of the window, and shows nothing.</summary>
internal sealed class FakeUiServices : IUiServices
{
    public List<MessageRequest> Messages { get; } = [];

    public List<IReadOnlyList<ShortcutRow>> ShortcutDialogs { get; } = [];

    public int ExitRequests { get; private set; }

    /// <summary>Answers for <see cref="AskAsync"/>, in order; the questions asked are recorded.</summary>
    public Queue<int> Answers { get; } = new();

    public List<ChoiceRequest> Questions { get; } = [];

    /// <summary>What the open and save dialogs "pick"; null is a cancelled dialog.</summary>
    public Queue<string?> PickedPaths { get; } = new();

    public Task<int> AskAsync(ChoiceRequest request)
    {
        Questions.Add(request);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : request.CancelAnswer);
    }

    /// <summary>When set, the open and save dialogs throw it: an unexpected failure inside a file command.</summary>
    public Exception? PickFails { get; set; }

    public Task<string?> PickOpenPathAsync() =>
        PickFails is { } failure ? Task.FromException<string?>(failure) : Task.FromResult(PickedPaths.Count > 0 ? PickedPaths.Dequeue() : null);

    public Task<string?> PickSavePathAsync(string suggestedName)
    {
        SuggestedNames.Add(suggestedName);
        return PickOpenPathAsync();
    }

    public List<string> SuggestedNames { get; } = [];

    public Task<string?> PickExportPathAsync(string suggestedName)
    {
        SuggestedNames.Add(suggestedName);
        return PickOpenPathAsync();
    }

    /// <summary>What the link dialog "returns", in order (null is Cancel); the requests are recorded.</summary>
    public Queue<InternetLink?> EditedLinks { get; } = new();

    public List<LinkEditRequest> LinkRequests { get; } = [];

    public Task<InternetLink?> EditLinkAsync(LinkEditRequest request)
    {
        LinkRequests.Add(request);
        return Task.FromResult(EditedLinks.Count > 0 ? EditedLinks.Dequeue() : null);
    }

    /// <summary>What the password dialog "returns", in order (null is Cancel); the requests are recorded.</summary>
    public Queue<string?> Passwords { get; } = new();

    public List<PasswordRequest> PasswordRequests { get; } = [];

    public Task<string?> AskPasswordAsync(PasswordRequest request)
    {
        PasswordRequests.Add(request);
        return Task.FromResult(Passwords.Count > 0 ? Passwords.Dequeue() : null);
    }

    /// <summary>A clipboard of its own, so tests never touch the system's.</summary>
    public ClipboardContent Clipboard { get; set; } = new(null, null);

    /// <summary>When set, the clipboard throws it, as a busy clipboard can on Windows.</summary>
    public Exception? ClipboardFails { get; set; }

    /// <summary>False: a session without a clipboard, where nothing can be copied.</summary>
    public bool HasClipboard { get; set; } = true;

    public Task<bool> SetClipboardAsync(string clip, string text)
    {
        if (ClipboardFails is { } failure)
        {
            return Task.FromException<bool>(failure);
        }

        if (!HasClipboard)
        {
            return Task.FromResult(false);
        }

        Clipboard = new ClipboardContent(clip, text);
        return Task.FromResult(true);
    }

    public Task<ClipboardContent> GetClipboardAsync() =>
        ClipboardFails is { } failure ? Task.FromException<ClipboardContent>(failure) : Task.FromResult(Clipboard);

    public Task ShowMessageAsync(MessageRequest request)
    {
        Messages.Add(request);
        return Task.CompletedTask;
    }

    public Task ShowShortcutsAsync(IReadOnlyList<ShortcutRow> rows)
    {
        ShortcutDialogs.Add(rows);
        return Task.CompletedTask;
    }

    /// <summary>The crash dialogs shown, in order.</summary>
    public List<CrashRequest> Crashes { get; } = [];

    /// <summary>What the crash dialog answers: true is Quit.</summary>
    public bool CrashAnswerQuit { get; set; }

    /// <summary>When set, the crash dialog stays open until it completes, for testing a second error meanwhile.</summary>
    public TaskCompletionSource<bool>? CrashGate { get; set; }

    public Task<bool> ShowCrashAsync(CrashRequest request)
    {
        Crashes.Add(request);
        return CrashGate?.Task ?? Task.FromResult(CrashAnswerQuit);
    }

    public void RequestExit() => ExitRequests++;

    /// <summary>The theme showing; the OS's light one until a command picks another.</summary>
    public bool DarkTheme { get; set; }

    public bool ShowsDarkTheme() => DarkTheme;

    public void UseTheme(bool dark) => DarkTheme = dark;
}

/// <summary>Records the URLs it was asked to open; opens nothing.</summary>
internal sealed class FakePlatformShell : IPlatformShell
{
    public List<string> OpenedUrls { get; } = [];

    public bool OpenUrl(string url)
    {
        OpenedUrls.Add(url);
        return true;
    }

    /// <summary>The PDFs it was asked to open, with their file names.</summary>
    public List<(byte[] Pdf, string FileName)> OpenedPdfs { get; } = [];

    public bool OpenTemporaryPdf(byte[] pdf, string fileName)
    {
        OpenedPdfs.Add((pdf, fileName));
        return true;
    }
}

/// <summary>Captures what <see cref="PlatformShell"/> would have started, without starting it.</summary>
internal sealed class ProcessStartRecorder
{
    public List<ProcessStartInfo> Started { get; } = [];

    public void Start(ProcessStartInfo info) => Started.Add(info);
}
