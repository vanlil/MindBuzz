using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using MindBuzz.App.Localization;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// The format bar: bold, italic and underline as toggle buttons, hovering over the in-place editor while it is open.
/// The buttons show the styles at the caret; a tooltip names each with its shortcut.
/// </summary>
internal sealed partial class MapCanvas
{
    /// <summary>The gap between the bar and the editor.</summary>
    private const double FormatBarGap = 6;

    private Border _formatBar = null!;
    private ToggleButton _boldButton = null!;
    private ToggleButton _italicButton = null!;
    private ToggleButton _underlineButton = null!;
    private TitleFormatter _formatter = null!;

    /// <summary>The format bar, for tests.</summary>
    internal Border FormatBar => _formatBar;

    /// <inheritdoc cref="ViewModels.IMapView.ToggleTitleStyle"/>
    public void ToggleTitleStyle(TitleStyle style)
    {
        if (_editing is not null)
        {
            _formatter.Toggle(style);
        }
    }

    /// <summary>Each button's tooltip: the command's name and its shortcut on this platform, from the command
    /// table.</summary>
    internal void SetFormatTooltips(string bold, string italic, string underline)
    {
        ToolTip.SetTip(_boldButton, bold);
        ToolTip.SetTip(_italicButton, italic);
        ToolTip.SetTip(_underlineButton, underline);
    }

    private void CreateFormatBar()
    {
        _formatter = new TitleFormatter(_editor);
        _formatter.StyleChanged += (_, _) => ShowFormatState();

        _boldButton = FormatButton(Strings.Format_BoldLetter, TitleStyle.Bold, letter => letter.FontWeight = FontWeight.Bold);
        // Inter's plain capital I is a bare bar, which slanted reads as "/"; its character variant 8 has serifs.
        _italicButton = FormatButton(Strings.Format_ItalicLetter, TitleStyle.Italic, letter =>
        {
            letter.FontStyle = FontStyle.Italic;
            letter.FontFeatures = FontFeatureCollection.Parse("cv08");
        });
        _underlineButton = FormatButton(Strings.Format_UnderlineLetter, TitleStyle.Underline, letter => letter.TextDecorations = TextDecorations.Underline);
        _formatBar = new Border
        {
            Classes = { "formatBar" },
            IsVisible = false,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _boldButton, _italicButton, _underlineButton } },
        };

        // A press on a button acts at once and goes no further: neither the focus nor the canvas sees it, so the editor
        // keeps the keyboard and stays open (a click elsewhere commits).
        _formatBar.AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                e.Handled = true;
                if ((e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true) is { Tag: TitleStyle style })
                {
                    _formatter.Toggle(style);
                }
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        VisualChildren.Add(_formatBar);
        LogicalChildren.Add(_formatBar);
    }

    private static ToggleButton FormatButton(string letter, TitleStyle style, Action<TextBlock> look)
    {
        var text = new TextBlock { Text = letter, FontFamily = new FontFamily("fonts:Inter#Inter"), HorizontalAlignment = HorizontalAlignment.Center };
        look(text);
        return new ToggleButton { Classes = { "format" }, Content = text, Tag = style, Focusable = false };
    }

    private void ShowFormatState()
    {
        TitleStyle style = _editing is null ? TitleStyle.None : _formatter.Current;
        _boldButton.IsChecked = style.HasFlag(TitleStyle.Bold);
        _italicButton.IsChecked = style.HasFlag(TitleStyle.Italic);
        _underlineButton.IsChecked = style.HasFlag(TitleStyle.Underline);
    }

    /// <summary>Above the editor, centred on it; below it when there is no room above.</summary>
    private void ArrangeFormatBar(Rect editor)
    {
        _formatBar.Measure(Size.Infinity);
        Size size = _formatBar.DesiredSize;
        double x = Math.Clamp(editor.Center.X - (size.Width / 2), 0, Math.Max(0, Bounds.Width - size.Width));
        double y = editor.Y - FormatBarGap - size.Height;
        if (y < 0)
        {
            y = editor.Bottom + FormatBarGap;
        }

        _formatBar.Arrange(new Rect(new Point(x, y), size));
    }
}
