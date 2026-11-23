using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Canvas;

/// <summary>
/// Measures and shapes topic text with the bundled Inter font (one font, so the same map lays out the
/// same on every OS).
/// <para>
/// Two caches with different lifetimes. <b>Sizes</b> are kept for every text: they are small, and the layout
/// engine needs them for every topic. <b>Shaped text</b> (<see cref="TextLayout"/>, which holds native glyph
/// data) is kept only for the topics drawn recently, and disposed when evicted. Keeping it for every topic of a
/// 5,000-topic map cost well over 100 MB of native memory; panning to an unshaped area
/// costs a few milliseconds of shaping instead.
/// </para>
/// <para>
/// The foreground is one brush per level that this class owns. The canvas sets its colour from the theme before
/// drawing, so a theme switch re-colours text without shaping anything again.
/// </para>
/// </summary>
internal sealed class TopicText : ITextMeasurer
{
    /// <summary>
    /// Shaped layouts kept at most. A full window at 100 % shows a few hundred topics; this leaves room for panning
    /// back and forth without shaping again.
    /// </summary>
    private const int MaxShaped = 2_000;

    /// <summary>Sizes kept at most; an editing session never comes close.</summary>
    private const int MaxSizes = 100_000;

    private static readonly FontFamily Inter = new("fonts:Inter#Inter");

    /// <summary>
    /// The most lines a topic shows. Editing cannot go beyond it (<see cref="TitleLineGuard"/>);
    /// a longer title from an older file or a paste is cut after the last line with "…".
    /// </summary>
    internal const int MaxLines = 4;

    private readonly Dictionary<(string Text, TopicLevel Level), LayoutSize> _sizes = [];
    private readonly Dictionary<(string Text, TopicLevel Level), (TextLayout Layout, long LastUse)> _shaped = [];

    /// <summary>
    /// Evicted layouts and the frame they were evicted in. They are disposed a few frames later: the render
    /// thread may still be drawing the frame that used them.
    /// </summary>
    private readonly Queue<(TextLayout Layout, long Frame)> _retired = new();

    private long _frame;

    public SolidColorBrush CentralForeground { get; } = new(Colors.Black);

    public SolidColorBrush MainForeground { get; } = new(Colors.Black);

    public SolidColorBrush SubForeground { get; } = new(Colors.Black);

    /// <summary>How many shaped layouts are held now: a test seam for the eviction rule.</summary>
    internal int ShapedCount => _shaped.Count;

    /// <summary>Font sizes per level, in DIPs; the same values as the <c>Topic*FontSize</c> tokens.</summary>
    internal static double FontSize(TopicLevel level) => level switch
    {
        TopicLevel.Central => 18,
        TopicLevel.Main => 14,
        _ => 12.5,
    };

    internal static FontWeight Weight(TopicLevel level) => level == TopicLevel.Sub ? FontWeight.Normal : FontWeight.SemiBold;

    public LayoutSize Measure(string text, TopicLevel level, double maxWidth)
    {
        if (_sizes.TryGetValue((text, level), out LayoutSize size))
        {
            return size;
        }

        if (_sizes.Count >= MaxSizes)
        {
            _sizes.Clear();
        }

        // Measuring shapes the text anyway; the result is not kept, so a map that is only laid out (most of a
        // big map, at first) holds no glyph data.
        using TextLayout layout = Create(text, level, maxWidth);
        size = new LayoutSize(Math.Ceiling(layout.WidthIncludingTrailingWhitespace), Math.Ceiling(layout.Height));
        _sizes[(text, level)] = size;
        return size;
    }

    /// <summary>Called by the canvas at the start of every frame it renders.</summary>
    public void BeginFrame()
    {
        _frame++;

        // Two frames is enough for the compositor to have finished with a frame's drawing commands.
        while (_retired.Count > 0 && _retired.Peek().Frame < _frame - 2)
        {
            _retired.Dequeue().Layout.Dispose();
        }
    }

    /// <summary>
    /// The shaped text to draw. The wrap width is fixed per level by the layout metrics, so it is not part of the
    /// cache key.
    /// </summary>
    public TextLayout Get(string text, TopicLevel level, double maxWidth)
    {
        if (_shaped.TryGetValue((text, level), out var cached))
        {
            _shaped[(text, level)] = (cached.Layout, _frame);
            return cached.Layout;
        }

        if (_shaped.Count >= MaxShaped)
        {
            Evict();
        }

        TextLayout layout = Create(text, level, maxWidth);
        _shaped[(text, level)] = (layout, _frame);
        return layout;
    }

    /// <summary>
    /// Releases all shaped text now. The canvas calls this when it leaves the window; the instance stays usable
    /// and simply shapes again when asked.
    /// </summary>
    public void Release()
    {
        foreach ((TextLayout layout, _) in _shaped.Values)
        {
            layout.Dispose();
        }

        while (_retired.Count > 0)
        {
            _retired.Dequeue().Layout.Dispose();
        }

        _shaped.Clear();
        _sizes.Clear();
    }

    /// <summary>
    /// Retires the least recently drawn half, but never a layout drawn in the current frame: a frame showing more
    /// topics than the cap simply grows the cache for that frame.
    /// </summary>
    private void Evict()
    {
        long threshold = Math.Min(_frame - 1, _shaped.Values.Select(entry => entry.LastUse).Order().ElementAt(_shaped.Count / 2));
        foreach (var key in _shaped.Where(entry => entry.Value.LastUse <= threshold).Select(entry => entry.Key).ToList())
        {
            _retired.Enqueue((_shaped[key].Layout, _frame));
            _shaped.Remove(key);
        }
    }

    private TextLayout Create(string text, TopicLevel level, double maxWidth) =>
        Shape(text, level, maxWidth, ForegroundOf(level), MaxLines);

    /// <summary>How many lines <paramref name="text"/> takes at this level and width, without the line limit.</summary>
    internal static int CountLines(string text, TopicLevel level, double maxWidth)
    {
        using TextLayout layout = Shape(text, level, maxWidth, Brushes.Black, maxLines: 0);
        return layout.TextLines.Count;
    }

    /// <summary>
    /// The size of <paramref name="text"/> as typed, markers included and not cached: the in-place editor shows the
    /// title's Markdown, and its text changes with every key.
    /// </summary>
    internal static LayoutSize MeasureAsTyped(string text, TopicLevel level, double maxWidth)
    {
        using var layout = new TextLayout(
            text.Length == 0 ? " " : text,
            new Typeface(Inter, FontStyle.Normal, Weight(level)),
            FontSize(level),
            Brushes.Black,
            TextAlignment.Center,
            TextWrapping.Wrap,
            maxWidth: maxWidth);
        return new LayoutSize(Math.Ceiling(layout.WidthIncludingTrailingWhitespace), Math.Ceiling(layout.Height));
    }

    /// <summary>
    /// The title as shown: its Markdown read (<see cref="TitleMarkup"/>), the markers left out, bold, italic and
    /// underline as runs. Bold is Inter Bold on regular subtopics and Inter ExtraBold on the semibold central and main
    /// topics, where Bold would hardly show; italic is Inter slanted by the font engine, as Inter comes without an
    /// italic face here.
    /// </summary>
    private static TextLayout Shape(string text, TopicLevel level, double maxWidth, IBrush foreground, int maxLines)
    {
        FontWeight weight = Weight(level);
        double size = FontSize(level);
        List<ValueSpan<TextRunProperties>>? overrides = null;
        string plain = text;
        if (TitleMarkup.HasMarkers(text))
        {
            ParsedTitle parsed = TitleMarkup.Parse(text);
            plain = parsed.Plain;
            foreach (TitleRun run in parsed.Runs.Where(run => run.Style != TitleStyle.None))
            {
                overrides ??= [];
                FontStyle style = run.Style.HasFlag(TitleStyle.Italic) ? FontStyle.Italic : FontStyle.Normal;
                var typeface = !run.Style.HasFlag(TitleStyle.Bold) ? new Typeface(Inter, style, weight)
                    : weight >= FontWeight.SemiBold ? new Typeface(Fonts.InterExtraBold, style, FontWeight.ExtraBold)
                    : new Typeface(Inter, style, FontWeight.Bold);
                overrides.Add(new ValueSpan<TextRunProperties>(
                    run.Start,
                    run.Length,
                    new GenericTextRunProperties(typeface, size, run.Style.HasFlag(TitleStyle.Underline) ? TextDecorations.Underline : null, foreground)));
            }
        }

        // An empty topic (a new one, being typed into) still has the height of one line.
        return new TextLayout(
            plain.Length == 0 ? " " : plain,
            new Typeface(Inter, FontStyle.Normal, weight),
            size,
            foreground,
            TextAlignment.Center,
            TextWrapping.Wrap,
            maxLines > 0 ? TextTrimming.CharacterEllipsis : null,
            maxWidth: maxWidth,
            maxLines: maxLines,
            textStyleOverrides: overrides);
    }

    private SolidColorBrush ForegroundOf(TopicLevel level) => level switch
    {
        TopicLevel.Central => CentralForeground,
        TopicLevel.Main => MainForeground,
        _ => SubForeground,
    };
}
