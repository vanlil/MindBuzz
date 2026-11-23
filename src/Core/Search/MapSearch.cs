using System.Globalization;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Search;

/// <summary>Where a search hit is: the topic's text, its note, or one of its links' title or address.</summary>
public enum HitField
{
    Text,
    Note,
    LinkTitle,
    LinkUrl,
}

/// <summary>
/// One hit: a field of a topic that contains the query, with the first match in it. A field with
/// several matches is one hit; the results list shows each field once.
/// </summary>
/// <param name="Map">The map the topic is in.</param>
/// <param name="Topic">The topic.</param>
/// <param name="Field">Which of the topic's texts matched.</param>
/// <param name="LinkIndex">For a link hit, which link; otherwise -1.</param>
/// <param name="Start">Where the first match starts in the field's text.</param>
/// <param name="Length">How long the match is in the field's text; it can differ from the query's length when the
/// culture treats characters as equal ("ß" and "SS").</param>
public readonly record struct SearchHit(MapId Map, TopicId Topic, HitField Field, int LinkIndex, int Start, int Length);

/// <summary>
/// Finds text in topics, notes and links: case- and accent-insensitive and culture-aware, in document order
/// (maps in tab order, topics depth first, then text, note and links per topic). Pure and read-only: searching never
/// changes the document. Collapsed branches are searched too; revealing a hit is the app's job.
/// </summary>
public static class MapSearch
{
    /// <summary>
    /// Case and accents are ignored: "cafe" finds "Café". With them ICU also treats "ß" as "ss", so
    /// "strasse" finds "Straße". The culture decides the rest, e.g. the Turkish dotless ı.
    /// </summary>
    private const CompareOptions Matching = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static string FieldText(Topic topic, HitField field, int linkIndex)
    {
        ArgumentNullException.ThrowIfNull(topic);
        return field switch
        {
            // Search finds what is shown, so "buy milk" finds "Buy **milk**".
            HitField.Text => TitleMarkup.Plain(topic.Text),
            HitField.Note => topic.Note,
            HitField.LinkTitle => topic.Links[linkIndex].Title,
            HitField.LinkUrl => topic.Links[linkIndex].Url,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Every hit of <paramref name="query"/> in <paramref name="maps"/>. An empty or blank query finds nothing.
    /// </summary>
    /// <param name="maps">The maps to search, in the order the hits should come in.</param>
    /// <param name="query">What to find; leading and trailing spaces are ignored.</param>
    /// <param name="culture">The culture whose rules decide what counts as equal (the user's).</param>
    public static IReadOnlyList<SearchHit> Find(IEnumerable<Map> maps, string query, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(culture);

        string needle = query.Trim();
        if (needle.Length == 0)
        {
            return [];
        }

        CompareInfo compare = culture.CompareInfo;

        // Every topic in document order, then searched in chunks on all cores and put back together in order. The
        // culture-aware compare is the cost (about 20 ms for 5,000 topics with 1 MB of notes on one core, measured
        // on an Apple M3); split up, it fits in a frame.
        var topics = new List<(MapId Map, Topic Topic)>();
        foreach (Map map in maps)
        {
            foreach (Topic topic in map.Root.DescendantsAndSelf())
            {
                topics.Add((map.Id, topic));
            }
        }

        const int ChunkSize = 256;
        int chunks = (topics.Count + ChunkSize - 1) / ChunkSize;
        var found = new List<SearchHit>[chunks];

        if (chunks <= 1)
        {
            found = [Search(0, topics.Count)];
        }
        else
        {
            Parallel.For(0, chunks, chunk => found[chunk] = Search(chunk * ChunkSize, Math.Min(topics.Count, (chunk + 1) * ChunkSize)));
        }

        return [.. found.SelectMany(hits => hits)];

        List<SearchHit> Search(int from, int to)
        {
            var hits = new List<SearchHit>();
            for (int index = from; index < to; index++)
            {
                (MapId map, Topic topic) = topics[index];
                Add(hits, map, topic, HitField.Text, -1, TitleMarkup.Plain(topic.Text));
                Add(hits, map, topic, HitField.Note, -1, topic.Note);
                for (int i = 0; i < topic.Links.Length; i++)
                {
                    Add(hits, map, topic, HitField.LinkTitle, i, topic.Links[i].Title);
                    Add(hits, map, topic, HitField.LinkUrl, i, topic.Links[i].Url);
                }
            }

            return hits;
        }

        void Add(List<SearchHit> hits, MapId map, Topic topic, HitField field, int link, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            int start = compare.IndexOf(text, needle, Matching, out int length);
            if (start >= 0)
            {
                hits.Add(new SearchHit(map, topic.Id, field, link, start, length));
            }
        }
    }

    /// <summary>
    /// A short excerpt of <paramref name="text"/> around a match, on one line, for the results list: up to
    /// <paramref name="context"/> characters before the match and enough after it to fill a row. "…" marks a cut.
    /// </summary>
    /// <returns>The excerpt, and where the match is within it.</returns>
    public static (string Excerpt, int Start, int Length) Excerpt(string text, int start, int length, int context = 24, int width = 90)
    {
        ArgumentNullException.ThrowIfNull(text);

        int from = Math.Max(0, start - context);

        // Start at a word if one begins shortly after the cut, so the excerpt does not open with half a word.
        if (from > 0)
        {
            int space = text.IndexOfAny([' ', '\n', '\t'], from, Math.Max(0, start - from));
            if (space >= 0)
            {
                from = space + 1;
            }
        }

        int to = Math.Min(text.Length, Math.Max(start + length, from + width));
        string prefix = from > 0 ? "…" : string.Empty;
        string suffix = to < text.Length ? "…" : string.Empty;

        string middle = text[from..to].Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return (prefix + middle + suffix, prefix.Length + (start - from), length);
    }
}
