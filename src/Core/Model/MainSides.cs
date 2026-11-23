namespace MindBuzz.Core.Model;

/// <summary>The side of the central topic a main topic sits on.</summary>
public enum MainSide
{
    Right,
    Left,
}

/// <summary>
/// Main topics stay on their side, so adding a topic never shakes up the map.
/// Each main topic carries its side; adding, pasting and moving decide it once,
/// and nothing changes it afterwards except the user: a drag across, Move to Other Side, or Balance Map.
/// <para>
/// On each side the main topics keep their order in the central topic's list: the right side top to bottom, the left
/// side bottom to top, so the list reads clockwise from the top right.
/// </para>
/// </summary>
public static class MainSides
{
    /// <summary>The side of a main topic: its own, or for a main topic without one the old count rule.</summary>
    public static MainSide Of(Map map, Topic main)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(main);

        if (main.Side is { } side)
        {
            return side;
        }

        int index = map.Root.Children.IndexOf(main);
        return index >= 0 && index < CountRuleRightCount(map.Root.Children.Length) ? MainSide.Right : MainSide.Left;
    }

    /// <summary>
    /// The map with every main topic's side written down: for files from before sides were saved, which then look
    /// exactly as they did (the count rule: the first half, rounded up, on the right).
    /// </summary>
    public static Map Fill(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (map.Root.Children.All(child => child.Side is not null))
        {
            return map;
        }

        Topic root = map.Root;
        return map with
        {
            Root = root with { Children = [.. root.Children.Select(child => child.Side is null ? child with { Side = Of(map, child) } : child)] },
        };
    }

    /// <summary>The side with fewer main topics, the right one on a tie: where a new one goes without a better
    /// hint.</summary>
    public static MainSide Fewer(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);
        int right = map.Root.Children.Count(child => Of(map, child) == MainSide.Right);
        return right <= map.Root.Children.Length - right ? MainSide.Right : MainSide.Left;
    }

    /// <summary>
    /// The main topics on <paramref name="side"/> as seen from top to bottom: the right side in list order, the left
    /// side in reverse (the list runs clockwise).
    /// </summary>
    public static IReadOnlyList<Topic> TopToBottom(Map map, MainSide side)
    {
        ArgumentNullException.ThrowIfNull(map);
        List<Topic> onSide = [.. map.Root.Children.Where(child => Of(map, child) == side)];
        if (side == MainSide.Left)
        {
            onSide.Reverse();
        }

        return onSide;
    }

    private static int CountRuleRightCount(int count) => (count + 1) / 2;
}
