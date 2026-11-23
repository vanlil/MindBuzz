using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MindBuzz.App.Canvas;
using MindBuzz.App.Commands;
using MindBuzz.App.Platform;
using MindBuzz.App.Services;
using MindBuzz.App.ViewModels;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Views;

public partial class MainWindow : Window, IPanelView, IMapExport
{
    /// <summary>
    /// The topic panel's width: when it first opens its narrowest, where the notes toolbar, Edit/Preview and Help just
    /// fit on one row. Afterwards it keeps the width it was given.
    /// </summary>
    private double _panelWidth;

    /// <summary>The search panel's width when it first opens; then whatever the splitter left it at, for the
    /// session.</summary>
    private double _searchWidth = 300;

    /// <summary>The designer's constructor. The app uses the others.</summary>
    public MainWindow()
        : this(Host.Current, shell: null)
    {
    }

    internal MainWindow(AppOptions options)
        : this(Host.Current, shell: null, options)
    {
    }

    /// <summary>
    /// Builds the window for <paramref name="platform"/>. Tests pass a platform other than the host's to render
    /// another OS's arrangement, a fake shell so no browser opens, and fake dialogs where a test needs answers.
    /// </summary>
    internal MainWindow(HostPlatform platform, IPlatformShell? shell, AppOptions? options = null, IUiServices? ui = null)
    {
        options ??= new AppOptions();
        InitializeComponent();

        // The narrowest detail view holds the notes header on one row, whose words are longer in some languages; the
        // window leaves room for it beside the narrowest map and search panel.
        Grid notesHeader = TopicPanelView.FindControl<Grid>("NotesHeader")!;
        notesHeader.Measure(Size.Infinity);
        MinPanelWidth = Math.Max(EnglishMinPanelWidth, Math.Ceiling(notesHeader.DesiredSize.Width));
        MinWidth = Math.Max(MinWidth, MinCanvasWidth + MinSearchWidth + MinPanelWidth);
        _panelWidth = MinPanelWidth;

        var main = new MainViewModel(ui ?? new AvaloniaUiServices(this), shell ?? new PlatformShell(), platform);

        // A file on the command line opens once the window is up: opening may need a dialog (recovery offer,
        // problems), and a dialog needs a visible owner.
        if (options.FilePath is { } path)
        {
            Opened += async (_, _) => await main.OpenFromOutsideAsync(path, askFirst: false).ConfigureAwait(true);
        }

        main.MapView = Canvas;
        main.PanelView = this;
        main.MapExport = this;
        Canvas.ShowStats = options.ShowStats;
        DataContext = main;
        WireEditing(main);
        WirePanel(main);

        if (options.Benchmark)
        {
            Opened += (_, _) => CanvasBenchmark.Run(Canvas, report: Console.WriteLine, done: Close);
        }

        // In the background once the window is up; the answer, if any, appears in the status bar.
        if (options.CheckForUpdate)
        {
            Opened += async (_, _) => await main.CheckForUpdateAtStartAsync().ConfigureAwait(true);
        }

        ConfigureTitleStrip(TitleStripLayout.For(platform));
        BindKeys(main, platform);

        // Light Theme and Dark Theme grey out the one in use, also after the OS switched.
        ActualThemeVariantChanged += (_, _) => main.Commands.RefreshAll();

        if (platform == HostPlatform.MacOS)
        {
            // Only the window's top-level menus here; they appear in the system menu bar. The application menu,
            // where About lives, is set on the Application before this window exists. See MindBuzzApp.
            NativeMenu.SetMenu(this, NativeMenuFactory.Build(main.Menus.TopLevel, ActualThemeVariant));

            // A label changes with the document (Encrypt File → Change Password): the menu bar is built again.
            main.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Menus))
                {
                    NativeMenu.SetMenu(this, NativeMenuFactory.Build(main.Menus.TopLevel, ActualThemeVariant));
                }
            };

            // The colour pictures follow the theme. Posted, so nothing the native menu does can interrupt the theme
            // change still being raised through the window.
            ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (NativeMenu.GetMenu(this) is { } menu)
                {
                    NativeMenuFactory.Recolor(menu, main.Menus.TopLevel, ActualThemeVariant);
                }
            });
        }

        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsFullScreen))
            {
                ApplyFullScreen(main.IsFullScreen);
            }
        };

        PlaceOnPrimaryScreen();
        Opened += (_, _) => FitMenusToScreen();
        PositionChanged += (_, _) => FitMenusToScreen();
    }

    internal MainViewModel ViewModel => (MainViewModel)DataContext!;

    // --- distraction-free full screen --------------------------------------------------------------------------------

    /// <summary>The window's state before full screen, to go back to.</summary>
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    private bool _applyingFullScreen;

    /// <summary>
    /// Only the map: the title strip with its menu, the toolbar, the sheet tabs and the status bar go; search and the
    /// detail view stay where they are and still open and close. The window fills the screen.
    /// </summary>
    private void ApplyFullScreen(bool on)
    {
        _applyingFullScreen = true;
        try
        {
            TitleStrip.IsVisible = !on;
            ToolbarRow.IsVisible = !on;
            Tabs.IsVisible = !on;
            StatusBar.IsVisible = !on;
            if (on)
            {
                _stateBeforeFullScreen = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState;
                WindowState = WindowState.FullScreen;
            }
            else
            {
                WindowState = _stateBeforeFullScreen;
            }
        }
        finally
        {
            _applyingFullScreen = false;
        }
    }

    /// <summary>Leaving full screen the system's way (macOS's green button, the window manager) ends the mode
    /// too.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && !_applyingFullScreen && DataContext is MainViewModel { IsFullScreen: true } main
            && change.GetNewValue<WindowState>() != WindowState.FullScreen)
        {
            main.IsFullScreen = false;
        }
    }

    /// <summary>Gives the keyboard back to the map, e.g. after renaming a tab.</summary>
    /// <summary>Back to the map. A maximized panel returns to its normal size first, or the map would stay
    /// hidden.</summary>
    public void FocusCanvas()
    {
        if (ViewModel.Panel.IsMaximized)
        {
            ViewModel.Panel.IsMaximized = false;
        }

        Canvas.Focus();
    }

    /// <summary>
    /// Closing with unsaved changes asks "Save changes?" first. The close is cancelled, the
    /// question asked, and the window closed again if the answer allows it; the second close passes through.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        (DataContext as MainViewModel)?.CommitPendingEdits();
        if (DataContext is MainViewModel main && !main.CanCloseWithoutAsking)
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (await main.ConfirmCloseAsync().ConfigureAwait(true))
                {
                    Close();
                }
            });
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as MainViewModel)?.Dispose();
    }

    /// <summary>
    /// Applies the platform's title-strip layout. The decision itself lives in <see cref="TitleStripLayout.For"/>,
    /// where it is tested for platforms this machine is not.
    /// </summary>
    private void ConfigureTitleStrip(TitleStripLayout layout)
    {
        ExtendClientAreaToDecorationsHint = layout.ExtendClientArea;
        ExtendClientAreaTitleBarHeightHint = layout.ExtendClientArea ? layout.Height : -1;

        TitleStrip.Height = layout.Height;
        TitleContent.Margin = layout.ContentMargin;
        TitleText.IsVisible = layout.ShowTitleText;
        WindowMenu.IsVisible = layout.ShowInWindowMenu;
        WindowMenu.SizeChanged += (_, _) => PlaceTitle();
        TitleContent.SizeChanged += (_, _) => PlaceTitle();
        TitleText.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty)
            {
                PlaceTitle();
            }
        };

        // Avalonia 12: the strip is a drag area (move, double-click to zoom or maximise) and the menu inside
        // it still takes clicks. Element roles only take effect while the client area is extended, so on Linux
        // these are inert and the window manager's own title bar does the job.
        WindowDecorationProperties.SetElementRole(TitleStrip, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(WindowMenu, WindowDecorationsElementRole.User);
    }

    /// <summary>
    /// Puts the window's name centred over the whole strip when it fits there beside the menu, else right after the
    /// menu with a gap, where trimming shortens it only if even that is too little room.
    /// </summary>
    private void PlaceTitle()
    {
        const double gap = 16;
        double menu = WindowMenu.IsVisible ? WindowMenu.Bounds.Width : 0;
        double total = TitleContent.Bounds.Width;
        var text = new FormattedText(
            TitleText.Text ?? string.Empty,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(TitleText.FontFamily, TitleText.FontStyle, TitleText.FontWeight, TitleText.FontStretch),
            TitleText.FontSize,
            null);

        double centred = ((total - text.Width) / 2) - menu;
        TitleText.Margin = new Thickness(Math.Max(menu > 0 ? gap : 0, centred), 0, 0, 0);
    }

    /// <summary>
    /// The key handlers, generated from the command registry: window-wide commands on the window,
    /// canvas commands (plain keys like Enter, Delete, arrows) only on the canvas, so they never fire while the user
    /// types in a text box or a dialog.
    /// </summary>
    private void BindKeys(MainViewModel main, HostPlatform platform)
    {
        foreach ((KeyGesture gesture, AppCommand command) in KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Window))
        {
            KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command.Command });
        }

        foreach ((KeyGesture gesture, AppCommand command) in KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Canvas))
        {
            Canvas.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command.Command });
        }

        foreach ((KeyGesture gesture, AppCommand command) in KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Panel))
        {
            TopicPanelView.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command.Command });
        }

        // Bold, italic and underline: in the panel's notes editor and title box, and in the map's in-place editor.
        foreach ((KeyGesture gesture, AppCommand command) in KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Text))
        {
            TopicPanelView.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command.Command });
            Canvas.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command.Command });
        }

        Canvas.SetFormatTooltips(main.TooltipOf(CommandId.Bold), main.TooltipOf(CommandId.Italic), main.TooltipOf(CommandId.Underline));
    }

    // --- the topic panel ---------------------------------------------------------------------------------------------

    public void FocusNotes(bool caretAtEnd = false) => TopicPanelView.FocusNotes(caretAtEnd);

    /// <summary>
    /// F6 and Shift+F6: the map, the search panel and the detail view in turn, left to right or back, skipping a panel
    /// that is closed.
    /// </summary>
    public void CyclePane(bool forward)
    {
        var panes = new List<Action> { () => { ViewModel.Panel.IsMaximized = false; Canvas.Focus(NavigationMethod.Tab); } };
        int current = 0;
        if (ViewModel.Search.IsOpen)
        {
            panes.Add(SearchPanelView.FocusQuery);
            current = SearchPanelView.IsKeyboardFocusWithin ? panes.Count - 1 : current;
        }

        // The detail view is always in the cycle; reaching it opens it if it is closed.
        panes.Add(() => ViewModel.OpenNotes());
        current = ViewModel.Panel.IsOpen && TopicPanelView.IsKeyboardFocusWithin ? panes.Count - 1 : current;

        panes[(current + (forward ? 1 : -1) + panes.Count) % panes.Count]();
    }

    public void FocusSearch() => SearchPanelView.FocusQuery();

    /// <summary>Draws the map in the hidden export spot and writes the PNG atomically, like a document.</summary>
    public async Task<bool> CopyImageAsync(Core.Model.Map map)
    {
        if (Clipboard is not { } clipboard)
        {
            return false;
        }

        // Not disposed here: a clipboard may read the picture only when another app pastes it.
#pragma warning disable CA2000
        Avalonia.Media.Imaging.RenderTargetBitmap bitmap = MindBuzz.App.Canvas.MapExport.Render(ExportHost, map, whiteBackground: true);
#pragma warning restore CA2000
        await Avalonia.Input.Platform.ClipboardExtensions.SetBitmapAsync(clipboard, bitmap).ConfigureAwait(true);
        return true;
    }

    public void SavePng(Core.Model.Map map, string path, bool whiteBackground)
    {
        using Avalonia.Media.Imaging.RenderTargetBitmap bitmap = MindBuzz.App.Canvas.MapExport.Render(ExportHost, map, whiteBackground);
        Core.Serialization.AtomicFile.Write(path, stream => bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions()));
    }

    public void ShowNoteMatch(int start, int length) => TopicPanelView.ShowNoteMatch(start, length);

    public void WrapSelection(string marker) => TopicPanelView.WrapSelection(marker);

    public void ToggleTitleStyle(TitleStyle style) => TopicPanelView.ToggleTitleStyle(style);

    public void CommitPendingText()
    {
        TopicPanelView.CommitPendingTitle();
        Tabs.CommitPendingRename();
    }

    public void FocusLinks() => TopicPanelView.FocusLinks();

    /// <summary>The narrowest the map, the search pane and the detail view may be dragged.</summary>
    internal const double MinCanvasWidth = 240;

    internal const double MinSearchWidth = 220;

    /// <summary>
    /// The narrowest topic panel in English: the notes header (toolbar, Edit/Preview, Help) on one row, and the
    /// window's
    /// 980 px with the map and search panel. NotesToolbarTests measures the header and fails when this no longer fits
    /// it.
    /// </summary>
    internal const double EnglishMinPanelWidth = 508;

    /// <summary>
    /// The narrowest topic panel in the language in use: the English width, or more where the notes header's words are
    /// longer (German needs about 553 px), measured when the window is built.
    /// </summary>
    internal double MinPanelWidth { get; }

    private void WirePanel(MainViewModel main)
    {
        SearchSplitter.Dragged += (_, dx) => ResizeSearchAgainstCanvas(dx);
        PanelSplitter.Dragged += (_, dx) =>
        {
            if (main.Search.IsOpen)
            {
                ResizeBetweenSearchAndPanel(dx);
            }
            else
            {
                ResizePanelAgainstCanvas(dx);
            }
        };

        main.Panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TopicPanelViewModel.IsOpen) or nameof(TopicPanelViewModel.IsMaximized))
            {
                ShowPanel(main.Panel.IsOpen, main.Panel.IsMaximized);
            }
        };

        main.Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.IsOpen))
            {
                ShowSearch(main.Search.IsOpen);
            }
        };
    }

    /// <summary>
    /// Opens or closes the panel's column. Closing remembers the width the splitter left it at, for the next time it
    /// opens in this session; nothing is persisted.
    /// </summary>
    /// <remarks>
    /// Maximized, the panel takes the canvas's place beside the toolbar and above the sheet tabs;
    /// the canvas is hidden, not resized, so its zoom and scroll are unchanged when it comes back.
    /// </remarks>
    private void ShowPanel(bool open, bool maximized)
    {
        ColumnDefinition canvasColumn = Workspace.ColumnDefinitions[0];
        ColumnDefinition column = Workspace.ColumnDefinitions[4];
        if (column.Width.IsAbsolute)
        {
            _panelWidth = column.Width.Value;
        }

        if (open && maximized)
        {
            canvasColumn.Width = new GridLength(0);
            column.MinWidth = MinPanelWidth;
            column.Width = new GridLength(1, GridUnitType.Star);
        }
        else if (open)
        {
            canvasColumn.Width = new GridLength(1, GridUnitType.Star);
            column.MinWidth = MinPanelWidth;
            column.Width = new GridLength(_panelWidth);
        }
        else
        {
            canvasColumn.Width = new GridLength(1, GridUnitType.Star);
            column.MinWidth = 0;
            column.Width = GridLength.Auto;
        }

        Canvas.IsVisible = !(open && maximized);
        PanelSplitter.IsVisible = open && !maximized;
        TopicPanelView.IsVisible = open;
    }

    /// <summary>The divider left of the search pane: search grows to the left, the map gives way.</summary>
    private void ResizeSearchAgainstCanvas(double dx)
    {
        ColumnDefinition search = Workspace.ColumnDefinitions[2];
        double spare = Workspace.ColumnDefinitions[0].ActualWidth - MinCanvasWidth;
        double width = Math.Clamp(search.ActualWidth - dx, MinSearchWidth, Math.Max(MinSearchWidth, search.ActualWidth + spare));
        search.Width = new GridLength(width);
        _searchWidth = width;
    }

    /// <summary>The divider left of the detail view with search closed: the detail view against the map.</summary>
    private void ResizePanelAgainstCanvas(double dx)
    {
        if (ViewModel.Panel.IsMaximized)
        {
            return;
        }

        ColumnDefinition panel = Workspace.ColumnDefinitions[4];
        double spare = Workspace.ColumnDefinitions[0].ActualWidth - MinCanvasWidth;
        double width = Math.Clamp(panel.ActualWidth - dx, MinPanelWidth, Math.Max(MinPanelWidth, panel.ActualWidth + spare));
        panel.Width = new GridLength(width);
        _panelWidth = width;
    }

    /// <summary>The divider between search and the detail view: one gains what the other loses; the map
    /// stays.</summary>
    private void ResizeBetweenSearchAndPanel(double dx)
    {
        if (ViewModel.Panel.IsMaximized)
        {
            return;
        }

        ColumnDefinition search = Workspace.ColumnDefinitions[2];
        ColumnDefinition panel = Workspace.ColumnDefinitions[4];
        double total = search.ActualWidth + panel.ActualWidth;
        double searchWidth = Math.Clamp(search.ActualWidth + dx, MinSearchWidth, total - MinPanelWidth);
        search.Width = new GridLength(searchWidth);
        panel.Width = new GridLength(total - searchWidth);
        _searchWidth = searchWidth;
        _panelWidth = total - searchWidth;
    }

    /// <summary>Opens or closes the search panel's column, keeping its width for the session like the detail
    /// view's.</summary>
    private void ShowSearch(bool open)
    {
        ColumnDefinition column = Workspace.ColumnDefinitions[2];
        if (column.Width.IsAbsolute)
        {
            _searchWidth = column.Width.Value;
        }

        column.MinWidth = open ? 220 : 0;
        column.Width = open ? new GridLength(_searchWidth) : GridLength.Auto;
        SearchSplitter.IsVisible = open;
        SearchPanelView.IsVisible = open;
    }

    /// <summary>
    /// The in-place editor: the canvas reports what happens in its text box, the view model decides what it means, and
    /// the canvas shows or hides the box when the view model's editing state changes.
    /// </summary>
    private void WireEditing(MainViewModel main)
    {
        Canvas.TextTyped += (_, text) => main.BeginEdit(EditStart.Replace, text);
        Canvas.TopicDoubleClicked += (_, _) => main.BeginEdit(EditStart.CaretAtEnd);
        Canvas.EditTextChanged += (_, text) => main.UpdateEditText(text);
        Canvas.EditCommitted += (_, text) => main.CommitEdit(text);
        Canvas.EditCancelled += (_, _) => main.CancelEdit();
        Canvas.HandleClicked += (_, topic) => main.ToggleCollapsed(topic);
        Canvas.LinkTyped += (_, text) => main.LinkType(text);
        Canvas.IndicatorClicked += (_, click) =>
        {
            if (click.Kind == IndicatorKind.Link)
            {
                main.LinkIndicatorClicked(click.Topic, click.Direct);
            }
            else
            {
                main.NoteIndicatorClicked(click.Topic);
            }
        };
        Canvas.RelationshipBent += (_, bent) => main.BendRelationship(bent.Relationship, bent.Bend);
        Canvas.LinkPicked += (_, pick) => main.LinkPick(pick.Target, pick.TwoWay);
        Canvas.LinkKeyPressed += (_, key) =>
        {
            switch (key)
            {
                case LinkKey.Up: main.LinkMove(NavigationMove.Up); break;
                case LinkKey.Down: main.LinkMove(NavigationMove.Down); break;
                case LinkKey.Left: main.LinkMove(NavigationMove.Left); break;
                case LinkKey.Right: main.LinkMove(NavigationMove.Right); break;
                case LinkKey.Commit: main.LinkCommit(twoWay: false); break;
                case LinkKey.CommitTwoWay: main.LinkCommit(twoWay: true); break;
                case LinkKey.Cancel: main.LinkCancel(); break;
                case LinkKey.Backspace: main.LinkBackspace(); break;
            }
        };
        Canvas.TopicDropped += (_, drop) => main.MoveByDrag(drop.Topic, drop.NewParent, drop.Index, drop.Side);
        Canvas.TopicToggled += (_, topic) => main.ToggleSelected(topic);

        // Esc on the map, with nothing else to cancel, leaves distraction-free full screen.
        Canvas.EscapeUnused = () =>
        {
            if (!main.IsFullScreen)
            {
                return false;
            }

            main.IsFullScreen = false;
            return true;
        };
        Canvas.SelectOnlyRequested += (_, topic) => main.SelectOnly(topic);

        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.EditingTopic))
            {
                return;
            }

            if (main.EditingTopic is { } topic)
            {
                Canvas.OpenEditor(topic, main.EditText, main.EditStartMode);
            }
            else
            {
                Canvas.HideEditor();
            }
        };

        // The canvas has the keyboard from the start: a new document is ready to type into.
        Opened += (_, _) => Canvas.Focus();
    }

    /// <summary>
    /// Opens centred on the primary monitor at 1280x800, clamped to the working area so the window never lands
    /// off-screen: there is no remembered geometry to fall back on.
    /// </summary>
    /// <summary>
    /// Menus may be as tall as the screen the window is on. Semi caps every menu at 400 px and scrolls the rest, which
    /// cut the Topic menu (about 670 px) short with plenty of room below it (seen on Windows).
    /// </summary>
    private void FitMenusToScreen()
    {
        if (Screens.ScreenFromWindow(this) is { } screen && Application.Current is { } app)
        {
            double height = (screen.WorkingArea.Height / screen.Scaling) - 48;
            if (!app.Resources.TryGetValue("MenuFlyoutMaxHeight", out object? current) || current is not double known || Math.Abs(known - height) > 0.5)
            {
                app.Resources["MenuFlyoutMaxHeight"] = height;
            }
        }
    }

    private void PlaceOnPrimaryScreen()
    {
        Screen? screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        PixelRect working = screen.WorkingArea;
        double scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;

        // The frame is not measured yet, so leave room for a system title bar rather than let the status bar
        // slide under the taskbar on a screen that is exactly 1280x800 of working area.
        const double FrameAllowance = 48;

        double width = Math.Min(Width, (working.Width / scaling) - 8);
        double height = Math.Min(Height, (working.Height / scaling) - FrameAllowance);
        Width = width;
        Height = height;

        var size = new PixelSize((int)Math.Round(width * scaling), (int)Math.Round(height * scaling));
        int x = working.X + ((working.Width - size.Width) / 2);
        int y = working.Y + ((working.Height - size.Height) / 2);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(
            Math.Clamp(x, working.X, Math.Max(working.X, working.Right - size.Width)),
            Math.Clamp(y, working.Y, Math.Max(working.Y, working.Bottom - size.Height)));
    }
}
