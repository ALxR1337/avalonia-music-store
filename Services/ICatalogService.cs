using System.Collections.Generic;
using MusicApp.Models;

namespace MusicApp.Services;

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
}
