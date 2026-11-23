using MindBuzz.Core.Model;

namespace MindBuzz.Core.Editing;

/// <summary>A step the view has to show after undo or redo: the document, and where to put the selection.</summary>
/// <param name="Document">The document after the step.</param>
/// <param name="Target">The map to switch to and what to select there.</param>
/// <param name="Kind">What was undone or redone, for a status message.</param>
public sealed record HistoryStep(Document Document, EditTarget Target, CommandKind Kind);

/// <summary>
/// One undo history per document, spanning all maps. Entries hold the document before and
/// after each change; with the immutable model those are shared snapshots, not copies, so undo restores exactly
/// what was there.
/// <para>
/// The history is cleared when a document is opened or created (<see cref="Reset"/>), not when it is saved.
/// Saving calls <see cref="MarkClean"/>, so undoing back to the saved state clears <see cref="IsDirty"/>.
/// </para>
/// </summary>
public sealed class UndoHistory
{
    private readonly List<Entry> _entries = [];

    /// <summary>How many entries are applied: entries below this are undoable, entries from it on are
    /// redoable.</summary>
    private int _position;

    /// <summary>The <see cref="_position"/> at the last save, or -1 when that state can no longer be reached.</summary>
    private int _cleanPosition;

    public UndoHistory(Document initial)
    {
        Current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public Document Current { get; private set; }

    public bool CanUndo => _position > 0;

    public bool CanRedo => _position < _entries.Count;

    /// <summary>What <see cref="Undo"/> would undo, for the menu label; null when there is nothing.</summary>
    public CommandKind? UndoKind => CanUndo ? _entries[_position - 1].Kind : null;

    public CommandKind? RedoKind => CanRedo ? _entries[_position].Kind : null;

    /// <summary>True when the document differs from what was last saved (or created or opened).</summary>
    public bool IsDirty => _position != _cleanPosition;

    /// <summary>Raised after every change to <see cref="Current"/> or <see cref="IsDirty"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Applies a command. A refused command changes nothing and is not recorded. A command whose
    /// <see cref="IDocumentCommand.CoalesceKey"/> matches the previous entry's merges into it, so a whole editing
    /// session undoes in one step, but never across a save: the saved state stays reachable by undo.
    /// </summary>
    /// <param name="command">The change.</param>
    /// <param name="selectionBefore">Where the selection is now, restored when this step is undone.</param>
    public CommandResult Execute(IDocumentCommand command, EditTarget selectionBefore)
    {
        ArgumentNullException.ThrowIfNull(command);

        CommandResult result = command.Apply(Current);
        if (!result.IsApplied)
        {
            return result;
        }

        // A new change after undo discards the redo branch. If the saved state was on it, it is gone for good.
        if (_position < _entries.Count)
        {
            _entries.RemoveRange(_position, _entries.Count - _position);
            if (_cleanPosition > _position)
            {
                _cleanPosition = -1;
            }
        }

        object? key = command.CoalesceKey;
        bool merge = key is not null
            && _position > 0
            && _cleanPosition != _position
            && Equals(_entries[_position - 1].CoalesceKey, key);

        if (merge)
        {
            Entry previous = _entries[_position - 1];
            _entries[_position - 1] = previous with { After = result.After!, SelectionAfter = result.Target!.Value };
        }
        else
        {
            _entries.Add(new Entry(command.Kind, key, Current, result.After!, selectionBefore, result.Target!.Value));
            _position++;
        }

        Current = result.After!;
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public HistoryStep? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        Entry entry = _entries[--_position];
        Current = entry.Before;
        Changed?.Invoke(this, EventArgs.Empty);
        return new HistoryStep(entry.Before, entry.SelectionBefore, entry.Kind);
    }

    public HistoryStep? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        Entry entry = _entries[_position++];
        Current = entry.After;
        Changed?.Invoke(this, EventArgs.Empty);
        return new HistoryStep(entry.After, entry.SelectionAfter, entry.Kind);
    }

    /// <summary>Records that <see cref="Current"/> is what is on disk now.</summary>
    public void MarkClean()
    {
        _cleanPosition = _position;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts over with <paramref name="document"/>: for New and Open. The document counts as clean unless
    /// <paramref name="unsaved"/> says otherwise, as for a restored recovery file, which is not what is on disk.
    /// </summary>
    public void Reset(Document document, bool unsaved = false)
    {
        Current = document ?? throw new ArgumentNullException(nameof(document));
        _entries.Clear();
        _position = 0;
        _cleanPosition = unsaved ? -1 : 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes back the last step if it belongs to the session <paramref name="coalesceKey"/>, as if it never
    /// happened: no redo is left behind. For an editing session that is cancelled (Esc), or that ends empty on
    /// a new topic, which then disappears.
    /// </summary>
    /// <returns>The document before the session, or null when the last step is not that session's.</returns>
    public Document? Abandon(object coalesceKey)
    {
        ArgumentNullException.ThrowIfNull(coalesceKey);

        if (_position == 0 || _position != _entries.Count || !Equals(_entries[_position - 1].CoalesceKey, coalesceKey))
        {
            return null;
        }

        Entry entry = _entries[--_position];
        _entries.RemoveAt(_position);
        if (_cleanPosition > _position)
        {
            _cleanPosition = -1;
        }

        Current = entry.Before;
        Changed?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    /// <summary>Ends coalescing: the next command starts a new step even if its key matches.</summary>
    public void Seal()
    {
        if (_position > 0)
        {
            _entries[_position - 1] = _entries[_position - 1] with { CoalesceKey = null };
        }
    }

    private sealed record Entry(
        CommandKind Kind,
        object? CoalesceKey,
        Document Before,
        Document After,
        EditTarget SelectionBefore,
        EditTarget SelectionAfter);
}
