using MindBuzz.Core.Model;

namespace MindBuzz.Core.Serialization;

/// <summary>
/// Writes a file so that it is either fully the old version or fully the new one, never half: write to a temporary file
/// in the same folder, flush it to disk, then replace the original in one
/// step. The original is never truncated first.
/// </summary>
public static class AtomicFile
{
    /// <param name="path">The file to create or replace.</param>
    /// <param name="write">Writes the new content. If it throws, the original is untouched and the temporary file is
    /// removed.</param>
    /// <param name="mode">macOS and Linux: the permissions the file ends up with. Null keeps those of the file being
    /// replaced, or gives a new file the usual ones. The recovery file passes its document's, so a private document
    /// never has a readable copy beside it.</param>
    public static void Write(string path, Action<Stream> write, UnixFileMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(write);

        string target = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(target)!;

        // Same folder, so the final rename never crosses a volume (which would make it a copy, not atomic).
        // Hidden-style name, so a crash between write and rename leaves nothing in the user's way.
        string temporary = Path.Join(folder, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");

        try
        {
            UnixFileMode? usual = null;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                // The temporary file is for the owner only while it fills, whatever the umask would allow: a crash
                // can leave it behind with the whole document in it. Still empty here, so nothing was readable.
                if (!OperatingSystem.IsWindows())
                {
                    usual = File.GetUnixFileMode(temporary);
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                write(stream);
                stream.Flush(flushToDisk: true);
            }

            bool replacing = File.Exists(target);

            // A rename on macOS and Linux gives the file the temporary file's permissions: the requested ones, else
            // those of the file being replaced (a document the user made private, chmod 600, stays private), else
            // the usual ones. ReplaceFile on Windows keeps attributes and ACLs itself.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, mode ?? (replacing ? File.GetUnixFileMode(target) : usual!.Value));
            }

            if (replacing)
            {
                File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, target);
            }
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the original is safe, which is what matters.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Saving and opening <c>.mbz</c> files on disk.</summary>
public static class MbzFile
{
    /// <summary>Saves atomically. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> on
    /// failure; the original is then unchanged.</summary>
    public static void Save(string path, Document document, string generator, string? password = null) =>
        AtomicFile.Write(path, stream => MbzWriter.Write(stream, document, generator, password));

    /// <summary>Opens a file. Problems with the content are in the result; only I/O failures throw.</summary>
    /// <param name="path">The file.</param>
    /// <param name="password">The password, for an encrypted file; without it, one comes back as <see
    /// cref="LoadFailure.PasswordRequired"/>.</param>
    public static LoadResult Load(string path, string? password = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return MbzReader.Read(stream, password);
    }
}
