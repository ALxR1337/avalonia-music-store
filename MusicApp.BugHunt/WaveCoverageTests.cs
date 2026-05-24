using Avalonia.Headless.XUnit;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Exercises features added in waves 3-6. Each test opens MainWindow,
/// drives it through nav clicks / VM mutations, and dumps tree + screenshot
/// for each step so we can triage any layout/binding/runtime breakage.
/// </summary>
public class WaveCoverageTests
{
    [AvaloniaFact]
    public void Catalog_renders_seeded_albums()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);
        h.RunStep("wave3-01-catalog-initial", () => { /* default landing */ });
    }

    [AvaloniaFact]
    public void Cart_renders_empty_for_guest()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        // Sidebar buttons don't have x:Name — nav via the VM is acceptable per BugHunt rules:
        // "Use Find for *driving* the app." For pages without a Find target we navigate directly.
        h.RunStep("wave3-02-cart-empty", () => h.Nav!.NavigateTo(NavTarget.Cart));
    }

    [AvaloniaFact]
    public void Search_full_pipeline_returns_results()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        h.RunStep("wave4-01-search-death",
            () => h.Nav!.NavigateTo(NavTarget.SearchResults, "death"));
        h.RunStep("wave4-02-search-money",
            () => h.Nav!.NavigateTo(NavTarget.SearchResults, "money"));
        h.RunStep("wave4-03-search-nomatch",
            () => h.Nav!.NavigateTo(NavTarget.SearchResults, "xxxxxnomatch"));
    }

    [AvaloniaFact]
    public void Product_card_renders_with_format_toggle()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        // Product id 1 exists in seed data; sibling (other format of the same album)
        // should exist too — Wave 5's format toggle target.
        h.RunStep("wave5-01-product-1",
            () => h.Nav!.NavigateTo(NavTarget.Product, 1));

        var sibling = h.Catalog!.GetSiblingProduct(1);
        if (sibling is not null)
        {
            h.RunStep("wave5-02-sibling-product",
                () => h.Nav!.NavigateTo(NavTarget.Product, sibling.Id));
        }
    }

    [AvaloniaFact]
    public void Player_page_renders_for_guest()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        h.RunStep("wave3-03-player-guest",
            () => h.Nav!.NavigateTo(NavTarget.Player));
    }

    [AvaloniaFact]
    public void Autocomplete_dropdown_populates_after_typing()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        if (h.Window!.DataContext is MainWindowViewModel mwvm)
        {
            // Drive the VM directly: TextBox isn't named, so Type() can't find it.
            // Setting SearchQuery triggers the same OnSearchQueryChanged debounce timer.
            h.RunStep("wave4-04-autocomplete-typing", () =>
            {
                mwvm.SearchQuery = "money";
                // Force the debounced autocomplete to fire now rather than after 200ms.
                System.Threading.Thread.Sleep(250);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
        }
    }

    [AvaloniaFact]
    public void Cart_add_from_product_card_updates_count()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.SetWindowSize(1280, 800);

        h.RunStep("wave3-04-product-before-add", () => h.Nav!.NavigateTo(NavTarget.Product, 1));
        if (h.Nav!.CurrentView is ProductViewModel pvm)
        {
            h.RunStep("wave3-05-product-after-add", () => pvm.AddToCartCommand.Execute(null));
        }
        h.RunStep("wave3-06-cart-shows-item", () => h.Nav!.NavigateTo(NavTarget.Cart));
    }

    [AvaloniaFact]
    public void Admin_page_renders_for_admin_login()
    {
        var h = new Harness();
        // Seed creates admin/admin BCrypt user.
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.SetWindowSize(1400, 900);

        h.RunStep("wave6-01-admin-products",
            () => h.Nav!.NavigateTo(NavTarget.Admin));

        // Probe order expander by toggling on the first order.
        var orders = h.Catalog!.Orders;
        if (orders.Count > 0 && h.Nav!.CurrentView is AdminViewModel admin)
        {
            h.RunStep("wave6-02-admin-expand-order",
                () => admin.ToggleOrderDetailsCommand.Execute(orders[0]));
        }
    }

    [AvaloniaFact]
    public void Profile_tabs_render_for_logged_in_user()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.SetWindowSize(1280, 800);

        h.RunStep("wave7-01-profile-initial",
            () => h.Nav!.NavigateTo(NavTarget.Profile));

        // Toggling a saved-search row shouldn't throw even when the user has none.
        if (h.Nav!.CurrentView is ProfileViewModel pvm)
        {
            h.RunStep("wave7-02-profile-status-filter",
                () => pvm.OrderStatusFilter = MusicApp.Models.OrderStatus.Completed);
            h.RunStep("wave7-03-profile-clear-filter",
                () => pvm.ClearOrderFilterCommand.Execute(null));
        }
    }

    [AvaloniaFact]
    public void Orders_page_expands_details_for_user()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.SetWindowSize(1280, 800);

        h.RunStep("wave7-04-orders-initial",
            () => h.Nav!.NavigateTo(NavTarget.Orders));

        if (h.Nav!.CurrentView is OrdersViewModel ovm && ovm.Orders.Count > 0)
        {
            h.RunStep("wave7-05-orders-expand",
                () => ovm.ToggleDetailsCommand.Execute(ovm.Orders[0]));
        }
    }

    [AvaloniaFact]
    public void Sidebar_collapses_at_narrow_width_and_shows_cart_badge()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.SetWindowSize(1280, 800);

        h.RunStep("wave8-01-sidebar-expanded", () => { /* default expanded */ });
        h.RunStep("wave8-02-sidebar-collapsed", () => h.SetWindowSize(900, 700));

        // Push a product into the cart so the badge renders.
        if (h.Window!.DataContext is MainWindowViewModel mwvm && h.Catalog!.Products.Count > 0)
        {
            h.RunStep("wave8-03-cart-badge", () =>
            {
                // simulate ProductView add by reusing the catalog product
                h.Nav!.NavigateTo(NavTarget.Product, h.Catalog!.Products[0].Id);
                if (h.Nav!.CurrentView is ProductViewModel pvm)
                    pvm.AddToCartCommand.Execute(null);
            });
        }
    }

    [AvaloniaFact]
    public void Cart_blocks_guest_checkout_with_banner()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        if (h.Catalog!.Products.Count > 0)
        {
            h.Nav!.NavigateTo(NavTarget.Product, h.Catalog!.Products[0].Id);
            if (h.Nav!.CurrentView is ProductViewModel pvm)
                pvm.AddToCartCommand.Execute(null);
        }
        h.RunStep("wave8-04-cart-guest", () => h.Nav!.NavigateTo(NavTarget.Cart));
        if (h.Nav!.CurrentView is CartViewModel cvm)
        {
            h.RunStep("wave8-05-checkout-blocked", () => cvm.CheckoutCommand.Execute(null));
        }
    }

    [AvaloniaFact]
    public void Search_top_result_and_removable_chips_render()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        h.RunStep("wave8-06-search-with-genre",
            () => h.Nav!.NavigateTo(NavTarget.SearchResults, "money"));

        if (h.Nav!.CurrentView is SearchResultsViewModel svm)
        {
            h.RunStep("wave8-07-search-format-chip",
                () => svm.SelectedFormatLabel = "LP");
            // Remove the format chip via the remove-filter command.
            if (svm.ActiveFilterChips.Count > 0)
            {
                h.RunStep("wave8-08-remove-chip",
                    () => svm.RemoveFilterCommand.Execute(svm.ActiveFilterChips[0]));
            }
        }
    }

    [AvaloniaFact]
    public void Seed_contains_real_music_with_multi_genres_and_bios()
    {
        var h = new Harness();
        h.OpenMainWindow();
        Assert.NotNull(h.Catalog);

        // 23 curated albums in the new seed.
        Assert.True(h.Catalog!.Albums.Count >= 20,
            $"Expected at least 20 albums, found {h.Catalog!.Albums.Count}");

        // Every artist has a curated bio (Description).
        Assert.All(h.Catalog!.Artists,
            a => Assert.False(string.IsNullOrWhiteSpace(a.Description),
                $"Artist '{a.Name}' is missing a bio"));

        // Every album has a description.
        Assert.All(h.Catalog!.Albums,
            a => Assert.False(string.IsNullOrWhiteSpace(a.Description),
                $"Album '{a.Title}' is missing a description"));

        // At least one album sits in multiple genres via AlbumGenres.
        Assert.Contains(h.Catalog!.Albums,
            a => a.AlbumGenres != null && a.AlbumGenres.Count > 1);

        h.RunStep("wave-real-01-product-with-bio",
            () => h.Nav!.NavigateTo(NavTarget.Product, h.Catalog!.Products[0].Id));
    }
}
