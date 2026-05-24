using System;
using System.Collections.Generic;
using MusicApp.Models;

namespace MusicApp.Services;

public sealed record RevenueReport(decimal Total, int OrderCount, DateTime From, DateTime To);
public sealed record ProductDraft(
    int? ExistingAlbumId,
    string? NewAlbumTitle, int NewAlbumYear, string? NewAlbumDescription, string? CoverPath,
    int? ExistingArtistId, string? NewArtistName,
    int? ExistingGenreId, string? NewGenreName,
    ProductFormat Format, decimal Price, int Stock, int ReleaseYear, string? Label,
    string? SamplePath, string? FullPath, bool IsActive);

public interface ICatalogService
{
    IReadOnlyList<Genre> Genres { get; }
    IReadOnlyList<Artist> Artists { get; }
    IReadOnlyList<Album> Albums { get; }
    IReadOnlyList<Product> Products { get; }
    IReadOnlyList<Order> Orders { get; }
    IReadOnlyList<Review> Reviews { get; }

    Product? GetProduct(int id);
    Album? GetAlbum(int id);
    IReadOnlyList<Product> GetNewArrivals(int count = 8);
    IReadOnlyList<NewArrivalAlbum> GetNewArrivalAlbums(int count = 8);
    IReadOnlyList<Product> GetPopular(int count = 8);
    IReadOnlyList<Product> SearchProducts(string query);
    IReadOnlyList<Review> GetReviewsFor(int productId);
    IReadOnlyList<Order> GetOrdersFor(int userId);
    IReadOnlyList<Album> GetPurchasedAlbums(int userId);
    bool IsAlbumPurchased(int albumId, int userId);

    Product? GetSiblingProduct(int productId);
    Review AddReview(int productId, int userId, string userDisplayName, string text, int rating);
    IReadOnlyList<Review> GetReviewsByUser(int userId);
    bool UpdateReview(int reviewId, int userId, string text, int rating);
    bool DeleteReview(int reviewId, int userId);

    IReadOnlyList<Album> GetAlbumsByArtist(int artistId, int? excludeAlbumId = null);
    (double Avg, int Count) GetAlbumRating(int albumId);
    IReadOnlyList<Review> GetReviewsForAlbum(int albumId);
    int? GetPrimaryProductId(int albumId);

    bool IsInWishlist(int userId, int productId);
    void AddToWishlist(int userId, int productId);
    void RemoveFromWishlist(int userId, int productId);

    Product AddProduct(ProductDraft draft);
    void UpdateProduct(int productId, ProductFormat format, decimal price, int stock, int releaseYear,
        string? label, string? samplePath, string? fullPath, bool isActive);
    void SetProductActive(int productId, bool isActive);

    void UpdateOrderStatus(int orderId, OrderStatus status);
    IReadOnlyList<User> GetUsers();
    void SetUserRole(int userId, UserRole role);
    RevenueReport RevenueForPeriod(DateTime from, DateTime to);

    void ExportOrdersToExcel(string path);
    void ExportProductsToCsv(string path);
}
