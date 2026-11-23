using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MindBuzz.App.Commands;
using MindBuzz.App.Views;
using MindBuzz.Core.Serialization;
using Xunit;

namespace MindBuzz.App.Tests;

/// <summary>
/// Text typed into the detail view's title box or a tab's rename box is part of the document before Save, Close, New
/// and Open look at it: they commit it first, as they do the map's own editor.
/// </summary>
public sealed class PendingEditTests
{
    private static TextBox TitleBox(Session s) =>
        s.Window.FindControl<TopicPanel>("TopicPanelView")!.FindControl<TextBox>("TitleBox")!;

    private static void TypeInTitleBox(Session s, string text)
    {
        s.Main.Execute(CommandId.ToggleTopicPanel);
        Session.Pump();
        TextBox title = TitleBox(s);
        title.Focus();
        Session.Pump();
        title.Text = text;
        Session.Pump();
    }

    [AvaloniaFact]
    public void Closing_with_a_title_typed_in_the_detail_view_asks_to_save_it()
    {
        var ui = new FakeUiServices();
        var s = new Session(ui: ui);
        TypeInTitleBox(s, "Typed, not yet confirmed");

        s.Window.Close();
        Session.Pump();

        Assert.Equal("Typed, not yet confirmed", s.Central.Text);
        Assert.Single(ui.Questions);
    }

    [AvaloniaFact]
    public void Save_writes_a_title_typed_in_the_detail_view()
    {
        string path = Path.Join(Path.GetTempPath(), $"pending-{Guid.NewGuid():N}.mbz");
        try
        {
            var ui = new FakeUiServices();
            ui.PickedPaths.Enqueue(path);
            var s = new Session(ui: ui);
            TypeInTitleBox(s, "Saved title");

            s.Main.Execute(CommandId.Save);
            Session.Pump();

            Assert.Equal("Saved title", MbzFile.Load(path).Document!.Maps[0].Root.Text);
            Assert.False(s.Main.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void New_commits_a_tab_rename_in_progress_and_asks_to_save_it()
    {
        var ui = new FakeUiServices();
        var s = new Session(ui: ui);
        s.Main.Execute(CommandId.RenameMap);
        Session.Pump();
        TextBox rename = s.Window.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsVisible && box.IsFocused);
        rename.Text = "Renamed, not yet confirmed";

        s.Main.Execute(CommandId.New);
        Session.Pump();

        Assert.Single(ui.Questions);
        Assert.Equal("Renamed, not yet confirmed", s.Central.Text);
    }
}
