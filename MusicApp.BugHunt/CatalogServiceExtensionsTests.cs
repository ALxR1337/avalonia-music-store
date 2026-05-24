using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;
using MusicApp.Services;
using Xunit;

namespace MusicApp.BugHunt;

public class CatalogServiceExtensionsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MusicStoreDbContextFactory _factory;
    private readonly CatalogService _catalog;
    private int _artistId;
    private int _albumAId;
    private int _albumBId;
    private int _productAId;
    private int _productBId;

    public CatalogServiceExtensionsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"catalog-ext-tests-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", _dbPath);
        _factory = new MusicStoreDbContextFactory();
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        Seed();
        _catalog = new CatalogService(_factory);
    }

    private void Seed()
    {
        using var db = _factory.CreateDbContext();
        var artist = new Artist { Name = "ArtistX" };
        db.Artists.Add(artist);
        db.SaveChanges();
        _artistId = artist.Id;

        var albumA = new Album { ArtistId = _artistId, Title = "Album A", Year = 2020 };
        var albumB = new Album { ArtistId = _artistId, Title = "Album B", Year = 2022 };
        db.Albums.AddRange(albumA, albumB);
        db.SaveChanges();
        _albumAId = albumA.Id;
        _albumBId = albumB.Id;

        var productA = new Product { AlbumId = _albumAId, Format = ProductFormat.CD, Price = 100, Stock = 5, ReleaseYear = 2020, IsActive = true };
        var productB = new Product { AlbumId = _albumAId, Format = ProductFormat.Vinyl, Price = 200, Stock = 3, ReleaseYear = 2020, IsActive = true };
        db.Products.AddRange(productA, productB);
        db.SaveChanges();
        _productAId = productA.Id;
        _productBId = productB.Id;

        var user = new User { Username = "u", PasswordHash = "x", Email = "u@u" };
        db.Users.Add(user);
        db.SaveChanges();

        db.Reviews.AddRange(
            new Review { ProductId = _productAId, UserId = user.Id, Text = "great", Rating = 5, UserDisplayName = "u" },
            new Review { ProductId = _productBId, UserId = user.Id, Text = "ok", Rating = 3, UserDisplayName = "u" });
        db.SaveChanges();
    }

    [Fact]
    public void GetAlbumsByArtist_returns_albums_excluding_current()
    {
        var others = _catalog.GetAlbumsByArtist(_artistId, excludeAlbumId: _albumAId);
        Assert.Single(others);
        Assert.Equal(_albumBId, others[0].Id);
    }

    [Fact]
    public void GetAlbumsByArtist_with_null_exclude_returns_all()
    {
        var all = _catalog.GetAlbumsByArtist(_artistId, excludeAlbumId: null);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetAlbumRating_aggregates_across_album_products()
    {
        var (avg, count) = _catalog.GetAlbumRating(_albumAId);
        Assert.Equal(2, count);
        Assert.Equal(4.0, avg, 1); // (5+3)/2
    }

    [Fact]
    public void GetAlbumRating_returns_zero_when_no_reviews()
    {
        var (avg, count) = _catalog.GetAlbumRating(_albumBId);
        Assert.Equal(0, count);
        Assert.Equal(0.0, avg);
    }

    [Fact]
    public void GetReviewsForAlbum_returns_reviews_across_all_products()
    {
        var reviews = _catalog.GetReviewsForAlbum(_albumAId);
        Assert.Equal(2, reviews.Count);
    }

    [Fact]
    public void GetPrimaryProductId_returns_lowest_id_product()
    {
        var primary = _catalog.GetPrimaryProductId(_albumAId);
        Assert.Equal(_productAId, primary);
    }

    [Fact]
    public void GetPrimaryProductId_null_for_album_without_products()
    {
        Assert.Null(_catalog.GetPrimaryProductId(_albumBId));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", null);
        try { File.Delete(_dbPath); } catch { }
    }
}
