using MindBuzz.Core.Model;

namespace MindBuzz.Core.Serialization;

/// <summary>
/// The recovery file of a saved document: a copy of the unsaved state, written next to the
/// document as <c>.&lt;name&gt;.mbz~</c>. The leading dot hides it on macOS and Linux; on Windows the hidden
/// attribute is set. It is the only file besides the document that MindBuzz writes.
/// </summary>
public static class RecoveryFile
{
    /// <summary>The recovery file's path for <paramref name="documentPath"/>: same folder, hidden-style name.</summary>
    public static string PathFor(string documentPath)
    {
        string full = Path.GetFullPath(documentPath);
        return Path.Join(Path.GetDirectoryName(full), $".{Path.GetFileName(full)}~");
    }

    /// <summary>
    /// True when a recovery file exists that is newer than the document: the case where opening offers to
    /// restore it. An older one is a leftover and is not offered.
    /// </summary>
    public static bool HasNewerRecovery(string documentPath)
    {
        string recovery = PathFor(documentPath);
        return File.Exists(recovery)
            && (!File.Exists(documentPath) || File.GetLastWriteTimeUtc(recovery) > File.GetLastWriteTimeUtc(documentPath));
    }

    /// <summary>Writes the recovery file atomically, so a crash during the write never leaves half a file.</summary>
    /// <param name="documentPath">The document the recovery belongs to.</param>
    /// <param name="document">The unsaved state.</param>
    /// <param name="generator">For <c>format.json</c>.</param>
    /// <param name="hide">True on Windows, where a leading dot does not hide a file and the hidden attribute must be
    /// set.</param>
    /// <param name="password">The document's password: an encrypted document's recovery copy is encrypted too, or it
    /// would hold the content in the open.</param>
    public static void Write(string documentPath, Document document, string generator, bool hide, string? password = null)
    {
        string path = PathFor(documentPath);

        // As private as the document: a copy beside a chmod 600 document must not be readable by others.
        UnixFileMode? mode = !OperatingSystem.IsWindows() && File.Exists(documentPath) ? File.GetUnixFileMode(documentPath) : null;
        AtomicFile.Write(path, stream => MbzWriter.Write(stream, document, generator, password), mode);

        if (hide)
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
    }

    /// <summary>Deletes the recovery file if there is one: after a clean save, after "Don't Save", after "Keep
    /// document".</summary>
    public static void Delete(string documentPath)
    {
        string path = PathFor(documentPath);
        if (File.Exists(path))
        {
            // A hidden or read-only file cannot be deleted on Windows until its attributes are cleared.
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}

/// <summary>
/// Keeps the recovery file current while a saved document has unsaved changes: written after
/// 2 s without changes, and at most every 30 s, always from the latest snapshot. With the immutable model a
/// snapshot is just a reference, so writing it on a background thread needs no lock and never stalls typing.
/// <para>
/// All file work runs on one ordered queue: a delete queued after a write (a save, a "Don't Save") always runs
/// after it, so a slow write can never bring the recovery file back after the document was saved. That ordering
/// does what a generation counter does, in queue form.
/// </para>
/// </summary>
public sealed class RecoveryWriter : IDisposable
{
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time;
    private readonly string _generator;
    private readonly bool _hide;
    private readonly Action<Exception> _report;
    private readonly ITimer _timer;
    private readonly object _gate = new();

    private Task _queue = Task.CompletedTask;
    private string? _path;
    private string? _password;
    private Document? _pending;
    private DateTimeOffset _lastChange;
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;

    /// <param name="time">The clock and timers; tests pass a manual one.</param>
    /// <param name="generator">For <c>format.json</c>.</param>
    /// <param name="hide">Set the hidden attribute (Windows).</param>
    /// <param name="report">Where write failures go. A failed recovery write must never take the app down.</param>
    public RecoveryWriter(TimeProvider time, string generator, bool hide, Action<Exception> report)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _generator = generator;
        _hide = hide;
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _timer = _time.CreateTimer(_ => OnDue(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The queue's tail, so a test can wait for the background work instead of sleeping.</summary>
    public Task Idle
    {
        get
        {
            lock (_gate)
            {
                return _queue;
            }
        }
    }

    /// <summary>
    /// The document changed. <paramref name="documentPath"/> is null for an untitled document, which gets no
    /// recovery file; <paramref name="dirty"/> false means it is saved, so nothing to protect.
    /// </summary>
    public void Changed(string? documentPath, Document document, bool dirty)
    {
        lock (_gate)
        {
            _path = documentPath;
            if (documentPath is null || !dirty)
            {
                _pending = null;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            _pending = document;
            _lastChange = _time.GetUtcNow();
            Schedule();
        }
    }

    /// <summary>
    /// The document's password, or null for a plain document: recovery copies are written encrypted with it. Set
    /// whenever it changes; kept in memory only.
    /// </summary>
    public string? Password
    {
        set
        {
            lock (_gate)
            {
                _password = value;
            }
        }
    }

    /// <summary>The document was saved, or its changes discarded: forget pending work and delete the recovery
    /// file.</summary>
    public void Discard(string? documentPath)
    {
        lock (_gate)
        {
            _pending = null;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (documentPath is not null)
            {
                Enqueue(() => RecoveryFile.Delete(documentPath));
            }
        }
    }

    /// <summary>
    /// Writes the pending snapshot now, on the calling thread: the emergency save from the global exception handler. It
    /// never touches the document itself.
    /// </summary>
    public void WriteNow()
    {
        // A write still queued holds an older snapshot: let it finish first, or it would replace the newest one
        // written here. Bounded, because this runs while the process is going down.
        Flush();

        string? path;
        Document? document;
        string? password;
        lock (_gate)
        {
            (path, document, password) = (_path, _pending, _password);
        }

        if (path is not null && document is not null)
        {
            RecoveryFile.Write(path, document, _generator, _hide, password);
        }
    }

    /// <summary>
    /// Stops the timer and finishes the queued work, so a delete queued by "Don't Save" just before quitting still
    /// happens: the queue runs on background threads, which end with the process. Bounded, so a hanging disk cannot
    /// keep the app from quitting.
    /// </summary>
    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }

    /// <summary>The longest the queue is waited for (quitting, the emergency write).</summary>
    private static readonly TimeSpan FlushLimit = TimeSpan.FromSeconds(5);

    private void Flush()
    {
        // Each step catches and reports its own failures, so waiting cannot throw them here.
        Idle.Wait(FlushLimit);
    }

    private void Schedule()
    {
        DateTimeOffset due = Max(_lastChange + IdleDelay, _lastWrite == DateTimeOffset.MinValue ? DateTimeOffset.MinValue : _lastWrite + MinimumInterval);
        TimeSpan delay = due - _time.GetUtcNow();
        _timer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
    }

    private void OnDue()
    {
        lock (_gate)
        {
            if (_pending is not { } document || _path is not { } path)
            {
                return;
            }

            // Changes kept coming since the timer was set: wait for the idle moment.
            DateTimeOffset now = _time.GetUtcNow();
            if (now < _lastChange + IdleDelay)
            {
                Schedule();
                return;
            }

            _pending = null;
            _lastWrite = now;
            string? password = _password;
            Enqueue(() => RecoveryFile.Write(path, document, _generator, _hide, password));
        }
    }

    private void Enqueue(Action work)
    {
        _queue = _queue.ContinueWith(
            _ =>
            {
                try
                {
                    work();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _report(exception);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
