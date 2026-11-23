using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MindBuzz.App.Diagnostics;
using MindBuzz.App.Commands;
using MindBuzz.App.Localization;
using MindBuzz.App.Views;

namespace MindBuzz.App;

/// <summary>
/// The application. It persists nothing: no settings file, no registry key, no recent files, no remembered
/// window geometry. The only files it writes are the user's documents and their recovery
/// files.
/// </summary>
public sealed class MindBuzzApp : Application
{
    /// <summary>The commands behind the macOS application menu, kept to refresh their enabled state.</summary>
    private CommandRegistry? _applicationMenuCommands;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // macOS titles the application menu from this when the app runs unbundled, and from the bundle's
        // CFBundleName when bundled, hence both saying the same thing. Set here rather than in XAML so the
        // name still comes from Strings.resx.
        Name = Strings.AppTitle;

        // Here, not in OnFrameworkInitializationCompleted: Avalonia's macOS exporter reads this menu once, and
        // if it finds nothing it installs its own, the one with "About Avalonia", and never looks again.
        if (Host.Current == HostPlatform.MacOS)
        {
            SetMacApplicationMenu();
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // One document, one main window. Closing it quits, on macOS too: a document app
            // with no window has nothing to show, and nothing is remembered to reopen.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new MainWindow(AppOptions.Parse(desktop.Args ?? []));
            desktop.MainWindow = window;

            // The application menu was built before the window; ask its items again now that there is one.
            _applicationMenuCommands?.RefreshAll();

            // A crash writes the recovery file, never the document.
            GlobalExceptionHandler.EmergencySave = window.ViewModel.EmergencySave;
            GlobalExceptionHandler.UiError = exception => _ = window.ViewModel.ShowCrashAsync(exception);

            // Quitting (⌘Q on macOS, logging off) with unsaved changes asks first, like closing the window.
            desktop.ShutdownRequested += (_, e) =>
            {
                if (!window.ViewModel.CanCloseWithoutAsking)
                {
                    e.Cancel = true;
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (await window.ViewModel.ConfirmCloseAsync().ConfigureAwait(true))
                        {
                            desktop.Shutdown();
                        }
                    });
                }
            };

            // macOS hands files to an app through an event rather than the command line: Finder's "Open With",
            // dropping on the Dock icon. Avalonia 12 raises it as a FileActivatedEventArgs.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += async (_, e) =>
                {
                    if (e is FileActivatedEventArgs { Files: [var file, ..] } && file.TryGetLocalPath() is { } path)
                    {
                        await window.ViewModel.OpenFromOutsideAsync(path, askFirst: true).ConfigureAwait(true);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The macOS application menu, the leftmost one. The items set here <em>are</em> that menu's items: they go
    /// in flat, and the platform appends its own Services, Hide and Quit entries below them. Wrapping them in a
    /// container <c>NativeMenuItem</c> nests them one level too deep.
    /// <para>
    /// The items come from the same command table and menu layout as every other menu. The main window's view
    /// model does not exist yet, so the commands are built over a host that finds it when an item is picked:
    /// the deferred form of "use Click, not Command" for native menus.
    /// </para>
    /// </summary>
    private void SetMacApplicationMenu()
    {
        CommandRegistry commands = AppCommands.Create(new DeferredCommandHost(MainWindowViewModel));
        _applicationMenuCommands = commands;
        MenuModel menus = MenuLayout.Build(commands, HostPlatform.MacOS);
        NativeMenu.SetMenu(this, NativeMenuFactory.Build(menus.MacApplicationItems));
    }

    private ViewModels.MainViewModel? MainWindowViewModel() =>
        ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: ViewModels.MainViewModel main }
            ? main
            : null;

    /// <summary>Forwards each command to the main view model, resolved when the command runs.</summary>
    private sealed class DeferredCommandHost(Func<ICommandHost?> resolve) : ICommandHost
    {
        public void Execute(CommandId command) => resolve()?.Execute(command);

        // Before the main window exists there is nothing to ask, and the item must not start greyed out: the menu
        // reads this once when it is built. Otherwise "About MindBuzz" stays disabled.
        public bool CanExecute(CommandId command) => resolve()?.CanExecute(command) ?? true;
    }
}
