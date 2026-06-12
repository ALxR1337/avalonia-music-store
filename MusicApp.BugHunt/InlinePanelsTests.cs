using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.ViewModels;
using MusicApp.Views;
using Xunit;

namespace MusicApp.BugHunt;

// The change-password and product-edit flows used to open their own OS windows.
// They are now inline panels inside the main window. These tests assert the
// trigger reveals the panel in-place and that no second Window is spawned.
public class InlinePanelsTests
{
    private static (Harness h, MainWindowViewModel shell) Open(string? login = "admin")
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: login, password: login);
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        return (h, shell);
    }

    // === Change password ===

    // The title-bar menu "Змінити пароль" navigates to Profile and opens the
    // inline panel there — instead of a ChangePasswordWindow dialog.
    [AvaloniaFact]
    public void Menu_change_password_opens_inline_panel_on_profile()
    {
        var (h, shell) = Open();

        shell.ChangePasswordCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NavTarget.Profile, shell.CurrentTarget);
        Assert.False(shell.IsUserMenuOpen);
        var profile = Assert.IsType<ProfileViewModel>(shell.CurrentView);
        Assert.True(profile.IsPasswordPanelOpen);

        // The form renders inline inside the main window — not in a dialog.
        Assert.NotEmpty(h.Window!.GetVisualDescendants().OfType<ChangePasswordView>());

        h.Snapshot("change-password-inline");
    }

    // Mismatched confirmation surfaces an error and keeps the panel open,
    // without touching the stored password.
    [AvaloniaFact]
    public void Password_mismatch_shows_error_and_keeps_panel_open()
    {
        var (_, shell) = Open();
        shell.ChangePasswordCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var profile = (ProfileViewModel)shell.CurrentView!;

        profile.PasswordForm.OldPassword = "admin";
        profile.PasswordForm.NewPassword = "abcd";
        profile.PasswordForm.ConfirmPassword = "different";
        profile.PasswordForm.SubmitCommand.Execute(null);

        Assert.False(string.IsNullOrEmpty(profile.PasswordForm.Error));
        Assert.True(profile.IsPasswordPanelOpen);
    }

    // A real change on a throwaway user (so the shared admin/admin login other
    // tests rely on is never mutated) closes the panel and actually rotates the
    // password in the store.
    [AvaloniaFact]
    public void Successful_change_closes_panel_and_rotates_password()
    {
        var (h, _) = Open(login: null); // guest shell
        Assert.True(h.Auth!.TryRegister("pwrotate", "oldpass", "pwrotate@x.io"));

        var profile = new ProfileViewModel(h.Auth!, h.Catalog!);
        profile.OpenPasswordPanel();
        Assert.True(profile.IsPasswordPanelOpen);

        profile.PasswordForm.OldPassword = "oldpass";
        profile.PasswordForm.NewPassword = "newpass1";
        profile.PasswordForm.ConfirmPassword = "newpass1";
        profile.PasswordForm.SubmitCommand.Execute(null);

        Assert.False(profile.IsPasswordPanelOpen);
        Assert.Equal("Пароль змінено.", profile.StatusMessage);

        var fresh = new AuthService(new MusicStoreDbContextFactory());
        Assert.True(fresh.TryLogin("pwrotate", "newpass1"));
        Assert.False(fresh.TryLogin("pwrotate", "oldpass"));
    }

    // === Product edit ===

    // Add/Edit reveal the inline editor in the Admin screen (no ProductEditWindow).
    [AvaloniaFact]
    public void Add_product_opens_inline_editor_in_admin()
    {
        var (h, shell) = Open();
        shell.NavigateCommand.Execute("Admin");
        Dispatcher.UIThread.RunJobs();
        var admin = (AdminViewModel)shell.CurrentView!;

        admin.AddProductCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(admin.IsEditingProduct);
        Assert.NotNull(admin.EditingProduct);
        Assert.True(admin.EditingProduct!.IsAddMode);
        // The inline editor view is rendered inside the main window.
        Assert.NotEmpty(h.Window!.GetVisualDescendants().OfType<ProductEditView>());

        h.Snapshot("product-edit-inline");

        admin.EditingProduct!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(admin.IsEditingProduct);
    }

    // Editing an existing product loads its values into the inline editor.
    [AvaloniaFact]
    public void Edit_product_loads_values_into_inline_editor()
    {
        var (h, shell) = Open();
        shell.NavigateCommand.Execute("Admin");
        Dispatcher.UIThread.RunJobs();
        var admin = (AdminViewModel)shell.CurrentView!;
        var product = admin.Products.First();

        admin.EditProductCommand.Execute(product);
        Dispatcher.UIThread.RunJobs();

        Assert.True(admin.IsEditingProduct);
        Assert.True(admin.EditingProduct!.IsEditMode);
        Assert.Equal(product.Product.Price, admin.EditingProduct!.Price);

        admin.EditingProduct!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(admin.IsEditingProduct);
    }
}
