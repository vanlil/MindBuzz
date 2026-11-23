namespace MindBuzz.Core.Model;

/// <summary>One internet link of a topic. The URL is required and allowed by <see cref="LinkUrl"/>.</summary>
/// <param name="Url">An absolute http, https or mailto URI.</param>
/// <param name="Title">An optional title; empty when there is none.</param>
public sealed record InternetLink(string Url, string Title = "");

/// <summary>Whether a relationship has an arrowhead at the target only, or at both ends.</summary>
public enum RelationshipDirection
{
    OneWay,
    TwoWay,
}

/// <summary>
/// A link between two topics of the same map. For a one-way relationship the arrow points at
/// <paramref name="To"/>.
/// </summary>
public sealed record Relationship(RelationshipId Id, TopicId From, TopicId To, RelationshipDirection Direction)
{
    /// <summary>
    /// The curve's shape when the user dragged it, or null for the automatic shape.
    /// </summary>
    public RelationshipBend? Bend { get; init; }

    /// <summary>The line's colour, from a named palette with a light and a dark tone each.</summary>
    public RelationshipColor Color { get; init; }

    /// <summary>The line's pattern.</summary>
    public RelationshipLine Line { get; init; }

    /// <summary>True when the relationship has <paramref name="topic"/> at either end.</summary>
    public bool Touches(TopicId topic) => From == topic || To == topic;

    /// <summary>True when this relationship connects the same two topics, in either direction.</summary>
    public bool Connects(TopicId a, TopicId b) => (From == a && To == b) || (From == b && To == a);
}

/// <summary>
/// A relationship's colour. Names, not RGB values: each has a light and a dark tone in the theme, so
/// a coloured line reads well in both. Default is the theme's soft relationship colour.
/// </summary>
public enum RelationshipColor
{
    Default,
    Red,
    Orange,
    Yellow,
    Green,
    Teal,
    Blue,
    Purple,
}

/// <summary>A relationship's line pattern.</summary>
public enum RelationshipLine
{
    Solid,
    Dashed,
    Dotted,
}

/// <summary>The names the file uses for <see cref="RelationshipColor"/> and <see cref="RelationshipLine"/>.</summary>
public static class RelationshipStyleNames
{
    public static string Of(RelationshipColor color) => color.ToString().ToLowerInvariant();

    public static string Of(RelationshipLine line) => line.ToString().ToLowerInvariant();

    public static bool TryParse(string? name, out RelationshipColor color)
    {
        foreach (RelationshipColor value in Enum.GetValues<RelationshipColor>())
        {
            if (Of(value) == name)
            {
                color = value;
                return true;
            }
        }

        color = RelationshipColor.Default;
        return false;
    }

    public static bool TryParse(string? name, out RelationshipLine line)
    {
        foreach (RelationshipLine value in Enum.GetValues<RelationshipLine>())
        {
            if (Of(value) == name)
            {
                line = value;
                return true;
            }
        }

        line = RelationshipLine.Solid;
        return false;
    }
}

/// <summary>
/// Where a dragged relationship curve's middle passes through, relative to the two topics it connects: from the
/// midpoint between the source's and the target's centres, <see cref="Along"/> × d along the line towards the
/// target and <see cref="Across"/> × d to its left, seen from the source (d is the distance between the centres).
/// Relative, so the bend keeps its shape when the layout moves the topics; measured from the midpoint, so reversing
/// the relationship is a sign flip.
/// </summary>
public readonly record struct RelationshipBend(double Along, double Across)
{
    /// <summary>
    /// The same shape seen from the other end: for a reversed relationship. A method, not a property, because a
    /// record prints its properties in ToString, and a property returning another bend would recurse forever.
    /// </summary>
    public RelationshipBend Reversed() => new(-Along, -Across);
}

/// <summary>
/// A marker: a group plus a value. The group keeps the set extensible, so flags, smileys or
/// arrows fit later without a format break.
/// </summary>
public readonly record struct Marker(string Group, string Value);

/// <summary>The marker groups v1 knows, with their values in order.</summary>
public static class MarkerGroups
{
    public const string Priority = "priority";

    public const string Progress = "progress";

    /// <summary>A symbol on the topic's side border.</summary>
    public const string Icon = "icon";

    /// <summary>Group order: markers are kept sorted by it, so a topic's markers always draw the same way.</summary>
    public static IReadOnlyList<string> All { get; } = [Priority, Progress, Icon];

    public static IReadOnlyList<string> PriorityValues { get; } = ["1", "2", "3", "4", "5"];

    /// <summary>Progress steps in percent; "100" is done.</summary>
    public static IReadOnlyList<string> ProgressValues { get; } = ["0", "25", "50", "75", "100"];

    /// <summary>The topic icons, in menu order. Names, not pictures, so each theme and a later icon set can draw
    /// them.</summary>
    public static IReadOnlyList<string> IconValues { get; } =
        ["alert", "exclamation", "question", "money", "idea", "star", "flag", "heart", "clock", "pin"];

    public static IReadOnlyList<string>? ValuesOf(string group) => group switch
    {
        Priority => PriorityValues,
        Progress => ProgressValues,
        Icon => IconValues,
        _ => null,
    };

    public static bool IsKnown(Marker marker) => ValuesOf(marker.Group)?.Contains(marker.Value) == true;

    internal static int OrderOf(string group)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] == group)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}

/// <summary>
/// The rule for internet links: only absolute <c>http</c>, <c>https</c> and
/// <c>mailto</c> URIs. Refused when entered, ignored (and reported) when loaded. The app's shell checks the same
/// rule again before anything leaves the process.
/// </summary>
public static class LinkUrl
{
    public static bool IsAllowed(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Trim() == url
        && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp
            || parsed.Scheme == Uri.UriSchemeHttps
            || parsed.Scheme == Uri.UriSchemeMailto);
}
