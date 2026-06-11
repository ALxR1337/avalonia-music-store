using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.Data;

public static class DbSeeder
{
    // The seeded demo customer (`demo` / `demo`) — the account you log in as to
    // exercise the per-user features. Its activity tables are populated by
    // SeedTestActivity below.
    private const int DemoUserId = 2;

    public static void EnsureSeeded(MusicStoreDbContext db)
    {
        db.Database.Migrate();

        WipeLegacyIfPresent(db);

        if (!(db.Users.Any() || db.Artists.Any() || db.Albums.Any()))
            SeedFresh(db);

        // Runs for both a freshly-seeded and a pre-existing database so artist
        // avatars and the bundled fallback album covers land even on installs
        // whose tables predate them.
        BackfillArtistPhotos(db);
        BackfillAlbumCovers(db);

        // Demo activity (likes, playlists, cart, wishlist, saved searches, extra
        // orders) so every customer-facing feature and the admin analytics have
        // something to show. Idempotent and safe on a pre-existing database.
        SeedTestActivity(db);
    }

    private static void SeedFresh(MusicStoreDbContext db)
    {
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

    // Populates the per-user activity tables with demo content so every feature is
    // testable out of the box: the `demo` customer gets liked albums/tracks,
    // playlists, a cart, a wishlist, saved searches and search history, and a small
    // fleet of extra customers place orders across several months so the admin
    // statistics (KPIs, period revenue, Top-10) and catalog ratings are populated.
    //
    // Idempotent: gated on whether the demo customer already owns a playlist, so it
    // runs exactly once and never clobbers activity the user created through the UI.
    // Product aggregates (Rating/ReviewCount/SalesCount) are left to the SQLite
    // triggers and the Fts5Initializer backfill that runs right after seeding.
    private static void SeedTestActivity(MusicStoreDbContext db)
    {
        // Needs the base catalog and the demo account in place.
        if (!db.Users.Any(u => u.Id == DemoUserId)) return;
        // Already seeded (or the demo user built their own playlist) — leave it be.
        if (db.Playlists.Any(p => p.UserId == DemoUserId)) return;

        var products = db.Products
            .Include(p => p.Album).ThenInclude(a => a!.Artist)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .ToList();
        if (products.Count == 0) return;

        var demoUser = db.Users.First(u => u.Id == DemoUserId);
        var albums = db.Albums.OrderBy(a => a.Id).ToList();
        var tracks = db.Tracks.OrderBy(t => t.AlbumId).ThenBy(t => t.Position).ToList();
        var hasTracks = tracks.Count > 0;

        // --- resolvers (distinctive title substrings keep these stable if Ids shift) ---
        Product? FindProduct(string albumContains, ProductFormat fmt) =>
            products.FirstOrDefault(p => p.Format == fmt
                && (p.Album?.Title.Contains(albumContains, StringComparison.OrdinalIgnoreCase) ?? false));
        Album? FindAlbum(string titleContains) =>
            albums.FirstOrDefault(a => a.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));

        using var tx = db.Database.BeginTransaction();

        // --- extra demo customers (Ids auto-assigned; referenced via the saved objects) ---
        var anna = new User { Username = "anna_lviv", Email = "anna.lviv@example.com",
            Role = UserRole.Customer, CreatedAt = new DateTime(2026, 1, 18),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo1234") };
        var oleh = new User { Username = "oleh_kyiv", Email = "oleh.kyiv@example.com",
            Role = UserRole.Customer, CreatedAt = new DateTime(2026, 2, 2),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo1234") };
        var maryna = new User { Username = "maryna_odesa", Email = "maryna.odesa@example.com",
            Role = UserRole.Customer, CreatedAt = new DateTime(2026, 2, 27),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo1234") };
        db.Users.AddRange(anna, oleh, maryna);
        db.SaveChanges();

        // --- orders across several months: Completed ones drive revenue + Top-10;
        // Processing/New/Cancelled exercise the status filters but are not income. ---
        OrderItem? Line(string albumContains, ProductFormat fmt, int qty)
        {
            var p = FindProduct(albumContains, fmt);
            if (p is null) return null;
            return new OrderItem
            {
                ProductId = p.Id,
                Quantity = qty,
                UnitPrice = p.Price,
                ProductTitle = $"{p.Album?.Title ?? "—"} ({p.FormatBadge})",
                AlbumTitle = p.Album?.Title ?? "—",
                ArtistName = p.Album?.Artist?.Name ?? "—",
                FormatLabel = p.FormatBadge,
            };
        }

        Order? Build(User u, string address, DateTime date, OrderStatus status,
                     params (string album, ProductFormat fmt, int qty)[] lines)
        {
            var items = lines.Select(l => Line(l.album, l.fmt, l.qty)).Where(i => i is not null).Cast<OrderItem>().ToList();
            if (items.Count == 0) return null;
            var order = new Order
            {
                UserId = u.Id,
                CreatedAt = date,
                Status = status,
                Currency = "UAH",
                UserEmail = u.Email,
                ShippingAddress = address,
                Items = items,
            };
            order.TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity);
            return order;
        }

        const string kyiv = "м. Київ, вул. Хрещатик, 12, кв. 7";
        const string lviv = "м. Львів, пл. Ринок, 3, кв. 2";
        const string odesa = "м. Одеса, вул. Дерибасівська, 18, кв. 11";

        var orders = new[]
        {
            // within the dashboard's default last-month window (≈ 2026-04-29 … 2026-05-29)
            Build(demoUser, kyiv,  new DateTime(2026, 5, 3),  OrderStatus.Completed,  ("Dark Side", ProductFormat.Vinyl, 1), ("Kind of Blue", ProductFormat.CD, 1)),
            Build(anna,     lviv,  new DateTime(2026, 5, 6),  OrderStatus.Completed,  ("Abbey Road", ProductFormat.Vinyl, 1)),
            Build(oleh,     kyiv,  new DateTime(2026, 5, 11), OrderStatus.Completed,  ("To Pimp", ProductFormat.Vinyl, 1), ("Yeezus", ProductFormat.CD, 2)),
            Build(maryna,   odesa, new DateTime(2026, 5, 16), OrderStatus.Completed,  ("Discovery", ProductFormat.Vinyl, 1), ("Blonde", ProductFormat.Vinyl, 1)),
            Build(demoUser, kyiv,  new DateTime(2026, 5, 20), OrderStatus.Completed,  ("Illmatic", ProductFormat.Vinyl, 1), ("A Love Supreme", ProductFormat.CD, 1)),
            Build(anna,     lviv,  new DateTime(2026, 5, 24), OrderStatus.Completed,  ("Led Zeppelin IV", ProductFormat.Vinyl, 2)),
            Build(oleh,     kyiv,  new DateTime(2026, 5, 27), OrderStatus.Processing, ("UTOPIA", ProductFormat.Vinyl, 1)),
            Build(demoUser, kyiv,  new DateTime(2026, 5, 28), OrderStatus.New,        ("Time Out", ProductFormat.CD, 1)),
            // earlier history for all-time revenue + a deeper Top-10
            Build(anna,     lviv,  new DateTime(2026, 2, 20), OrderStatus.Completed,  ("Dark Side", ProductFormat.CD, 1), ("Abbey Road", ProductFormat.CD, 1)),
            Build(maryna,   odesa, new DateTime(2026, 3, 12), OrderStatus.Completed,  ("Kind of Blue", ProductFormat.Vinyl, 1), ("A Love Supreme", ProductFormat.Vinyl, 1)),
            Build(oleh,     kyiv,  new DateTime(2026, 4, 8),  OrderStatus.Completed,  ("To Pimp", ProductFormat.CD, 1), ("Illmatic", ProductFormat.CD, 1)),
            Build(demoUser, kyiv,  new DateTime(2026, 4, 15), OrderStatus.Cancelled,  ("Veteran", ProductFormat.Vinyl, 1)),
            Build(maryna,   odesa, new DateTime(2026, 4, 22), OrderStatus.Completed,  ("Discovery", ProductFormat.CD, 2), ("Queen Is Dead", ProductFormat.Vinyl, 1)),
        }.Where(o => o is not null).Cast<Order>().ToList();
        db.Orders.AddRange(orders);

        // --- reviews from the demo customers (drive Rating/ReviewCount + the rating facet) ---
        void AddReview(string albumContains, ProductFormat fmt, int userId, string name, int rating, DateTime date, string text)
        {
            var p = FindProduct(albumContains, fmt);
            if (p is null) return;
            db.Reviews.Add(new Review { ProductId = p.Id, UserId = userId, UserDisplayName = name,
                Rating = rating, CreatedAt = date, Text = text });
        }
        AddReview("Dark Side", ProductFormat.Vinyl, anna.Id, "Анна Л.", 5, new DateTime(2026, 3, 1),
            "Класика прогресиву — пресинг бездоганний, динаміка дихає.");
        AddReview("Abbey Road", ProductFormat.Vinyl, oleh.Id, "Олег К.", 4, new DateTime(2026, 5, 9),
            "Тепле звучання, медлі на другій стороні — окремий ритуал.");
        AddReview("To Pimp", ProductFormat.Vinyl, maryna.Id, "Марина О.", 5, new DateTime(2026, 4, 11),
            "Найважливіший альбом десятиліття, на вінілі ще густіший.");
        AddReview("Kind of Blue", ProductFormat.Vinyl, DemoUserId, "demo", 5, new DateTime(2026, 3, 18),
            "Еталон джазу, без якого не починається жодна колекція.");
        AddReview("Discovery", ProductFormat.Vinyl, anna.Id, "Анна Л.", 4, new DateTime(2026, 5, 17),
            "Ностальгія у чистому вигляді, бас качає.");
        AddReview("Illmatic", ProductFormat.Vinyl, oleh.Id, "Олег К.", 5, new DateTime(2026, 4, 9),
            "Десять ідеальних треків, нічого зайвого.");

        // --- demo customer: liked albums ---
        var likedAlbumTitles = new[] { "Yeezus", "Blonde", "To Pimp", "Dark Side",
                                       "Kind of Blue", "Discovery", "Illmatic", "Abbey Road" };
        foreach (var title in likedAlbumTitles)
        {
            var album = FindAlbum(title);
            if (album is null) continue;
            db.AlbumLikes.Add(new AlbumLike { UserId = DemoUserId, AlbumId = album.Id,
                CreatedAt = new DateTime(2026, 5, 22) });
        }

        // --- demo customer: liked tracks + playlists (track-dependent → guarded) ---
        IEnumerable<Track> TracksOf(params string[] albumTitles)
        {
            var ids = albumTitles.Select(FindAlbum).Where(a => a is not null).Select(a => a!.Id).ToHashSet();
            return tracks.Where(t => ids.Contains(t.AlbumId));
        }

        if (hasTracks)
        {
            foreach (var t in tracks.Take(12))
                db.TrackLikes.Add(new TrackLike { UserId = DemoUserId, TrackId = t.Id,
                    CreatedAt = new DateTime(2026, 5, 22) });
        }

        Playlist MakePlaylist(string name, DateTime created, IEnumerable<Track> picks)
        {
            var pl = new Playlist { UserId = DemoUserId, Name = name, CreatedAt = created };
            int pos = 1;
            foreach (var t in picks.Take(12))
                pl.Tracks.Add(new PlaylistTrack { TrackId = t.Id, Position = pos++ });
            return pl;
        }

        var eveningPlaylist = MakePlaylist("Вечірній вініл", new DateTime(2026, 5, 10),
            hasTracks ? tracks.Take(10) : Enumerable.Empty<Track>());
        var jazzPlaylist = MakePlaylist("Джаз для фокусу", new DateTime(2026, 5, 12),
            hasTracks ? TracksOf("Kind of Blue", "A Love Supreme", "Time Out", "Mingus Ah Um") : Enumerable.Empty<Track>());
        var hipHopPlaylist = MakePlaylist("Сучасний хіп-хоп", new DateTime(2026, 5, 15),
            hasTracks ? TracksOf("To Pimp", "Yeezus", "Illmatic", "UTOPIA") : Enumerable.Empty<Track>());
        db.Playlists.AddRange(eveningPlaylist, jazzPlaylist, hipHopPlaylist);

        // --- demo customer: cart (distinct products) ---
        foreach (var (album, fmt, qty) in new[]
                 {
                     ("Abbey Road", ProductFormat.Vinyl, 1),
                     ("Discovery", ProductFormat.CD, 2),
                     ("Kind of Blue", ProductFormat.Vinyl, 1),
                 })
        {
            var p = FindProduct(album, fmt);
            if (p is null) continue;
            db.CartItems.Add(new CartItem { UserId = DemoUserId, ProductId = p.Id, Quantity = qty,
                AddedAt = new DateTime(2026, 5, 28) });
        }

        // --- demo customer: wishlist (distinct products, table empty for this user) ---
        foreach (var (album, fmt) in new[]
                 {
                     ("Dark Side", ProductFormat.Vinyl),
                     ("To Pimp", ProductFormat.Vinyl),
                     ("UTOPIA", ProductFormat.Vinyl),
                     ("Blonde", ProductFormat.Vinyl),
                 })
        {
            var p = FindProduct(album, fmt);
            if (p is null) continue;
            db.Wishlists.Add(new Wishlist { UserId = DemoUserId, ProductId = p.Id,
                AddedAt = new DateTime(2026, 5, 25) });
        }

        // --- demo customer: saved searches (DSL strings the search parser understands) ---
        foreach (var query in new[]
                 {
                     "жанр:\"Jazz\"",
                     "виконавець:\"Pink Floyd\" формат:lp",
                     "рейтинг:>=4",
                     "жанр:\"Electronic\" рік:1990..2005",
                 })
        {
            db.SavedSearches.Add(new SavedSearch { UserId = DemoUserId, Name = query,
                QueryJson = query, NotifyOnNew = false, CreatedAt = new DateTime(2026, 5, 24) });
        }

        // --- demo customer: recent search history ---
        db.SearchHistory.AddRange(
            new SearchHistory { UserId = DemoUserId, Query = "Kind of Blue", ResultCount = 1, ExecutedAt = new DateTime(2026, 5, 26) },
            new SearchHistory { UserId = DemoUserId, Query = "жанр:\"Hip-Hop\"", ResultCount = 6, ExecutedAt = new DateTime(2026, 5, 27) },
            new SearchHistory { UserId = DemoUserId, Query = "Beatles", ResultCount = 1, ExecutedAt = new DateTime(2026, 5, 28) },
            new SearchHistory { UserId = DemoUserId, Query = "рік:1970..1980", ResultCount = 7, ExecutedAt = new DateTime(2026, 5, 29) });

        // --- demo customer: player settings (no row exists for this user yet) ---
        if (!db.PlayerSettings.Any(s => s.UserId == DemoUserId))
        {
            db.PlayerSettings.Add(new PlayerSettings
            {
                UserId = DemoUserId,
                Volume = 70,
                RepeatMode = RepeatMode.All,
                ShuffleMode = true,
                LastTrackId = hasTracks ? eveningPlaylist.Tracks.FirstOrDefault()?.TrackId : null,
            });
        }

        db.SaveChanges();
        tx.Commit();
    }

    // Fills Artist.PhotoPath from the bundled avatar assets for any artist that
    // does not yet have a photo. Admin-set photos are left untouched, and an
    // artist with no matching asset keeps its null path (letter-placeholder
    // fallback). Idempotent: a no-op once every artist has a photo.
    private static void BackfillArtistPhotos(MusicStoreDbContext db)
    {
        var artists = db.Artists
            .Where(a => a.PhotoPath == null || a.PhotoPath == "")
            .ToList();
        if (artists.Count == 0)
            return;

        var changed = false;
        foreach (var artist in artists)
        {
            var asset = ArtistPhotoAssets.For(artist.Name);
            if (asset is null)
                continue;

            artist.PhotoPath = asset;
            changed = true;
        }

        if (changed)
            db.SaveChanges();
    }

    // Fills Album.CoverPath from a bundled cover asset for albums whose seed
    // folder shipped no usable image (covers we fetched and embedded under
    // Assets/covers/). Locally-scanned covers and admin uploads are untouched;
    // albums with no matching asset keep their null path. Idempotent.
    private static void BackfillAlbumCovers(MusicStoreDbContext db)
    {
        var albums = db.Albums
            .Include(a => a.Artist)
            .Where(a => a.CoverPath == null || a.CoverPath == "")
            .ToList();
        if (albums.Count == 0)
            return;

        var changed = false;
        foreach (var album in albums)
        {
            var asset = AlbumCoverAssets.For(album.Artist?.Name, album.Title);
            if (asset is null)
                continue;

            album.CoverPath = asset;
            changed = true;
        }

        if (changed)
            db.SaveChanges();
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

        // SAFETY GUARD: never destroy real user activity. The "granular genre" heuristic
        // above also fires when an admin legitimately adds a new genre through the product
        // form, which would otherwise wipe the whole database (orders, reviews, registered
        // users) on the next launch. Seeded demo accounts always have Id 1 (admin) and 2
        // (demo); anything authored by a user with a higher Id is real and must be kept.
        var hasRealUserData =
            db.Users.Any(u => u.Id > 2) ||
            db.Orders.Any(o => o.UserId > 2) ||
            db.Reviews.Any(r => r.UserId > 2);
        if (hasRealUserData) return;

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
