using System.Globalization;
using System.Text;
using Avalonia.Input;
using MindBuzz.App.Localization;

namespace MindBuzz.App.Commands;

/// <summary>
/// Platform-neutral modifiers for the command table. <see cref="Command"/> is "the platform's command key":
/// Ctrl on Windows and Linux, ⌘ on macOS, which is what Avalonia's
/// <c>PlatformHotkeyConfiguration.CommandModifiers</c> reports on each OS. The table maps it explicitly rather
/// than asking Avalonia, because the table must be asserted for every platform from any host,
/// and Avalonia only knows the platform it is running on.
/// </summary>
[Flags]
internal enum ChordModifiers
{
    None = 0,

    /// <summary>Ctrl on Windows and Linux, ⌘ on macOS.</summary>
    Command = 1,

    Shift = 2,

    /// <summary>Alt on Windows and Linux, ⌥ on macOS.</summary>
    Alt = 4,

    /// <summary>
    /// The literal Control key on every OS: ⌃ on macOS, plain Ctrl elsewhere. Only for gestures that must be
    /// ⌃ on a Mac, such as ⌃Tab for the next map, because ⌘Tab belongs to the OS.
    /// </summary>
    Control = 8,
}

/// <summary>One key plus modifiers, written once for every platform.</summary>
internal readonly record struct Chord(Key Key, ChordModifiers Modifiers = ChordModifiers.None)
{
    /// <summary>The Avalonia gesture this chord becomes on <paramref name="platform"/>.</summary>
    internal KeyGesture ToKeyGesture(HostPlatform platform) => new(Key, ToKeyModifiers(Modifiers, platform));

    internal static KeyModifiers ToKeyModifiers(ChordModifiers modifiers, HostPlatform platform)
    {
        KeyModifiers result = KeyModifiers.None;

        if (modifiers.HasFlag(ChordModifiers.Command))
        {
            result |= platform == HostPlatform.MacOS ? KeyModifiers.Meta : KeyModifiers.Control;
        }

        if (modifiers.HasFlag(ChordModifiers.Control))
        {
            result |= KeyModifiers.Control;
        }

        if (modifiers.HasFlag(ChordModifiers.Shift))
        {
            result |= KeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ChordModifiers.Alt))
        {
            result |= KeyModifiers.Alt;
        }

        return result;
    }
}

/// <summary>
/// Formats gestures the way each platform writes them: "Ctrl+Shift+K" on Windows and Linux, "⇧⌘K" on macOS
/// (modifier order ⌃⌥⇧⌘, as in Apple's menus). Pure, so the shortcuts dialog and the generated shortcut list can be
/// checked for every platform from any machine.
/// </summary>
internal static class GestureText
{
    internal static string Format(KeyGesture gesture, HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        var text = new StringBuilder();
        KeyModifiers modifiers = gesture.KeyModifiers;

        if (platform == HostPlatform.MacOS)
        {
            AppendIf(text, modifiers.HasFlag(KeyModifiers.Control), "⌃");
            AppendIf(text, modifiers.HasFlag(KeyModifiers.Alt), "⌥");
            AppendIf(text, modifiers.HasFlag(KeyModifiers.Shift), "⇧");
            AppendIf(text, modifiers.HasFlag(KeyModifiers.Meta), "⌘");
            return text.Append(KeyName(gesture.Key, platform)).ToString();
        }

        AppendIf(text, modifiers.HasFlag(KeyModifiers.Control), Strings.Key_Ctrl + "+");
        AppendIf(text, modifiers.HasFlag(KeyModifiers.Alt), Strings.Key_Alt + "+");
        AppendIf(text, modifiers.HasFlag(KeyModifiers.Shift), Strings.Key_Shift + "+");
        AppendIf(text, modifiers.HasFlag(KeyModifiers.Meta), Strings.Key_Win + "+");
        return text.Append(KeyName(gesture.Key, platform)).ToString();
    }

    /// <summary>
    /// Key names are symbols on macOS and, elsewhere, what is printed on the key in the user's language, as Windows and
    /// Office write them: "Strg", "Entf", "Pos1" on German keyboards (Strings.resx, Key_*).
    /// </summary>
    internal static string KeyName(Key key, HostPlatform platform)
    {
        bool mac = platform == HostPlatform.MacOS;

        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture),
            Key.Enter => mac ? "↩" : Strings.Key_Enter,
            Key.Escape => mac ? "⎋" : Strings.Key_Esc,
            Key.Tab => mac ? "⇥" : Strings.Key_Tab,
            Key.Back => mac ? "⌫" : Strings.Key_Backspace,
            Key.Delete => mac ? "⌦" : Strings.Key_Delete,
            Key.Insert => Strings.Key_Insert,
            Key.Space => Strings.Key_Space,
            Key.Up => mac ? "↑" : Strings.Key_Up,
            Key.Down => mac ? "↓" : Strings.Key_Down,
            Key.Left => mac ? "←" : Strings.Key_Left,
            Key.Right => mac ? "→" : Strings.Key_Right,
            Key.Home => mac ? "↖" : Strings.Key_Home,
            Key.End => mac ? "↘" : Strings.Key_End,
            Key.PageUp => mac ? "⇞" : Strings.Key_PageUp,
            Key.PageDown => mac ? "⇟" : Strings.Key_PageDown,
            Key.OemPlus or Key.Add => "+",
            Key.OemMinus or Key.Subtract => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString(),
        };
    }

    private static void AppendIf(StringBuilder text, bool condition, string value)
    {
        if (condition)
        {
            text.Append(value);
        }
    }
}
