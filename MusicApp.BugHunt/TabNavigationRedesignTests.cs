using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Regression tests for the tab/navigation redesign:
//  • Search is a first-class section (own sidebar tab, highlightable).
//  • Detail pages (Product) keep the section they were opened from highlighted.
//  • Scroll position is remembered per history entry; fresh pages open at top.
//  • Switching LP/CD is page state — it must NOT pollute back/forward history.
public class TabNavigationRedesignTests
{
    private static MainWindowViewModel Shell(Harness h) =>
        (MainWindowViewModel)h.Window!.DataContext!;

    [AvaloniaFact]
    public void Switching_LP_CD_swaps_in_place_without_touching_history()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        var shell = Shell(h);

        var product = h.Catalog!.Products.First();
        h.Nav!.NavigateTo(NavTarget.Product, product.Id);
        Dispatcher.UIThread.RunJobs();

        var pvm = (ProductViewModel)h.Nav.CurrentView!;
        var canBackBefore = shell.CanGoBack;   // Catalog sits behind us → true
        var canFwdBefore = shell.CanGoForward;
        var originalFormat = pvm.Product!.Format;
        var wanted = originalFormat == ProductFormat.Vinyl ? "CD" : "LP";

        pvm.SelectFormatCommand.Execute(wanted);
        Dispatcher.UIThread.RunJobs();

        // Same VM instance — the page was mutated, not rebuilt.
        Assert.Same(pvm, h.Nav.CurrentView);
        // Format actually flipped.
        Assert.NotEqual(originalFormat, pvm.Product!.Format);
        // History untouched: no new back entry, forward not cleared.
        Assert.Equal(canBackBefore, shell.CanGoBack);
        Assert.Equal(canFwdBefore, shell.CanGoForward);

        // Back still returns to the catalog, not to the other format.
        shell.GoBackCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<CatalogViewModel>(h.Nav.CurrentView);
    }

    [AvaloniaFact]
    public void Product_opened_from_catalog_keeps_catalog_tab_active()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        var shell = Shell(h);

        var catalog = (CatalogViewModel)h.Nav!.CurrentView!;
        catalog.OpenProductCommand.Execute(h.Catalog!.Products.First());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NavTarget.Product, h.Nav.CurrentTarget);
        Assert.Equal(NavTarget.Catalog, h.Nav.CurrentSection);
        Assert.True(shell.IsCatalogActive);
        Assert.False(shell.IsSearchActive);
    }

    [AvaloniaFact]
    public void Search_is_a_section_and_product_from_search_keeps_search_active()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        var shell = Shell(h);

        // Empty query → browse mode; the "Пошук" sidebar tab lights up.
        h.Nav!.NavigateTo(NavTarget.SearchResults, string.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<SearchResultsViewModel>(h.Nav.CurrentView);
        Assert.True(shell.IsSearchActive);

        var search = (SearchResultsViewModel)h.Nav.CurrentView!;
        // Browse mode populates albums synchronously; opening one drills into its
        // purchasable product.
        search.OpenAlbumCommand.Execute(search.Albums.First());
        Dispatcher.UIThread.RunJobs();

        // Drilling into a product from search keeps "Пошук" highlighted.
        Assert.Equal(NavTarget.Product, h.Nav.CurrentTarget);
        Assert.Equal(NavTarget.SearchResults, h.Nav.CurrentSection);
        Assert.True(shell.IsSearchActive);
        Assert.False(shell.IsCatalogActive);
    }

    [AvaloniaFact]
    public void Back_restores_the_same_section_view_instance()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        var catalog = (CatalogViewModel)h.Nav!.CurrentView!;
        catalog.OpenProductCommand.Execute(h.Catalog!.Products.First());
        Dispatcher.UIThread.RunJobs();

        Shell(h).GoBackCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Same instance → the catalog's state (incl. scroll) survives the round trip.
        Assert.Same(catalog, h.Nav.CurrentView);
        Assert.Equal(NavTarget.Catalog, h.Nav.CurrentSection);
    }

    [AvaloniaFact]
    public void Scroll_offset_is_remembered_per_history_entry()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        // Pretend the user scrolled the catalog down.
        h.Nav!.SaveScroll(640);

        var catalog = (CatalogViewModel)h.Nav.CurrentView!;
        catalog.OpenProductCommand.Execute(h.Catalog!.Products.First());
        Dispatcher.UIThread.RunJobs();
        // Fresh detail page opens at the top.
        Assert.Equal(0d, h.Nav.CurrentScrollOffset);

        // Back to catalog restores the remembered position.
        h.Nav.GoBack();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(640d, h.Nav.CurrentScrollOffset);
    }

    [AvaloniaFact]
    public void Album_picked_from_search_box_belongs_to_search_section()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        var shell = Shell(h);

        // Start on the catalog to prove the search-box override wins over the
        // section the user happens to be standing in.
        Assert.Equal(NavTarget.Catalog, h.Nav!.CurrentSection);

        var albumId = h.Catalog!.Products.First().AlbumId;
        shell.PickSuggestionCommand.Execute(
            new MusicApp.Services.Search.AutocompleteHit("any", "album", albumId));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NavTarget.Product, h.Nav.CurrentTarget);
        Assert.Equal(NavTarget.SearchResults, h.Nav.CurrentSection);
        Assert.True(shell.IsSearchActive);
        Assert.False(shell.IsCatalogActive);
    }
}
