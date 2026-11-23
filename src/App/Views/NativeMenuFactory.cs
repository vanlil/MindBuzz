using Avalonia.Controls;
using MindBuzz.App.Commands;

namespace MindBuzz.App.Views;

/// <summary>The shared menu model rendered as a macOS <see cref="NativeMenu"/>.</summary>
internal static class NativeMenuFactory
{
    /// <param name="entries">The menus.</param>
    /// <param name="theme">The theme the entries' colour pictures are drawn for; bitmaps do not follow a theme change,
    /// so the window redraws them with <see cref="Recolor"/>.</param>
    internal static NativeMenu Build(IEnumerable<MenuEntry> entries, Avalonia.Styling.ThemeVariant? theme = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var menu = new NativeMenu();

        foreach (MenuEntry entry in entries)
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            // The macOS menu bar has no access keys, so the underscore that marks one in-window is removed.
            var item = new NativeMenuItem(entry.PlainHeader)
            {
                Command = entry.Command,
                Gesture = entry.Gesture,
                Icon = entry.Swatch is { } swatch ? MenuSwatches.Render(swatch, theme ?? Avalonia.Styling.ThemeVariant.Light) : null,
            };

            if (entry.HasChildren)
            {
                item.Menu = Build(entry.Children!, theme);

                // macOS appends "Enter Full Screen", with an icon, to a menu titled View, in the last group, so that
                // group's items were indented to line up with it. A closing separator gives the
                // system item a group of its own.
                if (entry.PlainHeader == MenuText.WithoutAccessKey(Localization.Strings.Menu_View))
                {
                    item.Menu.Items.Add(new NativeMenuItemSeparator());
                }
            }

            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>
    /// Draws the colour pictures of a menu built from <paramref name="entries"/> again for <paramref name="theme"/>, in
    /// place. <em>Not</em> by building a new menu bar: replacing it while a theme change is being raised made Avalonia
    /// 12.1.6 throw ("The menu being updated does not match"), which stopped the window from taking the new theme.
    /// </summary>
    internal static void Recolor(NativeMenu menu, IEnumerable<MenuEntry> entries, Avalonia.Styling.ThemeVariant theme)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(entries);

        foreach ((NativeMenuItemBase itemBase, MenuEntry entry) in menu.Items.Zip(entries))
        {
            if (itemBase is not NativeMenuItem item)
            {
                continue;
            }

            if (entry.Swatch is { } swatch)
            {
                item.Icon = MenuSwatches.Render(swatch, theme);
            }

            if (entry.HasChildren && item.Menu is { } submenu)
            {
                Recolor(submenu, entry.Children!, theme);
            }
        }
    }
}
