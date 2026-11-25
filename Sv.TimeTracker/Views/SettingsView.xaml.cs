using System.Windows.Controls;
using System.Windows.Input;
using Sv.TimeTracker.Services;

namespace Sv.TimeTracker.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void ShortcutTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        // Allow Tab navigation out of the box
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            return;
        }

        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HotkeyParser.IsModifierKey(actualKey))
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            return;
        }

        textBox.Text = HotkeyParser.Format(modifiers, actualKey);
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
