using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.ViewModels;
using MusicApp.Views;
using Xunit;

namespace MusicApp.BugHunt;

// Coverage for the login redesign: it is now an in-app centered overlay card
// (not a separate window), with a "remember me" toggle and Enter-key flow
// (username → password → submit) so the user never reaches for Tab.
public class LoginOverlayTests
{
    private static (Harness h, MainWindowViewModel shell) OpenWithLogin()
    {
        var h = new Harness();
        h.OpenMainWindow(); // guest — no auto login
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        shell.ShowLogin();
        Dispatcher.UIThread.RunJobs();
        return (h, shell);
    }

    // The card renders inside the main window (no second OS window) and is
    // horizontally centred rather than pinned to the edge.
    [AvaloniaFact]
    public void Login_overlay_shows_a_centered_card_inside_the_main_window()
    {
        var (h, shell) = OpenWithLogin();

        Assert.True(shell.IsLoginVisible);
        var card = h.Find<LoginView>("LoginCard");
        Assert.True(card.IsEffectivelyVisible);
        Assert.InRange(card.Bounds.Width, 400, 460);
        Assert.True(card.Bounds.X > 50, "card should be centred, not pinned to the left edge");

        h.Snapshot("login-overlay");
    }

    // Enter on the username field advances focus to the password field.
    [AvaloniaFact]
    public void Enter_on_username_advances_to_password()
    {
        var (h, _) = OpenWithLogin();

        var username = h.Find<TextBox>("UsernameBox");
        var password = h.Find<TextBox>("PasswordBox");
        username.Focus();
        Dispatcher.UIThread.RunJobs();

        h.Window!.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        Dispatcher.UIThread.RunJobs();

        Assert.True(password.IsFocused);
    }

    // Enter on the password field submits the form and, with remember-me on,
    // logs in and dismisses the overlay.
    [AvaloniaFact]
    public void Enter_on_password_logs_in_and_closes_overlay()
    {
        var (h, shell) = OpenWithLogin();

        h.Type("UsernameBox", "admin");
        h.Type("PasswordBox", "admin");
        var password = h.Find<TextBox>("PasswordBox");
        password.Focus();
        Dispatcher.UIThread.RunJobs();

        h.Window!.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        Dispatcher.UIThread.RunJobs();

        Assert.True(h.Auth!.IsAuthenticated);
        Assert.False(shell.IsLoginVisible);
    }

    // "Remember me" persists the session so a fresh AuthService restores it on
    // the next launch (same isolated DB dir → same session file).
    [AvaloniaFact]
    public void Remember_me_persists_session_across_restart()
    {
        var (h, _) = OpenWithLogin();

        var login = ((MainWindowViewModel)h.Window!.DataContext!).Login;
        login.Username = "admin";
        login.Password = "admin";
        login.RememberMe = true;
        login.LoginCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(h.Auth!.IsAuthenticated);

        // Simulate a relaunch: a brand-new auth service over the same store.
        var restored = new AuthService(new MusicStoreDbContextFactory());
        Assert.True(restored.TryRestoreSession());
        Assert.Equal("admin", restored.CurrentUser!.Username);

        // And the opposite: logging out clears the remembered session.
        restored.Logout();
        Assert.False(new AuthService(new MusicStoreDbContextFactory()).TryRestoreSession());
    }
}
