using Avalonia.Controls;
using Avalonia.Interactivity;
using MindBuzz.App.Services;

namespace MindBuzz.App.Views;

/// <summary>
/// The modal used for messages such as About. It holds no strings of its own: every word arrives
/// pre-formatted in a <see cref="MessageRequest"/>.
/// </summary>
public partial class DialogWindow : Window
{
    /// <summary>The designer's constructor.</summary>
    public DialogWindow()
    {
        InitializeComponent();
    }

    public DialogWindow(MessageRequest request)
        : this()
    {
        DataContext = request;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Runs whatever the request said a click on the link should do. The view has no idea that this ends up at
    /// the platform shell, and no idea what a URL is.
    /// </summary>
    private void OnOpenLink(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessageRequest { OpenLink: { } open })
        {
            open();
        }
    }
}
