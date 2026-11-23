using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The tooltip over a topic (TopicSummary). The canvas draws its topics, so there is no control to hang a tooltip on:
/// an invisible anchor is placed over the hovered topic and the tooltip opens on it, above the topic like every
/// tooltip in the app. The anchor takes no pointer input, so clicks still reach the canvas.
/// </summary>
internal sealed partial class MapCanvas
{
    /// <summary>How long the pointer rests on a topic before its summary shows.</summary>
    private static readonly TimeSpan TipDelay = TimeSpan.FromMilliseconds(600);

    private Border? _tipAnchor;
    private DispatcherTimer? _tipTimer;
    private Rect _tipBounds;

    /// <summary>The summary shown now, or null: a test seam.</summary>
    internal string? OpenTip => _tipAnchor is { } anchor && ToolTip.GetIsOpen(anchor) ? ToolTip.GetTip(anchor) as string : null;

    /// <summary>The hovered topic changed: the tooltip closes, and a new one is scheduled if the topic has something to say.</summary>
    private void OnHoverChangedForTip()
    {
        CloseTip();
        if (_hover is { } id && _editing is null && _layout?.Find(id) is { } box && TopicSummary.For(box.Topic) is not null)
        {
            _tipTimer ??= new DispatcherTimer { Interval = TipDelay };
            _tipTimer.Tick -= OnTipDue;
            _tipTimer.Tick += OnTipDue;
            _tipTimer.Start();
        }
    }

    private void OnTipDue(object? sender, EventArgs e)
    {
        _tipTimer?.Stop();
        ShowTip();
    }

    /// <summary>Opens the summary of the hovered topic now; the timer calls this, and tests do.</summary>
    internal void ShowTip()
    {
        if (_hover is not { } id || _editing is not null || _layout?.Find(id) is not { } box || TopicSummary.For(box.Topic) is not { } text)
        {
            return;
        }

        if (_tipAnchor is null)
        {
            _tipAnchor = new Border { IsHitTestVisible = false, Background = null };
            VisualChildren.Add(_tipAnchor);
            LogicalChildren.Add(_tipAnchor);
        }

        Point topLeft = _viewport.ToScreen(new LayoutPoint(box.Bounds.X, box.Bounds.Y));
        Point bottomRight = _viewport.ToScreen(new LayoutPoint(box.Bounds.Right, box.Bounds.Bottom));
        _tipBounds = new Rect(topLeft, bottomRight);
        _tipAnchor.Measure(_tipBounds.Size);
        _tipAnchor.Arrange(_tipBounds);
        ToolTip.SetTip(_tipAnchor, text);
        ToolTip.SetIsOpen(_tipAnchor, true);
    }

    /// <summary>Closes the summary: on leaving the topic, a click, a drag, the wheel, editing.</summary>
    internal void CloseTip()
    {
        _tipTimer?.Stop();
        if (_tipAnchor is { } anchor && ToolTip.GetIsOpen(anchor))
        {
            ToolTip.SetIsOpen(anchor, false);
        }
    }

    private void ArrangeTipAnchor() => _tipAnchor?.Arrange(_tipBounds);
}
