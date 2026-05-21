using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _auth;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private bool _isRegistering;
    [ObservableProperty] private string? _error;

    public event Action? RequestClose;

    public LoginViewModel(IAuthService auth) => _auth = auth;

    [RelayCommand]
    private void Login()
    {
        Error = null;
        if (IsRegistering)
        {
            if (!_auth.TryRegister(Username, Password, Email))
            {
                Error = "Не вдалося зареєструватися. Перевірте поля.";
                return;
            }
        }
        else
        {
            if (!_auth.TryLogin(Username, Password))
            {
                Error = "Невірний логін або пароль.";
                return;
            }
        }
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void ToggleMode() => IsRegistering = !IsRegistering;

    [RelayCommand]
    private void Guest()
    {
        _auth.LoginAsGuest();
        RequestClose?.Invoke();
    }
}
