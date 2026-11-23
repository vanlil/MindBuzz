namespace MindBuzz.App;

/// <summary>
/// Facts about this build. Not settings: nothing here is read from disk or remembered between runs.
/// </summary>
public static class AppInfo
{
    /// <summary>
    /// Where the source lives. Not in <c>Strings.resx</c>: a URL is not translatable text, and a language
    /// file that could change it would be a language file that could point the app somewhere else.
    /// </summary>
    public const string RepositoryUrl = "https://github.com/vanlil/MindBuzz";

    /// <summary>
    /// The version as set once in <c>Directory.Build.props</c>, three parts. Falls back to an empty string
    /// rather than throwing: a missing version is worth hiding a line of the About dialog over, and nothing
    /// more.
    /// </summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? string.Empty;

    /// <summary>
    /// The bundled components' copyright and licence notices, read from the embedded
    /// <c>THIRD-PARTY-NOTICES.txt</c>. Shown in the About dialog because the MIT, BSD and Open Font licences
    /// of what this app bundles require their notices to travel with the binary, and a single-file binary
    /// has nowhere else to put them.
    /// <para>
    /// Never translated: this is the licensors' text, reproduced verbatim. Falls back to an empty string,
    /// which hides the section rather than throwing; a missing resource is caught by a test, not by the
    /// user's About dialog.
    /// </para>
    /// </summary>
    public static string ThirdPartyNotices { get; } = ReadNotices();

    internal const string NoticesResourceName = "MindBuzz.App.ThirdPartyNotices.txt";

    private static string ReadNotices()
    {
        try
        {
            using Stream? stream = typeof(AppInfo).Assembly.GetManifestResourceStream(NoticesResourceName);

            if (stream is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
