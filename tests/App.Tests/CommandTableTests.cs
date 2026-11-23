using Avalonia.Input;
using MindBuzz.App.Commands;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// Guard tests on the command table: asserted for each platform from any host, because the
/// table is pure data plus a platform parameter.
/// </summary>
public sealed class CommandTableTests
{
    public static TheoryData<HostPlatform> Platforms => [HostPlatform.Windows, HostPlatform.MacOS, HostPlatform.Linux];

    private static CommandRegistry Registry() => AppCommands.Create(new NullCommandHost());

    [Theory]
    [MemberData(nameof(Platforms))]
    public void No_two_commands_share_a_gesture_on_the_same_platform(HostPlatform platform)
    {
        // The strictest form of the rule: no gesture is used twice on one platform, whatever the context.
        var duplicates = Registry().All
            .SelectMany(command => command.Gestures.KeyGestures(platform).Select(gesture => (gesture, command.Id)))
            .GroupBy(pair => pair.gesture)
            .Where(group => group.Count() > 1)
            .Select(group => $"{GestureText.Format(group.Key, platform)}: {string.Join(", ", group.Select(pair => pair.Id))}")
            .ToList();

        Assert.True(duplicates.Count == 0, string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void No_command_uses_a_shortcut_macOS_reserves_for_itself()
    {
        // ⌘H hides, ⌘M minimises, ⌘Q quits, ⌘Tab and ⌘` switch apps and windows.
        KeyGesture[] reserved =
        [
            new(Key.H, KeyModifiers.Meta),
            new(Key.M, KeyModifiers.Meta),
            new(Key.Q, KeyModifiers.Meta),
            new(Key.Tab, KeyModifiers.Meta),
            new(Key.OemTilde, KeyModifiers.Meta),
        ];

        var used = Registry().All.SelectMany(command => command.Gestures.KeyGestures(HostPlatform.MacOS)).ToHashSet();

        Assert.All(reserved, gesture => Assert.DoesNotContain(gesture, used));
    }

    [Fact]
    public void No_command_uses_alt_F4_on_Windows()
    {
        var used = Registry().All.SelectMany(command => command.Gestures.KeyGestures(HostPlatform.Windows));

        Assert.DoesNotContain(new KeyGesture(Key.F4, KeyModifiers.Alt), used);
    }

    [Theory]
    [InlineData(HostPlatform.Windows, KeyModifiers.Control)]
    [InlineData(HostPlatform.Linux, KeyModifiers.Control)]
    [InlineData(HostPlatform.MacOS, KeyModifiers.Meta)]
    public void The_command_modifier_is_ctrl_except_on_macOS_where_it_is_cmd(HostPlatform platform, KeyModifiers expected)
    {
        KeyGesture gesture = new Chord(Key.S, ChordModifiers.Command).ToKeyGesture(platform);

        Assert.Equal(expected, gesture.KeyModifiers);
    }

    [Fact]
    public void The_literal_control_modifier_stays_control_on_macOS()
    {
        // ⌃Tab for the next map must not become ⌘Tab, which belongs to the OS.
        KeyGesture gesture = new Chord(Key.Tab, ChordModifiers.Control).ToKeyGesture(HostPlatform.MacOS);

        Assert.Equal(KeyModifiers.Control, gesture.KeyModifiers);
    }

    [Theory]
    [InlineData(HostPlatform.Windows, "Ctrl+Shift+K")]
    [InlineData(HostPlatform.Linux, "Ctrl+Shift+K")]
    [InlineData(HostPlatform.MacOS, "⇧⌘K")]
    public void Gestures_are_written_the_way_each_platform_writes_them(HostPlatform platform, string expected)
    {
        KeyGesture gesture = new Chord(Key.K, ChordModifiers.Command | ChordModifiers.Shift).ToKeyGesture(platform);

        Assert.Equal(expected, GestureText.Format(gesture, platform));
    }

    [Fact]
    public void MacOS_modifiers_are_written_in_the_order_apple_uses()
    {
        var gesture = new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta);

        Assert.Equal("⌃⌥⇧⌘P", GestureText.Format(gesture, HostPlatform.MacOS));
    }

    [Fact]
    public void Every_command_has_a_label()
    {
        Assert.All(Registry().All, command => Assert.False(string.IsNullOrWhiteSpace(command.Label)));
    }

    [Fact]
    public void Every_command_id_is_registered_exactly_once()
    {
        CommandRegistry registry = Registry();

        Assert.Equal(Enum.GetValues<CommandId>().Order(), registry.All.Select(command => command.Id).Order());
    }

    /// <summary>A command host that does nothing, for tests that only read the table.</summary>
    private sealed class NullCommandHost : ICommandHost
    {
        public void Execute(CommandId command)
        {
        }

        public bool CanExecute(CommandId command) => true;
    }
}
