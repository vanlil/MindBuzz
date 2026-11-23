using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Serialization;

/// <summary>Why a file could not be opened at all.</summary>
public enum LoadFailure
{
    None,

    /// <summary>Not a ZIP, or a ZIP too damaged to read.</summary>
    NotAnArchive,

    /// <summary>A ZIP, but without a readable <c>format.json</c>: not a MindBuzz document.</summary>
    NotAMindBuzzDocument,

    /// <summary>Written by a newer MindBuzz. Refused, never half-loaded.</summary>
    NewerFormat,

    /// <summary>The ZIP breaks a safety rule: path traversal, too many entries, too large, a compression
    /// bomb.</summary>
    UnsafeArchive,

    /// <summary><c>document.json</c> is missing, is not valid JSON, or holds no loadable map.</summary>
    DamagedDocument,

    /// <summary>The file is encrypted and no password was given: ask for one and read again.</summary>
    PasswordRequired,

    /// <summary>The password does not open the encrypted file.</summary>
    WrongPassword,
}

/// <summary>Something in an otherwise loadable file that could not be loaded, or had to be repaired.</summary>
public enum LoadProblemKind
{
    /// <summary>A map without a usable central topic was left out.</summary>
    MapSkipped,

    /// <summary>A topic that was not a JSON object was left out (its subtree with it).</summary>
    TopicSkipped,

    /// <summary>A topic had no text, or text that was not a string; it was loaded with empty text.</summary>
    TextMissing,

    /// <summary>A missing, malformed or duplicate id was replaced by a new one.</summary>
    IdReplaced,

    /// <summary>A link with a disallowed scheme or a malformed URL was left out.</summary>
    LinkSkipped,

    /// <summary>A marker of an unknown group or value, or a second marker of the same group, was left out.</summary>
    MarkerSkipped,

    /// <summary>A relationship with a missing end, to itself, or duplicating another was left out.</summary>
    RelationshipSkipped,

    /// <summary>Topics nested deeper than <see cref="MbzLimits.MaxTopicDepth"/> were left out.</summary>
    TooDeep,

    /// <summary>A relationship's dragged shape could not be read; the curve takes its automatic shape.</summary>
    BendIgnored,

    /// <summary>A relationship's colour or line pattern was not known; the default is used.</summary>
    StyleIgnored,

    /// <summary>An entry this version does not know was ignored; it will not be saved back.</summary>
    UnknownEntry,
}

/// <summary>One problem, with where it was (a map name, a topic's text) so the app can say what was lost.</summary>
public sealed record LoadProblem(LoadProblemKind Kind, string Where);

/// <summary>The outcome of loading a file.</summary>
public sealed record LoadResult
{
    /// <summary>The document, or null when <see cref="Failure"/> is set.</summary>
    public Document? Document { get; init; }

    public LoadFailure Failure { get; init; }

    /// <summary>The format version found in the file, when known: for the "newer version" message.</summary>
    public int? FoundVersion { get; init; }

    /// <summary>Everything that could not be loaded as-is. Empty for a clean file. Never silently empty.</summary>
    public ImmutableArray<LoadProblem> Problems { get; init; } = [];

    public bool Succeeded => Document is not null;

    internal static LoadResult Failed(LoadFailure failure, int? version = null) => new() { Failure = failure, FoundVersion = version };
}

/// <summary>
/// The limits the loader enforces on untrusted input. Generous for real maps: a
/// 5,000-topic map with notes is well under 10 MiB of JSON.
/// </summary>
public static class MbzLimits
{
    public const int MaxEntries = 64;

    public const long MaxEntryBytes = 128L * 1024 * 1024;

    public const long MaxTotalBytes = 256L * 1024 * 1024;

    public const int MaxTopicDepth = 200;
}

/// <summary>
/// Reads <c>.mbz</c> files. Tolerant by design: it loads whatever can be loaded and
/// reports the rest as <see cref="LoadProblem"/>s, element by element. That is why it walks a
/// <see cref="JsonDocument"/> instead of deserialising into types: a bad link or topic costs that link or
/// topic, never the file. A <c>document.json</c> that is not valid JSON at all fails as a whole
/// (<see cref="LoadFailure.DamagedDocument"/>); atomic saving (<see cref="AtomicFile"/>) is what keeps that from
/// happening.
/// </summary>
public static class MbzReader
{
    /// <param name="stream">The file; must be seekable.</param>
    /// <param name="password">The password of an encrypted file; ignored for a plain one.</param>
    public static LoadResult Read(Stream stream, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Before any ZIP library reads the directory: both build an object per entry first and count afterwards.
        if (!ZipSafety.CheckDirectorySize(stream))
        {
            return LoadResult.Failed(LoadFailure.UnsafeArchive);
        }

        if (MbzCrypto.IsEncrypted(stream))
        {
            if (string.IsNullOrEmpty(password))
            {
                return LoadResult.Failed(LoadFailure.PasswordRequired);
            }

            using MemoryStream? plain = MbzCrypto.Decrypt(stream, password, out LoadFailure failure);
            return plain is null ? LoadResult.Failed(failure) : ReadPlain(plain);
        }

        return ReadPlain(stream);
    }

    private static LoadResult ReadPlain(Stream stream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return LoadResult.Failed(LoadFailure.NotAnArchive);
        }

        using (zip)
        {
            try
            {
                return Read(zip);
            }
            catch (InvalidDataException)
            {
                // A corrupt entry (bad CRC, truncated deflate stream) surfaces while reading it.
                return LoadResult.Failed(LoadFailure.NotAnArchive);
            }
        }
    }

    private static LoadResult Read(ZipArchive zip)
    {
        if (!ZipSafety.Check(zip))
        {
            return LoadResult.Failed(LoadFailure.UnsafeArchive);
        }

        var problems = new List<LoadProblem>();

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName != MbzFormat.FormatEntry && entry.FullName != MbzFormat.DocumentEntry)
            {
                problems.Add(new LoadProblem(LoadProblemKind.UnknownEntry, entry.FullName));
            }
        }

        // format.json first: a newer file is refused before anything else is parsed.
        if (zip.GetEntry(MbzFormat.FormatEntry) is not { } formatEntry || ReadJson(formatEntry) is not { } format)
        {
            return LoadResult.Failed(LoadFailure.NotAMindBuzzDocument);
        }

        using (format)
        {
            if (!format.RootElement.TryGetProperty("formatVersion", out JsonElement versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out int version)
                || version < 1)
            {
                return LoadResult.Failed(LoadFailure.NotAMindBuzzDocument);
            }

            if (version > MbzFormat.CurrentVersion)
            {
                return LoadResult.Failed(LoadFailure.NewerFormat, version);
            }
        }

        if (zip.GetEntry(MbzFormat.DocumentEntry) is not { } documentEntry || ReadJson(documentEntry) is not { } json)
        {
            return LoadResult.Failed(LoadFailure.DamagedDocument, MbzFormat.CurrentVersion);
        }

        using (json)
        {
            Document? document = new DocumentBuilder(problems).Build(json.RootElement);
            return document is null
                ? LoadResult.Failed(LoadFailure.DamagedDocument, MbzFormat.CurrentVersion)
                : new LoadResult { Document = document, FoundVersion = MbzFormat.CurrentVersion, Problems = [.. problems] };
        }
    }

    private static JsonDocument? ReadJson(ZipArchiveEntry entry)
    {
        using Stream raw = entry.Open();
        using var limited = new LimitedStream(raw, MbzLimits.MaxEntryBytes);

        try
        {
            // Each topic level nests an object in a "children" array: two JSON levels per topic level.
            return JsonDocument.Parse(limited, new JsonDocumentOptions { MaxDepth = (MbzLimits.MaxTopicDepth * 2) + 16 });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Turns the JSON tree into a document, collecting problems instead of failing.</summary>
    internal sealed class DocumentBuilder(List<LoadProblem> problems)
    {
        /// <summary>Every id seen so far, across all maps: ids are unique per document.</summary>
        private readonly HashSet<string> _ids = [];

        internal Document? Build(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("maps", out JsonElement mapsElement)
                || mapsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var maps = ImmutableArray.CreateBuilder<Map>();
            int number = 0;
            foreach (JsonElement element in mapsElement.EnumerateArray())
            {
                number++;
                if (BuildMap(element, number) is { } map)
                {
                    maps.Add(map);
                }
            }

            return maps.Count == 0 ? null : new Document(maps.ToImmutable());
        }

        private Map? BuildMap(JsonElement element, int number)
        {
            // A map has no name of its own: problems are reported by its position.
            string name = $"#{number}";

            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("root", out JsonElement rootElement)
                || BuildTopic(rootElement, depth: 0, name) is not { } root)
            {
                problems.Add(new LoadProblem(LoadProblemKind.MapSkipped, name));
                return null;
            }

            MapId id = new(UniqueId(String(element, "id"), name));
            // Main topics without a side (older files) get theirs from the old count rule: the map looks as it did.
            var map = MainSides.Fill(new Map(id, root));
            return map with { Relationships = BuildRelationships(element, map) };
        }

        internal Topic? BuildTopic(JsonElement element, int depth, string where)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                problems.Add(new LoadProblem(LoadProblemKind.TopicSkipped, where));
                return null;
            }

            string? text = String(element, "text");
            if (text is null)
            {
                problems.Add(new LoadProblem(LoadProblemKind.TextMissing, where));
            }

            string label = text ?? where;
            var id = new TopicId(UniqueId(String(element, "id"), label));

            var children = ImmutableArray.CreateBuilder<Topic>();
            if (element.TryGetProperty("children", out JsonElement childrenElement) && childrenElement.ValueKind == JsonValueKind.Array)
            {
                if (depth + 1 > MbzLimits.MaxTopicDepth)
                {
                    problems.Add(new LoadProblem(LoadProblemKind.TooDeep, label));
                }
                else
                {
                    foreach (JsonElement child in childrenElement.EnumerateArray())
                    {
                        if (BuildTopic(child, depth + 1, label) is { } topic)
                        {
                            children.Add(topic);
                        }
                    }
                }
            }

            return new Topic(id, text ?? string.Empty)
            {
                Children = children.ToImmutable(),
                // The central topic is never collapsed (it would hide the whole map), whatever the file says.
                IsCollapsed = depth > 0 && element.TryGetProperty("collapsed", out JsonElement collapsed) && collapsed.ValueKind == JsonValueKind.True,
                Note = String(element, "note") ?? string.Empty,
                Side = depth == 1 ? String(element, "side") switch { "left" => MainSide.Left, "right" => MainSide.Right, _ => null } : null,
                Links = BuildLinks(element, label),
                Markers = BuildMarkers(element, label),
            };
        }

        private ImmutableArray<InternetLink> BuildLinks(JsonElement topic, string where)
        {
            if (!topic.TryGetProperty("links", out JsonElement links) || links.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = ImmutableArray.CreateBuilder<InternetLink>();
            foreach (JsonElement link in links.EnumerateArray())
            {
                string? url = link.ValueKind == JsonValueKind.Object ? String(link, "url") : null;
                if (!LinkUrl.IsAllowed(url))
                {
                    problems.Add(new LoadProblem(LoadProblemKind.LinkSkipped, where));
                    continue;
                }

                result.Add(new InternetLink(url!, String(link, "title") ?? string.Empty));
            }

            return result.ToImmutable();
        }

        private ImmutableArray<Marker> BuildMarkers(JsonElement topic, string where)
        {
            if (!topic.TryGetProperty("markers", out JsonElement markers) || markers.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<Marker>();
            foreach (JsonElement element in markers.EnumerateArray())
            {
                var marker = new Marker(
                    element.ValueKind == JsonValueKind.Object ? String(element, "group") ?? string.Empty : string.Empty,
                    element.ValueKind == JsonValueKind.Object ? String(element, "value") ?? string.Empty : string.Empty);

                if (!MarkerGroups.IsKnown(marker) || result.Any(m => m.Group == marker.Group))
                {
                    problems.Add(new LoadProblem(LoadProblemKind.MarkerSkipped, where));
                    continue;
                }

                result.Add(marker);
            }

            return [.. result.OrderBy(m => MarkerGroups.OrderOf(m.Group))];
        }

        private ImmutableArray<Relationship> BuildRelationships(JsonElement mapElement, Map map)
        {
            if (!mapElement.TryGetProperty("relationships", out JsonElement relationships) || relationships.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<Relationship>();
            foreach (JsonElement element in relationships.EnumerateArray())
            {
                string? from = element.ValueKind == JsonValueKind.Object ? String(element, "from") : null;
                string? to = element.ValueKind == JsonValueKind.Object ? String(element, "to") : null;

                // Ends are matched against the ids as loaded. A topic whose id had to be replaced (duplicate or
                // malformed) loses its relationships, and each one is reported.
                if (from is null || to is null || from == to
                    || !map.Contains(new TopicId(from)) || !map.Contains(new TopicId(to))
                    || result.Any(r => r.Connects(new TopicId(from), new TopicId(to))))
                {
                    problems.Add(new LoadProblem(LoadProblemKind.RelationshipSkipped, map.Name));
                    continue;
                }

                RelationshipDirection direction = String(element, "direction") == "twoWay"
                    ? RelationshipDirection.TwoWay
                    : RelationshipDirection.OneWay;

                RelationshipBend? bend = ReadBend(element, out bool malformed);
                if (malformed)
                {
                    problems.Add(new LoadProblem(LoadProblemKind.BendIgnored, map.Name));
                }

                (RelationshipColor color, RelationshipLine line) = ReadStyle(element, out bool unknownStyle);
                if (unknownStyle)
                {
                    problems.Add(new LoadProblem(LoadProblemKind.StyleIgnored, map.Name));
                }

                result.Add(new Relationship(new RelationshipId(UniqueId(String(element, "id"), map.Name)), new TopicId(from), new TopicId(to), direction)
                {
                    Bend = bend,
                    Color = color,
                    Line = line,
                });
            }

            return [.. result];
        }

        /// <summary>The id as loaded, or a new one (reported) when it is missing, malformed or already used.</summary>
        private string UniqueId(string? id, string where)
        {
            if (IdText.IsValid(id) && _ids.Add(id!))
            {
                return id!;
            }

            problems.Add(new LoadProblem(LoadProblemKind.IdReplaced, where));
            string fresh = RandomIdSource.Instance.NewId();
            _ids.Add(fresh);
            return fresh;
        }

        /// <summary>
        /// A relationship's colour and line pattern; defaults when left out. <paramref name="unknown"/> says a value
        /// was
        /// there but is not one this version knows: the default is used, and the loader reports it.
        /// </summary>
        internal static (RelationshipColor Color, RelationshipLine Line) ReadStyle(JsonElement relationship, out bool unknown)
        {
            unknown = false;
            RelationshipColor color = RelationshipColor.Default;
            RelationshipLine line = RelationshipLine.Solid;
            if (relationship.TryGetProperty("color", out JsonElement colorElement) && !RelationshipStyleNames.TryParse(colorElement.ValueKind == JsonValueKind.String ? colorElement.GetString() : null, out color))
            {
                unknown = true;
            }

            if (relationship.TryGetProperty("line", out JsonElement lineElement) && !RelationshipStyleNames.TryParse(lineElement.ValueKind == JsonValueKind.String ? lineElement.GetString() : null, out line))
            {
                unknown = true;
            }

            return (color, line);
        }

        /// <summary>
        /// A relationship's dragged shape, or null. <paramref name="malformed"/> says a "bend" was there but could not
        /// be read: the curve then takes its automatic shape, and the loader reports it.
        /// </summary>
        internal static RelationshipBend? ReadBend(JsonElement relationship, out bool malformed)
        {
            malformed = false;
            if (!relationship.TryGetProperty("bend", out JsonElement bend))
            {
                return null;
            }

            if (bend.ValueKind == JsonValueKind.Object
                && bend.TryGetProperty("along", out JsonElement along) && along.TryGetDouble(out double alongValue)
                && bend.TryGetProperty("across", out JsonElement across) && across.TryGetDouble(out double acrossValue)
                && double.IsFinite(alongValue) && double.IsFinite(acrossValue)
                && Math.Abs(alongValue) <= 10 && Math.Abs(acrossValue) <= 10)
            {
                return new RelationshipBend(alongValue, acrossValue);
            }

            malformed = true;
            return null;
        }

        internal static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
