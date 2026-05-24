using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;

namespace MusicApp.Services;

public class AuthService : IAuthService
{
    private readonly IDbContextFactory<MusicStoreDbContext> _dbFactory;

    public AuthService(IDbContextFactory<MusicStoreDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public event EventHandler? CurrentUserChanged;

    public User? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is { Role: not UserRole.Guest };
    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;

    public bool TryLogin(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        User? user;
        try
        {
            using var db = _dbFactory.CreateDbContext();
            user = db.Users.AsNoTracking()
                .FirstOrDefault(u => u.Username == username);
        }
        catch
        {
            return false;
        }

        if (user is null) return false;
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return false;

        CurrentUser = user;
        Raise();
        return true;
    }

    public bool TryRegister(string username, string password, string email)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;

        username = username.Trim();
        email = email?.Trim() ?? string.Empty;

        try
        {
            using var db = _dbFactory.CreateDbContext();

            if (db.Users.Any(u => u.Username == username))
                return false;
            if (!string.IsNullOrEmpty(email) && db.Users.Any(u => u.Email == email))
                return false;

            var user = new User
            {
                Username = username,
                Email = string.IsNullOrEmpty(email) ? null : email,
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
            };
            db.Users.Add(user);
            db.SaveChanges();
            CurrentUser = user;
        }
        catch
        {
            return false;
        }

        Raise();
        return true;
    }

    public bool TryChangePassword(string oldPassword, string newPassword)
    {
        if (CurrentUser is null || CurrentUser.Role == UserRole.Guest) return false;
        if (string.IsNullOrWhiteSpace(newPassword)) return false;

        try
        {
            using var db = _dbFactory.CreateDbContext();
            var user = db.Users.FirstOrDefault(u => u.Id == CurrentUser.Id);
            if (user is null) return false;
            if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash)) return false;

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            db.SaveChanges();
            CurrentUser.PasswordHash = user.PasswordHash;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void LoginAsGuest()
    {
        CurrentUser = new User { Id = 0, Username = "Гість", Role = UserRole.Guest };
        Raise();
    }

    public void Logout()
    {
        CurrentUser = null;
        Raise();
    }

    private void Raise() => CurrentUserChanged?.Invoke(this, EventArgs.Empty);
}
