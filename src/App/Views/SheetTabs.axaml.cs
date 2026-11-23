using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MindBuzz.App.Canvas;
using MindBuzz.App.ViewModels;

namespace MindBuzz.App.Views;

/// <summary>
/// The sheet tabs. A view of the main view model: clicks, renames and drags become calls to it;
/// the tabs themselves are its <see cref="MainViewModel.Tabs"/>.
/// </summary>
public partial class SheetTabs : UserControl
{
    /// <summary>The pointer must move this far before a press on a tab becomes a drag.</summary>
    private const double DragThreshold = 6;

    private MapTab? _pressed;
    private Point _pressPoint;
    private bool _dragging;
    private int _dropIndex = -1;
    private bool _renameDone;

    public SheetTabs()
    {
        InitializeComponent();

        // Show the scroll buttons only when the tabs do not fit, and keep the active tab in view.
        Scroller.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ExtentProperty || e.Property == BoundsProperty)
            {
                ScrollButtons.IsVisible = Scroller.Extent.Width > Scroller.Viewport.Width + 1;
            }
        };
        TabList.PropertyChanged += (_, e) =>
        {
            if (e.Property == ItemsControl.ItemsSourceProperty)
            {
                Dispatcher.UIThread.Post(BringActiveIntoView, DispatcherPriority.Background);
            }
        };
    }

    private MainViewModel? Main => DataContext as MainViewModel;

    /// <summary>The tab containers, for hit tests and placement: a test seam too.</summary>
    internal IReadOnlyList<Control> TabControls => [.. TabList.GetRealizedContainers()];

    private void BringActiveIntoView()
    {
        if (Main is { } main && main.Tabs.ToList().FindIndex(tab => tab.IsActive) is >= 0 and var index
            && TabList.ContainerFromIndex(index) is { } container)
        {
            container.BringIntoView();
        }
    }

    private void OnScrollLeft(object? sender, RoutedEventArgs e) => Scroller.Offset = Scroller.Offset.WithX(Math.Max(0, Scroller.Offset.X - 160));

    private void OnScrollRight(object? sender, RoutedEventArgs e) => Scroller.Offset = Scroller.Offset.WithX(Scroller.Offset.X + 160);

    // --- click, double-click and drag --------------------------------------------------------------------------------

    private void OnTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MapTab tab } control || Main is not { } main || tab.IsRenaming)
        {
            return;
        }

        PointerPointProperties properties = e.GetCurrentPoint(control).Properties;
        if (properties.IsRightButtonPressed)
        {
            // Right-click activates the tab first, so the context menu acts on it.
            main.ActivateMap(tab.Id);
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            main.BeginRenameMap(tab.Id);
            e.Handled = true;
            return;
        }

        main.ActivateMap(tab.Id);
        _pressed = tab;
        _pressPoint = e.GetPosition(TabList);
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnTabMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null)
        {
            return;
        }

        Point point = e.GetPosition(TabList);
        if (!_dragging && Math.Abs(point.X - _pressPoint.X) < DragThreshold)
        {
            return;
        }

        _dragging = true;
        _dropIndex = DropIndex(point.X);
        ShowDropMarker(_dropIndex);
    }

    private void OnTabReleased(object? sender, PointerReleasedEventArgs e)
    {
        MapTab? pressed = _pressed;
        bool dragged = _dragging;
        int index = _dropIndex;

        _pressed = null;
        _dragging = false;
        _dropIndex = -1;
        DropMarker.IsVisible = false;
        e.Pointer.Capture(null);

        if (dragged && pressed is not null && Main is { } main)
        {
            // The marker index counts the dragged tab itself; MoveMap counts after it has left its place.
            int from = main.Tabs.ToList().FindIndex(tab => tab.Id == pressed.Id);
            int target = index > from ? index - 1 : index;
            if (target != from)
            {
                main.MoveMapTo(pressed.Id, target);
            }
        }
    }

    /// <summary>The gap a drag at <paramref name="x"/> points at: 0 before the first tab, n after the last.</summary>
    private int DropIndex(double x)
    {
        IReadOnlyList<Control> tabs = TabControls;
        for (int i = 0; i < tabs.Count; i++)
        {
            Rect bounds = tabs[i].Bounds;
            if (x < bounds.X + (bounds.Width / 2))
            {
                return i;
            }
        }

        return tabs.Count;
    }

    private void ShowDropMarker(int index)
    {
        IReadOnlyList<Control> tabs = TabControls;
        if (tabs.Count == 0)
        {
            return;
        }

        double x = index < tabs.Count ? tabs[index].Bounds.X : tabs[^1].Bounds.Right;
        Avalonia.Controls.Canvas.SetLeft(DropMarker, x - 1);
        DropMarker.Height = tabs[0].Bounds.Height;
        DropMarker.IsVisible = true;
    }

    // --- context menu ------------------------------------------------------------------------------------------------

    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || Main is not { } main)
        {
            return;
        }

        var menu = new ContextMenu { ItemsSource = MapCanvas.BuildMenuItems(main.TabMenu) };
        menu.Open(control);
        e.Handled = true;
    }

    // --- rename in place ---------------------------------------------------------------------------------------------

    private void OnEditorAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox editor)
        {
            editor.PropertyChanged += (_, change) =>
            {
                if (change.Property == IsVisibleProperty && editor.IsVisible)
                {
                    StartRename(editor);
                }
            };

            if (editor.IsVisible)
            {
                StartRename(editor);
            }
        }
    }

    private void StartRename(TextBox editor)
    {
        _renameDone = false;
        editor.Text = (editor.DataContext as MapTab)?.FullName;
        Dispatcher.UIThread.Post(
            () =>
            {
                editor.Focus();
                editor.SelectAll();
            },
            DispatcherPriority.Background);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            FinishRename(editor, commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FinishRename(editor, commit: false);
            e.Handled = true;
        }
    }

    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } editor)
        {
            FinishRename(editor, commit: true);
        }
    }

    /// <summary>Commits a rename in progress (Save, Close, New, Open).</summary>
    internal void CommitPendingRename()
    {
        if (!_renameDone && this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsVisible) is { } editor)
        {
            FinishRename(editor, commit: true);
        }
    }

    private void FinishRename(TextBox editor, bool commit)
    {
        if (_renameDone || Main is not { } main)
        {
            return;
        }

        _renameDone = true;
        if (commit)
        {
            main.CommitRenameMap(editor.Text ?? string.Empty);
        }
        else
        {
            main.CancelRenameMap();
        }

        // Back to the map: the keyboard belongs to the canvas.
        (TopLevel.GetTopLevel(this) as MainWindow)?.FocusCanvas();
    }
}
