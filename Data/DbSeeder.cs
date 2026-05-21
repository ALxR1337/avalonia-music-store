using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.Data;

public static class DbSeeder
{
    public static void EnsureSeeded(MusicStoreDbContext db)
    {
        db.Database.Migrate();

        if (db.Users.Any() || db.Artists.Any() || db.Albums.Any())
            return;

        var (genres, artists, albums, products, reviews, orders) = SampleData.Build();

        var demoUsers = new[]
        {
            new User
            {
                Id = 1,
                Username = "admin",
                Email = "admin@musicstore.local",
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin")
            },
            new User
            {
                Id = 2,
                Username = "demo",
                Email = "demo@musicstore.local",
                Role = UserRole.Customer,
                CreatedAt = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo")
            }
        };

        // Strip in-memory navigation backrefs so EF inserts without duplicate entities.
        foreach (var album in albums)
        {
            album.Artist = null;
            album.Genre = null;
        }
        foreach (var product in products) product.Album = null;
        foreach (var order in orders)
            foreach (var item in order.Items)
                item.Product = null;

        using var tx = db.Database.BeginTransaction();

        db.Genres.AddRange(genres);
        db.Artists.AddRange(artists);
        db.SaveChanges();

        db.Albums.AddRange(albums);
        db.SaveChanges();

        db.Products.AddRange(products);
        db.Users.AddRange(demoUsers);
        db.SaveChanges();

        db.Reviews.AddRange(reviews);
        db.Orders.AddRange(orders);
        db.SaveChanges();

        tx.Commit();
    }
}
