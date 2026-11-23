using MindBuzz.Core.Layout;

namespace MindBuzz.App.Canvas;

/// <summary>
/// A uniform grid over map space that finds the topics and connectors in a rectangle without looking at the
/// others. Culling (draw only what is visible) and hit testing both use it, so a 5,000-topic map costs what the
/// visible part costs. Built once per layout.
/// </summary>
internal sealed class SpatialGrid
{
    /// <summary>About two subtopics high: most topics fall in one or two cells.</summary>
    private const double CellSize = 128;

    private readonly Dictionary<(int X, int Y), List<int>> _topicCells = [];
    private readonly Dictionary<(int X, int Y), List<int>> _connectorCells = [];
    private readonly MapLayout _layout;

    public SpatialGrid(MapLayout layout)
    {
        _layout = layout;

        for (int i = 0; i < layout.Boxes.Count; i++)
        {
            Add(_topicCells, layout.Boxes[i].Bounds, i);
        }

        for (int i = 0; i < layout.Connectors.Count; i++)
        {
            Add(_connectorCells, layout.Connectors[i].Bounds.Inflate(2), i);
        }
    }

    /// <summary>Indices into <see cref="MapLayout.Boxes"/> of topics intersecting <paramref name="area"/>,
    /// ascending.</summary>
    public List<int> TopicsIn(LayoutRect area) => Query(_topicCells, area, index => _layout.Boxes[index].Bounds.Intersects(area));

    public List<int> ConnectorsIn(LayoutRect area) =>
        Query(_connectorCells, area, index => _layout.Connectors[index].Bounds.Inflate(2).Intersects(area));

    /// <summary>The topic under <paramref name="point"/>, or null. Later boxes win, as they are drawn on top.</summary>
    public TopicBox? TopicAt(LayoutPoint point)
    {
        if (!_topicCells.TryGetValue(Cell(point.X, point.Y), out List<int>? candidates))
        {
            return null;
        }

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            TopicBox box = _layout.Boxes[candidates[i]];
            if (box.Bounds.Contains(point))
            {
                return box;
            }
        }

        return null;
    }

    private static List<int> Query(Dictionary<(int X, int Y), List<int>> cells, LayoutRect area, Func<int, bool> intersects)
    {
        var found = new HashSet<int>();
        (int x0, int y0) = Cell(area.X, area.Y);
        (int x1, int y1) = Cell(area.Right, area.Bottom);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (cells.TryGetValue((x, y), out List<int>? items))
                {
                    foreach (int item in items)
                    {
                        if (intersects(item))
                        {
                            found.Add(item);
                        }
                    }
                }
            }
        }

        var sorted = new List<int>(found);
        sorted.Sort();
        return sorted;
    }

    private static void Add(Dictionary<(int X, int Y), List<int>> cells, LayoutRect bounds, int index)
    {
        (int x0, int y0) = Cell(bounds.X, bounds.Y);
        (int x1, int y1) = Cell(bounds.Right, bounds.Bottom);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (!cells.TryGetValue((x, y), out List<int>? items))
                {
                    items = [];
                    cells[(x, y)] = items;
                }

                items.Add(index);
            }
        }
    }

    private static (int X, int Y) Cell(double x, double y) => ((int)Math.Floor(x / CellSize), (int)Math.Floor(y / CellSize));
}
