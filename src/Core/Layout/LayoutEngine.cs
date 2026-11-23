using System.Runtime.CompilerServices;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Layout;

/// <summary>The result of laying out one map: every visible topic and connector, in document order.</summary>
public sealed class MapLayout
{
    private readonly Dictionary<TopicId, int> _byId;

    internal MapLayout(List<TopicBox> boxes, List<Connector> connectors)
    {
        Boxes = boxes;
        Connectors = connectors;
        _byId = new Dictionary<TopicId, int>(boxes.Count);

        LayoutRect bounds = boxes[0].Bounds;
        for (int i = 0; i < boxes.Count; i++)
        {
            _byId[boxes[i].Id] = i;
            bounds = bounds.Union(boxes[i].Bounds);
        }

        Bounds = bounds;
    }

    /// <summary>Visible topics. The central topic is first.</summary>
    public IReadOnlyList<TopicBox> Boxes { get; }

    public IReadOnlyList<Connector> Connectors { get; }

    /// <summary>The union of every topic's bounds.</summary>
    public LayoutRect Bounds { get; }

    /// <summary>The box of a visible topic, or null when it is hidden in a collapsed branch.</summary>
    public TopicBox? Find(TopicId id) => _byId.TryGetValue(id, out int index) ? Boxes[index] : null;
}

/// <summary>
/// The balanced layout: pure, deterministic for the same map and the same text measurements.
/// <list type="bullet">
/// <item>Each main topic stays on its own side (<see cref="MainSides"/>): the right side top to bottom in list order,
/// the left side bottom to top, so the list runs clockwise
/// from the top right. The layout never moves a main topic to the other side.</item>
/// <item>Subtopics stack vertically on their side; a parent is centred on its children's block.</item>
/// </list>
/// <para>
/// Incremental by construction: the shape of each subtree (its sizes and the offsets of its children) is cached
/// per immutable <see cref="Topic"/> instance. An edit replaces only the topics on the path to the change, so only
/// those are measured and shaped again; placing the visible topics afterwards is a cheap walk.
/// </para>
/// </summary>
public sealed class LayoutEngine
{
    private readonly ITextMeasurer _measurer;
    private readonly LayoutMetrics _metrics;
    private readonly ConditionalWeakTable<Topic, Shape> _shapes = [];
    private readonly Dictionary<TopicLevel, double> _iconSizes = [];
    private readonly Dictionary<TopicLevel, double> _lineHeights = [];

    public LayoutEngine(ITextMeasurer measurer, LayoutMetrics? metrics = null)
    {
        _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
        _metrics = metrics ?? LayoutMetrics.Default;
    }

    /// <summary>Forgets every cached shape: for when text measurements change (they do not with a theme
    /// switch).</summary>
    public void Clear()
    {
        _shapes.Clear();
        _iconSizes.Clear();
        _lineHeights.Clear();
    }

    /// <summary>
    /// A topic icon's size at <paramref name="level"/>: three quarters of a one-line topic's height,
    /// measured once per level with the real font.
    /// </summary>
    public double IconSizeOf(TopicLevel level)
    {
        if (!_iconSizes.TryGetValue(level, out double size))
        {
            size = Math.Round(0.75 * (LineHeightOf(level) + (2 * _metrics.PaddingOf(level).Height)));
            _iconSizes[level] = size;
        }

        return size;
    }

    /// <summary>The height of one line of text at <paramref name="level"/>, measured once with the real font.</summary>
    public double LineHeightOf(TopicLevel level)
    {
        if (!_lineHeights.TryGetValue(level, out double height))
        {
            height = _measurer.Measure("Ag", level, _metrics.TextWidthOf(level)).Height;
            _lineHeights[level] = height;
        }

        return height;
    }

    public MapLayout Layout(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        Topic root = map.Root;
        LayoutSize centralSize = BoxSize(root, TopicLevel.Central, out LayoutRect centralText);
        var central = new LayoutRect(-centralSize.Width / 2, -centralSize.Height / 2, centralSize.Width, centralSize.Height);

        var boxes = new List<TopicBox>(map.Index.Count)
        {
            new(root, TopicLevel.Central, Side.Center, central, Offset(centralText, central.X, central.Y), 0, null)
            {
                IconSize = root.MarkerOf(MarkerGroups.Icon) is null ? 0 : IconSizeOf(TopicLevel.Central),
                LineHeight = HasIndicators(root) ? LineHeightOf(TopicLevel.Central) : 0,
            },
        };
        var connectors = new List<Connector>(map.Index.Count);

        if (!root.IsCollapsed && root.HasChildren)
        {
            PlaceSide(root, central, [.. MainSides.TopToBottom(map, MainSide.Right)], Side.Right, boxes, connectors);

            // Clockwise: the left side continues from the bottom up, so read top to bottom it is reversed.
            PlaceSide(root, central, [.. MainSides.TopToBottom(map, MainSide.Left)], Side.Left, boxes, connectors);
        }

        return new MapLayout(boxes, connectors);
    }

    private void PlaceSide(Topic root, LayoutRect central, Topic[] mains, Side side, List<TopicBox> boxes, List<Connector> connectors)
    {
        if (mains.Length == 0)
        {
            return;
        }

        Shape[] shapes = [.. mains.Select(main => ShapeOf(main, TopicLevel.Main))];
        double height = shapes.Sum(shape => shape.SubtreeHeight) + (_metrics.MainGap * (shapes.Length - 1));

        // The side's block is centred on the central topic.
        double top = central.CenterY - (height / 2);
        double edge = side == Side.Right ? central.Right + _metrics.CentralGap : central.X - _metrics.CentralGap;
        var start = new LayoutPoint(side == Side.Right ? central.Right : central.X, central.CenterY);

        for (int i = 0; i < mains.Length; i++)
        {
            Place(mains[i], shapes[i], side, edge, top, depth: 1, root.Id, start, boxes, connectors);
            top += shapes[i].SubtreeHeight + _metrics.MainGap;
        }
    }

    /// <summary>
    /// Places a topic whose inner edge is at <paramref name="edge"/> (its left edge on the right side, its right
    /// edge on the left side) and whose subtree starts at <paramref name="top"/>, then its children.
    /// </summary>
    private void Place(
        Topic topic,
        Shape shape,
        Side side,
        double edge,
        double top,
        int depth,
        TopicId parent,
        LayoutPoint connectorStart,
        List<TopicBox> boxes,
        List<Connector> connectors)
    {
        double x = side == Side.Right ? edge : edge - shape.Box.Width;
        var bounds = new LayoutRect(x, top + shape.BoxTop, shape.Box.Width, shape.Box.Height);
        bool icon = topic.MarkerOf(MarkerGroups.Icon) is not null;

        // On the left side the icon is on the right border: the text moves left by the inset it was given on the left.
        LayoutRect text = icon && side == Side.Left ? shape.Text with { X = shape.Text.X - TopicParts.IconInset(IconSizeOf(shape.Level), _metrics) } : shape.Text;
        boxes.Add(new TopicBox(topic, shape.Level, side, bounds, Offset(text, bounds.X, bounds.Y), depth, parent)
        {
            IconSize = icon ? IconSizeOf(shape.Level) : 0,
            LineHeight = HasIndicators(topic) ? LineHeightOf(shape.Level) : 0,
        });

        var inner = new LayoutPoint(side == Side.Right ? bounds.X : bounds.Right, bounds.CenterY);
        connectors.Add(new Connector(parent, topic.Id, connectorStart, inner, side));

        if (shape.Children.Length == 0)
        {
            return;
        }

        double childEdge = side == Side.Right ? bounds.Right + _metrics.LevelGap : bounds.X - _metrics.LevelGap;
        var outer = new LayoutPoint(side == Side.Right ? bounds.Right : bounds.X, bounds.CenterY);

        for (int i = 0; i < shape.Children.Length; i++)
        {
            Place(topic.Children[i], shape.Children[i], side, childEdge, top + shape.ChildTops[i], depth + 1, topic.Id, outer, boxes, connectors);
        }
    }

    /// <summary>The subtree shape of <paramref name="topic"/> at <paramref name="level"/>, from the cache when
    /// possible.</summary>
    private Shape ShapeOf(Topic topic, TopicLevel level)
    {
        if (_shapes.TryGetValue(topic, out Shape? cached) && cached.Level == level)
        {
            return cached;
        }

        LayoutSize box = BoxSize(topic, level, out LayoutRect text);

        Shape[] children = topic.IsCollapsed ? [] : [.. topic.Children.Select(child => ShapeOf(child, TopicLevel.Sub))];
        double[] childTops = new double[children.Length];

        double stack = children.Sum(child => child.SubtreeHeight) + (_metrics.SubGap * Math.Max(0, children.Length - 1));
        double subtree = Math.Max(box.Height, stack);

        double y = (subtree - stack) / 2;
        for (int i = 0; i < children.Length; i++)
        {
            childTops[i] = y;
            y += children[i].SubtreeHeight + _metrics.SubGap;
        }

        var shape = new Shape(level, box, text, (subtree - box.Height) / 2, subtree, children, childTops);
        _shapes.AddOrUpdate(topic, shape);
        return shape;
    }

    /// <summary>A topic's outline size, and where its text sits inside (relative to the outline's top left).</summary>
    private LayoutSize BoxSize(Topic topic, TopicLevel level, out LayoutRect text)
    {
        LayoutSize padding = _metrics.PaddingOf(level);
        LayoutSize measured = _measurer.Measure(topic.Text, level, _metrics.TextWidthOf(level));

        // Markers hover on the border and take no room inside (TopicParts), apart from the icon: half of it lies
        // inside, so the content moves away from its border by that half. Laid out for an icon on the left; Place
        // mirrors the text for the left side of the map, where the icon is on the right border.
        double icon = topic.MarkerOf(MarkerGroups.Icon) is null ? 0 : TopicParts.IconInset(IconSizeOf(level), _metrics);
        int indicatorCount = (topic.Note.Length > 0 ? 1 : 0) + (topic.Links.IsEmpty ? 0 : 1);
        double indicators = indicatorCount * (_metrics.IndicatorSize + _metrics.IndicatorGap);

        double contentHeight = measured.Height;
        double width = Math.Max(_metrics.MinWidthOf(level), icon + padding.Width + measured.Width + indicators + padding.Width);
        double height = contentHeight + (2 * padding.Height);

        // Text is centred in whatever width the minimum leaves over.
        double free = width - icon - (2 * padding.Width) - indicators - measured.Width;
        text = new LayoutRect(icon + padding.Width + (free / 2), (height - measured.Height) / 2, measured.Width, measured.Height);
        return new LayoutSize(width, height);
    }

    private static bool HasIndicators(Topic topic) => topic.Note.Length > 0 || !topic.Links.IsEmpty;

    private static LayoutRect Offset(LayoutRect rect, double x, double y) => rect with { X = rect.X + x, Y = rect.Y + y };

    /// <summary>
    /// A subtree's shape, independent of where it ends up: the topic's own size, where its box sits in the
    /// subtree's height, and where each child's subtree starts. Immutable, so sharing it between layouts is safe.
    /// </summary>
    private sealed record Shape(
        TopicLevel Level,
        LayoutSize Box,
        LayoutRect Text,
        double BoxTop,
        double SubtreeHeight,
        Shape[] Children,
        double[] ChildTops);
}
