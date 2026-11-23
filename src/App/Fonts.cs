using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace MindBuzz.App;

/// <summary>
/// The fonts: Inter from Avalonia.Fonts.Inter (Thin to Bold), plus Inter ExtraBold bundled with the app, for bold
/// text on the central and main topics, which are semibold already: Bold beside SemiBold hardly shows. Same Inter
/// version (3.19) as Avalonia's, so the metrics match.
/// </summary>
public static class Fonts
{
    internal static FontFamily InterExtraBold { get; } = new("fonts:MindBuzz#Inter");

    /// <summary>Registers Inter and the bundled weight. The app and the UI tests both start through this.</summary>
    public static AppBuilder WithMindBuzzFonts(this AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithInterFont()
            .ConfigureFonts(manager => manager.AddFontCollection(
                new EmbeddedFontCollection(new Uri("fonts:MindBuzz"), new Uri("avares://MindBuzz.App/Assets/Fonts"))));
    }
}
