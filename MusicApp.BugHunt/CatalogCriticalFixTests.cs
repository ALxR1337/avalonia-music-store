using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Covers the two critical catalog fixes from the UX audit:
/// 1) deactivated products must not reach the "Нові надходження" shelf;
/// 2) adding to cart from the shelf gives toast feedback, including the
///    out-of-stock refusal that CartService swallows silently.
/// </summary>
public class CatalogCriticalFixTests
{
    private static CatalogViewModel OpenCatalog(Harness h)
    {
        h.Nav!.NavigateTo(NavTarget.Catalog);
        Dispatcher.UIThread.RunJobs();
        var cvm = h.Nav!.CurrentView as CatalogViewModel;
        Assert.NotNull(cvm);
        return cvm!;
    }

    [AvaloniaFact]
    public void Deactivated_edition_drops_off_the_new_arrivals_shelf()
    {
        var h = new Harness();
        h.OpenMainWindow();

        var arrivals = h.Catalog!.GetNewArrivalAlbums();
        var victim = arrivals.First(a => a.HasVinyl);
        var vinylId = victim.Vinyl!.Id;

        // The harness DB is per-process, not per-test: undo the deactivation
        // afterwards so later tests see the seeded shelf.
        h.Catalog.SetProductActive(vinylId, false);
        try
        {
            var after = h.Catalog.GetNewArrivalAlbums();
            Assert.DoesNotContain(after,
                a => (a.Vinyl?.Id == vinylId) || (a.Cd?.Id == vinylId));

            // Every shelf entry still has at least one (active) edition, so the
            // card's click target / context menu never dereference a null product.
            Assert.All(after, a =>
            {
                Assert.NotNull(a.PrimaryProduct);
                Assert.True(a.PrimaryProduct!.IsActive);
            });
        }
        finally
        {
            h.Catalog.SetProductActive(vinylId, true);
        }

        // Self-check the cleanup: the edition is back for whichever test runs next.
        Assert.Contains(h.Catalog.GetNewArrivalAlbums(), a => a.Vinyl?.Id == vinylId);
    }

    [AvaloniaFact]
    public void Album_with_all_editions_deactivated_disappears_entirely()
    {
        var h = new Harness();
        h.OpenMainWindow();

        var victim = h.Catalog!.GetNewArrivalAlbums().First();
        var albumId = victim.Album.Id;
        var editionIds = h.Catalog.Products
            .Where(p => p.AlbumId == albumId).Select(p => p.Id).ToList();

        // Per-process DB: restore the editions afterwards (see test above).
        foreach (var id in editionIds)
            h.Catalog.SetProductActive(id, false);
        try
        {
            Assert.DoesNotContain(h.Catalog.GetNewArrivalAlbums(), a => a.Album.Id == albumId);

            // The freshly built catalog page must agree with the service.
            var cvm = OpenCatalog(h);
            Assert.DoesNotContain(cvm.NewArrivals, a => a.Album.Id == albumId);
        }
        finally
        {
            foreach (var id in editionIds)
                h.Catalog.SetProductActive(id, true);
        }

        // Self-check the cleanup: the album is back for whichever test runs next.
        Assert.Contains(h.Catalog.GetNewArrivalAlbums(), a => a.Album.Id == albumId);
    }

    [AvaloniaFact]
    public void Add_to_cart_shows_confirmation_toast()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);
        var cvm = OpenCatalog(h);

        var product = cvm.NewArrivals
            .Select(a => a.PrimaryProduct)
            .First(p => p is { Stock: > 0 });

        var before = h.Cart!.ItemCount;
        h.RunStep("fix-toast-added", () => cvm.AddToCartCommand.Execute(product));

        Assert.Equal(before + 1, h.Cart.ItemCount);
        Assert.Contains("додано в кошик", cvm.ToastMessage);

        cvm.DismissToastCommand.Execute(null);
        Assert.Null(cvm.ToastMessage);
    }

    [AvaloniaFact]
    public void Out_of_stock_add_shows_refusal_toast_and_keeps_cart_intact()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);
        var cvm = OpenCatalog(h);

        var product = cvm.NewArrivals
            .Select(a => a.PrimaryProduct)
            .First(p => p is not null)!;
        product.Stock = 0; // in-memory cache entity — enough for the VM-level gate

        var before = h.Cart!.ItemCount;
        h.RunStep("fix-toast-out-of-stock", () => cvm.AddToCartCommand.Execute(product));

        Assert.Equal(before, h.Cart.ItemCount);
        Assert.Contains("немає в наявності", cvm.ToastMessage);
    }
}
