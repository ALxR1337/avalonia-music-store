using System.Collections.Generic;
using System.Linq;
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
                .Include(a => a.Genre)
                .Include(a => a.Tracks)
                .OrderBy(a => a.Id)
                .ToList();

            _products = db.Products.AsNoTracking()
                .Include(p => p.Album)!.ThenInclude(a => a!.Artist)
                .Include(p => p.Album)!.ThenInclude(a => a!.Genre)
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
