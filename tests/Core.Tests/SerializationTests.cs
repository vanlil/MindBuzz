using System.IO.Compression;
using System.Text;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using MindBuzz.SyntheticMap;
using Xunit;

namespace MindBuzz.Core.Tests;

public sealed class SerializationTests
{
    private const string Generator = "MindBuzz tests";

    private static LoadResult Load(byte[] bytes) => MbzReader.Read(new MemoryStream(bytes));

    [Fact]
    public void A_file_with_a_collapsed_central_topic_opens_with_the_map_shown()
    {
        Document document = TestMaps.Small();
        Map map = document.Maps[0];
        document = document.ReplaceMap(map with { Root = map.Root with { IsCollapsed = true } });

        Assert.False(Load(MbzWriter.ToBytes(document, Generator)).Document!.Maps[0].Root.IsCollapsed);
    }

    /// <summary>A hand-made archive, for files the writer would never produce.</summary>
    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return stream.ToArray();
    }

    private const string Format = """{ "formatVersion": 1 }""";

    private static byte[] WithDocument(string json) => Zip((MbzFormat.FormatEntry, Format), (MbzFormat.DocumentEntry, json));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Generated_documents_round_trip_to_equal_documents_and_identical_bytes(int seed)
    {
        Document document = MapGenerator.Generate(new MapSpec(Topics: 400, Maps: 3, NoteRatio: 0.3, LinkRatio: 0.3, MarkerRatio: 0.5, CollapsedRatio: 0.2, Seed: seed));
        byte[] bytes = MbzWriter.ToBytes(document, Generator);

        LoadResult loaded = Load(bytes);

        Assert.Empty(loaded.Problems);
        Assert.True(DocumentEquality.AreEqual(document, loaded.Document!));
        Assert.Equal(bytes, MbzWriter.ToBytes(loaded.Document!, Generator));
    }

    [Fact]
    public void The_json_inside_uses_lf_keeps_non_ascii_readable_and_leaves_defaults_out()
    {
        var document = new Document([new Map(new MapId("M"), new Topic(new TopicId("T"), "Straße 🐝\nzwei")), new Map(new MapId("N"), new Topic(new TopicId("U"), "Größe"))]);

        string json = ReadEntry(MbzWriter.ToBytes(document, Generator), MbzFormat.DocumentEntry);

        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.Contains("Größe", json, StringComparison.Ordinal);
        Assert.Contains("Straße ", json, StringComparison.Ordinal);
        Assert.Contains("\\nzwei", json, StringComparison.Ordinal);

        // The encoder escapes characters outside the Basic Multilingual Plane (emoji) as \u surrogate pairs. That
        // is valid JSON and loads back unchanged.
        Assert.Equal("Straße 🐝\nzwei", MbzReader.Read(new MemoryStream(MbzWriter.ToBytes(document, Generator))).Document!.Maps[0].Root.Text);
        Assert.DoesNotContain("collapsed", json, StringComparison.Ordinal);
        Assert.DoesNotContain("children", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Relationship_colours_and_lines_round_trip_and_unknown_ones_fall_back_and_are_reported()
    {
        var root = new Topic(new TopicId("T"), "Root") { Children = [new Topic(new TopicId("A"), "A"), new Topic(new TopicId("B"), "B")] };
        var map = new Map(new MapId("M"), root)
        {
            Relationships = [new Relationship(new RelationshipId("R"), new TopicId("A"), new TopicId("B"), RelationshipDirection.OneWay) { Color = RelationshipColor.Teal, Line = RelationshipLine.Dashed }],
        };
        byte[] bytes = MbzWriter.ToBytes(new Document([map]), Generator);
        string json = ReadEntry(bytes, MbzFormat.DocumentEntry);

        Relationship loaded = Load(bytes).Document!.Maps[0].Relationships[0];
        Assert.Equal((RelationshipColor.Teal, RelationshipLine.Dashed), (loaded.Color, loaded.Line));
        Assert.Contains("\"color\": \"teal\"", json, StringComparison.Ordinal);

        LoadResult odd = Load(WithDocument(json.Replace("teal", "chartreuse", StringComparison.Ordinal)));
        Assert.Equal(RelationshipColor.Default, odd.Document!.Maps[0].Relationships[0].Color);
        Assert.Contains(odd.Problems, problem => problem.Kind == LoadProblemKind.StyleIgnored);
    }

    /// <summary>
    /// A note with a table, a task list and fenced code survives save and reload exactly, including
    /// CRLF line ends, tabs, trailing spaces, raw HTML and emoji (notes are stored verbatim).
    /// </summary>
    [Fact]
    public void A_markdown_note_survives_save_and_reload_byte_for_byte()
    {
        const string note = "# Plan\r\n\r\n| a | b |\n|---|--:|\n| 1 | 2 |\n\n- [x] done  \n- [ ] open\n\n```cs\n\tvar x = 1;\n```\n<b>raw</b> 🐝 \u00A0end ";
        var document = new Document([new Map(new MapId("M"), new Topic(new TopicId("T"), "Topic") { Note = note })]);

        Document loaded = Load(MbzWriter.ToBytes(document, Generator)).Document!;

        Assert.Equal(note, loaded.Maps[0].Root.Note);
    }

    [Fact]
    public void The_format_entry_comes_first_and_holds_the_version()
    {
        byte[] bytes = MbzWriter.ToBytes(Document.CreateNew(new SeededIdSource(1), "Central Topic"), Generator);
        using var zip = new ZipArchive(new MemoryStream(bytes));

        Assert.Equal([MbzFormat.FormatEntry, MbzFormat.DocumentEntry], zip.Entries.Select(entry => entry.FullName));
        Assert.Contains("\"formatVersion\": 1", ReadEntry(bytes, MbzFormat.FormatEntry), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_from_a_newer_version_is_refused_with_its_version()
    {
        LoadResult result = Load(Zip((MbzFormat.FormatEntry, """{ "formatVersion": 7 }"""), (MbzFormat.DocumentEntry, "{}")));

        Assert.Equal(LoadFailure.NewerFormat, result.Failure);
        Assert.Equal(7, result.FoundVersion);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("PK\u0003\u0004 truncated")]
    public void Something_that_is_not_a_zip_is_refused(string content)
    {
        Assert.Equal(LoadFailure.NotAnArchive, Load(Encoding.UTF8.GetBytes(content)).Failure);
    }

    [Fact]
    public void A_zip_without_format_json_is_not_a_mindbuzz_document()
    {
        Assert.Equal(LoadFailure.NotAMindBuzzDocument, Load(Zip(("readme.txt", "hello"))).Failure);
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("/etc/passwd")]
    [InlineData("a\\..\\b.json")]
    [InlineData("C:/windows.json")]
    [InlineData("folder/../../up.json")]
    public void Entry_names_that_could_point_outside_a_folder_make_the_archive_unsafe(string name)
    {
        Assert.Equal(LoadFailure.UnsafeArchive, Load(Zip((MbzFormat.FormatEntry, Format), (name, "{}"))).Failure);
    }

    [Fact]
    public void Too_many_entries_make_the_archive_unsafe()
    {
        var entries = Enumerable.Range(0, MbzLimits.MaxEntries + 1).Select(i => ($"e{i}.json", "{}")).ToArray();

        Assert.Equal(LoadFailure.UnsafeArchive, Load(Zip(entries)).Failure);
    }

    /// <summary>
    /// The end record is checked before any ZIP library reads the directory, so a small file declaring millions of
    /// entries never makes them allocate an object for each.
    /// </summary>
    [Fact]
    public void An_archive_declaring_too_many_entries_is_refused_before_its_directory_is_read()
    {
        byte[] endRecord = new byte[22];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(endRecord, 0x06054b50);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(endRecord.AsSpan(8), 60000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(endRecord.AsSpan(10), 60000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(endRecord.AsSpan(12), 60000 * 46);

        Assert.Equal(LoadFailure.UnsafeArchive, Load(endRecord).Failure);
    }

    [Fact]
    public void A_compression_bomb_is_refused()
    {
        // 50 MiB of zeros deflates to about 50 KiB: a ratio far above the limit.
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream format = zip.CreateEntry(MbzFormat.FormatEntry).Open())
            {
                format.Write(Encoding.UTF8.GetBytes(Format));
            }

            using Stream bomb = zip.CreateEntry(MbzFormat.DocumentEntry, CompressionLevel.SmallestSize).Open();
            bomb.Write(new byte[50 * 1024 * 1024]);
        }

        Assert.Equal(LoadFailure.UnsafeArchive, Load(stream.ToArray()).Failure);
    }

    [Fact]
    public void Invalid_json_in_the_document_is_a_damaged_document_not_a_crash()
    {
        Assert.Equal(LoadFailure.DamagedDocument, Load(WithDocument("""{ "maps": [ { "id": "M", "root": """)).Failure);
    }

    [Fact]
    public void Bad_parts_are_left_out_and_reported_while_the_rest_loads()
    {
        const string json = """
            {
              "maps": [
                {
                  "id": "M1",
                  "name": "Good",
                  "root": {
                    "id": "R1",
                    "text": "Root",
                    "links": [ { "url": "javascript:alert(1)" }, { "url": "https://ok.example", "title": "ok" } ],
                    "markers": [ { "group": "priority", "value": "9" }, { "group": "progress", "value": "50" }, { "group": "progress", "value": "75" } ],
                    "children": [
                      { "id": "T1", "text": "first" },
                      { "id": "T1", "text": "duplicate id" },
                      { "id": "lowercase-bad", "text": "malformed id" },
                      { "id": "T4" },
                      42
                    ]
                  },
                  "relationships": [
                    { "id": "X1", "from": "T1", "to": "R1", "direction": "twoWay" },
                    { "id": "X2", "from": "T1", "to": "NOPE" },
                    { "id": "X3", "from": "T1", "to": "T1" },
                    { "id": "X4", "from": "R1", "to": "T1" }
                  ]
                },
                { "id": "M2", "name": "Broken" }
              ]
            }
            """;

        LoadResult result = Load(WithDocument(json));

        Map map = Assert.Single(result.Document!.Maps);
        Assert.Equal(["https://ok.example"], map.Root.Links.Select(link => link.Url));
        Assert.Equal([new Marker(MarkerGroups.Progress, "50")], map.Root.Markers);
        Assert.Equal(4, map.Root.Children.Length);
        Assert.Equal(4, map.Root.Children.Select(child => child.Id).Distinct().Count());
        Assert.Equal(string.Empty, map.Root.Children[3].Text);
        Assert.Equal(["X1"], map.Relationships.Select(r => r.Id.Value));
        Assert.Equal(RelationshipDirection.TwoWay, map.Relationships[0].Direction);

        var kinds = result.Problems.Select(problem => problem.Kind).ToList();
        Assert.Equal(1, kinds.Count(kind => kind == LoadProblemKind.LinkSkipped));
        Assert.Equal(2, kinds.Count(kind => kind == LoadProblemKind.MarkerSkipped));
        Assert.Equal(2, kinds.Count(kind => kind == LoadProblemKind.IdReplaced));
        Assert.Equal(1, kinds.Count(kind => kind == LoadProblemKind.TextMissing));
        Assert.Equal(1, kinds.Count(kind => kind == LoadProblemKind.TopicSkipped));
        Assert.Equal(3, kinds.Count(kind => kind == LoadProblemKind.RelationshipSkipped));
        Assert.Contains(result.Problems, problem => problem is { Kind: LoadProblemKind.MapSkipped, Where: "#2" });
        RandomSequenceTests.AssertInvariants(result.Document);
    }

    [Fact]
    public void Unknown_entries_are_reported_because_they_will_not_be_saved_back()
    {
        byte[] bytes = Zip(
            (MbzFormat.FormatEntry, Format),
            (MbzFormat.DocumentEntry, """{ "maps": [ { "id": "M", "name": "N", "root": { "id": "R", "text": "x" } } ] }"""),
            ("images/photo.png", "png"));

        LoadResult result = Load(bytes);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Problems, problem => problem is { Kind: LoadProblemKind.UnknownEntry, Where: "images/photo.png" });
    }

    [Fact]
    public void A_document_with_no_loadable_map_is_damaged()
    {
        Assert.Equal(LoadFailure.DamagedDocument, Load(WithDocument("""{ "maps": [] }""")).Failure);
    }

    [Fact]
    public void Topics_nested_deeper_than_the_limit_are_cut_and_reported()
    {
        var builder = new StringBuilder("""{ "maps": [ { "id": "M", "name": "Deep", "root": """);
        int depth = MbzLimits.MaxTopicDepth + 5;
        for (int i = 0; i < depth; i++)
        {
            builder.Append("{ \"text\": \"t\", \"children\": [ ");
        }

        builder.Append("{ \"text\": \"leaf\" }");
        for (int i = 0; i < depth; i++)
        {
            builder.Append(" ] }");
        }

        builder.Append(" } ] }");

        LoadResult result = Load(WithDocument(builder.ToString()));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Problems, problem => problem.Kind == LoadProblemKind.TooDeep);
    }

    private static string ReadEntry(byte[] bytes, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mindbuzz-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_failed_save_leaves_the_original_untouched_and_no_temporary_file_behind()
    {
        string path = Path.Join(_folder, "map.mbz");
        File.WriteAllText(path, "original");

        Assert.Throws<IOException>(() => AtomicFile.Write(path, stream =>
        {
            stream.Write("half of the new conte"u8);
            throw new IOException("disk full");
        }));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public void A_save_creates_a_new_file_and_replaces_an_existing_one()
    {
        string path = Path.Join(_folder, "map.mbz");

        AtomicFile.Write(path, stream => stream.Write("one"u8));
        AtomicFile.Write(path, stream => stream.Write("two"u8));

        Assert.Equal("two", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    /// <summary>On macOS and Linux the save is a rename, which must not widen a private document's
    /// permissions.</summary>
    [Fact]
    public void A_save_keeps_the_original_files_permissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string path = Path.Join(_folder, "map.mbz");
        AtomicFile.Write(path, stream => stream.Write("one"u8));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        AtomicFile.Write(path, stream => stream.Write("two"u8));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    /// <summary>A crashed save can leave its temporary file behind, with the whole document in it.</summary>
    [Fact]
    public void The_temporary_file_is_for_the_owner_only_while_it_is_written()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string path = Path.Join(_folder, "map.mbz");
        UnixFileMode? whileWriting = null;
        AtomicFile.Write(path, stream =>
        {
            if (!OperatingSystem.IsWindows())
            {
                whileWriting = File.GetUnixFileMode(Directory.GetFiles(_folder, ".map.mbz.*.tmp").Single());
            }
            stream.Write("one"u8);
        });

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, whileWriting);

        // A new file still ends up with the usual permissions, the ones any newly created file gets.
        string usual = Path.Join(_folder, "usual.txt");
        File.WriteAllText(usual, "x");
        Assert.Equal(File.GetUnixFileMode(usual), File.GetUnixFileMode(path));
    }

    [Fact]
    public void A_document_saved_to_disk_loads_back_equal()
    {
        string path = Path.Join(_folder, "map.mbz");
        Document document = MapGenerator.Generate(new MapSpec(Topics: 100, Seed: 5));

        MbzFile.Save(path, document, "tests");

        Assert.True(DocumentEquality.AreEqual(document, MbzFile.Load(path).Document!));
    }
}
