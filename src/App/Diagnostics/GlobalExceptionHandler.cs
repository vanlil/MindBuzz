using System.Globalization;
using System.Text;
using Avalonia.Threading;

namespace MindBuzz.App.Diagnostics;

/// <summary>
/// Catches everything the runtime is willing to hand over: exceptions thrown on the UI thread, faulted
/// tasks nobody awaited, and exceptions that reach the top of any other thread.
/// <para>
/// Reports go to stderr through a replaceable <see cref="Sink"/>; the app writes no log file. When the
/// process is going down, <see cref="EmergencySave"/> runs first, so an editor never loses work to a crash
/// without at least trying to write the recovery file. A caught UI-thread exception then shows the
/// crash dialog through <see cref="UiError"/>.
/// </para>
/// </summary>
public static class GlobalExceptionHandler
{
    private static int processHandlersInstalled;
    private static int dispatcherHandlersInstalled;

    /// <summary>Where reports go. Replace it to change how reports are shown. Never null.</summary>
    public static Action<ExceptionReport> Sink { get; set; } = WriteToStandardError;

    /// <summary>
    /// Runs once when the process is terminating because of an unhandled exception, before the report is
    /// written. It must only ever write the recovery file next to the document, never the document itself.
    /// Null until a document exists that has something to save.
    /// </summary>
    public static Action? EmergencySave { get; set; }

    /// <summary>
    /// Runs after a UI-thread exception has been reported and caught, posted so it runs outside the failed frame: the
    /// crash dialog. Null until the main window exists. A crash on another thread ends the process,
    /// where no window can be shown reliably; that case keeps to the emergency save and the report.
    /// </summary>
    public static Action<Exception>? UiError { get; set; }

    /// <summary>
    /// Installs the process-wide handlers. Call this as early as possible: it touches nothing in Avalonia.
    /// Safe to call more than once; only the first call does anything.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref processHandlersInstalled, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Installs the UI thread handlers. Must run <em>after</em> Avalonia setup: reading
    /// <see cref="Dispatcher.UIThread"/> creates the dispatcher singleton, and doing that before the platform
    /// is initialised pins a dispatcher with no platform implementation, whose main loop then throws
    /// <see cref="PlatformNotSupportedException"/> at startup. Safe to call more than once.
    /// </summary>
    public static void InstallDispatcherHandlers()
    {
        if (Interlocked.Exchange(ref dispatcherHandlersInstalled, 1) != 0)
        {
            return;
        }

        // The filter has to opt in before the dispatcher will catch anything at all: without RequestCatch the
        // exception keeps unwinding and takes the process with it.
        Dispatcher.UIThread.UnhandledExceptionFilter += OnDispatcherFilter;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherFilter(object? sender, DispatcherUnhandledExceptionFilterEventArgs e) =>
        e.RequestCatch = true;

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report("UI thread", e.Exception, isFatal: false);

        // A failed command or a failed render pass is not a reason to throw away the user's map.
        e.Handled = true;

        if (UiError is { } show)
        {
            Exception exception = e.Exception;
            Dispatcher.UIThread.Post(() => ShowUiError(show, exception));
        }
    }

    /// <summary>Runs the <see cref="UiError"/> hook; a failure there is reported, never rethrown into a loop.</summary>
    internal static void ShowUiError(Action<Exception> show, Exception exception)
    {
        try
        {
            show(exception);
        }
        catch (Exception failure)
        {
            Report("Crash dialog", failure, isFatal: false);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Report("Background task", e.Exception, isFatal: false);

        // Unobserved exceptions no longer kill the process by default, but marking them observed keeps the
        // behaviour independent of the ThrowUnobservedTaskExceptions switch.
        e.SetObserved();
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // ExceptionObject is typed as object because a non-CLS language can throw something that is not an
        // Exception; that cannot happen in this process, so the fallback only keeps the report non-null.
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "non-exception throw");

        HandleUnhandled(exception, e.IsTerminating);
    }

    /// <summary>
    /// What the process-wide handler does, separated from the event so a test can drive it: the emergency
    /// save first while the process is going down, then the report.
    /// </summary>
    internal static void HandleUnhandled(Exception exception, bool isTerminating)
    {
        if (isTerminating)
        {
            TryEmergencySave();
        }

        Report("Unhandled", exception, isTerminating);
    }

    private static void TryEmergencySave()
    {
        try
        {
            EmergencySave?.Invoke();
        }
        catch (Exception exception)
        {
            // The save failing is reported, never rethrown: a second failure here would hide the first.
            Report("Emergency save", exception, isFatal: true);
        }
    }

    /// <summary>Hands one exception to the <see cref="Sink"/>. Never throws.</summary>
    public static void Report(string origin, Exception exception, bool isFatal)
    {
        try
        {
            Sink(new ExceptionReport(origin, exception, isFatal));
        }
        catch
        {
            // A throwing reporter must never escalate into a second, worse failure.
        }
    }

    private static void WriteToStandardError(ExceptionReport report)
    {
        // Diagnostics, not UI: deliberately not routed through Strings.resx, and formatted invariantly so a
        // pasted report reads the same everywhere.
        var text = new StringBuilder()
            .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(report.IsFatal ? "  FATAL  " : "  ERROR  ")
            .Append(report.Origin)
            .Append(": ")
            .AppendLine(report.Exception.ToString())
            .ToString();

        Console.Error.Write(text);
        Console.Error.Flush();
    }

    /// <summary>One caught exception, with where it came from and whether the process is going down.</summary>
    /// <param name="Origin">Which handler caught it, for the report header.</param>
    /// <param name="Exception">The exception itself.</param>
    /// <param name="IsFatal">True when the runtime is terminating regardless of what the sink does.</param>
    public readonly record struct ExceptionReport(string Origin, Exception Exception, bool IsFatal);
}
