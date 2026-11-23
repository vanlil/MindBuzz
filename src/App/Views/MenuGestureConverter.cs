using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;
using MindBuzz.App.Commands;

namespace MindBuzz.App.Views;

/// <summary>
/// A menu item's shortcut in the app's own wording (<see cref="GestureText"/>), the same as the shortcuts dialog and
/// the printed sheet: "Enter" and "Ctrl+Alt+Up" on Windows, where Avalonia's own formatting says "Return" and
/// "Ctrl+Alt+Up Arrow".
/// </summary>
internal sealed class MenuGestureConverter : IValueConverter
{
    public static MenuGestureConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is KeyGesture gesture ? GestureText.Format(gesture, Host.Current) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
