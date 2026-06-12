using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Regression coverage for the title-bar account menu (the "Покупець ▼" chip).
//
// The first cut hung the menu off a Button.Flyout. A Flyout hosts its content
// in a detached tree that does NOT inherit the window DataContext, so every
// row's Command="{Binding …}" resolved to null and clicking did nothing. The
// menu now lives in an inline <Popup> (same pattern as the search autocomplete),
// whose content inherits the VM — so the row Commands bind and fire.
public class UserMenuTests
{
    private static (Harness h, MainWindowViewModel shell) OpenAdminMenu()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        shell.IsUserMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        return (h, shell);
    }

    // Scoped to the popup content so we don't accidentally grab the sidebar
    // "Профіль" button (which shares the label).
    private static Button MenuRow(Harness h, string label)
    {
        var content = h.Window!.GetLogicalDescendants().OfType<Popup>()
            .Select(p => p.Child as Border)
            .First(b => b is not null && b.Classes.Contains("app-menu"))!;
        return content.GetLogicalDescendants().OfType<Button>()
            .First(btn => btn.GetLogicalDescendants().OfType<TextBlock>()
                .Any(t => t.Text == label));
    }

    // The core of the reported bug: rows must carry a bound Command, not null.
    [AvaloniaFact]
    public void Menu_rows_have_their_commands_bound()
    {
        var (h, shell) = OpenAdminMenu();

        Assert.Same(shell.NavigateCommand, MenuRow(h, "Профіль").Command);
        Assert.Same(shell.ChangePasswordCommand, MenuRow(h, "Змінити пароль").Command);
        Assert.Same(shell.LogoutCommand, MenuRow(h, "Вийти").Command);

        // …and the Navigate row must pass its target as the parameter.
        // («Мої замовлення» is gone: order history is the profile's first tab,
        // so the row duplicated «Профіль».)
        Assert.Equal("Profile", MenuRow(h, "Профіль").CommandParameter);
    }

    // Clicking "Профіль" navigates and closes the menu.
    [AvaloniaFact]
    public void Profile_row_navigates_and_closes_the_menu()
    {
        var (h, shell) = OpenAdminMenu();
        var row = MenuRow(h, "Профіль");

        row.Command!.Execute(row.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NavTarget.Profile, shell.CurrentTarget);
        Assert.False(shell.IsUserMenuOpen);
    }

    // Clicking "Вийти" drops the session and lands back on the catalog.
    [AvaloniaFact]
    public void Logout_row_signs_out_and_returns_to_catalog()
    {
        var (h, shell) = OpenAdminMenu();
        var row = MenuRow(h, "Вийти");

        row.Command!.Execute(row.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(h.Auth!.CurrentUser);
        Assert.True(shell.IsGuest);
        Assert.Equal(NavTarget.Catalog, shell.CurrentTarget);
        Assert.False(shell.IsUserMenuOpen);
    }

    // A guest sees the login/register rows, not the signed-in ones.
    [AvaloniaFact]
    public void Guest_sees_login_and_register_rows()
    {
        var h = new Harness();
        h.OpenMainWindow(); // no login → guest
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        shell.IsUserMenuOpen = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.IsGuest);
        Assert.Same(shell.OpenLoginCommand, MenuRow(h, "Увійти").Command);
        Assert.Same(shell.OpenRegisterCommand, MenuRow(h, "Реєстрація").Command);
    }
}
