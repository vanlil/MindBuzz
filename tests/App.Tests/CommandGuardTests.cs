using System.Globalization;
using System.Text;
using Avalonia.Input;
using MindBuzz.App.Commands;
using MindBuzz.App.ViewModels;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>Guard tests on the command registry. Changing them is a deliberate edit.</summary>
public sealed class CommandGuardTests
{
    public static TheoryData<HostPlatform> Platforms => [HostPlatform.Windows, HostPlatform.MacOS, HostPlatform.Linux];

    private static MainViewModel CreateMain(HostPlatform platform) => new(new FakeUiServices(), new FakePlatformShell(), platform);

    private static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> entries) =>
        entries.SelectMany(entry => entry.HasChildren ? Flatten(entry.Children!).Prepend(entry) : [entry]);

    /// <summary>The owner requires mouse parity for every keyboard action on a topic.</summary>
    [Theory]
    [MemberData(nameof(Platforms))]
    public void Every_topic_command_with_a_gesture_is_in_the_topic_context_menu(HostPlatform platform)
    {
        MainViewModel main = CreateMain(platform);
        var inMenu = Flatten(main.TopicMenu).Select(entry => entry.Command).ToHashSet();

        var missing = main.Commands.All
            .Where(command => command.ActsOnTopic && command.Gestures.For(platform).Count > 0)
            .Where(command => !MenuLayout.NotInTopicContextMenu.Contains(command.Id))
            .Where(command => !inMenu.Contains(command.Command))
            .Select(command => command.Id)
            .ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void The_context_menu_shows_the_shortcut_of_every_command_that_has_one(HostPlatform platform)
    {
        MainViewModel main = CreateMain(platform);

        // Menu-only commands (the progress values) have no gesture to show.
        Assert.All(
            Flatten(main.TopicMenu).Where(entry => !entry.IsSeparator && !entry.HasChildren),
            entry =>
            {
                AppCommand command = main.Commands.All.Single(c => c.Command == entry.Command);
                Assert.Equal(command.Gestures.For(platform).Count > 0, entry.Gesture is not null);
            });
    }

    /// <summary>
    /// A macOS menu-bar key equivalent catches its key everywhere in the app. Plain keys there would delete topics
    /// while the user types in a text box or a dialog.
    /// </summary>
    [Fact]
    public void The_macOS_menu_bar_has_no_gesture_without_cmd_or_ctrl()
    {
        MenuModel menus = CreateMain(HostPlatform.MacOS).Menus;

        Assert.All(
            Flatten(menus.TopLevel).Concat(Flatten(menus.MacApplicationItems)).Select(entry => entry.Gesture).OfType<KeyGesture>(),
            gesture => Assert.True(
                (gesture.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0 || gesture.Key >= Key.F1 && gesture.Key <= Key.F24,
                $"{gesture} would be caught app-wide"));
    }

    /// <summary>
    /// Keyboard Shortcuts keeps F1 only, so the macOS menu bar shows it; commands with a ⌘ key show that
    /// one; a plain F-key of a canvas command (F2 Edit Text) stays out, or it would be caught while typing.
    /// </summary>
    [Fact]
    public void The_macOS_menu_bar_shows_F1_and_the_command_keys_but_no_canvas_F_key()
    {
        MainViewModel main = CreateMain(HostPlatform.MacOS);
        MenuModel menus = main.Menus;
        var entries = Flatten(menus.TopLevel).Concat(Flatten(menus.MacApplicationItems)).ToList();
        KeyGesture? GestureOf(CommandId id) => entries.First(entry => entry.Command == main.Commands[id].Command).Gesture as KeyGesture;

        Assert.Equal(new KeyGesture(Key.F1), GestureOf(CommandId.KeyboardShortcuts));
        Assert.True((GestureOf(CommandId.NextPane)!.KeyModifiers & KeyModifiers.Meta) != 0, "the pane cycle shows ⌥⌘T, not F6");
        Assert.DoesNotContain(entries, entry => entry.Gesture is KeyGesture { Key: Key.F2, KeyModifiers: KeyModifiers.None });
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void Canvas_commands_are_never_bound_on_the_window(HostPlatform platform)
    {
        MainViewModel main = CreateMain(platform);

        var windowBindings = KeyBindingPlan.For(main.Commands, main.Menus, platform, KeyScope.Window);

        Assert.DoesNotContain(windowBindings, binding => binding.Command.Scope == KeyScope.Canvas);
        Assert.DoesNotContain(windowBindings, binding => binding.Gesture.Key == Key.Enter && binding.Gesture.KeyModifiers == KeyModifiers.None);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void Every_command_with_a_gesture_names_its_source_or_says_it_is_ours(HostPlatform platform)
    {
        Assert.All(
            CreateMain(platform).Commands.All.Where(command => command.Gestures.For(platform).Count > 0),
            command => Assert.False(string.IsNullOrWhiteSpace(command.Source), $"{command.Id} has no source"));
    }

    /// <summary>
    /// <c>docs/keyboard.md</c> is generated from the command table. This fails when the file is stale; run the
    /// tests with <c>MINDBUZZ_UPDATE_DOCS=1</c> to rewrite it, then review the diff.
    /// </summary>
    [Fact]
    public void Docs_keyboard_md_matches_the_command_table()
    {
        string path = Path.Join(RepositoryRoot(), "docs", "keyboard.md");
        string expected = KeyboardDoc();

        if (Environment.GetEnvironmentVariable("MINDBUZZ_UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path), "docs/keyboard.md is missing; run the tests with MINDBUZZ_UPDATE_DOCS=1.");
        Assert.True(
            File.ReadAllText(path).ReplaceLineEndings("\n") == expected,
            "docs/keyboard.md is stale; run the tests with MINDBUZZ_UPDATE_DOCS=1 and review the diff.");
    }

    private static string KeyboardDoc()
    {
        CommandRegistry windows = CreateMain(HostPlatform.Windows).Commands;
        CommandRegistry mac = CreateMain(HostPlatform.MacOS).Commands;

        var text = new StringBuilder();
        text.Append("# Keyboard shortcuts\n\n")
            .Append("Generated from the command table (`src/App/Commands/AppCommands.cs`) by `CommandGuardTests`. Do not edit by\n")
            .Append("hand: change the table, then run the tests with `MINDBUZZ_UPDATE_DOCS=1`. Linux uses the Windows gestures\n")
            .Append("except where the table says otherwise (Quit: Ctrl+Q).\n\n");

        foreach (IGrouping<string, AppCommand> group in windows.All.GroupBy(command => command.Group))
        {
            text.Append("## ").Append(MenuText.WithoutAccessKey(group.Key)).Append("\n\n")
                .Append("| Command | Windows | macOS | Source |\n|---|---|---|---|\n");

            foreach (AppCommand command in group)
            {
                text.Append(CultureInfo.InvariantCulture, $"| {MenuText.WithoutAccessKey(command.Label)} ")
                    .Append(CultureInfo.InvariantCulture, $"| {Gestures(command, HostPlatform.Windows)} ")
                    .Append(CultureInfo.InvariantCulture, $"| {Gestures(mac[command.Id], HostPlatform.MacOS)} ")
                    .Append(CultureInfo.InvariantCulture, $"| {command.Source ?? "—"} |\n");
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    private static string Gestures(AppCommand command, HostPlatform platform)
    {
        IReadOnlyList<KeyGesture> gestures = command.Gestures.KeyGestures(platform);
        return gestures.Count == 0 ? "—" : string.Join(", ", gestures.Select(gesture => GestureText.Format(gesture, platform).Replace("|", "\\|", StringComparison.Ordinal)));
    }

    private static string RepositoryRoot()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Join(folder, "MindBuzz.slnx")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new InvalidOperationException("Repository root not found.");
    }
}
