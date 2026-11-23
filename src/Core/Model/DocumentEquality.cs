namespace MindBuzz.Core.Model;

/// <summary>
/// Structural equality for documents: same maps, topics, texts, links, notes, markers, collapsed flags and
/// relationships, in the same order. Records compare their <c>ImmutableArray</c> members by reference, so this is
/// what tests and the round-trip checks use.
/// </summary>
public static class DocumentEquality
{
    public static bool AreEqual(Document a, Document b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return a.Maps.Length == b.Maps.Length && a.Maps.Zip(b.Maps).All(pair => AreEqual(pair.First, pair.Second));
    }

    public static bool AreEqual(Map a, Map b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        // A main topic without a side and one with the side it would get anyway are the same map: compare the sides
        // as they take effect (older documents carry none; loading writes them down).
        return a.Id == b.Id
            && a.Name == b.Name
            && a.Relationships.SequenceEqual(b.Relationships)
            && AreEqual(MainSides.Fill(a).Root, MainSides.Fill(b).Root);
    }

    public static bool AreEqual(Topic a, Topic b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        return a.Id == b.Id
            && a.Text == b.Text
            && a.IsCollapsed == b.IsCollapsed
            && a.Note == b.Note
            && a.Side == b.Side
            && a.Links.SequenceEqual(b.Links)
            && a.Markers.SequenceEqual(b.Markers)
            && a.Children.Length == b.Children.Length
            && a.Children.Zip(b.Children).All(pair => AreEqual(pair.First, pair.Second));
    }
}
