using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.Diagnostics;
using MindBuzz.App.Localization;
using MindBuzz.App.Services;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The crash dialog after a caught UI-thread error.</summary>
public sealed class CrashTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mindbuzz-crash-").FullName;
    private readonly FakeUiServices _ui = new();
    private readonly MainViewModel _main;

    public CrashTests()
    {
        _main = new MainViewModel(_ui, new FakePlatformShell(), HostPlatform.MacOS);
    }

    public void Dispose()
    {
        _main.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private void Edit(string text) => _main.Apply(new SetTopicText(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, text));

    [AvaloniaFact]
    public async Task A_failing_clipboard_shows_the_crash_dialog_and_cut_keeps_the_topic()
    {
        _ui.ClipboardFails = new InvalidOperationException("clipboard busy");
        var topic = new TopicId("CUT");
        _main.Apply(new AddTopic(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, NewTopicPlacement.Subtopic, topic, "Cut me"));
        _main.Select(topic);

        _main.Execute(CommandId.Cut);
        await Task.Yield();

        Assert.Single(_ui.Crashes);
        Assert.NotNull(_main.ActiveMap.Find(topic));
    }

    [AvaloniaFact]
    public async Task A_saved_document_with_changes_gets_its_recovery_file_and_quit_closes_through_the_save_prompt()
    {
        string path = Path.Join(_folder, "Plan.mbz");
        _ui.PickedPaths.Enqueue(path);
        Edit("Plan");
        _main.Execute(CommandId.Save);
        await Task.Yield();
        Edit("Plan, changed");
        _ui.CrashAnswerQuit = true;

        await _main.ShowCrashAsync(new InvalidOperationException("boom"));

        CrashRequest crash = Assert.Single(_ui.Crashes);
        Assert.Equal(Strings.Crash_RecoveryWritten, crash.Detail);
        Assert.Contains("boom", crash.TechnicalDetails, StringComparison.Ordinal);
        Assert.True(File.Exists(RecoveryFile.PathFor(path)));
        Assert.Equal(1, _ui.ExitRequests);
    }

    [AvaloniaFact]
    public async Task An_untitled_document_says_there_is_no_recovery_file_and_continue_keeps_working()
    {
        Edit("Ideas");

        await _main.ShowCrashAsync(new InvalidOperationException("boom"));

        Assert.Equal(Strings.Crash_Untitled, Assert.Single(_ui.Crashes).Detail);
        Assert.Equal(0, _ui.ExitRequests);
        Assert.Equal("Ideas", _main.ActiveMap.Root.Text);
    }

    [AvaloniaFact]
    public async Task Without_changes_there_is_nothing_to_rescue()
    {
        await _main.ShowCrashAsync(new InvalidOperationException("boom"));

        Assert.Equal(Strings.Crash_NothingUnsaved, Assert.Single(_ui.Crashes).Detail);
    }

    [AvaloniaFact]
    public async Task A_second_error_while_the_dialog_is_open_does_not_stack_another_one()
    {
        _ui.CrashGate = new TaskCompletionSource<bool>();
        Task first = _main.ShowCrashAsync(new InvalidOperationException("first"));

        await _main.ShowCrashAsync(new InvalidOperationException("second"));
        _ui.CrashGate.SetResult(false);
        await first;

        Assert.Single(_ui.Crashes);
    }

    [AvaloniaFact]
    public async Task An_unexpected_failure_in_a_file_command_shows_the_crash_dialog()
    {
        _ui.PickFails = new NotSupportedException("unsupported entry");

        _main.Execute(CommandId.Open);
        await Task.Yield();

        Assert.Contains("unsupported entry", Assert.Single(_ui.Crashes).TechnicalDetails, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_failing_crash_dialog_is_reported_not_rethrown()
    {
        Action<GlobalExceptionHandler.ExceptionReport> original = GlobalExceptionHandler.Sink;
        var origins = new List<string>();
        GlobalExceptionHandler.Sink = report => origins.Add(report.Origin);
        try
        {
            GlobalExceptionHandler.ShowUiError(_ => throw new InvalidOperationException("dialog broke"), new InvalidOperationException("boom"));
        }
        finally
        {
            GlobalExceptionHandler.Sink = original;
        }

        Assert.Equal(["Crash dialog"], origins);
    }

    [AvaloniaFact]
    public void The_crash_dialog_renders_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var window = new CrashWindow(new CrashRequest(
                Strings.Crash_Title,
                Strings.Crash_Message,
                Strings.Crash_RecoveryWritten,
                "MindBuzz 0.9.1\nmacOS\n\nSystem.InvalidOperationException: boom"));
            Render.Save(window, theme, "crash");
        }
    }

    [AvaloniaFact]
    public void The_crash_dialog_with_its_details_open_renders_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var window = new CrashWindow(new CrashRequest(
                Strings.Crash_Title,
                Strings.Crash_Message,
                Strings.Crash_Untitled,
                "MindBuzz 0.9.1\nDarwin 25.6.0 (Arm64)\n\nSystem.InvalidOperationException: boom\n   at MindBuzz.App.Canvas.MapCanvas.Render(DrawingContext context)\n   at Avalonia.Rendering.Composition.Server.ServerCompositionTarget.Render()"));
            window.FindControl<Expander>("Expander")!.IsExpanded = true;
            Render.Save(window, theme, "crash-details");
        }
    }
}
