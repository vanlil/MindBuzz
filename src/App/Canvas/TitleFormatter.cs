using Avalonia;
using Avalonia.Controls;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// Bold, italic and underline in a text box holding a topic's title as Markdown: the in-place editor
/// and the detail view's title box. A selection, or the word the caret is inside, toggles at once
/// (<see cref="TitleMarkup.Toggle"/>). Elsewhere, at a word's edge or the end of the text, the style is switched for
/// what is typed next, as in word processors, and forgotten when the caret moves first.
/// </summary>
internal sealed class TitleFormatter
{
    private readonly TextBox _box;

    /// <summary>Styles switched for the next typed text, at <see cref="_pendingCaret"/> in <see
    /// cref="_pendingText"/>.</summary>
    private TitleStyle _pending;

    private int _pendingCaret;

    private string _pendingText = string.Empty;

    /// <summary>The box's text as last seen, to tell what was typed.</summary>
    private string _last = string.Empty;

    private bool _applying;

    public TitleFormatter(TextBox box)
    {
        _box = box ?? throw new ArgumentNullException(nameof(box));
        _box.TextChanged += (_, _) => OnTextChanged();
        _box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
            {
                OnCaretMoved();
            }
        };
    }

    /// <summary>The styles at the caret or of the selection changed: the toggle buttons follow.</summary>
    public event EventHandler? StyleChanged;

    /// <summary>The styles the toggle buttons show.</summary>
    public TitleStyle Current
    {
        get
        {
            string text = _box.Text ?? string.Empty;
            (int start, int length) = Selection();
            TitleStyle style = TitleMarkup.StyleAt(text, start, length);
            return _pending != TitleStyle.None && length == 0 && start == _pendingCaret && text == _pendingText ? style ^ _pending : style;
        }
    }

    /// <summary>The text was set from outside (a topic opened): nothing is switched.</summary>
    public void Reset()
    {
        _pending = TitleStyle.None;
        _last = _box.Text ?? string.Empty;
        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ctrl/⌘+B, I, U or a toggle button.</summary>
    public void Toggle(TitleStyle style)
    {
        string text = _box.Text ?? string.Empty;
        (int start, int length) = Selection();
        if (TitleMarkup.Toggle(text, start, length, style) is { } result)
        {
            _pending = TitleStyle.None;
            Apply(result.Text, result.SelectionStart, result.SelectionLength);
        }
        else
        {
            // At a word's edge or the end: for what is typed next. Pressed again, switched back.
            _pending = start == _pendingCaret && text == _pendingText ? _pending ^ style : style;
            _pendingCaret = start;
            _pendingText = text;
        }

        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTextChanged()
    {
        string text = _box.Text ?? string.Empty;
        if (_applying || text == _last)
        {
            return;
        }

        string before = _last;
        _last = text;
        TitleStyle pending = _pending;
        _pending = TitleStyle.None;

        // Typed at the caret where a style was switched: the typed text gets it.
        int inserted = text.Length - before.Length;
        if (pending != TitleStyle.None && before == _pendingText && inserted > 0
            && string.CompareOrdinal(text, 0, before, 0, _pendingCaret) == 0
            && string.CompareOrdinal(text, _pendingCaret + inserted, before, _pendingCaret, before.Length - _pendingCaret) == 0)
        {
            if (string.IsNullOrWhiteSpace(text.Substring(_pendingCaret, inserted)))
            {
                // Spaces take no style of their own (TitleMarkup.Write): the switch waits for the next word.
                _pending = pending;
                _pendingCaret += inserted;
                _pendingText = text;
            }
            else
            {
                (string styled, int caret) = TitleMarkup.StyleTyped(text, _pendingCaret, inserted, pending);
                Apply(styled, caret, 0);
            }
        }

        StyleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCaretMoved()
    {
        // Typing moves the caret too; only a move without typing forgets the switched style.
        if (!_applying && _pending != TitleStyle.None && (_box.Text ?? string.Empty) == _pendingText && Selection() != (_pendingCaret, 0))
        {
            _pending = TitleStyle.None;
        }

        if (!_applying)
        {
            StyleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private (int Start, int Length) Selection()
    {
        int a = _box.SelectionStart, b = _box.SelectionEnd;
        int caret = _box.CaretIndex;
        return a == b ? (caret, 0) : (Math.Min(a, b), Math.Abs(b - a));
    }

    private void Apply(string text, int start, int length)
    {
        _applying = true;
        try
        {
            _box.Text = text;
            _last = _box.Text ?? string.Empty;
            _box.CaretIndex = start + length;
            _box.SelectionStart = start;
            _box.SelectionEnd = start + length;
        }
        finally
        {
            _applying = false;
        }

        // The line guard may have put the text back (a fifth line); then that is what was seen.
        _last = _box.Text ?? string.Empty;
    }
}
