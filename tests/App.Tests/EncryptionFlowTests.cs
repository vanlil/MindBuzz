using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Avalonia.Styling;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Encrypt File, Change Password, Remove Encryption and opening an encrypted file.</summary>
public sealed class EncryptionFlowTests : IDisposable
{
    private const string Secret = "correct horse";
    private const string Other = "battery staple";

    private readonly string _folder = Directory.CreateTempSubdirectory("mindbuzz-crypto-").FullName;
    private readonly FakeUiServices _ui = new();
    private readonly MainViewModel _main;

    public EncryptionFlowTests()
    {
        _main = new MainViewModel(_ui, new FakePlatformShell(), HostPlatform.MacOS);
    }

    public void Dispose()
    {
        _main.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private string PathOf(string name) => Path.Join(_folder, name);

    private async Task Run(CommandId command)
    {
        _main.Execute(command);
        await Task.Yield();
    }

    /// <summary>An unexpected failure during the save must not leave the new password in effect for an unencrypted
    /// file.</summary>
    [AvaloniaFact]
    public async Task A_save_that_throws_keeps_the_old_password()
    {
        _ui.Passwords.Enqueue(Secret);
        _ui.PickFails = new NotSupportedException("unexpected");

        await Run(CommandId.EncryptFile);

        Assert.False(_main.IsEncrypted);
        Assert.Single(_ui.Crashes);
    }

    private static string? FileLabel(MainViewModel main, CommandId id) =>
        main.Menus.TopLevel[0].Children!.FirstOrDefault(entry => entry.Command == main.Commands[id].Command)?.Header;

    private async Task<string> EncryptedFile()
    {
        _main.Apply(new SetTopicText(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, "Secret plan"));
        _ui.Passwords.Enqueue(Secret);
        _ui.PickedPaths.Enqueue(PathOf("Plan.mbz"));
        await Run(CommandId.EncryptFile);
        return PathOf("Plan.mbz");
    }

    [AvaloniaFact]
    public async Task Encrypting_an_untitled_document_asks_for_a_new_password_then_a_file_and_saves_it_encrypted()
    {
        string path = await EncryptedFile();

        Assert.True(_ui.PasswordRequests[0].IsNew);
        Assert.Equal(path, _main.FilePath);
        Assert.True(_main.IsEncrypted);
        Assert.False(_main.IsDirty);
        Assert.Equal(LoadFailure.PasswordRequired, MbzFile.Load(path).Failure);
        Assert.Equal("Secret plan", MbzFile.Load(path, Secret).Document!.Maps[0].Root.Text);
        Assert.Equal(MenuText.WithoutAccessKey(Localization.Strings.Command_ChangePassword), MenuText.WithoutAccessKey(FileLabel(_main, CommandId.EncryptFile)!));
        Assert.True(_main.Commands[CommandId.RemoveEncryption].Command.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Encrypting_removes_the_plain_recovery_file_or_says_where_it_is()
    {
        string path = PathOf("Plan.mbz");
        _ui.PickedPaths.Enqueue(path);
        await Run(CommandId.Save);
        _main.Apply(new SetTopicText(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, "Secret plan"));
        string recovery = RecoveryFile.PathFor(path);
        RecoveryFile.Write(path, _main.Document, "test", hide: false);

        // On macOS a locked file cannot be deleted; elsewhere only the delete itself is checked.
        bool locked = OperatingSystem.IsMacOS() && Chflags("uchg", recovery);
        try
        {
            _ui.Passwords.Enqueue(Secret);
            await Run(CommandId.EncryptFile);

            // The flow waits for the background delete: give it a moment instead of a seam.
            for (int i = 0; i < 100 && (locked ? _ui.Messages.Count == 0 : File.Exists(recovery)); i++)
            {
                await Task.Delay(20);
            }

            Assert.True(_main.IsEncrypted);
            if (locked)
            {
                Assert.Contains(recovery, Assert.Single(_ui.Messages).Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.False(File.Exists(recovery));
            }
        }
        finally
        {
            if (locked)
            {
                Chflags("nouchg", recovery);
            }
        }
    }

    private static bool Chflags(string flag, string path)
    {
        using var process = System.Diagnostics.Process.Start("chflags", [flag, path]);
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    [AvaloniaFact]
    public async Task Cancelling_the_save_dialog_leaves_the_document_unencrypted()
    {
        _ui.Passwords.Enqueue(Secret);
        _ui.PickedPaths.Enqueue(null);

        await Run(CommandId.EncryptFile);

        Assert.False(_main.IsEncrypted);
        Assert.Null(_main.FilePath);
        Assert.False(_main.Commands[CommandId.RemoveEncryption].Command.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Opening_an_encrypted_file_asks_for_the_password_until_it_is_right()
    {
        string path = await EncryptedFile();
        await Run(CommandId.New);
        Assert.False(_main.IsEncrypted);

        _ui.PickedPaths.Enqueue(path);
        _ui.Passwords.Enqueue("wrong one");
        _ui.Passwords.Enqueue(Secret);
        await Run(CommandId.Open);

        Assert.Equal("Secret plan", _main.ActiveMap.Root.Text);
        Assert.True(_main.IsEncrypted);
        Assert.Null(_ui.PasswordRequests[^2].Error);
        Assert.NotNull(_ui.PasswordRequests[^1].Error);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_password_does_not_open_the_file()
    {
        string path = await EncryptedFile();
        await Run(CommandId.New);

        _ui.PickedPaths.Enqueue(path);
        await Run(CommandId.Open);

        Assert.Null(_main.FilePath);
        Assert.False(_main.IsEncrypted);
    }

    [AvaloniaFact]
    public async Task Removing_encryption_needs_the_current_password_and_saves_the_file_plain()
    {
        string path = await EncryptedFile();

        _ui.Passwords.Enqueue("wrong one");
        _ui.Passwords.Enqueue(Secret);
        await Run(CommandId.RemoveEncryption);

        Assert.False(_main.IsEncrypted);
        Assert.True(MbzFile.Load(path).Succeeded);
        Assert.Equal(MenuText.WithoutAccessKey(Localization.Strings.Command_EncryptFile), MenuText.WithoutAccessKey(FileLabel(_main, CommandId.EncryptFile)!));
    }

    [AvaloniaFact]
    public async Task Change_password_asks_for_the_current_one_then_saves_with_the_new_one()
    {
        string path = await EncryptedFile();

        _ui.Passwords.Enqueue(Secret);
        _ui.Passwords.Enqueue(Other);
        await Run(CommandId.EncryptFile);

        Assert.Equal(LoadFailure.WrongPassword, MbzFile.Load(path, Secret).Failure);
        Assert.True(MbzFile.Load(path, Other).Succeeded);
        Assert.False(_ui.PasswordRequests[^2].IsNew);
        Assert.True(_ui.PasswordRequests[^1].IsNew);
    }

    [AvaloniaFact]
    public async Task The_recovery_copy_of_an_encrypted_document_is_encrypted_and_restores_with_the_password()
    {
        string path = await EncryptedFile();
        _main.Apply(new SetTopicText(_main.ActiveMap.Id, _main.ActiveMap.Root.Id, "Unsaved change"));
        _main.EmergencySave();
        string recovery = RecoveryFile.PathFor(path);

        Assert.Equal(LoadFailure.PasswordRequired, MbzFile.Load(recovery).Failure);
        File.SetLastWriteTimeUtc(recovery, DateTime.UtcNow.AddMinutes(1));

        // Another window opens the file after the "crash": password, then Restore.
        var ui = new FakeUiServices();
        using var other = new MainViewModel(ui, new FakePlatformShell(), HostPlatform.MacOS);
        ui.PickedPaths.Enqueue(path);
        ui.Passwords.Enqueue(Secret);
        ui.Answers.Enqueue(2);
        other.Execute(CommandId.Open);
        await Task.Yield();

        Assert.Equal("Unsaved change", other.ActiveMap.Root.Text);
        Assert.True(other.IsEncrypted);
        Assert.True(other.IsDirty);
    }

    [AvaloniaFact]
    public void A_new_password_needs_eight_characters_and_the_same_repetition()
    {
        var window = new PasswordWindow(new Services.PasswordRequest("Encrypt File", "Choose a password.", IsNew: true, "Encrypt"));
        var password = window.FindControl<Avalonia.Controls.TextBox>("PasswordBox")!;
        var repeat = window.FindControl<Avalonia.Controls.TextBox>("RepeatBox")!;

        password.Text = "short";
        repeat.Text = "short";
        Assert.Null(window.Result());

        password.Text = "long enough";
        repeat.Text = "long enougH";
        Assert.Null(window.Result());

        repeat.Text = "long enough";
        Assert.Equal("long enough", window.Result());
    }

    [AvaloniaFact]
    public void The_password_dialog_and_the_status_lock_render_in_both_themes()
    {
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            Render.Save(new PasswordWindow(new Services.PasswordRequest("Encrypt File", "Choose a password for “Plan”. The file is saved encrypted right away.", IsNew: true, "Encrypt")), theme, "password-new");
            var ui = new FakeUiServices();
            var s = new Session(ui: ui);
            ui.Passwords.Enqueue(Secret);
            ui.PickedPaths.Enqueue(PathOf($"Locked-{theme}.mbz"));
            s.Main.Execute(CommandId.EncryptFile);
            Session.Pump();
            Assert.True(s.Main.IsEncrypted);
            Render.Save(s.Window, theme, "status-encrypted");

            Render.Save(new PasswordWindow(new Services.PasswordRequest("Open Encrypted File", "“Plan.mbz” is encrypted. Enter its password to open it.", IsNew: false, "Open", "That password does not open the file. Try again.")), theme, "password-open");
        }
    }
}
