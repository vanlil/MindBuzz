using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MindBuzz.App.Services;

/// <summary>
/// The update check at start: one request to GitHub for the latest release, compared with this
/// build's version. It only tells; nothing is downloaded or installed. Every failure (offline, a slow or refused
/// answer, no release yet, a private repository) means "no news", silently: the check must never cost the user
/// anything, and it runs again at the next start.
/// </summary>
internal static partial class UpdateCheck
{
    /// <summary>GitHub's documented "latest release" endpoint: the newest published release, drafts and pre-releases
    /// left out (https://docs.github.com/en/rest/releases/releases#get-the-latest-release).</summary>
    internal static readonly Uri LatestReleaseApi =
        new(AppInfo.RepositoryUrl.Replace("https://github.com/", "https://api.github.com/repos/", StringComparison.Ordinal) + "/releases/latest");

    /// <summary>Long enough for a slow connection, short enough not to keep a socket open for long. Nothing waits on
    /// it.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>A release answer is a few kilobytes plus its notes; more than this is not GitHub's answer.</summary>
    internal const int MaxAnswerBytes = 1024 * 1024;

    /// <summary>
    /// Asks GitHub for the latest release. Never throws.
    /// </summary>
    /// <param name="current">This build's version.</param>
    /// <param name="handler">The test's stand-in for the network; null for the real one.</param>
    internal static async Task<UpdateAnswer> CheckAsync(Version current, HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        try
        {
            using HttpClient client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            client.Timeout = Timeout;
            client.MaxResponseContentBufferSize = MaxAnswerBytes;

            // GitHub refuses API requests without a User-Agent; the version header pins the answer's shape
            // (https://docs.github.com/en/rest/using-the-rest-api/getting-started-with-the-rest-api).
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MindBuzz", current.ToString(3)));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            string answer = await client.GetStringAsync(LatestReleaseApi, cancel).ConfigureAwait(false);
            return Read(answer, current);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Offline, timed out, 404 (no release yet, or the repository is private), too large.
            return UpdateAnswer.Unknown;
        }
    }

    /// <summary>What GitHub's answer says about <paramref name="current"/>; <see cref="UpdateAnswer.Unknown"/> for an
    /// answer that is not JSON or has no version this check understands.</summary>
    internal static UpdateAnswer Read(string answer, Version current)
    {
        try
        {
            using var json = JsonDocument.Parse(answer);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("tag_name", out JsonElement tag)
                || tag.ValueKind != JsonValueKind.String
                || ParseTag(tag.GetString()!) is not { } latest)
            {
                return UpdateAnswer.Unknown;
            }

            return latest > current ? UpdateAnswer.NewerRelease(latest) : UpdateAnswer.UpToDate;
        }
        catch (JsonException)
        {
            return UpdateAnswer.Unknown;
        }
    }

    /// <summary>
    /// "v1.2.3" or "1.2.3", nothing else: a tag like "v1.0.0-beta" or "latest" is not a version this check
    /// understands, so it is no news rather than a guess.
    /// </summary>
    internal static Version? ParseTag(string tag)
    {
        Match match = TagPattern().Match(tag);
        return match.Success
            ? new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// The release page the notice opens. Built from the repository and the version, never taken from GitHub's
    /// answer, so a tampered answer cannot send the user anywhere else.
    /// </summary>
    internal static string ReleasePage(Version version) =>
        $"{AppInfo.RepositoryUrl}/releases/tag/v{version.ToString(3)}";

    [GeneratedRegex(@"^v?(\d{1,5})\.(\d{1,5})\.(\d{1,5})$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}

/// <summary>The outcome of an update check.</summary>
/// <param name="Newer">The newer release's version; null unless <paramref name="Status"/> is <see
/// cref="UpdateStatus.Newer"/>.</param>
internal sealed record UpdateAnswer(UpdateStatus Status, Version? Newer = null)
{
    internal static readonly UpdateAnswer UpToDate = new(UpdateStatus.UpToDate);

    internal static readonly UpdateAnswer Unknown = new(UpdateStatus.Unknown);

    internal static UpdateAnswer NewerRelease(Version version) => new(UpdateStatus.Newer, version);
}

internal enum UpdateStatus
{
    /// <summary>GitHub's latest release is newer than this build.</summary>
    Newer,

    /// <summary>This build is the latest release, or newer (a development build).</summary>
    UpToDate,

    /// <summary>No answer: offline, a failure, no release yet, or an answer this check does not understand.</summary>
    Unknown,
}
