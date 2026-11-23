namespace MindBuzz.App.Platform;

/// <summary>
/// The gate in front of <see cref="IPlatformShell.OpenUrl"/>. Pure, and therefore testable without a browser.
/// </summary>
internal static class SafeUrl
{
    /// <summary>
    /// True only for an absolute <c>http</c>, <c>https</c> or <c>mailto</c> URI: the schemes allowed for
    /// internet links. Everything else, a relative string, a path or another scheme, is never
    /// handed to the OS, because the OS would happily run or open it. The rule itself lives in Core
    /// (<c>LinkUrl</c>), where the loader uses it too; this is the second check right before the shell.
    /// </summary>
    internal static bool IsAllowed(string? url) => Core.Model.LinkUrl.IsAllowed(url);
}
