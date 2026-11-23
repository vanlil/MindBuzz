using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using MindBuzz.App.Commands;
using MindBuzz.App.Localization;
using MindBuzz.App.Services;
using MindBuzz.App.ViewModels;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>The update check: GitHub's latest release against this build. No test reaches
/// the network: the requests go to a fake handler. Statuses arrive as names, since a public test cannot take an
/// internal enum.</summary>
public sealed class UpdateCheckTests
{
    private static readonly Version Current = new(0, 9, 1);

    private static string Release(string tag) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://example.com/elsewhere","draft":false,"prerelease":false}""";

    /// <summary>Answers every request with one response, and records the requests.</summary>
    private sealed class FakeGitHub(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond());
        }
    }

    private static FakeGitHub Answering(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(() => new HttpResponseMessage(status) { Content = new StringContent(json) });

    [Theory]
    [InlineData("v1.0.0", "Newer")]
    [InlineData("0.10.0", "Newer")]
    [InlineData("v0.9.1", "UpToDate")]
    [InlineData("v0.8.5", "UpToDate")]
    [InlineData("v1.0.0-beta", "Unknown")]
    [InlineData("latest", "Unknown")]
    [InlineData("v1.0", "Unknown")]
    public void The_latest_release_is_compared_with_this_build_as_numbers(string tag, string expected)
    {
        Assert.Equal(Enum.Parse<UpdateStatus>(expected), UpdateCheck.Read(Release(tag), Current).Status);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"tag_name":42}""")]
    [InlineData("""{"name":"MindBuzz 1.0.0"}""")]
    public void An_answer_without_a_readable_version_is_no_news(string answer)
    {
        Assert.Equal(UpdateAnswer.Unknown, UpdateCheck.Read(answer, Current));
    }

    [Fact]
    public async Task One_request_to_the_latest_release_naming_the_app_and_its_version()
    {
        var github = Answering(Release("v1.0.1"));

        UpdateAnswer answer = await UpdateCheck.CheckAsync(Current, github, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateAnswer.NewerRelease(new Version(1, 0, 1)), answer);
        HttpRequestMessage request = Assert.Single(github.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/vanlil/MindBuzz/releases/latest", request.RequestUri!.ToString());
        Assert.Equal("MindBuzz/0.9.1", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task No_release_yet_or_a_private_repository_is_no_news()
    {
        Assert.Equal(UpdateAnswer.Unknown, await UpdateCheck.CheckAsync(Current, Answering("""{"message":"Not Found"}""", HttpStatusCode.NotFound), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Being_offline_is_no_news()
    {
        var offline = new FakeGitHub(() => throw new HttpRequestException("No route to host"));

        Assert.Equal(UpdateAnswer.Unknown, await UpdateCheck.CheckAsync(Current, offline, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_cancelled_check_is_no_news()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        Assert.Equal(UpdateAnswer.Unknown, await UpdateCheck.CheckAsync(Current, Answering(Release("v1.0.1")), cancel.Token));
    }

    [Fact]
    public async Task An_answer_far_larger_than_a_release_is_refused()
    {
        string huge = Release("v1.0.1").Replace("\"draft\"", $"\"body\":\"{new string('x', UpdateCheck.MaxAnswerBytes)}\",\"draft\"", StringComparison.Ordinal);

        Assert.Equal(UpdateAnswer.Unknown, await UpdateCheck.CheckAsync(Current, Answering(huge), TestContext.Current.CancellationToken));
    }

    /// <summary>The page comes from the repository and the version, never from the answer's <c>html_url</c>.</summary>
    [Fact]
    public void The_release_page_is_built_from_the_repository_and_the_version()
    {
        Assert.Equal("https://github.com/vanlil/MindBuzz/releases/tag/v1.0.1", UpdateCheck.ReleasePage(new Version(1, 0, 1)));
    }

    [Fact]
    public void Only_a_real_start_checks_and_never_the_benchmark_or_a_test_window()
    {
        Assert.True(AppOptions.Parse([]).CheckForUpdate);
        Assert.True(AppOptions.Parse(["Plan.mbz"]).CheckForUpdate);
        Assert.False(AppOptions.Parse(["--bench"]).CheckForUpdate);
        Assert.False(new AppOptions().CheckForUpdate);
    }

    private static (MainViewModel Main, FakeUiServices Ui, FakePlatformShell Shell) Model(UpdateAnswer answer)
    {
        var ui = new FakeUiServices();
        var shell = new FakePlatformShell();
        var main = new MainViewModel(ui, shell, HostPlatform.Windows) { UpdateSource = () => Task.FromResult(answer) };
        return (main, ui, shell);
    }

    [AvaloniaFact]
    public async Task At_start_a_newer_release_shows_only_the_quiet_button_which_opens_its_page()
    {
        (MainViewModel main, FakeUiServices ui, FakePlatformShell shell) = Model(UpdateAnswer.NewerRelease(new Version(1, 0, 1)));

        await main.CheckForUpdateAtStartAsync();

        Assert.Equal(new Version(1, 0, 1), main.AvailableUpdate);
        Assert.Equal("Version 1.0.1 available", main.UpdateText);
        Assert.Empty(ui.Messages);
        Assert.Empty(ui.Questions);

        main.OpenUpdatePageCommand.Execute(null);
        Assert.Equal(["https://github.com/vanlil/MindBuzz/releases/tag/v1.0.1"], shell.OpenedUrls);
    }

    private sealed class SteppingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [AvaloniaFact]
    public async Task A_double_click_on_the_button_opens_the_page_once()
    {
        var clock = new SteppingClock();
        var shell = new FakePlatformShell();
        var main = new MainViewModel(new FakeUiServices(), shell, HostPlatform.Windows, clock)
        {
            UpdateSource = () => Task.FromResult(UpdateAnswer.NewerRelease(new Version(1, 0, 1))),
        };
        await main.CheckForUpdateAtStartAsync();

        main.OpenUpdatePageCommand.Execute(null);
        clock.Now += TimeSpan.FromMilliseconds(300);
        main.OpenUpdatePageCommand.Execute(null);
        Assert.Single(shell.OpenedUrls);

        clock.Now += MainViewModel.UpdatePageRepeatGap;
        main.OpenUpdatePageCommand.Execute(null);
        Assert.Equal(2, shell.OpenedUrls.Count);
    }

    [AvaloniaTheory]
    [InlineData("UpToDate")]
    [InlineData("Unknown")]
    public async Task At_start_no_news_shows_nothing(string status)
    {
        (MainViewModel main, FakeUiServices ui, _) = Model(new UpdateAnswer(Enum.Parse<UpdateStatus>(status)));

        await main.CheckForUpdateAtStartAsync();

        Assert.Null(main.AvailableUpdate);
        Assert.Empty(ui.Messages);
    }

    [AvaloniaFact]
    public async Task Check_for_updates_offers_the_download_page_of_a_newer_release()
    {
        (MainViewModel main, FakeUiServices ui, FakePlatformShell shell) = Model(UpdateAnswer.NewerRelease(new Version(1, 0, 1)));
        ui.Answers.Enqueue(1);

        main.Execute(CommandId.CheckForUpdate);
        await Task.Yield();

        ChoiceRequest question = Assert.Single(ui.Questions);
        Assert.Equal("MindBuzz 1.0.1 is available.", question.Message);
        Assert.Equal(Strings.Answer_OpenDownloadPage, question.Answers[question.DefaultAnswer]);
        Assert.Equal(["https://github.com/vanlil/MindBuzz/releases/tag/v1.0.1"], shell.OpenedUrls);
        Assert.Equal(new Version(1, 0, 1), main.AvailableUpdate);
    }

    [AvaloniaTheory]
    [InlineData("UpToDate")]
    [InlineData("Unknown")]
    public async Task Check_for_updates_always_answers_when_asked(string status)
    {
        (MainViewModel main, FakeUiServices ui, FakePlatformShell shell) = Model(new UpdateAnswer(Enum.Parse<UpdateStatus>(status)));

        main.Execute(CommandId.CheckForUpdate);
        await Task.Yield();

        MessageRequest message = Assert.Single(ui.Messages);
        Assert.Equal(status == nameof(UpdateStatus.UpToDate) ? Strings.Update_UpToDate : Strings.Update_Unknown, message.Message);
        Assert.Empty(shell.OpenedUrls);
        Assert.Equal(Strings.Status_Ready, main.StatusText);
    }

    [AvaloniaFact]
    public void Check_for_updates_is_greyed_out_while_it_waits_for_GitHub()
    {
        var github = new TaskCompletionSource<UpdateAnswer>();
        var main = new MainViewModel(new FakeUiServices(), new FakePlatformShell(), HostPlatform.Windows) { UpdateSource = () => github.Task };

        main.Execute(CommandId.CheckForUpdate);

        Assert.False(main.CanExecute(CommandId.CheckForUpdate));
        Assert.Equal(Strings.Status_CheckingForUpdate, main.StatusText);
        github.SetResult(UpdateAnswer.UpToDate);
        Assert.True(main.CanExecute(CommandId.CheckForUpdate));
    }

    [AvaloniaTheory]
    [InlineData(HostPlatform.Windows)]
    [InlineData(HostPlatform.MacOS)]
    [InlineData(HostPlatform.Linux)]
    public void Check_for_updates_is_in_the_help_menu(HostPlatform platform)
    {
        var main = new MainViewModel(new FakeUiServices(), new FakePlatformShell(), platform);
        MenuEntry help = main.Menus.TopLevel.Single(menu => menu.Header == Strings.Menu_Help);

        Assert.Contains(help.Children!, entry => entry.Command == main.Commands[CommandId.CheckForUpdate].Command);
    }

    [AvaloniaFact]
    public void The_update_button_renders_in_both_themes()
    {
        var frames = new List<string>();
        foreach (ThemeVariant theme in Render.BothThemes)
        {
            var s = new Session();
            s.Main.UpdateSource = () => Task.FromResult(UpdateAnswer.NewerRelease(new Version(1, 0, 1)));
            s.Main.CheckForUpdateAtStartAsync().GetAwaiter().GetResult();
            Session.Pump();
            Assert.True(s.Window.FindControl<Button>("UpdateNotice")!.IsVisible);
            frames.Add(Render.Save(s.Window, theme, "update-notice"));
        }

        Assert.NotEqual(File.ReadAllBytes(frames[0]), File.ReadAllBytes(frames[1]));
    }
}
