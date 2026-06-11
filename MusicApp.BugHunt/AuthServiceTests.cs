using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;
using MusicApp.Services;
using Xunit;

namespace MusicApp.BugHunt;

public class AuthServiceTests : IDisposable
{
    private readonly string _dbDir;
    private readonly MusicStoreDbContextFactory _factory;
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        // own subdirectory → the session.json next to the DB is isolated per test class
        _dbDir = Path.Combine(Path.GetTempPath(), $"auth-tests-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", Path.Combine(_dbDir, "store.db"));
        _factory = new MusicStoreDbContextFactory();
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        _auth = new AuthService(_factory);
    }

    // --- registration ---

    [Fact]
    public void Register_creates_customer_with_bcrypt_hash()
    {
        Assert.True(_auth.TryRegister("alice", "pa55word", "alice@test"));
        Assert.True(_auth.IsAuthenticated);
        Assert.Equal(UserRole.Customer, _auth.CurrentUser!.Role);

        using var db = _factory.CreateDbContext();
        var user = Assert.Single(db.Users.ToList());
        Assert.NotEqual("pa55word", user.PasswordHash); // never plaintext
        Assert.True(BCrypt.Net.BCrypt.Verify("pa55word", user.PasswordHash));
    }

    [Fact]
    public void Register_rejects_duplicate_username()
    {
        Assert.True(_auth.TryRegister("alice", "one", "alice@test"));
        Assert.False(_auth.TryRegister("alice", "two", "other@test"));
        using var db = _factory.CreateDbContext();
        Assert.Single(db.Users.ToList());
    }

    [Fact]
    public void Register_rejects_duplicate_email()
    {
        Assert.True(_auth.TryRegister("alice", "one", "same@test"));
        Assert.False(_auth.TryRegister("bob", "two", "same@test"));
    }

    [Fact]
    public void Register_trims_username_and_rejects_blank_input()
    {
        Assert.False(_auth.TryRegister("", "x", "a@test"));
        Assert.False(_auth.TryRegister("name", " ", "a@test"));
        Assert.True(_auth.TryRegister("  carol  ", "pw", "c@test"));
        Assert.Equal("carol", _auth.CurrentUser!.Username);
    }

    // --- login ---

    [Fact]
    public void Login_succeeds_with_correct_password_only()
    {
        Assert.True(_auth.TryRegister("dave", "right", "d@test"));
        _auth.Logout();

        Assert.False(_auth.TryLogin("dave", "wrong"));
        Assert.Null(_auth.CurrentUser);
        Assert.False(_auth.TryLogin("nobody", "right"));
        Assert.False(_auth.TryLogin("dave", ""));

        Assert.True(_auth.TryLogin("dave", "right"));
        Assert.True(_auth.IsAuthenticated);
        Assert.Equal("dave", _auth.CurrentUser!.Username);
    }

    [Fact]
    public void Admin_role_is_reflected_by_IsAdmin()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Username = "root",
                Email = "root@test",
                Role = UserRole.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("toor")
            });
            db.SaveChanges();
        }
        Assert.True(_auth.TryLogin("root", "toor"));
        Assert.True(_auth.IsAdmin);
    }

    // --- guest & logout ---

    [Fact]
    public void Guest_is_not_authenticated_and_not_admin()
    {
        _auth.LoginAsGuest();
        Assert.NotNull(_auth.CurrentUser);
        Assert.False(_auth.IsAuthenticated);
        Assert.False(_auth.IsAdmin);
    }

    [Fact]
    public void Logout_clears_current_user()
    {
        Assert.True(_auth.TryRegister("eve", "pw", "e@test"));
        _auth.Logout();
        Assert.Null(_auth.CurrentUser);
        Assert.False(_auth.IsAuthenticated);
    }

    [Fact]
    public void CurrentUserChanged_fires_on_login_and_logout()
    {
        int fired = 0;
        _auth.CurrentUserChanged += (_, _) => fired++;
        _auth.TryRegister("frank", "pw", "f@test"); // 1
        _auth.Logout();                             // 2
        _auth.TryLogin("frank", "pw");              // 3
        _auth.LoginAsGuest();                       // 4
        Assert.Equal(4, fired);
    }

    // --- change password ---

    [Fact]
    public void ChangePassword_requires_correct_old_password()
    {
        Assert.True(_auth.TryRegister("grace", "old", "g@test"));
        Assert.False(_auth.TryChangePassword("nope", "new"));
        Assert.False(_auth.TryChangePassword("old", " "));
        Assert.True(_auth.TryChangePassword("old", "new"));

        _auth.Logout();
        Assert.False(_auth.TryLogin("grace", "old"));
        Assert.True(_auth.TryLogin("grace", "new"));
    }

    [Fact]
    public void Guest_cannot_change_password()
    {
        _auth.LoginAsGuest();
        Assert.False(_auth.TryChangePassword("x", "y"));
    }

    // --- remember-me session ---

    [Fact]
    public void Session_restores_after_login_with_remember_me()
    {
        Assert.True(_auth.TryRegister("henry", "pw", "h@test", rememberMe: true));

        var fresh = new AuthService(_factory); // "next launch"
        Assert.True(fresh.TryRestoreSession());
        Assert.Equal("henry", fresh.CurrentUser!.Username);
    }

    [Fact]
    public void Session_is_not_saved_without_remember_me()
    {
        Assert.True(_auth.TryRegister("iris", "pw", "i@test", rememberMe: false));

        var fresh = new AuthService(_factory);
        Assert.False(fresh.TryRestoreSession());
        Assert.Null(fresh.CurrentUser);
    }

    [Fact]
    public void Logout_clears_remembered_session()
    {
        Assert.True(_auth.TryRegister("judy", "pw", "j@test", rememberMe: true));
        _auth.Logout();

        var fresh = new AuthService(_factory);
        Assert.False(fresh.TryRestoreSession());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", null);
        try { Directory.Delete(_dbDir, recursive: true); } catch { }
    }
}
