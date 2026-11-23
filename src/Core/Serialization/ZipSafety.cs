using System.Buffers.Binary;
using System.IO.Compression;

namespace MindBuzz.Core.Serialization;

/// <summary>
/// ZIP safety rules for untrusted input. v1 reads only its own format and never
/// extracts to disk, but users exchange files, and the same loader shape will serve a later import.
/// </summary>
internal static class ZipSafety
{
    /// <summary>
    /// True when the archive's directory is safe to read: few enough entries, plausible sizes and names that
    /// could never point outside a folder. Declared sizes can lie, so <see cref="LimitedStream"/> enforces the
    /// size limit again while reading. That limit is what stops a compression bomb; a limit on how well an entry
    /// compresses would add no protection and refused documents with long repeated notes.
    /// </summary>
    internal static bool Check(ZipArchive zip)
    {
        if (zip.Entries.Count > MbzLimits.MaxEntries)
        {
            return false;
        }

        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!IsSafeName(entry.FullName) || entry.Length > MbzLimits.MaxEntryBytes)
            {
                return false;
            }

            total += entry.Length;
            if (total > MbzLimits.MaxTotalBytes)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The largest central directory accepted: <see cref="MbzLimits.MaxEntries"/> entries with room to
    /// spare.</summary>
    internal const int MaxDirectoryBytes = 64 * 1024;

    /// <summary>
    /// True unless the ZIP's end record declares more entries or a larger central directory than any <c>.mbz</c> has,
    /// or ZIP64 (MindBuzz never writes it). Read from the end record alone, before <c>ZipArchive</c> or SharpZipLib
    /// parse the directory: both allocate an object per entry before anything counts them, so a small crafted file
    /// with millions of entries could exhaust memory. A stream without an end record passes; the ZIP reader then
    /// refuses it as not an archive. The position is restored.
    /// </summary>
    internal static bool CheckDirectorySize(Stream stream)
    {
        const int EndRecordSize = 22;
        const uint EndSignature = 0x06054b50;
        long position = stream.Position;
        try
        {
            // The end record sits at the very end, followed only by a comment of at most 65,535 bytes.
            int tail = (int)Math.Min(stream.Length, EndRecordSize + ushort.MaxValue);
            byte[] buffer = new byte[tail];
            stream.Position = stream.Length - tail;
            stream.ReadExactly(buffer);
            for (int i = tail - EndRecordSize; i >= 0; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i)) != EndSignature)
                {
                    continue;
                }

                ushort entries = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(i + 10));
                uint directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i + 12));
                return entries <= MbzLimits.MaxEntries && directoryBytes <= MaxDirectoryBytes;
            }

            return true;
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>
    /// No absolute paths, no drive letters, no backslashes, no <c>..</c> segment, no control characters. Checked
    /// with the rules of every OS at once, not the host's, so a name that is harmless here cannot be harmful on
    /// another machine.
    /// </summary>
    internal static bool IsSafeName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255)
        {
            return false;
        }

        if (name[0] == '/' || name.Contains('\\', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        if (name.Any(char.IsControl))
        {
            return false;
        }

        return !name.Split('/').Any(segment => segment is ".." or ".");
    }
}

/// <summary>A read-only stream that fails once more than a set number of bytes have been read.</summary>
internal sealed class LimitedStream(Stream inner, long limit) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        _read += read;
        if (_read > limit)
        {
            throw new InvalidDataException("A ZIP entry is larger than the loader allows.");
        }

        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
