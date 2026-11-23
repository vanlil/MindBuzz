using MindBuzz.Core.Model;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.Core.Tests;

public sealed class RecoveryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mindbuzz-recovery-").FullName;
    private readonly ManualTime _time = new();
    private readonly List<Exception> _failures = [];
    private readonly RecoveryWriter _writer;
    private readonly string _document;

    public RecoveryTests()
    {
        _writer = new RecoveryWriter(_time, "tests", hide: false, _failures.Add);
        _document = Path.Join(_folder, "Plan.mbz");
    }

    public void Dispose()
    {
        _writer.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static Document Doc(string text) => new([new Map(new MapId("M"), new Topic(new TopicId("T"), text))]);

    private string RecoveryPath => RecoveryFile.PathFor(_document);

    private string? RecoveredText() =>
        File.Exists(RecoveryPath) ? MbzFile.Load(RecoveryPath).Document!.Maps[0].Root.Text : null;

    private void Settle() => _writer.Idle.Wait(TimeSpan.FromSeconds(10));

    /// <summary>A copy of a private document must be as private as the document.</summary>
    [Fact]
    public void The_recovery_file_is_as_private_as_its_document()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        MbzFile.Save(_document, Doc("saved"), "tests");
        File.SetUnixFileMode(_document, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        RecoveryFile.Write(_document, Doc("first"), "tests", hide: false);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(RecoveryPath));

        // Also when an older recovery file was readable by others.
        File.SetUnixFileMode(RecoveryPath, File.GetUnixFileMode(RecoveryPath) | UnixFileMode.OtherRead);
        RecoveryFile.Write(_document, Doc("second"), "tests", hide: false);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(RecoveryPath));
    }

    /// <summary>Quitting right after "Don't Save": the queued delete still happens before the process ends.</summary>
    [Fact]
    public void Dispose_finishes_a_queued_delete()
    {
        MbzFile.Save(_document, Doc("saved"), "tests");
        RecoveryFile.Write(_document, Doc("unsaved"), "tests", hide: false);
        var writer = new RecoveryWriter(_time, "tests", hide: false, _failures.Add);

        writer.Discard(_document);
        writer.Dispose();

        Assert.True(writer.Idle.IsCompleted);
        Assert.False(File.Exists(RecoveryPath));
    }

    [Fact]
    public void The_recovery_file_sits_next_to_the_document_with_a_hidden_style_name()
    {
        Assert.Equal(Path.Join(_folder, ".Plan.mbz~"), RecoveryPath);
    }

    [Fact]
    public void It_is_written_after_two_seconds_without_changes()
    {
        _writer.Changed(_document, Doc("one"), dirty: true);

        _time.Advance(TimeSpan.FromSeconds(1.9));
        Settle();
        Assert.Null(RecoveredText());

        _time.Advance(TimeSpan.FromSeconds(0.2));
        Settle();
        Assert.Equal("one", RecoveredText());
    }

    [Fact]
    public void Continuous_typing_postpones_the_write_until_a_pause()
    {
        for (int i = 0; i < 10; i++)
        {
            _writer.Changed(_document, Doc($"v{i}"), dirty: true);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        Settle();
        Assert.Null(RecoveredText());

        _time.Advance(TimeSpan.FromSeconds(2));
        Settle();
        Assert.Equal("v9", RecoveredText());
    }

    [Fact]
    public void After_a_write_the_next_one_waits_at_least_thirty_seconds()
    {
        _writer.Changed(_document, Doc("first"), dirty: true);
        _time.Advance(TimeSpan.FromSeconds(3));
        Settle();

        _writer.Changed(_document, Doc("second"), dirty: true);
        _time.Advance(TimeSpan.FromSeconds(10));
        Settle();
        Assert.Equal("first", RecoveredText());

        _time.Advance(TimeSpan.FromSeconds(25));
        Settle();
        Assert.Equal("second", RecoveredText());
    }

    [Fact]
    public void An_untitled_or_clean_document_gets_no_recovery_file()
    {
        _writer.Changed(null, Doc("untitled"), dirty: true);
        _writer.Changed(_document, Doc("clean"), dirty: false);
        _time.Advance(TimeSpan.FromMinutes(1));
        Settle();

        Assert.False(File.Exists(RecoveryPath));
    }

    [Fact]
    public void A_save_deletes_the_recovery_file_even_right_after_a_write()
    {
        _writer.Changed(_document, Doc("one"), dirty: true);
        _time.Advance(TimeSpan.FromSeconds(3));

        // Not settled: the write may still be running when the save discards. The queue keeps the order.
        _writer.Discard(_document);
        Settle();

        Assert.False(File.Exists(RecoveryPath));
    }

    [Fact]
    public void The_emergency_write_happens_at_once()
    {
        _writer.Changed(_document, Doc("crash"), dirty: true);

        _writer.WriteNow();

        Assert.Equal("crash", RecoveredText());
    }

    [Fact]
    public void Only_a_recovery_file_newer_than_the_document_is_offered()
    {
        MbzFile.Save(_document, Doc("saved"), "tests");
        RecoveryFile.Write(_document, Doc("recovered"), "tests", hide: false);
        File.SetLastWriteTimeUtc(_document, DateTime.UtcNow.AddMinutes(-5));
        Assert.True(RecoveryFile.HasNewerRecovery(_document));

        File.SetLastWriteTimeUtc(_document, DateTime.UtcNow.AddMinutes(5));
        Assert.False(RecoveryFile.HasNewerRecovery(_document));
    }

    [Fact]
    public void Write_failures_are_reported_never_thrown()
    {
        string missing = Path.Join(_folder, "no-such-folder", "Plan.mbz");
        _writer.Changed(missing, Doc("x"), dirty: true);
        _time.Advance(TimeSpan.FromSeconds(3));
        Settle();

        Assert.NotEmpty(_failures);
    }
}
