using Avalonia;

namespace MindBuzz.App.Views;

/// <summary>
/// How the title strip is arranged on one platform. Pure, and chosen by a parameter rather than by asking the
/// OS, so all three layouts are tested from any machine. Getting this wrong is invisible until someone looks
/// at a screen on the other platform.
/// <para>
/// This is the Avalonia 12 shape of DiskAnalyzer's title strip. Avalonia 12 removed
/// <c>ExtendClientAreaChromeHints</c>: the client area is extended with
/// <c>Window.ExtendClientAreaToDecorationsHint</c>, the OS keeps its caption buttons (traffic lights on
/// macOS), and the strip marks itself as a drag area through <c>WindowDecorationProperties.ElementRole</c>.
/// The maximised-window inset workaround is not ported: Avalonia 12 handles it natively.
/// Checked against the Avalonia 12.1.6 API documentation in the NuGet package.
/// </para>
/// </summary>
/// <param name="ExtendClientArea">
/// True to paint the strip under the system decorations. Windows and macOS honour it. Linux does not get it:
/// KWin and Mutter draw a real title bar of their own, so extending there produced two title bars.
/// </param>
/// <param name="ShowTitleText">
/// Whether the strip paints the window's name. Only where the app has taken over the title bar; where the
/// window manager draws one, it already shows the title.
/// </param>
/// <param name="ShowInWindowMenu">
/// Whether the strip holds the in-window menu. False on macOS, where the menu lives in the system menu bar.
/// </param>
/// <param name="Height">The strip's height, also passed to the OS as the title bar height hint.</param>
/// <param name="ContentMargin">
/// Space kept clear around the strip's content: the traffic lights at the left on macOS, the caption buttons
/// at the right on Windows. Avalonia exposes no width for either, hence the constants.
/// </param>
internal readonly record struct TitleStripLayout(
    bool ExtendClientArea,
    bool ShowTitleText,
    bool ShowInWindowMenu,
    double Height,
    Thickness ContentMargin)
{
    /// <summary>Minimise, maximise and close at 46 px each. <em>Unverified</em> for Avalonia 12's buttons.</summary>
    private const double WindowsCaptionButtonsWidth = 138;

    /// <summary>The traffic lights plus the inset they sit in.</summary>
    private const double MacTrafficLightsWidth = 78;

    internal static TitleStripLayout For(HostPlatform platform) => platform switch
    {
        // The traffic lights sit top left, so content starts to their right; the menu is in the system menu
        // bar. 28 px is the native title bar height: Avalonia 12 keeps the lights at their native place (7 px
        // from the top, observed on macOS 26 through the accessibility API), so a taller strip put
        // the title text visibly lower than the lights.
        HostPlatform.MacOS => new TitleStripLayout(
            ExtendClientArea: true,
            ShowTitleText: true,
            ShowInWindowMenu: false,
            Height: 28,
            ContentMargin: new Thickness(MacTrafficLightsWidth, 0, 12, 0)),

        // The window manager owns the title bar and already shows the name, so the strip is just the menu
        // row. The desktop's own decorations also keep tiling and every KWin shortcut working.
        HostPlatform.Linux => new TitleStripLayout(
            ExtendClientArea: false,
            ShowTitleText: false,
            ShowInWindowMenu: true,
            Height: 32,
            ContentMargin: new Thickness(4, 0, 8, 0)),

        // The OS caption buttons are kept, never drawn by the app: they keep snap layouts, drag-to-snap and
        // the accessibility support that would otherwise be rebuilt badly.
        _ => new TitleStripLayout(
            ExtendClientArea: true,
            ShowTitleText: true,
            ShowInWindowMenu: true,
            Height: 34,
            ContentMargin: new Thickness(4, 0, WindowsCaptionButtonsWidth, 0)),
    };
}
