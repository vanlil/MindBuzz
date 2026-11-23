using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using MindBuzz.App.Localization;
using MindBuzz.App.Views;

namespace MindBuzz.App.Services;

/// <summary>The real <see cref="IUiServices"/>: dialogs owned by the main window, files through <see
/// cref="IStorageProvider"/>.</summary>
internal sealed class AvaloniaUiServices : IUiServices
{
    private readonly Window _owner;

    public AvaloniaUiServices(Window owner) => _owner = owner;

    /// <summary>The <c>.mbz</c> filter for the file dialogs.</summary>
    private static FilePickerFileType MindBuzzFiles => new(Strings.FileType_MindBuzz)
    {
        Patterns = ["*.mbz"],
        AppleUniformTypeIdentifiers = ["public.data"],
    };

    /// <summary>The <c>.png</c> filter for Export as PNG.</summary>
    private static FilePickerFileType PngFiles => new(Strings.FileType_Png)
    {
        Patterns = ["*.png"],
        AppleUniformTypeIdentifiers = ["public.png"],
        MimeTypes = ["image/png"],
    };

    public Task ShowMessageAsync(MessageRequest request) => new DialogWindow(request).ShowDialog(_owner);

    public Task ShowShortcutsAsync(IReadOnlyList<ShortcutRow> rows) => new ShortcutsWindow(rows).ShowDialog(_owner);

    public Task<int> AskAsync(ChoiceRequest request) => new ChoiceWindow(request).ShowDialog<int>(_owner);

    /// <summary>Owned by the topmost dialog, so the edit dialog opened from the link list sits on the list.</summary>
    public Task<Core.Model.InternetLink?> EditLinkAsync(LinkEditRequest request) =>
        new LinkEditWindow(request).ShowDialog<Core.Model.InternetLink?>(TopDialog());


    private Window TopDialog() => _owner.OwnedWindows.LastOrDefault(window => window.IsVisible) ?? _owner;

    public async Task<string?> PickOpenPathAsync()
    {
        IReadOnlyList<IStorageFile> files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Dialog_OpenTitle,
            AllowMultiple = false,
            FileTypeFilter = [MindBuzzFiles],
        }).ConfigureAwait(true);

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSavePathAsync(string suggestedName)
    {
        IStorageFile? file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Dialog_SaveTitle,
            SuggestedFileName = suggestedName,
            DefaultExtension = "mbz",
            FileTypeChoices = [MindBuzzFiles],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExportPathAsync(string suggestedName)
    {
        IStorageFile? file = await _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Export_SaveTitle,
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [PngFiles],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// The private clipboard format. An application format: Avalonia gives it a platform name of its own, so it
    /// travels between two MindBuzz processes on the same machine and no other app reads it.
    /// </summary>
    private static readonly DataFormat<string> ClipFormat = DataFormat.CreateStringApplicationFormat("mindbuzz-clip-v1");

    public async Task<bool> SetClipboardAsync(string clip, string text)
    {
        if (_owner.Clipboard is not { } clipboard)
        {
            return false;
        }

        var item = new DataTransferItem();
        item.Set(ClipFormat, clip);
        item.SetText(text);

        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data).ConfigureAwait(true);
        return true;
    }

    public async Task<ClipboardContent> GetClipboardAsync()
    {
        if (_owner.Clipboard is not { } clipboard)
        {
            return new ClipboardContent(null, null);
        }

        // The text too when there is a clip: a clip from a newer MindBuzz, or a damaged one, pastes its plain text
        // instead.
        string? clip = await clipboard.TryGetValueAsync(ClipFormat).ConfigureAwait(true);
        string? text = await clipboard.TryGetTextAsync().ConfigureAwait(true);
        return new ClipboardContent(clip, text);
    }

    public Task<string?> AskPasswordAsync(PasswordRequest request) =>
        new PasswordWindow(request).ShowDialog<string?>(TopDialog());

    /// <summary>Owned by the topmost dialog, so it shows even when the error came from inside another dialog.</summary>
    public async Task<bool> ShowCrashAsync(CrashRequest request) =>
        await new CrashWindow(request).ShowDialog<bool?>(TopDialog()).ConfigureAwait(true) == true;

    public void RequestExit() => _owner.Close();

    public bool ShowsDarkTheme() => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public void UseTheme(bool dark)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
