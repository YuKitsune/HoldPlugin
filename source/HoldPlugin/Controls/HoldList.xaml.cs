using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HoldPlugin.ViewModels;

namespace HoldPlugin.Controls;

public partial class HoldList : UserControl
{
    public HoldList(HoldListViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.OnViewLoaded();
    }

    void Designator_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.DesignateAircraftCommand.Execute(null);
        e.Handled = true;
    }

    void Acid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        if (sender is not FrameworkElement { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.CancelHoldCommand.Execute(null);
        e.Handled = true;
    }

    void ClearedLevel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.OpenClearedLevelMenuCommand.Execute(null);
        e.Handled = true;
    }

    void HoldExit_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.OpenHoldExitMenuCommand.Execute(null);
        e.Handled = true;
    }

    void HoldExit_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
            return;

        if (sender is not FrameworkElement { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.ClearHoldExitTimeCommand.Execute(null);
        e.Handled = true;
    }

    void GlobalOps_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.EditGlobalOpsCommand.Execute(null);
        e.Handled = true;
    }

    void GlobalOpsTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.CommitGlobalOpsEditCommand.Execute(null);
    }

    void GlobalOpsTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox textBox || e.NewValue is not bool isVisible || !isVisible)
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    void GlobalOpsTextBox_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not TextBox { DataContext: HoldItemViewModel viewModel })
            return;

        viewModel.CancelGlobalOpsEditCommand.Execute(null);
    }
}