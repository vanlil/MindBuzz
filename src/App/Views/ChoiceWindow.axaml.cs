using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using MindBuzz.App.Services;

namespace MindBuzz.App.Views;

/// <summary>
/// A question with a few answers. Returns the index of the button pressed; Esc and the window's close button count
/// as the request's cancel answer. The default answer is the accent button and is pressed by Enter.
/// </summary>
public partial class ChoiceWindow : Window
{
    private readonly int _cancelAnswer;
    private bool _answered;

    /// <summary>The designer's constructor.</summary>
    public ChoiceWindow()
    {
        InitializeComponent();
    }

    public ChoiceWindow(ChoiceRequest request)
        : this()
    {
        ArgumentNullException.ThrowIfNull(request);
        DataContext = request;
        _cancelAnswer = request.CancelAnswer;

        for (int i = 0; i < request.Answers.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = request.Answers[i],
                IsDefault = index == request.DefaultAnswer,
                IsCancel = index == request.CancelAnswer,
                MinWidth = 88,
            };

            if (index == request.DefaultAnswer)
            {
                button.Classes.Add("Primary");
                if (this.TryFindResource("SolidButton", out object? theme) && theme is ControlTheme solid)
                {
                    button.Theme = solid;
                }
            }

            button.Click += (_, _) => Answer(index);
            Buttons.Children.Add(button);
        }

        Opened += (_, _) => (Buttons.Children[request.DefaultAnswer] as Button)?.Focus();
    }

    /// <summary>
    /// Closing without a button (the window's close button) answers with the cancel answer. Without this, the
    /// dialog would return 0, which may well be "Save".
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_answered)
        {
            e.Cancel = true;
            Answer(_cancelAnswer);
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>Esc when no button is the cancel button (a cancel answer of its own): the same as closing.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape && !e.Handled && _cancelAnswer >= Buttons.Children.Count)
        {
            Answer(_cancelAnswer);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void Answer(int index)
    {
        _answered = true;
        Close(index);
    }
}
