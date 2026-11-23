using MindBuzz.Core.Model;

namespace MindBuzz.Core.Layout;

/// <summary>The three visual levels. Styling, font and wrap width differ per level.</summary>
public enum TopicLevel
{
    Central,
    Main,
    Sub,
}

/// <summary>Which side of the central topic a topic is on.</summary>
public enum Side
{
    Center,
    Right,
    Left,
}

public readonly record struct LayoutSize(double Width, double Height);

public readonly record struct LayoutPoint(double X, double Y);

/// <summary>A rectangle in map coordinates (device-independent pixels at 100 %, the central topic centred on
/// 0,0).</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    public bool Contains(LayoutPoint point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    public bool Intersects(LayoutRect other) => other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;

    public LayoutRect Union(LayoutRect other)
    {
        double x = Math.Min(X, other.X);
        double y = Math.Min(Y, other.Y);
        return new LayoutRect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }

    public LayoutRect Inflate(double amount) => new(X - amount, Y - amount, Width + (2 * amount), Height + (2 * amount));
}

/// <summary>
/// Measures topic text. Core cannot measure fonts itself (it has no UI references), so the app implements
/// this with the bundled Inter font. One font everywhere is what makes layout identical on every OS.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>The size of <paramref name="text"/> in the level's font, wrapped at <paramref
    /// name="maxWidth"/>.</summary>
    LayoutSize Measure(string text, TopicLevel level, double maxWidth);
}

/// <summary>
/// Spacing and sizing of the layout, on the 4 px grid. The defaults match the topic tokens in the app's
/// <c>Themes/Semantic.axaml</c> (padding, radius and font size per level).
/// </summary>
public sealed record LayoutMetrics
{
    public static LayoutMetrics Default { get; } = new();

    /// <summary>
    /// Horizontal and vertical padding inside a topic, per level: generous, because a crowded map is hard to
    /// read.
    /// </summary>
    public LayoutSize CentralPadding { get; init; } = new(24, 14);

    public LayoutSize MainPadding { get; init; } = new(16, 8);

    public LayoutSize SubPadding { get; init; } = new(12, 6);

    /// <summary>Text wraps at this width, per level (wraps, never truncates).</summary>
    public double CentralTextWidth { get; init; } = 280;

    public double MainTextWidth { get; init; } = 220;

    public double SubTextWidth { get; init; } = 240;

    /// <summary>A topic is never narrower than this, so short words still make a comfortable target.</summary>
    public double CentralMinWidth { get; init; } = 120;

    public double MainMinWidth { get; init; } = 64;

    public double SubMinWidth { get; init; } = 40;

    /// <summary>Space from the central topic to the main topics, and from any other topic to its subtopics.</summary>
    public double CentralGap { get; init; } = 80;

    public double LevelGap { get; init; } = 48;

    /// <summary>Vertical space between main topics' subtrees, and between subtopics' subtrees.</summary>
    public double MainGap { get; init; } = 32;

    public double SubGap { get; init; } = 14;

    /// <summary>A marker's size and the gap after it (drawn left of the text).</summary>
    public double MarkerSize { get; init; } = 21;

    public double MarkerGap { get; init; } = 4;

    /// <summary>A note or link indicator's size and the gap before it (drawn right of the text).</summary>
    public double IndicatorSize { get; init; } = 14;

    public double IndicatorGap { get; init; } = 4;

    public LayoutSize PaddingOf(TopicLevel level) => level switch
    {
        TopicLevel.Central => CentralPadding,
        TopicLevel.Main => MainPadding,
        _ => SubPadding,
    };

    public double TextWidthOf(TopicLevel level) => level switch
    {
        TopicLevel.Central => CentralTextWidth,
        TopicLevel.Main => MainTextWidth,
        _ => SubTextWidth,
    };

    internal double MinWidthOf(TopicLevel level) => level switch
    {
        TopicLevel.Central => CentralMinWidth,
        TopicLevel.Main => MainMinWidth,
        _ => SubMinWidth,
    };
}

/// <summary>Where one visible topic is drawn.</summary>
/// <param name="Topic">The topic, for its text, markers and indicators.</param>
/// <param name="Level">Its visual level.</param>
/// <param name="Side">Its side; <see cref="Side.Center"/> for the central topic.</param>
/// <param name="Bounds">The topic's outline.</param>
/// <param name="Text">Where the text goes, inside <paramref name="Bounds"/>.</param>
/// <param name="Depth">0 for the central topic.</param>
/// <param name="Parent">The parent's id; null for the central topic.</param>
public sealed record TopicBox(Topic Topic, TopicLevel Level, Side Side, LayoutRect Bounds, LayoutRect Text, int Depth, TopicId? Parent)
{
    public TopicId Id => Topic.Id;

    /// <summary>Subtopics hidden by a collapse: drawn as the count on the collapse handle.</summary>
    public int HiddenChildren => Topic.IsCollapsed ? Topic.Children.Length : 0;

    /// <summary>The size a topic icon has at this level (<see cref="LayoutEngine.IconSizeOf"/>); 0 when not laid out by
    /// it.</summary>
    public double IconSize { get; init; }

    /// <summary>One line of text at this level (<see cref="LayoutEngine.LineHeightOf"/>), for topics with indicators;
    /// else 0.</summary>
    public double LineHeight { get; init; }
}

/// <summary>A tree connector from a parent's edge into a child's inner edge.</summary>
public readonly record struct Connector(TopicId Parent, TopicId Child, LayoutPoint Start, LayoutPoint End, Side Side)
{
    /// <summary>The box the curve stays inside, for culling.</summary>
    public LayoutRect Bounds => new(
        Math.Min(Start.X, End.X),
        Math.Min(Start.Y, End.Y),
        Math.Abs(End.X - Start.X),
        Math.Abs(End.Y - Start.Y));
}
