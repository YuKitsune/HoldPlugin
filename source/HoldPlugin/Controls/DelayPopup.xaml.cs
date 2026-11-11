using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HoldPlugin.ViewModels;

namespace HoldPlugin.Controls;

public partial class DelayPopup : UserControl
{
    readonly DelayPopupViewModel _viewModel;

    // Occurs when the user presses the enter key or the escape key.
    public event EventHandler? CloseRequested;

    public DelayPopup(DelayPopupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
    }

    public void FocusDelayDuration()
    {
        DelayDurationTextBox.Focus();
        Keyboard.Focus(DelayDurationTextBox);
    }

    void DelayDurationTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigitsOnly(e.Text);
    }

    void DelayDurationTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(typeof(string)) is not string text || !IsDigitsOnly(text))
            e.CancelCommand();
    }

    void DelayDurationTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // The space key does not cause a PreviewTextInput event.
            case Key.Space:
                e.Handled = true;
                break;

            case Key.Enter:
                e.Handled = true;
                _viewModel.SubmitCommand.Execute(null);
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;

            case Key.Escape:
                e.Handled = true;
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    // char.IsDigit also accepts digits that are not ASCII.
    static bool IsDigitsOnly(string text) => text.All(c => c is >= '0' and <= '9');
}
