using System.Linq;
using Avalonia.Headless.XUnit;
using MusicApp.Services;
using MusicApp.ViewModels;

namespace MusicApp.BugHunt;

// Walks every page at the default window size (plus a tall window so the
// whole scrollable page lands in one capture) and snapshots each one.
// Pure observation pass — the artifacts feed manual UI/UX review.
public class UiAuditTests
{
    [AvaloniaFact]
    public void Audit_customer_pages()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.SetWindowSize(1280, 800);

        h.RunStep("audit-01-catalog", () => { });
        h.RunStep("audit-01b-catalog-tall", () => h.SetWindowSize(1280, 2600));
        h.SetWindowSize(1280, 800);

        h.RunStep("audit-02-search", () => h.Nav!.NavigateTo(NavTarget.SearchResults, "love"));
        h.RunStep("audit-02b-search-genre", () => h.Nav!.NavigateTo(NavTarget.SearchResults, "жанр:Rock"));
        h.RunStep("audit-02c-search-tall", () => h.SetWindowSize(1280, 2200));
        h.SetWindowSize(1280, 800);

        h.RunStep("audit-03-cart-with-items", () =>
        {
            var products = h.Catalog!.Products.Where(p => p.Stock > 1).Take(2).ToList();
            foreach (var p in products) h.Cart!.Add(p);
            h.Nav!.NavigateTo(NavTarget.Cart);
        });
        h.RunStep("audit-03t-cart-tall", () => h.SetWindowSize(1280, 1600));
        h.SetWindowSize(1280, 800);
        h.RunStep("audit-03b-cart-empty", () =>
        {
            h.Cart!.Clear();
            h.Nav!.NavigateTo(NavTarget.Catalog);
            h.Nav!.NavigateTo(NavTarget.Cart);
        });

        h.RunStep("audit-04-product", () =>
        {
            var p = h.Catalog!.Products.First();
            h.Nav!.NavigateTo(NavTarget.Product, p.Id);
        });
        h.RunStep("audit-04b-product-tall", () => h.SetWindowSize(1280, 3200));
        h.SetWindowSize(1280, 800);

        h.RunStep("audit-05-orders", () => h.Nav!.NavigateTo(NavTarget.Orders));
        h.RunStep("audit-06-profile", () => h.Nav!.NavigateTo(NavTarget.Profile));
        h.RunStep("audit-06b-profile-tall", () => h.SetWindowSize(1280, 2200));
        h.SetWindowSize(1280, 800);
        h.RunStep("audit-07-player", () => h.Nav!.NavigateTo(NavTarget.Player));
    }

    [AvaloniaFact]
    public void Audit_admin_and_login_overlay()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.SetWindowSize(1280, 800);

        h.RunStep("audit-10-admin", () => h.Nav!.NavigateTo(NavTarget.Admin));
        h.RunStep("audit-11-login-overlay", () =>
        {
            var vm = (MainWindowViewModel)h.Window!.DataContext!;
            vm.ShowLogin();
        });
    }
}
