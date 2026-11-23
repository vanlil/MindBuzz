using Avalonia.Controls;
using Avalonia.Interactivity;
using MindBuzz.App.Services;

namespace MindBuzz.App.Views;

/// <summary>The keyboard shortcuts dialog (Help → Keyboard Shortcuts, F1).</summary>
public partial class ShortcutsWindow : Window
{
    /// <summary>The designer's constructor.</summary>
    public ShortcutsWindow()
    {
        InitializeComponent();
    }

    public ShortcutsWindow(IReadOnlyList<ShortcutRow> rows)
        : this()
    {
        Rows.ItemsSource = rows;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
