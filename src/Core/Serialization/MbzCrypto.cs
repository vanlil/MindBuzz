using System.IO.Compression;
using ICSharpCode.SharpZipLib.Zip;
using SharpZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

namespace MindBuzz.Core.Serialization;

/// <summary>
/// Password protection for <c>.mbz</c> files: the same ZIP with the same entries, each encrypted
/// with WinZip AES-256 through SharpZipLib, because .NET's own ZIP code has no encryption. Other tools that read
/// WinZip AES (7-Zip) open the file with the password. The plain container is built and checked by the normal writer
/// and reader; this class only wraps and unwraps it.
/// </summary>
public static class MbzCrypto
{
    /// <summary>The fewest characters a new password may have.</summary>
    public const int MinPasswordLength = 8;

    /// <summary>True when <paramref name="stream"/> is a ZIP with encrypted entries. The position is
    /// restored.</summary>
    public static bool IsEncrypted(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long position = stream.Position;
        try
        {
            using var zip = new SharpZipFile(stream, leaveOpen: true);
            foreach (ZipEntry entry in zip)
            {
                if (entry.IsCrypted)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is ZipException or IOException or ArgumentException)
        {
            return false;
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>Re-packs a plain <c>.mbz</c> with every entry encrypted under <paramref name="password"/>.</summary>
    internal static void Encrypt(Stream plain, Stream output, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        using var source = new ZipArchive(plain, ZipArchiveMode.Read, leaveOpen: true);
        using var zip = new ZipOutputStream(output) { IsStreamOwner = false, Password = password };
        zip.SetLevel(9);
        foreach (ZipArchiveEntry entry in source.Entries)
        {
            zip.PutNextEntry(new ZipEntry(entry.FullName) { AESKeySize = 256, DateTime = MbzFormat.EntryTime.DateTime });
            using (Stream content = entry.Open())
            {
                content.CopyTo(zip);
            }

            zip.CloseEntry();
        }
    }

    /// <summary>
    /// Unwraps an encrypted <c>.mbz</c> into a plain one in memory, applying the same safety limits as the reader:
    /// entry count, names, and sizes enforced while decompressing, since declared sizes can lie.
    /// </summary>
    /// <returns>The plain container, or null with <paramref name="failure"/> saying why.</returns>
    internal static MemoryStream? Decrypt(Stream encrypted, string password, out LoadFailure failure)
    {
        failure = LoadFailure.None;
        var plain = new MemoryStream();
        try
        {
            using var zip = new SharpZipFile(encrypted, leaveOpen: true) { Password = password };
            if (zip.Count > MbzLimits.MaxEntries)
            {
                failure = LoadFailure.UnsafeArchive;
                return null;
            }

            using (var output = new ZipArchive(plain, ZipArchiveMode.Create, leaveOpen: true))
            {
                long total = 0;
                foreach (ZipEntry entry in zip)
                {
                    // Every entry AES-256, as MindBuzz writes them: a file with a plain or ZipCrypto entry among
                    // encrypted ones would open after a password while part of it was never protected.
                    if (!entry.IsFile || !ZipSafety.IsSafeName(entry.Name) || !entry.IsCrypted || entry.AESKeySize != 256)
                    {
                        failure = LoadFailure.UnsafeArchive;
                        return null;
                    }

                    using Stream raw = zip.GetInputStream(entry);
                    var source = new LimitedStream(raw, MbzLimits.MaxEntryBytes);
                    ZipArchiveEntry copy = output.CreateEntry(entry.Name, CompressionLevel.Fastest);
                    using Stream target = copy.Open();
                    var counter = new CountingStream(target);
                    source.CopyTo(counter);
                    total += counter.Written;
                    if (total > MbzLimits.MaxTotalBytes)
                    {
                        failure = LoadFailure.UnsafeArchive;
                        return null;
                    }
                }
            }

            plain.Position = 0;
            return plain;
        }
        catch (ZipException)
        {
            // AES checks the password with a two-byte check value and the entry's authentication code: a wrong
            // password fails one of them, a damaged entry too. Both mean: this password does not open the file.
            failure = LoadFailure.WrongPassword;
            return null;
        }
        catch (InvalidDataException)
        {
            // LimitedStream: an entry larger than allowed.
            failure = LoadFailure.UnsafeArchive;
            return null;
        }
    }

    /// <summary>Counts what goes through, for the total size limit.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            Written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Written += buffer.Length;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
