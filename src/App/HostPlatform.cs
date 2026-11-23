namespace MindBuzz.App;

/// <summary>
/// Which desktop the app is running on. Passed as a parameter wherever a per-OS decision is pure (the title
/// strip, the shortcut table, the menu arrangement), so every platform's behaviour is testable from any of
/// them. The OS is asked exactly once, at <see cref="Host.Current"/>, and never inside the logic.
/// </summary>
public enum HostPlatform
{
    Windows,
    MacOS,
    Linux,
}

internal static class Host
{
    /// <summary>The platform this process is actually running on.</summary>
    internal static HostPlatform Current =>
        OperatingSystem.IsMacOS() ? HostPlatform.MacOS
        : OperatingSystem.IsLinux() ? HostPlatform.Linux
        : HostPlatform.Windows;
}
