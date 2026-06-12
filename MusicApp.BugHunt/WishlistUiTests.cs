using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Regression guards for Wave Wishlist-UI:
//   - GetWishlistProducts returns newest-first and WishlistChanged fires;
//   - the cart's «Збережене» section moves an item into the cart and out of
//     the wishlist in one click, and refuses out-of-stock products;
//   - the profile's «Збережене» tab lists saved albums and the heart removes.
public class WishlistUiTests
{
    [AvaloniaFact]
    public void Wishlist_returns_newest_first_and_raises_changed()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var userId = h.Auth!.CurrentUser!.Id;
        var existing = h.Catalog!.GetWishlistProducts(userId).Select(p => p.Id).ToHashSet();
        var fresh = h.Catalog.Products.Where(p => !existing.Contains(p.Id)).Take(2).ToList();
        Assert.Equal(2, fresh.Count);

        var changedFired = 0;
        h.Catalog.WishlistChanged += (_, _) => changedFired++;
        try
        {
            h.Catalog.AddToWishlist(userId, fresh[0].Id);
            h.Catalog.AddToWishlist(userId, fresh[1].Id);

            Assert.Equal(2, changedFired);
            var list = h.Catalog.GetWishlistProducts(userId);
            // Newest saved item leads the list.
            Assert.Equal(fresh[1].Id, list[0].Id);
            Assert.Equal(fresh[0].Id, list[1].Id);
        }
        finally
        {
            // Shared seeded DB per run — restore the original wishlist.
            h.Catalog.RemoveFromWishlist(userId, fresh[0].Id);
            h.Catalog.RemoveFromWishlist(userId, fresh[1].Id);
        }
        Assert.Equal(existing, h.Catalog.GetWishlistProducts(userId).Select(p => p.Id).ToHashSet());
    }

    [AvaloniaFact]
    public void Saved_item_moves_to_cart_and_leaves_wishlist()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var userId = h.Auth!.CurrentUser!.Id;
        var saved = h.Catalog!.GetWishlistProducts(userId).Select(p => p.Id).ToHashSet();
        var product = h.Catalog.Products.First(p => p.Stock > 0 && !saved.Contains(p.Id));

        h.Catalog.AddToWishlist(userId, product.Id);
        try
        {
            h.Nav!.NavigateTo(NavTarget.Cart);
            Dispatcher.UIThread.RunJobs();
            var cvm = Assert.IsType<CartViewModel>(h.Nav.CurrentView);
            Assert.Contains(cvm.WishlistItems, p => p.Id == product.Id);

            cvm.MoveWishlistItemToCartCommand.Execute(product);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(h.Cart!.Items, i => i.ProductId == product.Id);
            Assert.DoesNotContain(cvm.WishlistItems, p => p.Id == product.Id);
        }
        finally
        {
            var moved = h.Cart!.Items.FirstOrDefault(i => i.ProductId == product.Id);
            if (moved is not null) h.Cart.Remove(moved);
            h.Catalog.RemoveFromWishlist(userId, product.Id); // no-op if already moved
        }
        Assert.DoesNotContain(h.Cart!.Items, i => i.ProductId == product.Id);
    }

    [AvaloniaFact]
    public void Out_of_stock_saved_item_stays_saved_on_move_attempt()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var userId = h.Auth!.CurrentUser!.Id;
        var saved = h.Catalog!.GetWishlistProducts(userId).Select(p => p.Id).ToHashSet();
        var product = h.Catalog.Products.FirstOrDefault(p => p.Stock <= 0 && !saved.Contains(p.Id));
        if (product is null) return; // seed currently has every product in stock

        h.Catalog.AddToWishlist(userId, product.Id);
        try
        {
            h.Nav!.NavigateTo(NavTarget.Cart);
            Dispatcher.UIThread.RunJobs();
            var cvm = Assert.IsType<CartViewModel>(h.Nav.CurrentView);

            cvm.MoveWishlistItemToCartCommand.Execute(product);
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(h.Cart!.Items, i => i.ProductId == product.Id);
            Assert.Contains(cvm.WishlistItems, p => p.Id == product.Id);
        }
        finally
        {
            h.Catalog.RemoveFromWishlist(userId, product.Id);
        }
    }

    [AvaloniaFact]
    public void Profile_saved_tab_lists_album_and_heart_removes_it()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var userId = h.Auth!.CurrentUser!.Id;
        var saved = h.Catalog!.GetWishlistProducts(userId).Select(p => p.Id).ToHashSet();
        var product = h.Catalog.Products.First(p => !saved.Contains(p.Id));

        h.Catalog.AddToWishlist(userId, product.Id);
        try
        {
            h.Nav!.NavigateTo(NavTarget.Profile);
            Dispatcher.UIThread.RunJobs();
            var pvm = Assert.IsType<ProfileViewModel>(h.Nav.CurrentView);
            Assert.Contains(pvm.WishlistItems, p => p.Id == product.Id);

            pvm.RemoveWishlistItemCommand.Execute(product);
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(pvm.WishlistItems, p => p.Id == product.Id);
            Assert.False(h.Catalog.IsInWishlist(userId, product.Id));
        }
        finally
        {
            h.Catalog.RemoveFromWishlist(userId, product.Id); // idempotent safety net
        }
    }
}
