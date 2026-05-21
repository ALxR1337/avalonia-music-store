using System;
using MusicApp.Models;

namespace MusicApp.Services;

public interface IAuthService
{
    event EventHandler? CurrentUserChanged;

    User? CurrentUser { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }

    bool TryLogin(string username, string password);
    bool TryRegister(string username, string password, string email);
    void LoginAsGuest();
    void Logout();
}
