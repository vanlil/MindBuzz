using Avalonia;
using Avalonia.Headless;
using MindBuzz.App;

[assembly: AvaloniaTestApplication(typeof(MindBuzz.App.Tests.TestAppBuilder))]

namespace MindBuzz.App.Tests;

/// <summary>
/// Headless Avalonia for the UI tests. Drawing is left on (Skia, not headless drawing), so frames are really
/// rendered, hit testing behaves, and the tests can save PNGs for review. Avalonia 12 needs
/// <c>UseHarfBuzz()</c> next to <c>UseSkia()</c>, or text shaping fails at startup.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<MindBuzzApp>()
            .UseSkia()
            .UseHarfBuzz()
            .WithMindBuzzFonts()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })

            // The map's glides run in real time here and halved the suite's speed; they still start, so tests that
            // hold a transition (HoldTransitionAt) check it as before. After setup: the canvas's static fields need
            // Avalonia running.
            .AfterSetup(_ => Canvas.MapCanvas.AnimationTime = TimeSpan.FromMilliseconds(1));
}
