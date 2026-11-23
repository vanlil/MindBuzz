using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MindBuzz.App.Localization;
using MindBuzz.App.Notes;
using MindBuzz.App.ViewModels;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Views;

/// <summary>
/// The topic panel's view. It keeps the notes editor and the view model's note in step in both
/// directions, renders the preview, and handles the keys that belong to the panel: Esc back to the map, the link
/// list's keys, and undo and redo while the editor has the keyboard.
/// </summary>
public partial class TopicPanel : UserControl
{
    private readonly NoteColorizer _colorizer;

    /// <summary>Set while the editor's text is replaced from the model, so that change is not sent back.</summary>
    private bool _syncing;

    /// <summary>The note the preview shows, to render only when it changed.</summary>
    private string? _renderedPreview;

    private TopicPanelViewModel? _model;

    /// <summary>Keeps the title box within the topic line limit.</summary>
    private readonly MindBuzz.App.Canvas.TitleLineGuard _titleGuard;

    /// <summary>Bold, italic and underline in the title box, as in the map's editor.</summary>
    private readonly MindBuzz.App.Canvas.TitleFormatter _titleFormatter;

    public TopicPanel()
    {
        _titleGuard = new MindBuzz.App.Canvas.TitleLineGuard(() => _model?.TopicLevel);
        InitializeComponent();

        _colorizer = new NoteColorizer(this);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);

        // The document's undo history is the only one: the editor keeps none of its own, and its
        // undo keys run the app's Undo (see OnKeyDown).
        Editor.Document.UndoStack.SizeLimit = 0;

        // Tab indents inside the editor; four spaces nest both "-" and "1." list items.
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;

        // Selection and caret from the theme, so they follow light and dark.
        Editor.TextArea.Bind(AvaloniaEdit.Editing.TextArea.SelectionBrushProperty, Editor.TextArea.GetResourceObservable("NoteSelectionBrush"));
        Editor.TextArea.Bind(AvaloniaEdit.Editing.TextArea.CaretBrushProperty, Editor.TextArea.GetResourceObservable("AppTextBrush"));
        Editor.TextArea.SelectionBorder = null;

        // The frame around the editor shows focus; a second ring inside it would be noise.
        Editor.TextArea.FocusAdorner = null;
        Editor.TextArea.SelectionForeground = null;

        Editor.TextChanged += (_, _) => OnEditorTextChanged();
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                _model?.SetNotesFocused(Editor.IsKeyboardFocusWithin);
            }
        };

        // The title keeps to the topic line limit while it is typed here too.
        TitleBox.TextChanged += (_, _) =>
        {
            if (TitleBox.IsKeyboardFocusWithin)
            {
                _titleGuard.Check(TitleBox);
            }
        };
        _titleFormatter = new MindBuzz.App.Canvas.TitleFormatter(TitleBox);

        TitleBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
            {
                _model?.SetTitleFocused(TitleBox.IsKeyboardFocusWithin);
                if (!TitleBox.IsKeyboardFocusWithin)
                {
                    CommitTitle();

                    // The selection may have moved while the box had the keyboard: show the topic now selected.
                    SyncTitle();
                }
            }
        };

        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        // A click anywhere else in the panel, even on a label or empty space, which take no focus themselves, ends
        // editing the title. Checked after the click, so a button or the editor keeps the focus
        // it took.
        AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (TitleBox.IsKeyboardFocusWithin && e.Source is Visual source && !TitleBox.IsVisualAncestorOf(source) && source != TitleBox)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (TitleBox.IsKeyboardFocusWithin)
                        {
                            _model?.LeaveTitle();
                        }
                    });
                }
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        LinkList.AddHandler(KeyDownEvent, (_, e) => { if (_model?.Links is { } links) LinkListKeys.Handle(links, e); }, RoutingStrategies.Tunnel);
        LinkList.DoubleTapped += (_, _) => _model?.Links?.OpenCommand.Execute(null);
        ActualThemeVariantChanged += (_, _) => Editor.TextArea.TextView.Redraw();
    }

    /// <summary>The notes editor, for tests.</summary>
    internal AvaloniaEdit.TextEditor NoteEditor => Editor;

    /// <summary>The rendered preview, for tests; null while there is nothing to show.</summary>
    internal Control? PreviewContent => PreviewHost.Content as Control;

    /// <summary>
    /// Puts the keyboard in the notes section once the panel is laid out: the editor in Edit mode, the preview in
    /// Preview mode. The frames around both show focus. Not focused "as if by Tab": AvaloniaEdit then
    /// selects the whole note, and the next keystroke would replace it (seen in the real app).
    /// </summary>
    internal void FocusNotes(bool caretAtEnd = false) => Dispatcher.UIThread.Post(
        () =>
        {
            if (_model is { IsPreview: true })
            {
                PreviewScroll.Focus();
            }
            else
            {
                if (caretAtEnd)
                {
                    Editor.Select(Editor.Document.TextLength, 0);
                    Editor.TextArea.Caret.BringCaretToView();
                }

                Editor.TextArea.Focus();
            }
        },
        DispatcherPriority.Loaded);

    /// <summary>The links list with the keyboard on its selected row, once the panel is laid out.</summary>
    internal void FocusLinks() => Dispatcher.UIThread.Post(
        () =>
        {
            LinkList.Focus();
            if (LinkList.ContainerFromIndex(Math.Max(0, LinkList.SelectedIndex)) is Control row)
            {
                row.Focus();
            }
        },
        DispatcherPriority.Loaded);

    /// <summary>
    /// A search hit in the note: selected in the editor and scrolled into view, without taking the keyboard, which
    /// stays
    /// in the search box for the next hit.
    /// </summary>
    internal void ShowNoteMatch(int start, int length) => Dispatcher.UIThread.Post(
        () =>
        {
            int from = Math.Clamp(start, 0, Editor.Document.TextLength);
            Editor.Select(from, Math.Clamp(length, 0, Editor.Document.TextLength - from));
            Editor.ScrollTo(Editor.Document.GetLineByOffset(from).LineNumber, 0);
        },
        DispatcherPriority.Loaded);

    /// <summary>Ctrl/⌘+B and I: wraps or unwraps the selection, which stays selected.</summary>
    internal void WrapSelection(string marker)
    {
        (string text, int start, int length) = MarkdownEdit.Toggle(Editor.Text ?? string.Empty, Editor.SelectionStart, Editor.SelectionLength, marker);
        ReplaceText(text);
        Editor.Select(start, length);
    }

    // --- the notes toolbar -------------------------------------------------------------------------------------------

    private void OnNoteBold(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.Toggle(text, start, length, "**"));

    private void OnNoteItalic(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.Toggle(text, start, length, "*"));

    private void OnNoteStrike(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.Toggle(text, start, length, "~~"));

    private void OnNoteCode(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.Toggle(text, start, length, "`"));

    private void OnNoteHeading(object? sender, RoutedEventArgs e) => NoteEdit(MarkdownEdit.CycleHeading);

    private void OnNoteBullet(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.ToggleLineKind(text, start, length, MarkdownLineKind.Bullet));

    private void OnNoteNumbered(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.ToggleLineKind(text, start, length, MarkdownLineKind.Numbered));

    private void OnNoteQuote(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.ToggleLineKind(text, start, length, MarkdownLineKind.Quote));

    private void OnNoteLink(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.InsertLink(text, start, length, Strings.Notes_LinkPlaceholder));

    private void OnNoteTable(object? sender, RoutedEventArgs e) => NoteEdit((text, start, length) => MarkdownEdit.InsertTable(
        text,
        start,
        length,
        string.Format(CultureInfo.CurrentCulture, Strings.Notes_TableColumnFormat, 1),
        string.Format(CultureInfo.CurrentCulture, Strings.Notes_TableColumnFormat, 2)));

    /// <summary>A toolbar edit of the note at the editor's selection; the keyboard goes back to the editor.</summary>
    private void NoteEdit(Func<string, int, int, (string Text, int SelectionStart, int SelectionLength)> edit)
    {
        if (_model is not { IsEditMode: true, HasTopic: true })
        {
            return;
        }

        (string text, int start, int length) = edit(Editor.Text ?? string.Empty, Editor.SelectionStart, Editor.SelectionLength);
        ReplaceText(text);
        Editor.Select(start, length);
        Editor.Focus();
    }

    /// <summary>Ctrl/⌘+B, I and U in the title box.</summary>
    internal void ToggleTitleStyle(TitleStyle style) => _titleFormatter.Toggle(style);

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as TopicPanelViewModel;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelChanged;
        }

        Sync();
        SyncTitle();
        base.OnDataContextChanged(e);
    }

    /// <summary>Shows the topic's text in the title box, unless the user is typing there.</summary>
    private void SyncTitle()
    {
        if (!TitleBox.IsKeyboardFocusWithin)
        {
            TitleBox.Text = _model?.TopicTitle ?? string.Empty;
            _titleGuard.Reset(TitleBox.Text);
            _titleFormatter.Reset();
        }
    }

    /// <summary>Commits the title box's text when the user is typing there (Save, Close, New, Open).</summary>
    internal void CommitPendingTitle()
    {
        if (TitleBox.IsKeyboardFocusWithin)
        {
            CommitTitle();
        }
    }

    private void CommitTitle()
    {
        if (_model is not null && !_model.CommitTitle(TitleBox.Text ?? string.Empty))
        {
            TitleBox.Text = _model.TopicTitle;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TopicPanelViewModel.NoteText) or nameof(TopicPanelViewModel.IsPreview) or nameof(TopicPanelViewModel.IsOpen))
        {
            Sync();
        }
        else if (e.PropertyName == nameof(TopicPanelViewModel.TopicTitle))
        {
            SyncTitle();
        }
    }

    /// <summary>Brings the editor, its colouring and the preview up to the model's note.</summary>
    private void Sync()
    {
        string text = _model?.NoteText ?? string.Empty;
        if (Editor.Text != text)
        {
            _syncing = true;
            try
            {
                ReplaceText(text);
            }
            finally
            {
                _syncing = false;
            }
        }

        _colorizer.Update(text);
        Editor.TextArea.TextView.Redraw();

        // Only while the panel is open: arrowing through topics with the panel closed must not render every note.
        if (_model is { IsPreview: true, IsOpen: true } && _renderedPreview != text)
        {
            PreviewHost.Content = text.Length == 0 ? null : MarkdownRenderer.Render(text, _model.OpenLink);
            _renderedPreview = text;
        }
    }

    private void OnEditorTextChanged()
    {
        if (_syncing || _model is null)
        {
            return;
        }

        // An accepted note changes NoteText, and Sync recolours the editor from there. A refused one (grown beyond
        // the limit) changes nothing, so Sync is called here to put back the note the document holds.
        if (!_model.EditNote(Editor.Text ?? string.Empty))
        {
            Sync();
        }
    }

    /// <summary>
    /// Replaces only the part of the editor's text that differs, so the caret and the scroll position stay where
    /// they are when an undo changes one word.
    /// </summary>
    private void ReplaceText(string text)
    {
        string old = Editor.Document.Text;
        int prefix = 0;
        int max = Math.Min(old.Length, text.Length);
        while (prefix < max && old[prefix] == text[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix])
        {
            suffix++;
        }

        Editor.Document.Replace(prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        // Esc returns to the map and leaves the panel open. In the title box it first throws the
        // typing away, so leaving the box does not commit it.
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (TitleBox.IsKeyboardFocusWithin)
            {
                TitleBox.Text = _model.TopicTitle;
                _model.CancelTitle();
            }

            _model.ReturnToCanvas();
            e.Handled = true;
            return;
        }

        // The title box: Enter commits and leaves the box, Shift+Enter breaks the line, as on the map.
        if (TitleBox.IsKeyboardFocusWithin && e.Key == Key.Enter)
        {
            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                int caret = TitleBox.CaretIndex;
                string text = TitleBox.Text ?? string.Empty;
                int start = Math.Min(TitleBox.SelectionStart, TitleBox.SelectionEnd);
                int end = Math.Max(TitleBox.SelectionStart, TitleBox.SelectionEnd);
                if (end == start)
                {
                    start = end = caret;
                }

                TitleBox.Text = text.Remove(start, end - start).Insert(start, "\n");
                TitleBox.CaretIndex = start + 1;
            }
            else
            {
                CommitTitle();
                _model.LeaveTitle();
            }

            e.Handled = true;
            return;
        }

        // The editor would take Ctrl+Z for its own undo, which is switched off; the document's undo runs instead.
        if (Editor.IsKeyboardFocusWithin && _model.HistoryCommandFor(e) is { } command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true;
        }
    }
}
