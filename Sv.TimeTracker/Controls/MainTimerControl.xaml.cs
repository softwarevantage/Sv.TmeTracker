using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sv.TimeTracker.ViewModels;

namespace Sv.TimeTracker.Controls;

public partial class MainTimerControl : UserControl
{
    public MainTimerControl()
    {
        InitializeComponent();
    }

    private void QuickAddTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TimerViewModel vm)
        {
            _ = vm.QuickAddWorkItemCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }

    private void SessionNotes_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is TimerViewModel vm)
        {
            _ = vm.SaveRunningNotesCommand.ExecuteAsync(null);
        }
    }
}
