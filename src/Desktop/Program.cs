using Avalonia;
using MindBuzz.App;
using MindBuzz.App.Diagnostics;

namespace MindBuzz.Desktop;

/// <summary>
/// The single head for Windows, macOS and Linux. There are deliberately no per-OS heads: platform
/// differences live behind <c>Host.Current</c> and the platform interfaces in the App project.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // First statement in the process: every thread is covered from here on. The UI thread handlers
        // come later, in AfterSetup, because they need an initialised dispatcher.
        GlobalExceptionHandler.Install();

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            // Startup and shutdown run outside the dispatcher's reach, and a failure there is fatal rather
            // than recoverable.
            GlobalExceptionHandler.Report("Startup", exception, isFatal: true);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<MindBuzzApp>()
            .UsePlatformDetect()
            .WithMindBuzzFonts()
            .LogToTrace()
            .AfterSetup(static _ => GlobalExceptionHandler.InstallDispatcherHandlers());
}
