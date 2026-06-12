using Avalonia.Controls;
using Avalonia.Input;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class ChangePasswordView : UserControl
{
    public ChangePasswordView() => InitializeComponent();

    /// <summary>Called by ProfileView when the inline panel opens.</summary>
    public void FocusFirstField() => OldBox.Focus();

    // Esc backs out of the panel from any field.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ChangePasswordViewModel vm)
        {
            e.Handled = true;
            vm.CancelCommand.Execute(null);
            return;
        }
        base.OnKeyDown(e);
    }

    // Enter walks the fields then submits — same no-Tab flow as the login card.
    private void OnOldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        NewBox.Focus();
    }

    private void OnNewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ConfirmBox.Focus();
    }

    private void OnConfirmKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (DataContext is ChangePasswordViewModel vm)
            vm.SubmitCommand.Execute(null);
    }
}
