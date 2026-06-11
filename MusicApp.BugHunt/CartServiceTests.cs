using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;
using MusicApp.Services;
using Xunit;

namespace MusicApp.BugHunt;

public class CartServiceTests : IDisposable
{
    private readonly string _dbDir;
    private readonly MusicStoreDbContextFactory _factory;
    private readonly AuthService _auth;
    private readonly CartService _cart;
    private int _productId;       // stock 5
    private int _scarceProductId; // stock 2

    public CartServiceTests()
    {
        // own subdirectory → the session.json next to the DB is isolated per test class
        _dbDir = Path.Combine(Path.GetTempPath(), $"cart-tests-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", Path.Combine(_dbDir, "store.db"));
        _factory = new MusicStoreDbContextFactory();
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        Seed();
        _auth = new AuthService(_factory);
        _cart = new CartService(_auth, _factory);
    }

    private void Seed()
    {
        using var db = _factory.CreateDbContext();
        var artist = new Artist { Name = "Cart Artist" };
        db.Artists.Add(artist);
        db.SaveChanges();
        var album = new Album { ArtistId = artist.Id, Title = "Cart Album", Year = 2021 };
        db.Albums.Add(album);
        db.SaveChanges();
        var product = new Product { AlbumId = album.Id, Format = ProductFormat.CD, Price = 250m, Stock = 5, ReleaseYear = 2021, IsActive = true };
        var scarce = new Product { AlbumId = album.Id, Format = ProductFormat.Vinyl, Price = 700m, Stock = 2, ReleaseYear = 2021, IsActive = true };
        db.Products.AddRange(product, scarce);
        db.SaveChanges();
        _productId = product.Id;
        _scarceProductId = scarce.Id;
    }

    private Product LoadProduct(int id)
    {
        using var db = _factory.CreateDbContext();
        return db.Products.AsNoTracking()
            .Include(p => p.Album)!.ThenInclude(a => a!.Artist)
            .First(p => p.Id == id);
    }

    private void LoginUser(string name = "buyer")
    {
        Assert.True(_auth.TryRegister(name, "secret", $"{name}@test"));
    }

    // --- guest (in-memory) cart ---

    [Fact]
    public void Guest_add_keeps_items_in_memory_with_negative_ids()
    {
        _cart.Add(LoadProduct(_productId), 2);
        var item = Assert.Single(_cart.Items);
        Assert.True(item.Id < 0);
        Assert.Equal(2, item.Quantity);
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.CartItems.ToList());
    }

    [Fact]
    public void Adding_same_product_twice_merges_quantity()
    {
        var p = LoadProduct(_productId);
        _cart.Add(p, 1);
        _cart.Add(p, 2);
        var item = Assert.Single(_cart.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(3, _cart.ItemCount);
    }

    [Fact]
    public void Add_clamps_quantity_to_stock()
    {
        _cart.Add(LoadProduct(_scarceProductId), 10);
        Assert.Equal(2, Assert.Single(_cart.Items).Quantity);
    }

    [Fact]
    public void UpdateQuantity_clamps_to_stock_and_zero_removes()
    {
        var p = LoadProduct(_scarceProductId);
        _cart.Add(p, 1);
        _cart.UpdateQuantity(_cart.Items[0], 99);
        Assert.Equal(2, _cart.Items[0].Quantity);
        _cart.UpdateQuantity(_cart.Items[0], 0);
        Assert.Empty(_cart.Items);
    }

    [Fact]
    public void Total_sums_line_totals()
    {
        _cart.Add(LoadProduct(_productId), 2);    // 2 × 250
        _cart.Add(LoadProduct(_scarceProductId)); // 1 × 700
        Assert.Equal(1200m, _cart.Total);
    }

    [Fact]
    public void CartChanged_fires_on_every_mutation()
    {
        int fired = 0;
        _cart.CartChanged += (_, _) => fired++;
        var p = LoadProduct(_productId);
        _cart.Add(p, 1);                          // 1
        _cart.UpdateQuantity(_cart.Items[0], 2);  // 2
        _cart.Remove(_cart.Items[0]);             // 3
        _cart.Clear();                            // 4
        Assert.Equal(4, fired);
    }

    // --- persisted cart for logged-in users ---

    [Fact]
    public void Authenticated_add_persists_to_db()
    {
        LoginUser();
        _cart.Add(LoadProduct(_productId), 2);
        using var db = _factory.CreateDbContext();
        var row = Assert.Single(db.CartItems.ToList());
        Assert.Equal(_auth.CurrentUser!.Id, row.UserId);
        Assert.Equal(2, row.Quantity);
        // reload populated Product navigation for UI
        Assert.NotNull(Assert.Single(_cart.Items).Product);
    }

    [Fact]
    public void Clear_removes_persisted_rows()
    {
        LoginUser();
        _cart.Add(LoadProduct(_productId), 1);
        _cart.Clear();
        Assert.Empty(_cart.Items);
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.CartItems.ToList());
    }

    // --- guest → user merge ---

    [Fact]
    public void Guest_cart_merges_into_user_cart_on_login()
    {
        _cart.Add(LoadProduct(_productId), 2); // as guest
        LoginUser();                           // triggers CurrentUserChanged → merge
        var item = Assert.Single(_cart.Items);
        Assert.Equal(_productId, item.ProductId);
        Assert.Equal(2, item.Quantity);
        using var db = _factory.CreateDbContext();
        var row = Assert.Single(db.CartItems.ToList());
        Assert.Equal(_auth.CurrentUser!.Id, row.UserId);
    }

    [Fact]
    public void Merge_combines_with_existing_user_rows_and_respects_stock()
    {
        LoginUser();
        _cart.Add(LoadProduct(_scarceProductId), 1); // user already has 1 (stock 2)
        _auth.Logout();
        _auth.LoginAsGuest();
        Assert.Empty(_cart.Items);
        _cart.Add(LoadProduct(_scarceProductId), 2); // guest picks 2 more
        Assert.True(_auth.TryLogin("buyer", "secret"));
        var item = Assert.Single(_cart.Items);
        Assert.Equal(2, item.Quantity); // 1 + 2 clamped to stock 2
    }

    [Fact]
    public void Logout_drops_cart_to_empty_guest_state()
    {
        LoginUser();
        _cart.Add(LoadProduct(_productId), 1);
        _auth.Logout();
        Assert.Empty(_cart.Items);
        using var db = _factory.CreateDbContext();
        Assert.Single(db.CartItems.ToList()); // user's cart stays persisted for next login
    }

    // --- checkout ---

    [Fact]
    public void Checkout_creates_order_decrements_stock_and_clears_cart()
    {
        LoginUser();
        _cart.Add(LoadProduct(_productId), 3);
        var order = _cart.Checkout();

        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Equal(750m, order.TotalAmount);
        var oi = Assert.Single(order.Items);
        Assert.Equal(3, oi.Quantity);
        Assert.Equal(250m, oi.UnitPrice);
        Assert.Contains("Cart Album", oi.ProductTitle);
        Assert.Equal("Cart Artist", oi.ArtistName);

        Assert.Empty(_cart.Items);
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.CartItems.ToList());
        Assert.Equal(2, db.Products.First(p => p.Id == _productId).Stock); // 5 − 3
        var persisted = Assert.Single(db.Orders.Include(o => o.Items).ToList());
        Assert.Equal(_auth.CurrentUser!.Id, persisted.UserId);
        Assert.Single(persisted.Items);
    }

    [Fact]
    public void Checkout_persists_shipping_address_and_comment()
    {
        LoginUser();
        _cart.Add(LoadProduct(_productId), 1);
        var order = _cart.Checkout("  м. Київ, вул. Тестова 1  ", "Подзвонити заздалегідь");

        Assert.Equal("м. Київ, вул. Тестова 1", order.ShippingAddress);
        Assert.Equal("Подзвонити заздалегідь", order.Comment);
        using var db = _factory.CreateDbContext();
        var persisted = Assert.Single(db.Orders.ToList());
        Assert.Equal("м. Київ, вул. Тестова 1", persisted.ShippingAddress);
        Assert.Equal("Подзвонити заздалегідь", persisted.Comment);
    }

    [Fact]
    public void Checkout_with_blank_address_stores_null()
    {
        LoginUser();
        _cart.Add(LoadProduct(_productId), 1);
        var order = _cart.Checkout("   ", "");

        Assert.Null(order.ShippingAddress);
        Assert.Null(order.Comment);
    }

    [Fact]
    public void Guest_checkout_is_not_persisted()
    {
        _cart.Add(LoadProduct(_productId), 1);
        var order = _cart.Checkout();
        Assert.Equal(0, order.UserId);
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Orders.ToList());
        Assert.Equal(5, db.Products.First(p => p.Id == _productId).Stock); // untouched
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", null);
        try { Directory.Delete(_dbDir, recursive: true); } catch { }
    }
}
