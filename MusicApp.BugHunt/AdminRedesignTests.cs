using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Redesigned admin screen: chip-tab sections (Огляд / Товари / Замовлення /
// Користувачі), product search + state filter, order auto-saved statuses,
// dirty-tracked role saves with a lockout guard. Screenshots land in
// bug-hunt/artifacts/.
public class AdminRedesignTests
{
    private static (Harness h, AdminViewModel admin) OpenAdmin()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.SetWindowSize(1400, 900);
        h.Nav!.NavigateTo(NavTarget.Admin);
        Dispatcher.UIThread.RunJobs();
        var admin = (AdminViewModel)h.Nav!.CurrentView!;
        return (h, admin);
    }

    [AvaloniaFact]
    public void Sections_switch_and_render()
    {
        var (h, admin) = OpenAdmin();

        Assert.True(admin.IsSectionOverview);
        h.RunStep("admin-rd-01-overview", () => { });

        h.RunStep("admin-rd-02-products",
            () => admin.SelectSectionCommand.Execute("Products"));
        Assert.True(admin.IsSectionProducts);

        h.RunStep("admin-rd-03-orders",
            () => admin.SelectSectionCommand.Execute("Orders"));
        Assert.True(admin.IsSectionOrders);

        h.RunStep("admin-rd-04-users",
            () => admin.SelectSectionCommand.Execute("Users"));
        Assert.True(admin.IsSectionUsers);
    }

    [AvaloniaFact]
    public void Product_search_filters_list()
    {
        var (h, admin) = OpenAdmin();
        admin.SelectSectionCommand.Execute("Products");
        Dispatcher.UIThread.RunJobs();

        var total = admin.Products.Count;
        Assert.True(total > 0);

        var firstTitle = admin.Products[0].Product.Album!.Title;
        admin.ProductSearch = firstTitle;
        Dispatcher.UIThread.RunJobs();
        h.RunStep("admin-rd-05-product-search", () => { });

        Assert.True(admin.Products.Count >= 1);
        Assert.True(admin.Products.Count <= total);
        Assert.All(admin.Products, r => Assert.True(
            r.Product.Album!.Title.Contains(firstTitle, System.StringComparison.OrdinalIgnoreCase)
            || (r.Product.Album!.Artist?.Name.Contains(firstTitle, System.StringComparison.OrdinalIgnoreCase) ?? false)
            || (r.Product.Label?.Contains(firstTitle, System.StringComparison.OrdinalIgnoreCase) ?? false)));

        // No matches → empty-state flag flips; the reset link restores everything.
        admin.ProductSearch = "zzz-немає-такого";
        admin.FilterProductsCommand.Execute("Inactive");
        Dispatcher.UIThread.RunJobs();
        Assert.True(admin.HasNoProducts);
        Assert.Empty(admin.Products);
        h.RunStep("admin-rd-05b-products-empty-state", () => { });

        admin.ResetProductFiltersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(admin.IsProductFilterAll);
        Assert.Equal(total, admin.Products.Count);
    }

    [AvaloniaFact]
    public void Product_deactivation_is_two_step_and_reversible()
    {
        var (h, admin) = OpenAdmin();
        admin.SelectSectionCommand.Execute("Products");
        Dispatcher.UIThread.RunJobs();

        var row = admin.Products.First(r => r.Product.IsActive);
        var id = row.Product.Id;

        // Step 1 arms the confirmation; nothing is written yet.
        admin.RequestDeactivateProductCommand.Execute(row);
        Dispatcher.UIThread.RunJobs();
        Assert.True(row.IsConfirmingDeactivation);
        Assert.True(h.Catalog!.Products.First(p => p.Id == id).IsActive);
        h.RunStep("admin-rd-06-deactivate-confirm", () => { });

        // Cancelling leaves the product untouched.
        admin.CancelDeactivateProductCommand.Execute(row);
        Assert.False(row.IsConfirmingDeactivation);
        Assert.True(h.Catalog!.Products.First(p => p.Id == id).IsActive);

        // Confirming persists, the row stays listed (marked inactive).
        admin.RequestDeactivateProductCommand.Execute(row);
        admin.ConfirmDeactivateProductCommand.Execute(row);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Catalog!.Products.First(p => p.Id == id).IsActive);
        Assert.Contains(admin.Products, r => r.Product.Id == id);
        h.RunStep("admin-rd-07-deactivated-row", () => { });

        // The "Неактивні" filter finds it; activation is the one-click undo.
        admin.FilterProductsCommand.Execute("Inactive");
        Dispatcher.UIThread.RunJobs();
        var inactiveRow = admin.Products.First(r => r.Product.Id == id);
        h.RunStep("admin-rd-08-inactive-filter", () => { });

        admin.ActivateProductCommand.Execute(inactiveRow);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Catalog!.Products.First(p => p.Id == id).IsActive);

        admin.FilterProductsCommand.Execute("All");
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Order_status_saves_immediately_and_filters_work()
    {
        var (h, admin) = OpenAdmin();
        admin.SelectSectionCommand.Execute("Orders");
        Dispatcher.UIThread.RunJobs();

        Assert.True(admin.OrderRows.Count > 0);

        // Inline details expand independently — two orders can be compared.
        var row = admin.OrderRows[0];
        admin.ToggleOrderDetailsCommand.Execute(row);
        Dispatcher.UIThread.RunJobs();
        Assert.True(row.IsExpanded);
        h.RunStep("admin-rd-09-order-details", () => { });

        if (admin.OrderRows.Count > 1)
        {
            var second = admin.OrderRows[1];
            admin.ToggleOrderDetailsCommand.Execute(second);
            Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsExpanded);
            Assert.True(second.IsExpanded);
            h.RunStep("admin-rd-09b-two-orders-expanded", () => { });
            admin.ToggleOrderDetailsCommand.Execute(second);
            Assert.False(second.IsExpanded);
            admin.ToggleOrderDetailsCommand.Execute(row);
            Assert.False(row.IsExpanded);
        }

        // Picking a status in the ComboBox persists immediately — no unsaved
        // limbo state, the toast confirms the write.
        var target = row.Order.Status == OrderStatus.Completed
            ? OrderStatus.Processing
            : OrderStatus.Completed;
        row.SelectedStatus = target;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(target, row.Order.Status);
        Assert.Equal(target, h.Catalog!.Orders.First(o => o.Id == row.Order.Id).Status);
        Assert.Contains("статус змінено", admin.StatusMessage);
        h.RunStep("admin-rd-10-order-status-autosaved", () => { });

        // Status filter: every visible row matches; the saved row shows up
        // under its new status.
        admin.FilterOrdersCommand.Execute(target.ToString());
        Dispatcher.UIThread.RunJobs();
        Assert.All(admin.OrderRows, r => Assert.Equal(target, r.Order.Status));
        Assert.Contains(admin.OrderRows, r => r.Order.Id == row.Order.Id);

        // Search narrows by order number or buyer.
        admin.FilterOrdersCommand.Execute("All");
        admin.OrderSearch = row.Order.Id.ToString();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(admin.OrderRows, r => r.Order.Id == row.Order.Id);
        h.RunStep("admin-rd-11-order-search", () => { });

        admin.OrderSearch = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.True(admin.OrderRows.Count >= 1);
    }

    [AvaloniaFact]
    public void User_role_save_is_dirty_tracked_with_revert()
    {
        var (h, admin) = OpenAdmin();
        admin.SelectSectionCommand.Execute("Users");
        Dispatcher.UIThread.RunJobs();

        // Guest is not offered for registered accounts.
        Assert.DoesNotContain(UserRole.Guest, admin.UserRoles);

        // Pick a non-admin user so we don't touch the logged-in admin.
        var row = admin.Users.First(u => u.User.Role == UserRole.Customer);

        Assert.False(row.HasUnsavedRole);
        row.SelectedRole = UserRole.Admin;
        Assert.True(row.HasUnsavedRole);
        h.RunStep("admin-rd-12-user-dirty-role", () => { });

        // Скасувати reverts without writing.
        admin.RevertUserRoleCommand.Execute(row);
        Assert.False(row.HasUnsavedRole);
        Assert.Equal(UserRole.Customer, h.Catalog!.GetUsers().First(u => u.Id == row.User.Id).Role);

        // Зберегти persists.
        row.SelectedRole = UserRole.Admin;
        admin.ChangeUserRoleCommand.Execute(row);
        Dispatcher.UIThread.RunJobs();
        Assert.False(row.HasUnsavedRole);
        Assert.Equal(UserRole.Admin, h.Catalog!.GetUsers().First(u => u.Id == row.User.Id).Role);

        // Put it back for other tests sharing the process-level DB.
        row.SelectedRole = UserRole.Customer;
        admin.ChangeUserRoleCommand.Execute(row);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Admin_cannot_demote_own_account()
    {
        var (h, admin) = OpenAdmin();
        admin.SelectSectionCommand.Execute("Users");
        Dispatcher.UIThread.RunJobs();

        var self = admin.Users.First(u => u.User.Username == "admin");
        self.SelectedRole = UserRole.Customer;
        admin.ChangeUserRoleCommand.Execute(self);
        Dispatcher.UIThread.RunJobs();

        // Write rejected, selection reverted, error toast shown.
        Assert.Equal(UserRole.Admin, h.Catalog!.GetUsers().First(u => u.Id == self.User.Id).Role);
        Assert.Equal(UserRole.Admin, self.SelectedRole);
        Assert.True(admin.IsStatusError);
        Assert.False(string.IsNullOrEmpty(admin.StatusMessage));
        h.RunStep("admin-rd-13-self-demotion-blocked", () => { });
    }

    [AvaloniaFact]
    public void Kpi_cards_navigate_to_sections()
    {
        var (h, admin) = OpenAdmin();

        admin.OpenKpiCommand.Execute("NewOrders");
        Assert.True(admin.IsSectionOrders);
        Assert.True(admin.IsOrderFilterNew);
        h.RunStep("admin-rd-17-kpi-new-orders", () => { });

        admin.OpenKpiCommand.Execute("Revenue");
        Assert.True(admin.IsOrderFilterCompleted);

        admin.OpenKpiCommand.Execute("Products");
        Assert.True(admin.IsSectionProducts);

        admin.OpenKpiCommand.Execute("Orders");
        Assert.True(admin.IsSectionOrders);
        Assert.True(admin.IsOrderFilterAll);
    }

    [AvaloniaFact]
    public void Periods_are_calendar_based_and_range_validated()
    {
        var (h, admin) = OpenAdmin();
        var today = System.DateTime.UtcNow.Date;

        // «Цей місяць» starts on the 1st, «Цей тиждень» on Monday — calendar
        // semantics, not rolling N-day windows.
        admin.SelectPeriodCommand.Execute("Month");
        Assert.Equal(new System.DateTime(today.Year, today.Month, 1), admin.RevenueFrom);
        Assert.Equal(today, admin.RevenueTo);

        admin.SelectPeriodCommand.Execute("Week");
        Assert.Equal(System.DayOfWeek.Monday, admin.RevenueFrom.DayOfWeek);

        // Hand-editing a date lands on «Власний» and an inverted range warns.
        Assert.False(admin.IsPeriodRangeInvalid);
        admin.RevenueFrom = today.AddDays(5);
        Dispatcher.UIThread.RunJobs();
        Assert.True(admin.IsPeriodCustom);
        Assert.True(admin.IsPeriodRangeInvalid);
        h.RunStep("admin-rd-18-period-range-invalid", () => { });
    }

    [AvaloniaFact]
    public void Product_editor_renders_and_guards_unsaved_changes()
    {
        var (h, admin) = OpenAdmin();
        admin.SelectSectionCommand.Execute("Products");
        Dispatcher.UIThread.RunJobs();

        admin.AddProductCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(admin.IsEditingProduct);
        h.RunStep("admin-rd-14-editor-add", () => { });

        // Pristine form closes without ceremony.
        admin.EditingProduct!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(admin.IsEditingProduct);

        // Edit mode identifies the product in the header.
        var row = admin.Products[0];
        admin.EditProductCommand.Execute(row);
        Dispatcher.UIThread.RunJobs();
        var editor = admin.EditingProduct!;
        Assert.True(editor.IsEditMode);
        Assert.Contains(row.Product.Album!.Title, editor.Subtitle);
        h.RunStep("admin-rd-15-editor-edit", () => { });

        // A dirty form intercepts Cancel with an inline confirmation.
        editor.Price += 10m;
        editor.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsConfirmingClose);
        Assert.True(admin.IsEditingProduct);
        h.RunStep("admin-rd-16-editor-discard-guard", () => { });

        // "Залишитись" keeps editing; explicit discard closes.
        editor.StayEditingCommand.Execute(null);
        Assert.False(editor.IsConfirmingClose);
        editor.DiscardAndCloseCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(admin.IsEditingProduct);
    }
}
