using Avalonia.Input;

namespace MindBuzz.App.Commands;

/// <summary>
/// Which gestures the window's key handler binds itself. Pure, so it is checked for every platform.
/// </summary>
internal static class KeyBindingPlan
{
    /// <summary>
    /// Every gesture of every command in <paramref name="scope"/>, except on macOS the ones the native menu bar
    /// already handles: a macOS menu item with a key equivalent fires on its own, and a binding for the same
    /// gesture would run the command twice.
    /// </summary>
    internal static IReadOnlyList<(KeyGesture Gesture, AppCommand Command)> For(
        CommandRegistry registry,
        MenuModel menus,
        HostPlatform platform,
        KeyScope scope = KeyScope.Window)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(menus);

        HashSet<KeyGesture> handledByNativeMenu = platform == HostPlatform.MacOS
            ? [.. Flatten(menus.TopLevel).Concat(Flatten(menus.MacApplicationItems))
                .Select(entry => entry.Gesture)
                .OfType<KeyGesture>()]
            : [];

        var bindings = new List<(KeyGesture, AppCommand)>();

        foreach (AppCommand command in registry.All.Where(command => command.Scope == scope))
        {
            foreach (KeyGesture gesture in command.Gestures.KeyGestures(platform))
            {
                if (!handledByNativeMenu.Contains(gesture))
                {
                    bindings.Add((gesture, command));
                }
            }
        }

        return bindings;
    }

    private static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> entries) =>
        entries.SelectMany(entry => entry.HasChildren ? Flatten(entry.Children!).Prepend(entry) : [entry]);
}
