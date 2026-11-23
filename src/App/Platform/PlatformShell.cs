using System.ComponentModel;
using System.Diagnostics;

namespace MindBuzz.App.Platform;

/// <summary>
/// The shell for every OS. One class, because .NET's shell execute already knows each platform's handler
/// (<c>ShellExecute</c> on Windows, <c>open</c> on macOS, <c>xdg-open</c> on Linux).
/// </summary>
internal sealed class PlatformShell : IPlatformShell
{
    /// <summary>The seam a test replaces, so no browser opens during a test run.</summary>
    private readonly Action<ProcessStartInfo> _start;

    /// <summary>The folders <see cref="OpenTemporaryPdf"/> wrote, deleted when the process ends.</summary>
    private readonly List<string> _temporaryFolders = [];

    public PlatformShell()
        : this(static info =>
        {
            using Process? process = Process.Start(info);
        })
    {
    }

    internal PlatformShell(Action<ProcessStartInfo> start) => _start = start;


    /// <summary>
    /// One of the two <c>UseShellExecute = true</c> calls in the app, both in this class; the other
    /// opens the keyboard sheet MindBuzz wrote itself. A URL needs shell execute,
    /// because that is what makes the platform resolve the protocol handler. It is reached only after
    /// <see cref="SafeUrl.IsAllowed"/>.
    /// </summary>
    public bool OpenUrl(string url)
    {
        if (!SafeUrl.IsAllowed(url))
        {
            return false;
        }

        try
        {
            _start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            // Linux desktop integration is convention, not API: xdg-open may not be installed. A link that
            // quietly does nothing is better than a crash.
            return false;
        }
    }

    /// <summary>
    /// The one file MindBuzz writes besides the document and its recovery file: the keyboard sheet,
    /// in a folder of its own so its name can be plain ("Keyboard Shortcuts.pdf") and two processes never meet. The
    /// folder is deleted when the process ends; a viewer that still holds the file open on Windows can stop that,
    /// and then the OS's temp clean-up removes it later.
    /// </summary>
    public bool OpenTemporaryPdf(byte[] pdf, string fileName)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        string path;
        try
        {
            string folder = Path.Join(Path.GetTempPath(), "MindBuzz-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            path = Path.Join(folder, fileName);
            File.WriteAllBytes(path, pdf);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        lock (_temporaryFolders)
        {
            if (_temporaryFolders.Count == 0)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteTemporaryFolders();
            }

            _temporaryFolders.Add(Path.GetDirectoryName(path)!);
        }

        try
        {
            // A local file we wrote ourselves, so no SafeUrl check: nothing from a document reaches this path.
            _start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    internal void DeleteTemporaryFolders()
    {
        lock (_temporaryFolders)
        {
            foreach (string folder in _temporaryFolders)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Still open in a viewer (Windows): the OS's temp clean-up takes it later.
                }
            }

            _temporaryFolders.Clear();
        }
    }
}
