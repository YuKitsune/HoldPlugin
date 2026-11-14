using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HoldPlugin.ViewModels;
using vatSysToolbox.Windows;

namespace HoldPlugin.Controls;

public partial class DelayPopup : UserControl
{
    public DelayPopup(DelayPopupViewModel viewModel, IWindowHandle windowHandle)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The text box can get the keyboard focus only after the popup shows
        windowHandle.Shown += (_, _) => DelayDurationTextBox.Focus();
    }

    // Select the current delay, so that the user can type over it.
    void DelayDurationTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        DelayDurationTextBox.SelectAll();
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

    // The space key does not cause a PreviewTextInput event.
    void DelayDurationTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    // char.IsDigit also accepts digits that are not ASCII.
    static bool IsDigitsOnly(string text) => text.All(c => c is >= '0' and <= '9');
}
