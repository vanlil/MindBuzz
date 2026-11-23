namespace MindBuzz.Core.Tests;

/// <summary>
/// A clock that only moves when told to, with timers that fire when it passes their due time. Tests wait on this
/// instead of sleeping.
/// </summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target = _now + by;
        while (true)
        {
            ManualTimer? next = _timers.Where(t => t.Due is { } due && due <= target).MinBy(t => t.Due);
            if (next is null)
            {
                break;
            }

            _now = next.Due!.Value;
            next.Due = null;
            next.Fire();
        }

        _now = target;
    }

    private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => Due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
