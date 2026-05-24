using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;

namespace MusicApp.Services;

public class CatalogService : ICatalogService
{
    private readonly IDbContextFactory<MusicStoreDbContext> _dbFactory;

    private List<Genre> _genres = new();
    private List<Artist> _artists = new();
    private List<Album> _albums = new();
    private List<Product> _products = new();
    private List<Review> _reviews = new();

    public CatalogService(IDbContextFactory<MusicStoreDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        LoadReferenceData();
    }

    public IReadOnlyList<Genre> Genres => _genres;
    public IReadOnlyList<Artist> Artists => _artists;
    public IReadOnlyList<Album> Albums => _albums;
    public IReadOnlyList<Product> Products => _products;
    public IReadOnlyList<Review> Reviews => _reviews;
    public IReadOnlyList<Order> Orders => GetOrdersInternal(null);

    public IReadOnlyList<Order> GetOrdersFor(int userId) => GetOrdersInternal(userId);

    public Product? GetProduct(int id) => _products.FirstOrDefault(p => p.Id == id);
    public Album? GetAlbum(int id) => _albums.FirstOrDefault(a => a.Id == id);

    public IReadOnlyList<Product> GetNewArrivals(int count = 8) =>
        _products.OrderByDescending(p => p.ReleaseYear).Take(count).ToList();

    public IReadOnlyList<NewArrivalAlbum> GetNewArrivalAlbums(int count = 8) =>
        _albums
            .OrderByDescending(a => a.Year)
            .Take(count)
            .Select(album =>
            {
                var formats = _products.Where(p => p.AlbumId == album.Id).ToList();
                return new NewArrivalAlbum
                {
                    Album = album,
                    Vinyl = formats.FirstOrDefault(p => p.Format == ProductFormat.Vinyl),
                    Cd = formats.FirstOrDefault(p => p.Format == ProductFormat.CD),
                };
            })
            .ToList();

    public IReadOnlyList<Product> GetPopular(int count = 8) =>
        _products.OrderByDescending(p => p.SalesCount).Take(count).ToList();

    public IReadOnlyList<Product> SearchProducts(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _products.Take(20).ToList();

        var q = query.Trim().ToLowerInvariant();
        return _products.Where(p =>
                (p.Album?.Title ?? "").ToLowerInvariant().Contains(q) ||
                (p.Album?.Artist?.Name ?? "").ToLowerInvariant().Contains(q) ||
                (p.Album?.Genre?.Name ?? "").ToLowerInvariant().Contains(q))
            .ToList();
    }

    public IReadOnlyList<Review> GetReviewsFor(int productId) =>
        _reviews.Where(r => r.ProductId == productId).ToList();

    public IReadOnlyList<Album> GetPurchasedAlbums(int userId)
    {
        if (userId <= 0) return System.Array.Empty<Album>();

        using var db = _dbFactory.CreateDbContext();
        var albumIds = db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId && o.Status == OrderStatus.Completed)
            .SelectMany(o => o.Items)
            .Select(i => i.Product!.AlbumId)
            .Distinct()
            .ToList();

        if (albumIds.Count == 0) return System.Array.Empty<Album>();

        return db.Albums.AsNoTracking()
            .Include(a => a.Artist)
            .Include(a => a.AlbumGenres)!.ThenInclude(ag => ag.Genre)
            .Include(a => a.Tracks)
            .Where(a => albumIds.Contains(a.Id))
            .OrderBy(a => a.Title)
            .ToList();
    }

    public bool IsAlbumPurchased(int albumId, int userId)
    {
        if (userId <= 0) return false;
        using var db = _dbFactory.CreateDbContext();
        return db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId && o.Status == OrderStatus.Completed)
            .SelectMany(o => o.Items)
            .Any(i => i.Product!.AlbumId == albumId);
    }

    public Product? GetSiblingProduct(int productId)
    {
        var current = GetProduct(productId);
        if (current is null) return null;
        var otherFormat = current.Format == ProductFormat.Vinyl ? ProductFormat.CD : ProductFormat.Vinyl;
        return _products.FirstOrDefault(p => p.AlbumId == current.AlbumId && p.Format == otherFormat);
    }

    public Review AddReview(int productId, int userId, string userDisplayName, string text, int rating)
    {
        using var db = _dbFactory.CreateDbContext();

        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            UserDisplayName = string.IsNullOrWhiteSpace(userDisplayName) ? "Користувач" : userDisplayName,
            Text = text?.Trim() ?? string.Empty,
            Rating = System.Math.Clamp(rating, 1, 5),
            CreatedAt = System.DateTime.UtcNow
        };
        db.Reviews.Add(review);

        // Product.Rating / ReviewCount are maintained by SQLite triggers — see
        // Fts5Initializer.EnsureProductAggregateTriggers — so no manual recompute here.
        db.SaveChanges();
        LoadReferenceData();
        return review;
    }

    public IReadOnlyList<Review> GetReviewsByUser(int userId)
    {
        if (userId <= 0) return System.Array.Empty<Review>();
        using var db = _dbFactory.CreateDbContext();
        // Eager-load Product+Album+Artist so the profile UI can show context for each review.
        var reviews = db.Reviews.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        if (reviews.Count == 0) return reviews;

        var productIds = reviews.Select(r => r.ProductId).Distinct().ToList();
        var productLookup = db.Products.AsNoTracking()
            .Include(p => p.Album)!.ThenInclude(a => a!.Artist)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionary(p => p.Id);

        foreach (var r in reviews)
        {
            if (productLookup.TryGetValue(r.ProductId, out var prod))
                r.Product = prod;
        }
        return reviews;
    }

    public bool UpdateReview(int reviewId, int userId, string text, int rating)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.Reviews.FirstOrDefault(r => r.Id == reviewId && r.UserId == userId);
        if (row is null) return false;

        row.Text = text?.Trim() ?? string.Empty;
        row.Rating = System.Math.Clamp(rating, 1, 5);
        // Product aggregates are maintained by SQLite triggers.

        db.SaveChanges();
        LoadReferenceData();
        return true;
    }

    public bool DeleteReview(int reviewId, int userId)
    {
        using var db = _dbFactory.CreateDbContext();
        var row = db.Reviews.FirstOrDefault(r => r.Id == reviewId && r.UserId == userId);
        if (row is null) return false;

        db.Reviews.Remove(row);
        // Product aggregates are maintained by SQLite triggers.

        db.SaveChanges();
        LoadReferenceData();
        return true;
    }

    public bool IsInWishlist(int userId, int productId)
    {
        if (userId <= 0) return false;
        using var db = _dbFactory.CreateDbContext();
        return db.Wishlists.AsNoTracking().Any(w => w.UserId == userId && w.ProductId == productId);
    }

    public void AddToWishlist(int userId, int productId)
    {
        if (userId <= 0) return;
        using var db = _dbFactory.CreateDbContext();
        var exists = db.Wishlists.Any(w => w.UserId == userId && w.ProductId == productId);
        if (exists) return;
        db.Wishlists.Add(new Wishlist
        {
            UserId = userId,
            ProductId = productId,
            AddedAt = System.DateTime.UtcNow
        });
        db.SaveChanges();
    }

    public void RemoveFromWishlist(int userId, int productId)
    {
        if (userId <= 0) return;
        using var db = _dbFactory.CreateDbContext();
        var row = db.Wishlists.FirstOrDefault(w => w.UserId == userId && w.ProductId == productId);
        if (row is null) return;
        db.Wishlists.Remove(row);
        db.SaveChanges();
    }

    public Product AddProduct(ProductDraft draft)
    {
        using var db = _dbFactory.CreateDbContext();
        using var tx = db.Database.BeginTransaction();

        int artistId = ResolveArtistId(db, draft);
        int genreId = ResolveGenreId(db, draft);
        int albumId = ResolveAlbumId(db, draft, artistId, genreId);

        var product = new Product
        {
            AlbumId = albumId,
            Format = draft.Format,
            Price = draft.Price,
            Stock = draft.Stock,
            ReleaseYear = draft.ReleaseYear,
            Label = string.IsNullOrWhiteSpace(draft.Label) ? null : draft.Label.Trim(),
            IsActive = draft.IsActive,
            // Aggregates (SalesCount/Rating/ReviewCount) start at 0 and are maintained
            // by SQLite triggers as Reviews and OrderItems get inserted.
        };
        db.Products.Add(product);
        db.SaveChanges();

        // Track paths live on the Track model; if SamplePath/FullPath are provided
        // we set them on every track of the album to keep playback wired up.
        if (!string.IsNullOrWhiteSpace(draft.SamplePath) || !string.IsNullOrWhiteSpace(draft.FullPath))
        {
            var tracks = db.Tracks.Where(t => t.AlbumId == albumId).ToList();
            foreach (var t in tracks)
            {
                if (!string.IsNullOrWhiteSpace(draft.SamplePath)) t.SamplePath = draft.SamplePath;
                if (!string.IsNullOrWhiteSpace(draft.FullPath)) t.FullPath = draft.FullPath;
            }
            db.SaveChanges();
        }

        tx.Commit();
        LoadReferenceData();
        return product;
    }

    private static int ResolveArtistId(MusicStoreDbContext db, ProductDraft d)
    {
        if (d.ExistingArtistId is int aid) return aid;
        var name = (d.NewArtistName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Потрібно вибрати або створити виконавця");
        var existing = db.Artists.FirstOrDefault(a => a.Name == name);
        if (existing is not null) return existing.Id;
        var fresh = new Artist { Name = name };
        db.Artists.Add(fresh);
        db.SaveChanges();
        return fresh.Id;
    }

    private static int ResolveGenreId(MusicStoreDbContext db, ProductDraft d)
    {
        if (d.ExistingGenreId is int gid) return gid;
        var name = (d.NewGenreName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Потрібно вибрати або створити жанр");
        var existing = db.Genres.FirstOrDefault(g => g.Name == name);
        if (existing is not null) return existing.Id;
        var fresh = new Genre { Name = name };
        db.Genres.Add(fresh);
        db.SaveChanges();
        return fresh.Id;
    }

    private static int ResolveAlbumId(MusicStoreDbContext db, ProductDraft d, int artistId, int genreId)
    {
        if (d.ExistingAlbumId is int alid)
        {
            // If the existing album has no AlbumGenres yet (legacy), attach the
            // resolved genre as its primary now.
            if (!db.AlbumGenres.Any(ag => ag.AlbumId == alid))
            {
                db.AlbumGenres.Add(new AlbumGenre { AlbumId = alid, GenreId = genreId, IsPrimary = true });
                db.SaveChanges();
            }
            return alid;
        }
        var title = (d.NewAlbumTitle ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(title))
            throw new ArgumentException("Потрібно вибрати або створити альбом");
        var fresh = new Album
        {
            ArtistId = artistId,
            Title = title,
            Year = d.NewAlbumYear > 0 ? d.NewAlbumYear : DateTime.UtcNow.Year,
            CoverPath = string.IsNullOrWhiteSpace(d.CoverPath) ? null : d.CoverPath,
            Description = d.NewAlbumDescription
        };
        db.Albums.Add(fresh);
        db.SaveChanges();
        db.AlbumGenres.Add(new AlbumGenre { AlbumId = fresh.Id, GenreId = genreId, IsPrimary = true });
        db.SaveChanges();
        return fresh.Id;
    }

    public void UpdateProduct(int productId, ProductFormat format, decimal price, int stock, int releaseYear,
        string? label, string? samplePath, string? fullPath, bool isActive)
    {
        using var db = _dbFactory.CreateDbContext();
        var product = db.Products.FirstOrDefault(p => p.Id == productId);
        if (product is null) return;

        product.Format = format;
        product.Price = price;
        product.Stock = stock;
        product.ReleaseYear = releaseYear;
        product.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        product.IsActive = isActive;

        if (!string.IsNullOrWhiteSpace(samplePath) || !string.IsNullOrWhiteSpace(fullPath))
        {
            var tracks = db.Tracks.Where(t => t.AlbumId == product.AlbumId).ToList();
            foreach (var t in tracks)
            {
                if (!string.IsNullOrWhiteSpace(samplePath)) t.SamplePath = samplePath;
                if (!string.IsNullOrWhiteSpace(fullPath)) t.FullPath = fullPath;
            }
        }

        db.SaveChanges();
        LoadReferenceData();
    }

    public void SetProductActive(int productId, bool isActive)
    {
        using var db = _dbFactory.CreateDbContext();
        var product = db.Products.FirstOrDefault(p => p.Id == productId);
        if (product is null) return;
        product.IsActive = isActive;
        db.SaveChanges();
        LoadReferenceData();
    }

    public void UpdateOrderStatus(int orderId, OrderStatus status)
    {
        using var db = _dbFactory.CreateDbContext();
        var order = db.Orders.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;
        order.Status = status;
        db.SaveChanges();
    }

    public IReadOnlyList<User> GetUsers()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Users.AsNoTracking().OrderBy(u => u.Id).ToList();
    }

    public void SetUserRole(int userId, UserRole role)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return;
        user.Role = role;
        db.SaveChanges();
    }

    public RevenueReport RevenueForPeriod(DateTime from, DateTime to)
    {
        using var db = _dbFactory.CreateDbContext();
        var inclusiveTo = to.Date.AddDays(1).AddTicks(-1);
        var orders = db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= from.Date && o.CreatedAt <= inclusiveTo
                        && o.Status == OrderStatus.Completed)
            .ToList();
        return new RevenueReport(orders.Sum(o => o.TotalAmount), orders.Count, from.Date, to.Date);
    }

    public void ExportOrdersToExcel(string path)
    {
        using var db = _dbFactory.CreateDbContext();
        var orders = db.Orders.AsNoTracking()
            .Include(o => o.Items)!.ThenInclude(i => i.Product)!.ThenInclude(p => p!.Album)!.ThenInclude(a => a!.Artist)
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Замовлення");

        var headers = new[] { "№ замовлення", "Дата", "Користувач", "Статус", "Кількість позицій", "Сума, ₴" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        int row = 2;
        foreach (var o in orders)
        {
            ws.Cell(row, 1).Value = o.Id;
            ws.Cell(row, 2).Value = o.CreatedAt;
            ws.Cell(row, 2).Style.DateFormat.Format = "yyyy-mm-dd HH:mm";
            ws.Cell(row, 3).Value = o.UserId;
            ws.Cell(row, 4).Value = o.Status.ToString();
            ws.Cell(row, 5).Value = o.Items.Count;
            ws.Cell(row, 6).Value = o.TotalAmount;
            row++;
        }

        var detailsWs = wb.AddWorksheet("Позиції");
        var detailHeaders = new[] { "№ замовлення", "Дата", "Альбом", "Виконавець", "Формат", "К-ть", "Ціна, ₴", "Сума, ₴" };
        for (int i = 0; i < detailHeaders.Length; i++)
        {
            detailsWs.Cell(1, i + 1).Value = detailHeaders[i];
            detailsWs.Cell(1, i + 1).Style.Font.Bold = true;
        }
        int dRow = 2;
        foreach (var o in orders)
        {
            foreach (var i in o.Items)
            {
                detailsWs.Cell(dRow, 1).Value = o.Id;
                detailsWs.Cell(dRow, 2).Value = o.CreatedAt;
                detailsWs.Cell(dRow, 2).Style.DateFormat.Format = "yyyy-mm-dd";
                detailsWs.Cell(dRow, 3).Value = i.Product?.Album?.Title ?? "—";
                detailsWs.Cell(dRow, 4).Value = i.Product?.Album?.Artist?.Name ?? "—";
                detailsWs.Cell(dRow, 5).Value = i.Product?.Format.ToString() ?? "—";
                detailsWs.Cell(dRow, 6).Value = i.Quantity;
                detailsWs.Cell(dRow, 7).Value = i.UnitPrice;
                detailsWs.Cell(dRow, 8).Value = i.LineTotal;
                dRow++;
            }
        }

        ws.Columns().AdjustToContents();
        detailsWs.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    public void ExportProductsToCsv(string path)
    {
        using var db = _dbFactory.CreateDbContext();
        var products = db.Products.AsNoTracking()
            .Include(p => p.Album)!.ThenInclude(a => a!.Artist)
            .Include(p => p.Album)!.ThenInclude(a => a!.AlbumGenres)!.ThenInclude(ag => ag.Genre)
            .OrderBy(p => p.Id)
            .ToList();

        using var w = new StreamWriter(path, append: false, encoding: System.Text.Encoding.UTF8);
        w.WriteLine("Id;Виконавець;Альбом;Жанр;Рік;Формат;Ціна;Залишок;Активний;Продажі;Рейтинг");
        foreach (var p in products)
        {
            var artist = Csv(p.Album?.Artist?.Name);
            var album = Csv(p.Album?.Title);
            var genre = Csv(p.Album?.Genre?.Name);
            var format = p.Format == ProductFormat.Vinyl ? "LP" : "CD";
            w.WriteLine($"{p.Id};{artist};{album};{genre};{p.Album?.Year};{format};{p.Price:0.00};{p.Stock};{(p.IsActive ? "так" : "ні")};{p.SalesCount};{p.Rating:0.00}");
        }
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public void RefreshReferenceData() => LoadReferenceData();

    private void LoadReferenceData()
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();

            _genres = db.Genres.AsNoTracking().OrderBy(g => g.Id).ToList();
            _artists = db.Artists.AsNoTracking().OrderBy(a => a.Id).ToList();

            _albums = db.Albums.AsNoTracking()
                .Include(a => a.Artist)
                .Include(a => a.Tracks)
                .Include(a => a.AlbumGenres).ThenInclude(ag => ag.Genre)
                .OrderBy(a => a.Id)
                .ToList();

            _products = db.Products.AsNoTracking()
                .Include(p => p.Album)!.ThenInclude(a => a!.Artist)
                .Include(p => p.Album)!.ThenInclude(a => a!.Tracks)
                .Include(p => p.Album)!.ThenInclude(a => a!.AlbumGenres).ThenInclude(ag => ag.Genre)
                .OrderBy(p => p.Id)
                .ToList();

            _reviews = db.Reviews.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToList();
        }
        catch
        {
            // design-time / DB not provisioned — leave lists empty
        }
    }

    private IReadOnlyList<Order> GetOrdersInternal(int? userId)
    {
        using var db = _dbFactory.CreateDbContext();
        var query = db.Orders.AsNoTracking()
            .Include(o => o.Items)!.ThenInclude(i => i.Product)!.ThenInclude(p => p!.Album)!.ThenInclude(a => a!.Artist)
            .AsQueryable();
        if (userId is int u) query = query.Where(o => o.UserId == u);
        return query.OrderByDescending(o => o.CreatedAt).ToList();
    }
}
