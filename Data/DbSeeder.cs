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

        WipeLegacyIfPresent(db);

        if (db.Users.Any() || db.Artists.Any() || db.Albums.Any())
            return;

        var (genres, artists, albums, products, reviews, orders, albumGenres) = SampleData.Build();

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
            album.Artist = null;
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
        db.AlbumGenres.AddRange(albumGenres);
        db.SaveChanges();

        tx.Commit();
    }

    // Wipes the DB when an outdated seed is detected so the current SampleData can populate.
    // Two trigger conditions:
    //   1. The original demo seed (Death Grips / Burial / Radiohead placeholders).
    //   2. A previous real-music seed that used granular genres (Industrial, Modal Jazz, Trap…).
    //      The current seed locks the genre table to the canonical 8.
    // Idempotent: no-op once the latest seed is in place.
    private static void WipeLegacyIfPresent(MusicStoreDbContext db)
    {
        var legacyDemo = db.Albums.Any(a => a.Title == "The Money Store"
                                          || a.Title == "Untrue"
                                          || a.Title == "In Rainbows");

        var basicGenres = new[] {
            "Rock", "Jazz", "Hip-Hop", "Electronic", "Classical", "Folk", "Experimental", "Indie"
        };
        var hasGranularGenre = db.Genres.Any() &&
                               db.Genres.AsEnumerable()
                                        .Any(g => !basicGenres.Contains(g.Name, StringComparer.OrdinalIgnoreCase));

        if (!legacyDemo && !hasGranularGenre) return;

        db.OrderItems.RemoveRange(db.OrderItems);
        db.Orders.RemoveRange(db.Orders);
        db.Reviews.RemoveRange(db.Reviews);
        db.CartItems.RemoveRange(db.CartItems);
        db.Wishlists.RemoveRange(db.Wishlists);
        db.SavedSearches.RemoveRange(db.SavedSearches);
        db.SearchHistory.RemoveRange(db.SearchHistory);
        db.PlayerSettings.RemoveRange(db.PlayerSettings);
        db.PlaylistTracks.RemoveRange(db.PlaylistTracks);
        db.Playlists.RemoveRange(db.Playlists);
        db.Products.RemoveRange(db.Products);
        db.Tracks.RemoveRange(db.Tracks);
        db.AlbumGenres.RemoveRange(db.AlbumGenres);
        db.Albums.RemoveRange(db.Albums);
        db.Artists.RemoveRange(db.Artists);
        db.Genres.RemoveRange(db.Genres);
        db.Users.RemoveRange(db.Users);
        db.SaveChanges();
    }
}
