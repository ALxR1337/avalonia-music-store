using System;
using System.Collections.Generic;
using System.Linq;
using MusicApp.Models;

namespace MusicApp.Services;

internal static class SampleData
{
    public static (List<Genre> Genres, List<Artist> Artists, List<Album> Albums,
                   List<Product> Products, List<Review> Reviews, List<Order> Orders)
        Build()
    {
        var genres = new List<Genre>
        {
            new() { Id = 1, Name = "Rock" },
            new() { Id = 2, Name = "Jazz" },
            new() { Id = 3, Name = "Hip-Hop" },
            new() { Id = 4, Name = "Electronic" },
            new() { Id = 5, Name = "Classical" },
            new() { Id = 6, Name = "Folk" },
            new() { Id = 7, Name = "Experimental" },
            new() { Id = 8, Name = "Indie" }
        };

        var artists = new List<Artist>
        {
            new() { Id = 1, Name = "Death Grips", Aliases = "DG, MC Ride", Country = "USA" },
            new() { Id = 2, Name = "Burial", Country = "UK" },
            new() { Id = 3, Name = "Radiohead", Country = "UK" },
            new() { Id = 4, Name = "Бьорк", Aliases = "Björk, Bjork", Country = "Iceland" },
            new() { Id = 5, Name = "Aphex Twin", Country = "UK" },
            new() { Id = 6, Name = "Бортнянський", Country = "Ukraine" },
            new() { Id = 7, Name = "ДахаБраха", Country = "Ukraine" },
            new() { Id = 8, Name = "Океан Ельзи", Country = "Ukraine" }
        };

        var albums = new List<Album>
        {
            new() { Id = 1, ArtistId = 1, Title = "The Money Store", Year = 2012, GenreId = 7, Description = "Experimental hip-hop landmark." },
            new() { Id = 2, ArtistId = 1, Title = "Exmilitary", Year = 2011, GenreId = 7 },
            new() { Id = 3, ArtistId = 2, Title = "Untrue", Year = 2007, GenreId = 4 },
            new() { Id = 4, ArtistId = 3, Title = "In Rainbows", Year = 2007, GenreId = 1 },
            new() { Id = 5, ArtistId = 4, Title = "Homogenic", Year = 1997, GenreId = 4 },
            new() { Id = 6, ArtistId = 5, Title = "Selected Ambient Works 85-92", Year = 1992, GenreId = 4 },
            new() { Id = 7, ArtistId = 7, Title = "Шлях", Year = 2016, GenreId = 6 },
            new() { Id = 8, ArtistId = 8, Title = "Земля", Year = 2013, GenreId = 1 }
        };

        // wire references
        foreach (var album in albums)
        {
            album.Artist = artists.First(a => a.Id == album.ArtistId);
            album.Genre = genres.First(g => g.Id == album.GenreId);
            album.Tracks = BuildTracks(album.Id);
        }

        var rnd = new Random(42);
        var products = new List<Product>();
        var pid = 1;
        foreach (var album in albums)
        {
            // vinyl + CD versions
            products.Add(new Product
            {
                Id = pid++,
                AlbumId = album.Id,
                Album = album,
                Format = ProductFormat.Vinyl,
                Price = 350 + rnd.Next(0, 250),
                Stock = rnd.Next(0, 8),
                ReleaseYear = album.Year,
                Label = "Hi-Fidelity Records",
                Rating = Math.Round(3.5 + rnd.NextDouble() * 1.5, 1),
                ReviewCount = rnd.Next(2, 60),
                SalesCount = rnd.Next(5, 250)
            });
            products.Add(new Product
            {
                Id = pid++,
                AlbumId = album.Id,
                Album = album,
                Format = ProductFormat.CD,
                Price = 220 + rnd.Next(0, 150),
                Stock = rnd.Next(0, 14),
                ReleaseYear = album.Year,
                Label = "Hi-Fidelity Records",
                Rating = Math.Round(3.5 + rnd.NextDouble() * 1.5, 1),
                ReviewCount = rnd.Next(2, 60),
                SalesCount = rnd.Next(5, 250)
            });
        }

        var reviews = new List<Review>
        {
            new() { Id = 1, ProductId = 1, UserId = 1, UserDisplayName = "Іван П.",
                    Rating = 5, CreatedAt = new DateTime(2025, 4, 12),
                    Text = "Найкращий альбом гурту, мастеринг винілу — на висоті." },
            new() { Id = 2, ProductId = 1, UserId = 2, UserDisplayName = "Олена С.",
                    Rating = 4, CreatedAt = new DateTime(2025, 4, 3),
                    Text = "Видано якісно, обкладинка з шорсткою фактурою. Звук пресу — добротний." },
            new() { Id = 3, ProductId = 3, UserId = 3, UserDisplayName = "Roman K.",
                    Rating = 5, CreatedAt = new DateTime(2025, 3, 27),
                    Text = "Burial звучить як треба — на вінілі це інше відчуття часу." }
        };

        var orders = new List<Order>
        {
            new()
            {
                Id = 1, UserId = 1, CreatedAt = new DateTime(2025, 5, 1),
                Status = OrderStatus.Completed, TotalAmount = 1010m,
                Items = new()
                {
                    new() { Id = 1, OrderId = 1, ProductId = 1, Product = products[0], Quantity = 1, UnitPrice = products[0].Price },
                    new() { Id = 2, OrderId = 1, ProductId = 4, Product = products[3], Quantity = 2, UnitPrice = products[3].Price }
                }
            },
            new()
            {
                Id = 2, UserId = 1, CreatedAt = new DateTime(2025, 5, 12),
                Status = OrderStatus.Processing, TotalAmount = 460m,
                Items = new()
                {
                    new() { Id = 3, OrderId = 2, ProductId = 8, Product = products[7], Quantity = 1, UnitPrice = products[7].Price }
                }
            }
        };

        return (genres, artists, albums, products, reviews, orders);
    }

    private static List<Track> BuildTracks(int albumId)
    {
        var titles = albumId switch
        {
            1 => new[] { "Get Got", "The Fever (Aye Aye)", "Lost Boys", "Blackjack", "Hustle Bones",
                         "I've Seen Footage", "Double Helix", "Hacker" },
            2 => new[] { "Beware", "Guillotine", "Spread Eagle Cross the Block", "Lord of the Game" },
            3 => new[] { "Archangel", "Near Dark", "Ghost Hardware", "Endorphin", "Etched Headplate" },
            4 => new[] { "15 Step", "Bodysnatchers", "Nude", "Weird Fishes/Arpeggi", "All I Need" },
            5 => new[] { "Hunter", "Jóga", "Unravel", "Bachelorette", "All Neon Like" },
            6 => new[] { "Xtal", "Tha", "Pulsewidth", "Ageispolis", "I" },
            7 => new[] { "Specially for You", "Carpathian Rap", "Ягудки", "Колискова" },
            _ => new[] { "Track 1", "Track 2", "Track 3", "Track 4", "Track 5" }
        };

        var list = new List<Track>();
        var pos = 1;
        var rnd = new Random(albumId * 17);
        foreach (var t in titles)
        {
            list.Add(new Track
            {
                Id = albumId * 100 + pos,
                AlbumId = albumId,
                Position = pos,
                Title = t,
                Duration = TimeSpan.FromSeconds(120 + rnd.Next(40, 200))
            });
            pos++;
        }
        return list;
    }
}
