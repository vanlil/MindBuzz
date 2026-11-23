using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using MindBuzz.App.Canvas;
using MindBuzz.App.ViewModels;
using MindBuzz.App.Views;
using MindBuzz.Core.Layout;
using MindBuzz.Core.Model;

namespace MindBuzz.App.Tests;

/// <summary>
/// A real main window on the headless platform, driven by simulated keys and mouse: the end-to-end harness for
/// canvas behaviour. Waits by running the dispatcher, never by sleeping.
/// </summary>
internal sealed class Session
{
    /// <param name="platform">The platform whose arrangement the window takes.</param>
    /// <param name="ui">Fake dialogs, for tests that answer them; null uses the real windows.</param>
    public Session(HostPlatform platform = HostPlatform.Windows, FakeUiServices? ui = null)
    {
        Ui = ui;
        Window = new MainWindow(platform, Shell, ui: ui);
        Window.Show();
        Pump();
        Canvas.Focus();
        Pump();
    }

    public MainWindow Window { get; }

    public FakePlatformShell Shell { get; } = new();

    public FakeUiServices? Ui { get; }

    public MainViewModel Main => Window.ViewModel;

    public MapCanvas Canvas => Window.FindControl<MapCanvas>("Canvas")!;

    public Map Map => Main.ActiveMap;

    public Topic Central => Map.Root;

    public Topic Selected => Map.Find(Main.SelectedTopic!.Value)!;

    public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Pump();
    }

    public void Type(string text)
    {
        foreach (char c in text)
        {
            Window.KeyTextInput(c.ToString());
            Pump();
        }
    }

    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }
}

/// <summary>Mouse helpers for the session: positions are computed from the canvas's own viewport.</summary>
internal static class SessionMouse
{
    /// <summary>A point on a topic, in window coordinates: <paramref name="fy"/> 0 is its top edge, 1 its
    /// bottom.</summary>
    public static Point On(this Session s, TopicId topic, double fy = 0.5)
    {
        TopicBox box = s.Canvas.Layout!.Find(topic)!;
        Point onCanvas = s.Canvas.Viewport.ToScreen(new LayoutPoint(box.Bounds.CenterX, box.Bounds.Y + (box.Bounds.Height * fy)));
        return s.Canvas.TranslatePoint(onCanvas, s.Window)!.Value;
    }

    public static void Drag(this Session s, Point from, Point to, bool cancelWithEscape = false)
    {
        s.Window.MouseDown(from, MouseButton.Left);
        Session.Pump();
        var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        s.Window.MouseMove(middle, RawInputModifiers.LeftMouseButton);
        Session.Pump();
        s.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Session.Pump();
        if (cancelWithEscape)
        {
            s.Press(Key.Escape);
        }

        s.Window.MouseUp(to, MouseButton.Left);
        Session.Pump();
    }

    public static void Click(this Session s, Point at)
    {
        s.Window.MouseDown(at, MouseButton.Left);
        s.Window.MouseUp(at, MouseButton.Left);
        Session.Pump();
    }
}
