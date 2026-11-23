using System.Reflection;
using Xunit;

namespace MindBuzz.Core.Tests;

/// <summary>
/// Guard: Core has no UI references. The model, commands, layout and serialisation must stay testable
/// without Avalonia, and fast to test.
/// </summary>
public sealed class CoreBoundaryTests
{
    [Fact]
    public void Core_references_no_ui_framework()
    {
        Assembly core = Assembly.Load("MindBuzz.Core");

        string[] references = [.. core.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)];

        Assert.DoesNotContain(references, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("CommunityToolkit", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("SkiaSharp", StringComparison.Ordinal));
    }
}
