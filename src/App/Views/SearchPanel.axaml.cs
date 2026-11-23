using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MindBuzz.App.ViewModels;

namespace MindBuzz.App.Views;

/// <summary>
/// The search panel's view: a thin layer over <see cref="SearchViewModel"/> for the keys. In the box,
/// Enter and Shift+Enter step through the hits and Down moves to the list; in the list, Enter and a click go to the
/// hit; Esc anywhere closes the panel.
/// </summary>
public partial class SearchPanel : UserControl
{
    public SearchPanel()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Results.AddHandler(PointerReleasedEvent, (_, _) => Model?.GoToSelected(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private SearchViewModel? Model => DataContext as SearchViewModel;

    /// <summary>The keyboard in the box with its text selected, once the panel is laid out.</summary>
    internal void FocusQuery() => Dispatcher.UIThread.Post(
        () =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        },
        DispatcherPriority.Loaded);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        bool inBox = QueryBox.IsKeyboardFocusWithin;
        bool inList = Results.IsKeyboardFocusWithin;
        switch (e.Key)
        {
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                model.CloseCommand.Execute(null);
                break;
            case Key.Enter when inBox:
                model.Step(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
            case Key.Enter when inList && e.Source is not Button:
                model.GoToSelected();
                break;
            case Key.Down when inBox && model.Rows.Count > 0:
                if (model.SelectedIndex < 0)
                {
                    model.SelectedIndex = model.Rows.Count > 1 && model.Rows[0] is SearchMapRow ? 1 : 0;
                }

                Dispatcher.UIThread.Post(() => (Results.ContainerFromIndex(model.SelectedIndex) as Control)?.Focus());
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
