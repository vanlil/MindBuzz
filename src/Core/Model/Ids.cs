using System.Security.Cryptography;

namespace MindBuzz.Core.Model;

/// <summary>
/// Stable identifiers for topics, maps and relationships. They are used by relationships, undo and the file
/// and never change once assigned, across saves too.
/// <para>
/// The text form is 26 characters of Crockford base32 encoding 128 bits, e.g. <c>0J5W8X3M9QK1V7N2C4H6R8T0BD</c>.
/// Crockford's alphabet has no I, L, O or U, so an id never reads as a word and never confuses 0 with O.
/// </para>
/// </summary>
public readonly record struct TopicId(string Value)
{
    public override string ToString() => Value;
}

/// <inheritdoc cref="TopicId"/>
public readonly record struct MapId(string Value)
{
    public override string ToString() => Value;
}

/// <inheritdoc cref="TopicId"/>
public readonly record struct RelationshipId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Where new ids come from. The app uses <see cref="RandomIdSource"/>; tests and the synthetic map generator use
/// <see cref="SeededIdSource"/>, so a seed reproduces the same document byte for byte. Commands never invent ids
/// themselves: the caller passes them in, which keeps every command deterministic and replayable.
/// </summary>
public interface IIdSource
{
    string NewId();
}

public static class IdSourceExtensions
{
    public static TopicId NextTopic(this IIdSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new TopicId(source.NewId());
    }

    public static MapId NextMap(this IIdSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new MapId(source.NewId());
    }

    public static RelationshipId NextRelationship(this IIdSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RelationshipId(source.NewId());
    }
}

/// <summary>128 random bits per id from the OS's cryptographic generator: collisions are not a practical
/// concern.</summary>
public sealed class RandomIdSource : IIdSource
{
    public static RandomIdSource Instance { get; } = new();

    public string NewId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return IdText.Encode(bytes);
    }
}

/// <summary>Reproducible ids for tests and generated maps. Not for real documents.</summary>
public sealed class SeededIdSource(int seed) : IIdSource
{
    private readonly Random _random = new(seed);

    public string NewId()
    {
        Span<byte> bytes = stackalloc byte[16];
        _random.NextBytes(bytes);
        return IdText.Encode(bytes);
    }
}

/// <summary>Crockford base32 for ids, and the check a loaded id must pass.</summary>
public static class IdText
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>128 bits need 26 base32 digits (130 bits, the top two always zero).</summary>
    public const int Length = 26;

    internal static string Encode(ReadOnlySpan<byte> bytes)
    {
        UInt128 value = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
        }

        Span<char> text = stackalloc char[Length];
        for (int i = Length - 1; i >= 0; i--)
        {
            text[i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(text);
    }

    /// <summary>
    /// True for a well-formed id. The loader is lenient about the form (any 1–64 characters from the alphabet,
    /// upper case) so that ids from a future import or a hand-edited file still load, but strict
    /// about characters, so an id can never smuggle in anything surprising.
    /// </summary>
    public static bool IsValid(string? text) =>
        text is { Length: > 0 and <= 64 } && text.All(c => Alphabet.Contains(c, StringComparison.Ordinal));
}
