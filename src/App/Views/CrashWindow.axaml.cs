using Avalonia.Controls;
using Avalonia.Input.Platform;
using MindBuzz.App.Services;

namespace MindBuzz.App.Views;

/// <summary>
/// The crash dialog. Returns true for Quit; Continue, Esc and the window's close button return false, because going on
/// working is the safe default after an error the app survived.
/// </summary>
public partial class CrashWindow : Window
{
    /// <summary>The designer's constructor.</summary>
    public CrashWindow()
    {
        InitializeComponent();
    }

    public CrashWindow(CrashRequest request)
        : this()
    {
        ArgumentNullException.ThrowIfNull(request);
        DataContext = request;

        ContinueButton.Click += (_, _) => Close(false);
        QuitButton.Click += (_, _) => Close(true);
        CopyButton.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(request.TechnicalDetails).ConfigureAwait(true);
            }
        };
        Opened += (_, _) => ContinueButton.Focus();
    }
}
