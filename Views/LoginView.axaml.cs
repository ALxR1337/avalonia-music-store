using Avalonia.Controls;
using Avalonia.Input;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    // Enter advances to the next field; Enter on the password submits — so the
    // user never has to reach for Tab.
    private void OnUsernameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (DataContext is LoginViewModel { IsRegistering: true })
            EmailBox.Focus();
        else
            PasswordBox.Focus();
    }

    private void OnEmailKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        PasswordBox.Focus();
    }

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (DataContext is LoginViewModel vm)
            vm.LoginCommand.Execute(null);
    }
}
