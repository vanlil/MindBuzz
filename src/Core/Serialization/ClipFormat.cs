using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MindBuzz.Core.Editing;
using MindBuzz.Core.Model;

namespace MindBuzz.Core.Serialization;

/// <summary>
/// The private clipboard format: copied topics with everything in them, as JSON, so paste keeps
/// full fidelity, also between two MindBuzz processes. Topics are written exactly as in
/// <c>document.json</c> and read with the same tolerant rules.
/// <code>
/// { "mindBuzzClip": 1, "roots": [ topic, … ], "relationships": [ { "from", "to", "direction" }, … ] }
/// </code>
/// </summary>
public static class ClipFormat
{
    /// <summary>The clip format's own version, separate from the file format's.</summary>
    public const int Version = 1;

    public static string Write(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, MbzWriter.JsonOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("mindBuzzClip", Version);
            writer.WriteStartArray("roots");
            foreach (Topic root in clip.Roots)
            {
                MbzWriter.WriteTopic(writer, root);
            }

            writer.WriteEndArray();

            if (!clip.Relationships.IsEmpty)
            {
                writer.WriteStartArray("relationships");
                foreach (Relationship relationship in clip.Relationships)
                {
                    writer.WriteStartObject();
                    writer.WriteString("from", relationship.From.Value);
                    writer.WriteString("to", relationship.To.Value);
                    writer.WriteString("direction", relationship.Direction == RelationshipDirection.TwoWay ? "twoWay" : "oneWay");
                    MbzWriter.WriteBend(writer, relationship.Bend);
                    MbzWriter.WriteStyle(writer, relationship);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads a clip, or returns null when the text is not one (another app's data, a newer clip version, damaged
    /// JSON). Ids are kept as written; the paste gives everything fresh ids anyway (<see cref="Clip.WithFreshIds"/>).
    /// </summary>
    /// <param name="json">The clipboard's private format.</param>
    /// <param name="problems">Collects what was left out (a damaged topic, a link with a refused scheme), so the paste
    /// can say so instead of dropping it silently.</param>
    public static Clip? Read(string? json, List<LoadProblem>? problems = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = (MbzLimits.MaxTopicDepth * 2) + 16 });
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("mindBuzzClip", out JsonElement version)
                || !version.TryGetInt32(out int number)
                || number < 1
                || number > Version
                || !root.TryGetProperty("roots", out JsonElement rootsElement)
                || rootsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var builder = new MbzReader.DocumentBuilder(problems ?? []);
            ImmutableArray<Topic> roots = [.. rootsElement.EnumerateArray().Select(element => builder.BuildTopic(element, depth: 1, "clip")).OfType<Topic>()];
            if (roots.IsEmpty)
            {
                return null;
            }

            HashSet<TopicId> inside = [.. roots.SelectMany(topic => topic.DescendantsAndSelf()).Select(topic => topic.Id)];
            var relationships = ImmutableArray.CreateBuilder<Relationship>();
            if (root.TryGetProperty("relationships", out JsonElement relationshipsElement) && relationshipsElement.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement element in relationshipsElement.EnumerateArray())
                {
                    string? from = element.ValueKind == JsonValueKind.Object ? MbzReader.DocumentBuilder.String(element, "from") : null;
                    string? to = element.ValueKind == JsonValueKind.Object ? MbzReader.DocumentBuilder.String(element, "to") : null;
                    if (from is null || to is null || from == to || !inside.Contains(new TopicId(from)) || !inside.Contains(new TopicId(to)))
                    {
                        continue;
                    }

                    RelationshipDirection direction = MbzReader.DocumentBuilder.String(element, "direction") == "twoWay"
                        ? RelationshipDirection.TwoWay
                        : RelationshipDirection.OneWay;

                    // A placeholder id: the paste replaces every id.
                    (RelationshipColor color, RelationshipLine line) = MbzReader.DocumentBuilder.ReadStyle(element, out _);
                    relationships.Add(new Relationship(new RelationshipId($"CLIP{index++}"), new TopicId(from), new TopicId(to), direction)
                    {
                        Bend = MbzReader.DocumentBuilder.ReadBend(element, out _),
                        Color = color,
                        Line = line,
                    });
                }
            }

            return new Clip(roots, relationships.ToImmutable());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
