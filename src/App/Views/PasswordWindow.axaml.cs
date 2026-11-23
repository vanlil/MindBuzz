using System.Globalization;
using Avalonia.Controls;
using MindBuzz.App.Localization;
using MindBuzz.App.Services;
using MindBuzz.Core.Serialization;

namespace MindBuzz.App.Views;

/// <summary>
/// The password dialog. For a new password, OK stays disabled until it has at least
/// <see cref="MbzCrypto.MinPasswordLength"/> characters and the repetition matches, and the hint says why. Returns the
/// password, or null when cancelled.
/// </summary>
public partial class PasswordWindow : Window
{
    private readonly PasswordRequest? _request;

    /// <summary>The designer's constructor.</summary>
    public PasswordWindow()
    {
        InitializeComponent();
    }

    public PasswordWindow(PasswordRequest request)
        : this()
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        DataContext = request;

        PasswordBox.TextChanged += (_, _) => Validate();
        RepeatBox.TextChanged += (_, _) => Validate();
        OkButton.Click += (_, _) =>
        {
            if (Result() is { } password)
            {
                Close(password);
            }
        };
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => PasswordBox.Focus();
        Validate();
    }

    /// <summary>The password the fields hold, or null while they do not make a valid one.</summary>
    internal string? Result()
    {
        string password = PasswordBox.Text ?? string.Empty;
        if (_request is not { IsNew: true })
        {
            return password.Length > 0 ? password : null;
        }

        return password.Length >= MbzCrypto.MinPasswordLength && password == (RepeatBox.Text ?? string.Empty) ? password : null;
    }

    private void Validate()
    {
        OkButton.IsEnabled = Result() is not null;
        if (_request is null)
        {
            return;
        }

        string password = PasswordBox.Text ?? string.Empty;
        string repeat = RepeatBox.Text ?? string.Empty;
        (string text, bool warning) = _request switch
        {
            { Error: { } error } when password.Length == 0 => (error, true),
            { IsNew: false } => (string.Empty, false),
            _ when password.Length < MbzCrypto.MinPasswordLength =>
                (string.Format(CultureInfo.CurrentCulture, Strings.Password_TooShortFormat, MbzCrypto.MinPasswordLength), password.Length > 0),
            _ when repeat.Length > 0 && repeat != password => (Strings.Password_Mismatch, true),
            _ => (string.Empty, false),
        };

        Hint.Text = text;
        Hint.IsVisible = text.Length > 0;
        Hint.Classes.Set("warning", warning);
    }
}
