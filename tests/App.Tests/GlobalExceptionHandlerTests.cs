using MindBuzz.App.Diagnostics;
using Xunit;
using static MindBuzz.App.Diagnostics.GlobalExceptionHandler;

namespace MindBuzz.App.Tests;

/// <summary>
/// The handler is process-wide state, so these tests swap the sink and the emergency hook and always put
/// them back. They run in one class, which xunit runs sequentially.
/// </summary>
public sealed class GlobalExceptionHandlerTests : IDisposable
{
    private readonly Action<ExceptionReport> _originalSink = Sink;
    private readonly Action? _originalSave = EmergencySave;
    private readonly List<string> _events = [];

    public GlobalExceptionHandlerTests()
    {
        Sink = report => _events.Add($"report:{report.Origin}:{report.IsFatal}");
    }

    public void Dispose()
    {
        Sink = _originalSink;
        EmergencySave = _originalSave;
    }

    [Fact]
    public void When_the_process_is_going_down_the_emergency_save_runs_before_the_report()
    {
        EmergencySave = () => _events.Add("save");

        HandleUnhandled(new InvalidOperationException("boom"), isTerminating: true);

        Assert.Equal(["save", "report:Unhandled:True"], _events);
    }

    [Fact]
    public void A_survivable_exception_does_not_trigger_the_emergency_save()
    {
        EmergencySave = () => _events.Add("save");

        HandleUnhandled(new InvalidOperationException("boom"), isTerminating: false);

        Assert.Equal(["report:Unhandled:False"], _events);
    }

    [Fact]
    public void A_failing_emergency_save_is_reported_and_does_not_hide_the_original_failure()
    {
        EmergencySave = () => throw new IOException("disk full");

        HandleUnhandled(new InvalidOperationException("boom"), isTerminating: true);

        Assert.Equal(["report:Emergency save:True", "report:Unhandled:True"], _events);
    }

    [Fact]
    public void A_throwing_sink_never_escalates()
    {
        Sink = _ => throw new InvalidOperationException("sink broke");

        Report("Test", new InvalidOperationException("boom"), isFatal: false);
    }

    [Fact]
    public void Installing_twice_is_harmless_and_touches_no_dispatcher()
    {
        // Install() runs before Avalonia exists in the real app. It must not read Dispatcher.UIThread, which
        // would pin a dispatcher with no platform. Calling it here, twice, would throw if it did.
        Install();
        Install();
    }
}
