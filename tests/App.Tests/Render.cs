using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace MindBuzz.App.Tests;

/// <summary>Renders windows headlessly and saves the frames where a human can look at them.</summary>
internal static class Render
{
    /// <summary>Where rendered frames land: <c>tests/App.Tests/bin/.../render</c>, next to the test binary.</summary>
    internal static string ArtifactDirectory
    {
        get
        {
            string path = Path.Join(AppContext.BaseDirectory, "render");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal static IEnumerable<ThemeVariant> BothThemes => [ThemeVariant.Light, ThemeVariant.Dark];

    /// <summary>
    /// Shows <paramref name="window"/> in <paramref name="theme"/>, renders it and saves the frame as
    /// <paramref name="name"/>-light.png or -dark.png. The first captured frame can be blank, so the frame is
    /// captured twice after running the dispatcher.
    /// </summary>
    internal static string Save(Window window, ThemeVariant theme, string name)
    {
        Application.Current!.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? first = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();

        string path = Path.Join(ArtifactDirectory, $"{name}-{theme.Key.ToString()!.ToLowerInvariant()}.png");
        (frame ?? first ?? throw new InvalidOperationException("Nothing was rendered.")).Save(path, new PngBitmapEncoderOptions());
        window.Close();
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        return path;
    }

    /// <summary>The window's current frame as BGRA bytes, without closing it: for comparing two moments.</summary>
    internal static byte[] Pixels(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered.");
        using ILockedFramebuffer buffer = frame.Lock();
        byte[] bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>True when the two frames differ within <paramref name="radius"/> pixels of <paramref
    /// name="at"/>.</summary>
    internal static bool DiffersNear(byte[] a, byte[] b, Window window, Point at, double radius)
    {
        int width = (int)window.ClientSize.Width;
        for (int y = (int)(at.Y - radius); y <= (int)(at.Y + radius); y++)
        {
            for (int x = (int)(at.X - radius); x <= (int)(at.X + radius); x++)
            {
                int i = ((y * width) + x) * 4;
                if (i >= 0 && i + 2 < a.Length && (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
