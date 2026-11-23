using Avalonia.Headless.XUnit;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// New, Open, Save, the save prompt and the recovery offer, through the view model with fake
/// dialogs and real files in a temporary folder.
/// </summary>
public sealed class FileTests : IDisposable
{
    // Answer indices, as the view model lays out its buttons.
    private const int DontSave = 0;
    private const int Cancel = 1;
    private const int Save = 2;
    private const int OpenCopy = 0;
    private const int KeepDocument = 1;
    private const int Restore = 2;

    private readonly string _folder = Directory.CreateTempSubdirectory("mindbuzz-files-").FullName;
    private readonly FakeUiServices _ui = new();
    private readonly MainViewModel _main;

    public FileTests()
    {
        _main = new MainViewModel(_ui, new FakePlatformShell(), HostPlatform.MacOS);
    }

    public void Dispose()
    {
        _main.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private string PathOf(string name) => Path.Join(_folder, name);

    private void Edit(string text)
    {
        _main.Select(_main.ActiveMap.Root.Id);
        _main.Apply(new SetTopicText(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, text));
    }

    private static Document Doc(string text) => new([new Map(new MapId("M"), new Topic(new TopicId("T"), text))]);

    private async Task Run(CommandId command)
    {
        _main.Execute(command);
        await Task.Yield();
    }

    [AvaloniaFact]
    public async Task Saving_an_untitled_document_asks_for_a_file_and_then_it_is_clean()
    {
        Edit("Plan");
        Assert.Equal("Untitled*", _main.WindowTitle);
        _ui.PickedPaths.Enqueue(PathOf("Plan"));

        await Run(CommandId.Save);

        Assert.Equal(PathOf("Plan.mbz"), _main.FilePath);
        Assert.False(_main.IsDirty);
        Assert.Equal("Plan", _main.WindowTitle);
        Assert.Equal("Plan", MbzFile.Load(PathOf("Plan.mbz")).Document!.Maps[0].Root.Text);
    }

    [AvaloniaFact]
    public async Task The_title_shows_the_name_and_the_unsaved_marker_and_undo_to_the_saved_state_clears_it()
    {
        _ui.PickedPaths.Enqueue(PathOf("Ideas.mbz"));
        await Run(CommandId.Save);
        Assert.Equal("Ideas", _main.WindowTitle);

        Edit("changed");
        Assert.Equal("Ideas*", _main.WindowTitle);

        _main.Execute(CommandId.Undo);
        Assert.Equal("Ideas", _main.WindowTitle);
    }

    [AvaloniaTheory]
    [InlineData(Cancel, false, "edited")]
    [InlineData(DontSave, true, "Central Topic")]
    public async Task New_with_unsaved_changes_asks_first(int answer, bool replaced, string centralAfter)
    {
        Edit("edited");
        _ui.Answers.Enqueue(answer);

        await Run(CommandId.New);

        Assert.Single(_ui.Questions);
        Assert.Equal(replaced, !_main.IsDirty);
        Assert.Equal(centralAfter, _main.ActiveMap.Root.Text);
    }

    [AvaloniaFact]
    public async Task Choosing_save_in_the_prompt_saves_before_going_on()
    {
        Edit("keep this");
        _ui.Answers.Enqueue(Save);
        _ui.PickedPaths.Enqueue(PathOf("Kept.mbz"));

        await Run(CommandId.New);

        Assert.Equal("keep this", MbzFile.Load(PathOf("Kept.mbz")).Document!.Maps[0].Root.Text);
        Assert.Equal("Central Topic", _main.ActiveMap.Root.Text);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_dialog_in_the_prompt_cancels_the_whole_action()
    {
        Edit("edited");
        _ui.Answers.Enqueue(Save);
        _ui.PickedPaths.Enqueue(null);

        await Run(CommandId.New);

        Assert.Equal("edited", _main.ActiveMap.Root.Text);
        Assert.True(_main.IsDirty);
    }

    [AvaloniaFact]
    public async Task A_clean_document_is_replaced_without_asking()
    {
        await Run(CommandId.New);

        Assert.Empty(_ui.Questions);
    }

    [AvaloniaFact]
    public async Task Open_loads_the_file_with_a_fresh_history()
    {
        MbzFile.Save(PathOf("Other.mbz"), Doc("from disk"), "tests");
        Edit("discard me");
        _ui.Answers.Enqueue(DontSave);
        _ui.PickedPaths.Enqueue(PathOf("Other.mbz"));

        await Run(CommandId.Open);

        Assert.Equal("from disk", _main.ActiveMap.Root.Text);
        Assert.Equal(PathOf("Other.mbz"), _main.FilePath);
        Assert.False(_main.History.CanUndo);
    }

    [AvaloniaFact]
    public async Task A_file_from_a_newer_version_is_refused_with_both_version_numbers()
    {
        string path = PathOf("Future.mbz");
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(path), System.IO.Compression.ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("format.json").Open());
            await writer.WriteAsync("""{ "formatVersion": 9 }""");
        }

        await _main.OpenFromOutsideAsync(path, askFirst: false);

        string message = Assert.Single(_ui.Messages).Message;
        Assert.Contains("9", message, StringComparison.Ordinal);
        Assert.Contains(MbzFormat.CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
        Assert.Equal("Central Topic", _main.ActiveMap.Root.Text);
    }

    [AvaloniaFact]
    public async Task A_partly_damaged_file_opens_and_says_what_was_left_out()
    {
        string path = PathOf("Damaged.mbz");
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(path), System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var format = new StreamWriter(zip.CreateEntry("format.json").Open()))
            {
                await format.WriteAsync("""{ "formatVersion": 1 }""");
            }

            using var document = new StreamWriter(zip.CreateEntry("document.json").Open());
            await document.WriteAsync("""{ "maps": [ { "id": "M", "name": "N", "root": { "id": "R", "text": "ok", "links": [ { "url": "file:///etc" } ] } } ] }""");
        }

        await _main.OpenFromOutsideAsync(path, askFirst: false);

        Assert.Equal("ok", _main.ActiveMap.Root.Text);
        Assert.Contains("1", Assert.Single(_ui.Messages).Legal, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_restored_recovery_file_says_what_was_left_out_too()
    {
        string path = PathOf("Plan.mbz");
        MbzFile.Save(path, Doc("saved"), "tests");
        using (var zip = new System.IO.Compression.ZipArchive(File.Create(RecoveryFile.PathFor(path)), System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var format = new StreamWriter(zip.CreateEntry("format.json").Open()))
            {
                await format.WriteAsync("""{ "formatVersion": 1 }""");
            }

            using var document = new StreamWriter(zip.CreateEntry("document.json").Open());
            await document.WriteAsync("""{ "maps": [ { "id": "M", "root": { "id": "R", "text": "recovered", "links": [ { "url": "file:///etc" } ] } } ] }""");
        }

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        _ui.Answers.Enqueue(Restore);

        await _main.OpenFromOutsideAsync(path, askFirst: false);

        Assert.Equal("recovered", _main.ActiveMap.Root.Text);
        Assert.Contains("1", Assert.Single(_ui.Messages).Legal, StringComparison.Ordinal);
    }

    /// <summary>Windows and Linux have no menu bar of their own: the title names the app, its version and the
    /// file.</summary>
    [AvaloniaFact]
    public async Task On_windows_the_title_names_the_app_version_and_file()
    {
        using var main = new MainViewModel(_ui, new FakePlatformShell(), HostPlatform.Windows);
        string product = $"MindBuzz {MindBuzz.App.AppInfo.Version}";
        Assert.Equal($"{product} | untitled*", main.WindowTitle);

        _ui.PickedPaths.Enqueue(PathOf("Plan.mbz"));
        main.Execute(CommandId.Save);
        await Task.Yield();
        Assert.Equal($"{product} | Plan.mbz", main.WindowTitle);

        main.Apply(new SetTopicText(main.ActiveMap.Id, main.ActiveMap.Root.Id, "Changed"));
        Assert.Equal($"{product} | Plan.mbz*", main.WindowTitle);
    }

    private string SavedWithNewerRecovery()
    {
        string path = PathOf("Plan.mbz");
        MbzFile.Save(path, Doc("saved"), "tests");
        RecoveryFile.Write(path, Doc("recovered"), "tests", hide: false);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        return path;
    }

    [AvaloniaFact]
    public async Task Restore_loads_the_recovered_state_as_unsaved_changes_to_the_document()
    {
        string path = SavedWithNewerRecovery();
        _ui.Answers.Enqueue(Restore);

        await _main.OpenFromOutsideAsync(path, askFirst: false);

        Assert.Equal("recovered", _main.ActiveMap.Root.Text);
        Assert.Equal(path, _main.FilePath);
        Assert.True(_main.IsDirty);
    }

    [AvaloniaFact]
    public async Task Keep_document_opens_the_saved_file_and_deletes_the_recovery()
    {
        string path = SavedWithNewerRecovery();
        _ui.Answers.Enqueue(KeepDocument);

        await _main.OpenFromOutsideAsync(path, askFirst: false);

        Assert.Equal("saved", _main.ActiveMap.Root.Text);
        Assert.False(_main.IsDirty);
        Assert.True(SpinUntil(() => !File.Exists(RecoveryFile.PathFor(path))));
    }

    /// <summary>Esc or the dialog's close button may be an accident: it must not delete the only copy of the
    /// work.</summary>
    [AvaloniaFact]
    public async Task Dismissing_the_recovery_question_opens_the_document_and_keeps_the_recovery()
    {
        string path = SavedWithNewerRecovery();

        // No answer queued: the fake dialog answers as Esc does.
        await _main.OpenFromOutsideAsync(path, askFirst: false);

        Assert.Equal("saved", _main.ActiveMap.Root.Text);
        Assert.False(_main.IsDirty);
        await Task.Delay(200);
        Assert.True(File.Exists(RecoveryFile.PathFor(path)));
        Assert.True(RecoveryFile.HasNewerRecovery(path), "offered again next time");
    }

    [AvaloniaFact]
    public async Task Open_as_copy_is_untitled_and_leaves_both_files_alone()
    {
        string path = SavedWithNewerRecovery();
        _ui.Answers.Enqueue(OpenCopy);

        await _main.OpenFromOutsideAsync(path, askFirst: false);

        Assert.Equal("recovered", _main.ActiveMap.Root.Text);
        Assert.Null(_main.FilePath);
        Assert.True(_main.IsDirty);
        Assert.True(File.Exists(RecoveryFile.PathFor(path)));
        Assert.Equal("saved", MbzFile.Load(path).Document!.Maps[0].Root.Text);
    }

    [AvaloniaFact]
    public async Task Dont_save_deletes_the_recovery_file()
    {
        string path = PathOf("Plan.mbz");
        MbzFile.Save(path, Doc("saved"), "tests");
        await _main.OpenFromOutsideAsync(path, askFirst: false);
        Edit("changed");
        RecoveryFile.Write(path, Doc("changed"), "tests", hide: false);
        _ui.Answers.Enqueue(DontSave);

        Assert.True(await _main.ConfirmCloseAsync());

        Assert.True(SpinUntil(() => !File.Exists(RecoveryFile.PathFor(path))));
    }

    [AvaloniaFact]
    public async Task Closing_with_unsaved_changes_asks_and_cancel_keeps_the_window()
    {
        Edit("changed");
        Assert.False(_main.CanCloseWithoutAsking);
        _ui.Answers.Enqueue(Cancel);

        Assert.False(await _main.ConfirmCloseAsync());
        Assert.False(_main.CanCloseWithoutAsking);
    }

    /// <summary>File deletes run on the recovery queue; wait for the condition, never a fixed sleep.</summary>
    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5));
}
