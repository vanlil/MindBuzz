using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace MindBuzz.App.Views;

/// <summary>
/// A vertical divider between two panes that reports how far it is dragged; the window decides which panes change
/// width. Used instead of Avalonia's <see cref="GridSplitter"/>, which with the search pane and the detail view side by
/// side resized the wrong neighbour and lagged behind the pointer (Avalonia 12.1.6).
/// </summary>
public sealed class PaneDivider : Border
{
    private Point? _from;

    public PaneDivider()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        Classes.Add("paneDivider");
    }

    /// <summary>Styled as a <see cref="Border"/>: Avalonia matches style selectors by exact type.</summary>
    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>The divider moved this far to the right (negative: to the left) since the last report.</summary>
    public event EventHandler<double>? Dragged;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && VisualRoot is Visual root)
        {
            _from = e.GetPosition(root);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_from is { } from && VisualRoot is Visual root)
        {
            Point now = e.GetPosition(root);
            if (now.X != from.X)
            {
                Dragged?.Invoke(this, now.X - from.X);
                _from = now;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _from = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _from = null;
    }
}
