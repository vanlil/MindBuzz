using Avalonia.Controls;
using MindBuzz.App.Services;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Views;

/// <summary>
/// The add or edit dialog for an internet link. The address is trimmed and must pass <see cref="LinkUrl"/>; until it
/// does, the confirm button is disabled and the hint is shown in the warning colour.
/// Returns the link, or null when cancelled.
/// </summary>
public partial class LinkEditWindow : Window
{
    /// <summary>The designer's constructor.</summary>
    public LinkEditWindow()
    {
        InitializeComponent();
    }

    public LinkEditWindow(LinkEditRequest request)
        : this()
    {
        ArgumentNullException.ThrowIfNull(request);
        DataContext = request;
        UrlBox.Text = request.Url;
        TitleBox.Text = request.LinkTitle;

        UrlBox.TextChanged += (_, _) => Validate();
        OkButton.Click += (_, _) =>
        {
            if (Result() is { } link)
            {
                Close(link);
            }
        };
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
        };
        Validate();
    }

    /// <summary>The link the fields describe, or null while the address is not allowed.</summary>
    internal InternetLink? Result()
    {
        string url = (UrlBox.Text ?? string.Empty).Trim();
        return LinkUrl.IsAllowed(url) ? new InternetLink(url, (TitleBox.Text ?? string.Empty).Trim()) : null;
    }

    private void Validate()
    {
        bool valid = Result() is not null;
        OkButton.IsEnabled = valid;

        // An empty field is not an error yet; only something typed that cannot be linked is.
        Hint.Classes.Set("warning", !valid && !string.IsNullOrWhiteSpace(UrlBox.Text));
    }
}
