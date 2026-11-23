using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Controls;
using Avalonia.Media;

namespace MindBuzz.App.Views;

/// <summary>
/// Turns an icon key from the command registry into its drawn geometry from <c>Themes/Icons.axaml</c>, so the
/// toolbar is generated from data like the menus.
/// </summary>
internal sealed class IconConverter : IValueConverter
{
    public static IconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource(key, out object? resource) == true && resource is Geometry geometry
            ? geometry
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
