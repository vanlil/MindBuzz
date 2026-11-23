using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using MindBuzz.SyntheticMap;
using Xunit;

namespace MindBuzz.Core.Tests;

/// <summary>Password-protected .mbz files: AES-256 ZIP through SharpZipLib.</summary>
public sealed class EncryptionTests
{
    private const string Generator = "MindBuzz tests";
    private const string Password = "correct horse";

    private static Document Sample() =>
        MapGenerator.Generate(new MapSpec(Topics: 200, Maps: 2, NoteRatio: 0.5, LinkRatio: 0.3, MarkerRatio: 0.3, Seed: 5));

    private static byte[] Encrypted(Document document, string password = Password)
    {
        using var stream = new MemoryStream();
        MbzWriter.Write(stream, document, Generator, password);
        return stream.ToArray();
    }

    [Fact]
    public void An_encrypted_file_loads_back_equal_with_the_password()
    {
        Document document = Sample();

        LoadResult loaded = MbzReader.Read(new MemoryStream(Encrypted(document)), Password);

        Assert.Empty(loaded.Problems);
        Assert.True(DocumentEquality.AreEqual(document, loaded.Document!));
    }

    [Fact]
    public void Without_a_password_it_asks_for_one_and_a_wrong_one_is_refused()
    {
        byte[] bytes = Encrypted(Sample());

        Assert.Equal(LoadFailure.PasswordRequired, MbzReader.Read(new MemoryStream(bytes)).Failure);
        Assert.Equal(LoadFailure.WrongPassword, MbzReader.Read(new MemoryStream(bytes), "wrong password").Failure);
    }

    [Fact]
    public void The_content_is_not_readable_without_the_password()
    {
        var document = new Document([new Map(new MapId("M"), new Topic(new TopicId("T"), "Secret plan") { Note = "very secret note" })]);

        byte[] bytes = Encrypted(document);

        Assert.True(MbzCrypto.IsEncrypted(new MemoryStream(bytes)));
        Assert.DoesNotContain("secret", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        using var zip = new ICSharpCode.SharpZipLib.Zip.ZipFile(new MemoryStream(bytes));
        Assert.All(zip.Cast<ZipEntry>(), entry => Assert.Equal(256, entry.AESKeySize));
    }

    [Fact]
    public void A_plain_file_ignores_a_password_and_is_not_encrypted()
    {
        byte[] bytes = MbzWriter.ToBytes(Sample(), Generator);

        Assert.False(MbzCrypto.IsEncrypted(new MemoryStream(bytes)));
        Assert.True(MbzReader.Read(new MemoryStream(bytes), Password).Succeeded);
    }

    [Fact]
    public void An_encrypted_archive_breaking_a_safety_rule_is_refused()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipOutputStream(stream) { IsStreamOwner = false, Password = Password })
        {
            zip.PutNextEntry(new ZipEntry("../evil.json") { AESKeySize = 256 });
            zip.Write("{}"u8);
            zip.CloseEntry();
        }

        Assert.Equal(LoadFailure.UnsafeArchive, MbzReader.Read(new MemoryStream(stream.ToArray()), Password).Failure);
    }

    [Fact]
    public void The_recovery_copy_of_an_encrypted_document_is_encrypted_too()
    {
        string folder = Path.Join(Path.GetTempPath(), "mindbuzz-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Join(folder, "Plan.mbz");
            Document document = Sample();

            RecoveryFile.Write(path, document, Generator, hide: false, Password);

            Assert.Equal(LoadFailure.PasswordRequired, MbzFile.Load(RecoveryFile.PathFor(path)).Failure);
            Assert.True(DocumentEquality.AreEqual(document, MbzFile.Load(RecoveryFile.PathFor(path), Password).Document!));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Every entry must be AES-256, as MindBuzz writes them: a file with a plain entry among encrypted ones would open
    /// after a password while part of it was never protected.
    /// </summary>
    [Fact]
    public void A_file_with_a_plain_entry_among_encrypted_ones_is_refused()
    {
        byte[] plain;
        using (var stream = new MemoryStream())
        {
            MbzWriter.Write(stream, Sample(), Generator);
            plain = stream.ToArray();
        }

        using var mixed = new MemoryStream();
        using (var source = new System.IO.Compression.ZipArchive(new MemoryStream(plain), System.IO.Compression.ZipArchiveMode.Read))
        using (var zip = new ZipOutputStream(mixed) { IsStreamOwner = false, Password = Password })
        {
            foreach (System.IO.Compression.ZipArchiveEntry entry in source.Entries)
            {
                // format.json encrypted, document.json plain: anyone could read the map without the password.
                bool encrypt = entry.FullName == "format.json";
                zip.Password = encrypt ? Password : null;
                zip.PutNextEntry(new ZipEntry(entry.FullName) { AESKeySize = encrypt ? 256 : 0 });
                using (Stream content = entry.Open())
                {
                    content.CopyTo(zip);
                }

                zip.CloseEntry();
            }
        }

        mixed.Position = 0;
        LoadResult loaded = MbzReader.Read(mixed, Password);

        Assert.Null(loaded.Document);
        Assert.Equal(LoadFailure.UnsafeArchive, loaded.Failure);
    }
}
