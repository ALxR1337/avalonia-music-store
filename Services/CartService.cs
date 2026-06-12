using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;

namespace MusicApp.Services;

public class CartService : ICartService
{
    private readonly IAuthService _auth;
    private readonly IDbContextFactory<MusicStoreDbContext> _dbFactory;
    private readonly ICatalogService? _catalog;

    private int _currentUserId;
    private int _guestNextId = -1;  // negative IDs for in-memory guest items

    public CartService(IAuthService auth, IDbContextFactory<MusicStoreDbContext> dbFactory,
        ICatalogService? catalog = null)
    {
        _auth = auth;
        _dbFactory = dbFactory;
        _catalog = catalog;
        _auth.CurrentUserChanged += OnCurrentUserChanged;
    }

    public event EventHandler? CartChanged;

    public ObservableCollection<CartItem> Items { get; } = new();

    public int ItemCount => Items.Sum(i => i.Quantity);
    public decimal Total => Items.Sum(i => i.LineTotal);

    public void Add(Product product, int quantity = 1)
    {
        if (product is null || quantity <= 0) return;
        // Out-of-stock products must not enter the cart at all — without this
        // the clamp below is skipped for Stock == 0 and the item slips in.
        if (product.Stock <= 0) return;

        var existing = Items.FirstOrDefault(i => i.ProductId == product.Id);
        var desired = Math.Min((existing?.Quantity ?? 0) + quantity, product.Stock);

        if (IsGuest)
        {
            if (existing is not null)
                existing.Quantity = desired;
            else
                Items.Add(new CartItem
                {
                    Id = _guestNextId--,
                    UserId = 0,
                    ProductId = product.Id,
                    Product = product,
                    Quantity = desired
                });
        }
        else
        {
            using var db = _dbFactory.CreateDbContext();
            var row = db.CartItems.FirstOrDefault(c => c.UserId == _currentUserId && c.ProductId == product.Id);
            if (row is null)
            {
                row = new CartItem
                {
                    UserId = _currentUserId,
                    ProductId = product.Id,
                    Quantity = desired,
                    AddedAt = DateTime.UtcNow
                };
                db.CartItems.Add(row);
            }
            else
            {
                row.Quantity = desired;
            }
            db.SaveChanges();
            ReloadFromDb(db);
        }

        Raise();
    }

    public void Remove(CartItem item)
    {
        if (IsGuest)
        {
            Items.Remove(item);
        }
        else
        {
            using var db = _dbFactory.CreateDbContext();
            var row = db.CartItems.FirstOrDefault(c => c.Id == item.Id);
            if (row is not null)
            {
                db.CartItems.Remove(row);
                db.SaveChanges();
            }
            ReloadFromDb(db);
        }
        Raise();
    }

    public void UpdateQuantity(CartItem item, int quantity)
    {
        if (quantity <= 0)
        {
            Remove(item);
            return;
        }

        if (item.Product is { Stock: > 0 } p)
            quantity = Math.Min(quantity, p.Stock);

        if (IsGuest)
        {
            item.Quantity = quantity;
        }
        else
        {
            using var db = _dbFactory.CreateDbContext();
            var row = db.CartItems.FirstOrDefault(c => c.Id == item.Id);
            if (row is not null)
            {
                row.Quantity = quantity;
                db.SaveChanges();
            }
            ReloadFromDb(db);
        }
        Raise();
    }

    public void Clear()
    {
        if (!IsGuest)
        {
            using var db = _dbFactory.CreateDbContext();
            var rows = db.CartItems.Where(c => c.UserId == _currentUserId);
            db.CartItems.RemoveRange(rows);
            db.SaveChanges();
        }
        Items.Clear();
        Raise();
    }

    public Order Checkout(string? shippingAddress = null, string? comment = null)
    {
        var userId = _currentUserId;
        var orderItems = Items.Select(i =>
        {
            var album = i.Product?.Album;
            var formatLabel = i.Product?.FormatBadge ?? string.Empty;
            return new OrderItem
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.Product?.Price ?? 0m,
                ProductTitle = album is null ? "—" : $"{album.Title} ({formatLabel})",
                AlbumTitle = album?.Title ?? "—",
                ArtistName = album?.Artist?.Name ?? "—",
                FormatLabel = formatLabel,
            };
        }).ToList();
        var total = orderItems.Sum(i => i.UnitPrice * i.Quantity);

        var userEmail = !IsGuest ? LookupUserEmail(userId) : null;
        var order = new Order
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            Status = OrderStatus.New,
            TotalAmount = total,
            UserEmail = userEmail,
            ShippingAddress = string.IsNullOrWhiteSpace(shippingAddress) ? null : shippingAddress.Trim(),
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            Currency = "UAH",
            Items = orderItems
        };

        if (!IsGuest && Items.Count > 0)
        {
            using var db = _dbFactory.CreateDbContext();
            db.Orders.Add(order);

            // decrement stock (SalesCount is incremented by the OrderItems trigger)
            foreach (var item in Items)
            {
                var product = db.Products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product is not null)
                    product.Stock = Math.Max(0, product.Stock - item.Quantity);
            }

            // drop persisted cart rows
            var cartRows = db.CartItems.Where(c => c.UserId == userId);
            db.CartItems.RemoveRange(cartRows);

            db.SaveChanges();

            // The checkout decremented Stock directly in the DB; refresh the catalog's
            // in-memory cache so subsequent stock checks and listings see live values.
            _catalog?.RefreshReferenceData();
        }

        Items.Clear();
        Raise();
        return order;
    }

    private string? LookupUserEmail(int userId)
    {
        if (userId <= 0) return null;
        using var db = _dbFactory.CreateDbContext();
        return db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefault();
    }

    private bool IsGuest => _currentUserId == 0;

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        var newUserId = _auth.CurrentUser?.Id ?? 0;
        if (newUserId == _currentUserId) return;

        var pendingFromGuest = _currentUserId == 0 && Items.Count > 0
            ? Items.Select(i => (i.ProductId, i.Quantity)).ToList()
            : null;

        _currentUserId = newUserId;
        Items.Clear();

        if (newUserId == 0)
        {
            // dropped to guest — leave empty
            Raise();
            return;
        }

        using var db = _dbFactory.CreateDbContext();

        if (pendingFromGuest is not null)
        {
            foreach (var (productId, qty) in pendingFromGuest)
            {
                var product = db.Products.FirstOrDefault(p => p.Id == productId);
                if (product is null) continue;

                var existing = db.CartItems.FirstOrDefault(c => c.UserId == newUserId && c.ProductId == productId);
                var combined = (existing?.Quantity ?? 0) + qty;
                if (product.Stock > 0)
                    combined = Math.Min(combined, product.Stock);

                if (existing is null)
                {
                    db.CartItems.Add(new CartItem
                    {
                        UserId = newUserId,
                        ProductId = productId,
                        Quantity = combined,
                        AddedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Quantity = combined;
                }
            }
            db.SaveChanges();
        }

        ReloadFromDb(db);
        Raise();
    }

    private void ReloadFromDb(MusicStoreDbContext db)
    {
        Items.Clear();
        var rows = db.CartItems
            .AsNoTracking()
            .Include(c => c.Product)!.ThenInclude(p => p!.Album)!.ThenInclude(a => a!.Artist)
            .Include(c => c.Product)!.ThenInclude(p => p!.Album)!.ThenInclude(a => a!.AlbumGenres)!.ThenInclude(ag => ag.Genre)
            .Where(c => c.UserId == _currentUserId)
            .OrderBy(c => c.AddedAt)
            .ToList();
        foreach (var r in rows) Items.Add(r);
    }

    private void Raise() => CartChanged?.Invoke(this, EventArgs.Empty);
}
