using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Serialization;

/// <summary>Names and limits of the <c>.mbz</c> format.</summary>
public static class MbzFormat
{
    /// <summary>The version this build writes and the newest it reads. Bump with every format change.</summary>
    public const int CurrentVersion = 1;

    public const string FormatEntry = "format.json";

    public const string DocumentEntry = "document.json";

    /// <summary>Every entry gets this time, so the same document gives the same bytes (1980-01-01 is ZIP's
    /// epoch).</summary>
    internal static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

/// <summary>
/// Writes a document as <c>.mbz</c>: a ZIP with <c>format.json</c> first, then <c>document.json</c>.
/// <para>
/// Written by hand with <see cref="Utf8JsonWriter"/> rather than through serializer-generated code, so the output
/// is fully under control: fixed property order, LF line endings on every OS, invariant culture, defaults left
/// out, non-ASCII text kept readable. The same document and generator give the same bytes on the same OS
/// (ZIP headers record the OS that wrote them). Trim- and AOT-safe: no reflection.
/// </para>
/// </summary>
public static class MbzWriter
{
    internal static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",

        // Files are not HTML: there is no reason to escape é, ü, < or &. Keeping them readable makes an unzipped
        // document diffable and lets a user recover text by hand if ever needed.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="stream">Where the ZIP goes; left open.</param>
    /// <param name="document">The document.</param>
    /// <param name="generator">Written into <c>format.json</c> for diagnostics, e.g. "MindBuzz 1.0.0".</param>
    /// <param name="password">
    /// When set, every entry is encrypted with AES-256 under it. An encrypted file is not
    /// byte-for-byte repeatable: each write draws a new random salt.
    /// </param>
    public static void Write(Stream stream, Document document, string generator, string? password = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);

        if (!string.IsNullOrEmpty(password))
        {
            using var plain = new MemoryStream();
            Write(plain, document, generator);
            plain.Position = 0;
            MbzCrypto.Encrypt(plain, stream, password);
            return;
        }

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(zip, MbzFormat.FormatEntry, writer => WriteFormat(writer, generator));
        WriteEntry(zip, MbzFormat.DocumentEntry, writer => WriteDocument(writer, document));
    }

    /// <summary>The document as bytes, for tests and the round-trip check.</summary>
    public static byte[] ToBytes(Document document, string generator)
    {
        using var stream = new MemoryStream();
        Write(stream, document, generator);
        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<Utf8JsonWriter> write)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = MbzFormat.EntryTime;

        using Stream stream = entry.Open();
        using (var writer = new Utf8JsonWriter(stream, JsonOptions))
        {
            write(writer);
        }

        stream.WriteByte((byte)'\n');
    }

    private static void WriteFormat(Utf8JsonWriter writer, string generator)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", MbzFormat.CurrentVersion);
        writer.WriteString("generator", generator);
        writer.WriteEndObject();
    }

    private static void WriteDocument(Utf8JsonWriter writer, Document document)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("maps");

        foreach (Map map in document.Maps)
        {
            writer.WriteStartObject();
            writer.WriteString("id", map.Id.Value);
            writer.WritePropertyName("root");
            WriteTopic(writer, map.Root);

            if (!map.Relationships.IsEmpty)
            {
                writer.WriteStartArray("relationships");
                foreach (Relationship relationship in map.Relationships)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", relationship.Id.Value);
                    writer.WriteString("from", relationship.From.Value);
                    writer.WriteString("to", relationship.To.Value);
                    writer.WriteString("direction", relationship.Direction == RelationshipDirection.TwoWay ? "twoWay" : "oneWay");
                    WriteBend(writer, relationship.Bend);
                    WriteStyle(writer, relationship);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// A dragged curve's shape, rounded to four decimals so the same drag always gives the same bytes. Left out
    /// for the automatic shape.
    /// </summary>
    /// <summary>Colour and line pattern, each left out at its default.</summary>
    internal static void WriteStyle(Utf8JsonWriter writer, Relationship relationship)
    {
        if (relationship.Color != RelationshipColor.Default)
        {
            writer.WriteString("color", RelationshipStyleNames.Of(relationship.Color));
        }

        if (relationship.Line != RelationshipLine.Solid)
        {
            writer.WriteString("line", RelationshipStyleNames.Of(relationship.Line));
        }
    }

    internal static void WriteBend(Utf8JsonWriter writer, RelationshipBend? bend)
    {
        if (bend is not { } value)
        {
            return;
        }

        writer.WriteStartObject("bend");
        writer.WriteNumber("along", Math.Round(value.Along, 4));
        writer.WriteNumber("across", Math.Round(value.Across, 4));
        writer.WriteEndObject();
    }

    internal static void WriteTopic(Utf8JsonWriter writer, Topic topic)
    {
        writer.WriteStartObject();
        writer.WriteString("id", topic.Id.Value);
        writer.WriteString("text", topic.Text);

        if (topic.IsCollapsed)
        {
            writer.WriteBoolean("collapsed", true);
        }

        if (topic.Side is { } side)
        {
            writer.WriteString("side", side == MainSide.Left ? "left" : "right");
        }

        if (!topic.Markers.IsEmpty)
        {
            writer.WriteStartArray("markers");
            foreach (Marker marker in topic.Markers)
            {
                writer.WriteStartObject();
                writer.WriteString("group", marker.Group);
                writer.WriteString("value", marker.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (!topic.Links.IsEmpty)
        {
            writer.WriteStartArray("links");
            foreach (InternetLink link in topic.Links)
            {
                writer.WriteStartObject();
                writer.WriteString("url", link.Url);
                if (link.Title.Length > 0)
                {
                    writer.WriteString("title", link.Title);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (topic.Note.Length > 0)
        {
            writer.WriteString("note", topic.Note);
        }

        // Children last, so an unzipped document reads top to bottom like the outline.
        if (topic.HasChildren)
        {
            writer.WriteStartArray("children");
            foreach (Topic child in topic.Children)
            {
                WriteTopic(writer, child);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}
