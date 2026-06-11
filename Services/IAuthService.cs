using System;
using MusicApp.Models;

namespace MusicApp.Services;

public interface IAuthService
{
    event EventHandler? CurrentUserChanged;

    User? CurrentUser { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }

    bool TryLogin(string username, string password, bool rememberMe = false);
    bool TryRegister(string username, string password, string email, bool rememberMe = false);
    bool TryChangePassword(string oldPassword, string newPassword);

    /// <summary>
    /// Restores a "remember me" session saved on a previous run, re-loading the
    /// user from the local DB. Returns true if a session was restored.
    /// </summary>
    bool TryRestoreSession();
    void LoginAsGuest();
    void Logout();
}
