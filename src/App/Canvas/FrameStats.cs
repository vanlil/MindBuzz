using System.Diagnostics;

namespace MindBuzz.App.Canvas;

/// <summary>
/// Frame timings for the overlay and the benchmark: how long the canvas's <c>Render</c> took (UI thread), and
/// the time between rendered frames. Keeps the last few seconds only.
/// </summary>
internal sealed class FrameStats
{
    private const int Capacity = 600;

    private readonly Queue<double> _render = new();
    private readonly Queue<double> _intervals = new();
    private long _lastFrame;

    public void AddRender(TimeSpan duration)
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastFrame != 0)
        {
            Enqueue(_intervals, Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
        }

        _lastFrame = now;
        Enqueue(_render, duration.TotalMilliseconds);
    }

    /// <summary>Forgets everything, e.g. between benchmark phases.</summary>
    public void Reset()
    {
        _render.Clear();
        _intervals.Clear();
        _lastFrame = 0;
    }

    public int Frames => _render.Count;

    public double RenderP50 => Percentile(_render, 0.5);

    public double RenderP95 => Percentile(_render, 0.95);

    public double RenderMax => _render.Count == 0 ? 0 : _render.Max();

    public double IntervalP50 => Percentile(_intervals, 0.5);

    public double IntervalP95 => Percentile(_intervals, 0.95);

    public double Fps => IntervalP50 <= 0 ? 0 : 1000 / IntervalP50;

    private static void Enqueue(Queue<double> queue, double value)
    {
        if (queue.Count == Capacity)
        {
            queue.Dequeue();
        }

        queue.Enqueue(value);
    }

    private static double Percentile(Queue<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = [.. values.Order()];
        return sorted[Math.Min(sorted.Length - 1, (int)(percentile * sorted.Length))];
    }
}
