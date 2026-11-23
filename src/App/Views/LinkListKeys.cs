using Avalonia.Controls;
using Avalonia.Input;
using MindBuzz.App.ViewModels;

namespace MindBuzz.App.Views;

/// <summary>
/// The link list's keys, in the detail view's links section: Enter opens, F2 edits, Delete
/// removes, Alt+Up/Down reorders. Arrows and Esc keep their usual meaning.
/// </summary>
internal static class LinkListKeys
{
    internal static void Handle(LinkListViewModel model, KeyEventArgs e)
    {
        // Buttons keep their own Enter and Space.
        if (e.Source is Button)
        {
            return;
        }

        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.Enter when !alt:
                model.OpenCommand.Execute(null);
                break;
            case Key.F2:
                model.EditCommand.Execute(null);
                break;
            case Key.Delete or Key.Back:
                model.RemoveCommand.Execute(null);
                break;
            case Key.Up when alt:
                model.MoveUpCommand.Execute(null);
                break;
            case Key.Down when alt:
                model.MoveDownCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
