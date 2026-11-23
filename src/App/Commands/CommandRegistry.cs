namespace MindBuzz.App.Commands;

/// <summary>
/// The one command registry. Menus, context menus, the toolbar, the key handler and the
/// shortcuts dialog are generated from it.
/// </summary>
internal sealed class CommandRegistry
{
    private readonly Dictionary<CommandId, AppCommand> _byId;

    public CommandRegistry(IEnumerable<AppCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        All = [.. commands];
        _byId = All.ToDictionary(command => command.Id);
    }

    /// <summary>Every command, in registration order, which is also the shortcuts dialog's order.</summary>
    public IReadOnlyList<AppCommand> All { get; }

    public AppCommand this[CommandId id] => _byId[id];

    public bool Contains(CommandId id) => _byId.ContainsKey(id);

    /// <summary>Re-evaluates every command's enabled state, after the selection, editing or history changed.</summary>
    public void RefreshAll()
    {
        foreach (AppCommand command in All)
        {
            command.Refresh();
        }
    }
}
