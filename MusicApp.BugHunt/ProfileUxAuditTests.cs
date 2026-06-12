using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;

namespace MusicApp.BugHunt;

/// <summary>
/// Screenshot + behaviour sweep for the Profile UX audit. Assertion-free on
/// purpose — artifacts land in bug-hunt/artifacts/, VM-level observations are
/// appended to profile-ux-audit.log in the same directory. No DB mutations:
/// the suite shares one seeded SQLite per process, so deletes/saves here would
/// leak into other tests.
/// </summary>
public class ProfileUxAuditTests
{
    private static readonly string LogPath =
        Path.Combine(Harness.ArtifactsDir, "profile-ux-audit.log");

    private static void Log(string line)
    {
        Directory.CreateDirectory(Harness.ArtifactsDir);
        File.AppendAllText(LogPath, line + "\n");
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static ProfileViewModel Pvm(Harness h) =>
        (ProfileViewModel)h.Nav!.CurrentView!;

    private static TabControl Tabs(Harness h) =>
        h.Window!.GetVisualDescendants().OfType<TabControl>()
            .First(t => t.DataContext is ProfileViewModel);

    private static void ScrollToEnd(Harness h)
    {
        var sv = h.Find<ScrollViewer>("ContentScroll");
        sv.ScrollToEnd();
        Pump();
    }

    [AvaloniaFact]
    public void Profile_ux_audit_demo_user()
    {
        Log($"==== demo sweep {DateTime.Now:O} ====");
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.SetWindowSize(1280, 800);

        // 01 — landing state: header + orders tab.
        h.RunStep("ux-prof-01-default", () => h.Nav!.NavigateTo(NavTarget.Profile));
        var vm = Pvm(h);
        Log($"01 counts: AllOrders={vm.AllOrders.Count} Orders={vm.Orders.Count} " +
            $"Reviews={vm.MyReviews.Count} Saved={vm.SavedSearches.Count} Wishlist={vm.WishlistItems.Count}");

        // 02 — expand details of the TOP order: where does the panel render
        //      relative to the row the user clicked?
        h.RunStep("ux-prof-02-details-top-order", () =>
            vm.ToggleOrderDetailsCommand.Execute(vm.Orders.First()));
        h.RunStep("ux-prof-03-details-scrolled", () => ScrollToEnd(h));

        // 04 — status filter set to Cancelled (demo has none): what does the
        //      list area look like, and what text does the ComboBox display?
        h.RunStep("ux-prof-04-filter-cancelled", () =>
        {
            vm.ToggleOrderDetailsCommand.Execute(vm.ExpandedOrder); // collapse
            vm.OrderStatusFilter = OrderStatus.Cancelled;
        });
        Log($"04 filter=Cancelled: visible Orders={vm.Orders.Count}, AllOrders={vm.AllOrders.Count}, " +
            $"combobox shows '{vm.SelectedStatusOption?.Label}'");
        vm.ClearOrderFilterCommand.Execute(null);
        Pump();
        Log($"04 after Скинути: combobox shows '{vm.SelectedStatusOption?.Label}'");

        // 05 — password panel; then re-invoke the header button while the user
        //      has half-typed input. Does the input survive?
        h.RunStep("ux-prof-05-password-open", () => vm.OpenPasswordPanelCommand.Execute(null));
        vm.PasswordForm.OldPassword = "demo";
        vm.PasswordForm.NewPassword = "half-typed";
        Pump();
        vm.OpenPasswordPanelCommand.Execute(null);
        Pump();
        Log($"05 reopen while typing: Old='{vm.PasswordForm.OldPassword}' New='{vm.PasswordForm.NewPassword}' " +
            "(empty strings = silent wipe of typed input)");
        h.RunStep("ux-prof-06-password-reopened", () => { });
        vm.PasswordForm.CancelCommand.Execute(null);
        Pump();

        // 07 — reviews tab; 08 — edit panel position relative to the item.
        h.RunStep("ux-prof-07-reviews", () => Tabs(h).SelectedIndex = 1);
        if (vm.MyReviews.Count > 0)
        {
            h.RunStep("ux-prof-08-review-edit", () =>
                vm.StartEditReviewCommand.Execute(vm.MyReviews[0]));
            vm.CancelEditReviewCommand.Execute(null);
            Pump();

            // Arm the delete confirmation, snapshot the inline «Видалити?» pair,
            // then back out without mutating the shared seeded DB.
            h.RunStep("ux-prof-08b-review-delete-arm", () =>
                vm.RequestDeleteReviewCommand.Execute(vm.MyReviews[0]));
            Log($"08b pending-delete armed: {vm.ReviewPendingDelete is not null}");
            vm.CancelDeleteReviewCommand.Execute(null);
            Pump();
        }

        // 09 — saved searches: what does the secondary line actually show?
        h.RunStep("ux-prof-09-saved-searches", () => Tabs(h).SelectedIndex = 2);
        foreach (var s in vm.SavedSearches)
            Log($"09 saved: Name='{s.Saved.Name}' QueryJson='{s.Saved.QueryJson}' Count={s.CurrentCount}");

        // 10 — wishlist grid.
        h.RunStep("ux-prof-10-wishlist", () => Tabs(h).SelectedIndex = 3);

        // 11/12 — orders tab at shrinking widths: fixed 70+140+170+160 columns.
        Tabs(h).SelectedIndex = 0;
        Pump();
        h.SetWindowSize(1000, 720);
        h.RunStep("ux-prof-11-narrow-1000", () => { });
        h.SetWindowSize(820, 640);
        h.RunStep("ux-prof-12-narrow-820", () => { });
    }

    [AvaloniaFact]
    public void Profile_ux_audit_admin_empty_states()
    {
        Log($"==== admin sweep {DateTime.Now:O} ====");
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.SetWindowSize(1280, 800);

        // admin has no orders / reviews / saved searches / wishlist seeded —
        // this sweep is entirely about the four empty states.
        h.RunStep("ux-prof-20-admin-orders-empty", () => h.Nav!.NavigateTo(NavTarget.Profile));
        var vm = Pvm(h);
        Log($"20 admin counts: AllOrders={vm.AllOrders.Count} Reviews={vm.MyReviews.Count} " +
            $"Saved={vm.SavedSearches.Count} Wishlist={vm.WishlistItems.Count}");

        h.RunStep("ux-prof-21-admin-reviews-empty", () => Tabs(h).SelectedIndex = 1);
        h.RunStep("ux-prof-22-admin-saved-empty", () => Tabs(h).SelectedIndex = 2);
        h.RunStep("ux-prof-23-admin-wishlist-empty", () => Tabs(h).SelectedIndex = 3);
    }

    [AvaloniaFact]
    public void Profile_ux_audit_guest()
    {
        Log($"==== guest sweep {DateTime.Now:O} ====");
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        h.RunStep("ux-prof-30-guest", () => h.Nav!.NavigateTo(NavTarget.Profile));
        var vm = Pvm(h);
        Log($"30 guest: Username='{vm.Username}' Email='{vm.Email}' Role='{vm.RoleLabel}' " +
            $"IsAuthenticated={vm.IsAuthenticated}");

        // The title-bar menu path can still call OpenPasswordPanel as guest —
        // what feedback does the page give?
        vm.OpenPasswordPanelCommand.Execute(null);
        Pump();
        Log($"30 guest OpenPasswordPanel: panelOpen={vm.IsPasswordPanelOpen} status='{vm.StatusMessage}'");
        h.RunStep("ux-prof-31-guest-status", () => { });
    }
}
