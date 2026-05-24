using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;
using MusicApp.Services;
using Xunit;

namespace MusicApp.BugHunt;

public class LikesServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MusicStoreDbContextFactory _factory;
    private readonly LikesService _svc;

    public LikesServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"likes-tests-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", _dbPath);
        _factory = new MusicStoreDbContextFactory();
        using (var db = _factory.CreateDbContext()) db.Database.Migrate();
        SeedMinimal();
        _svc = new LikesService(_factory);
    }

    private void SeedMinimal()
    {
        using var db = _factory.CreateDbContext();
        var artist = new Artist { Name = "Test Artist" };
        db.Artists.Add(artist);
        db.SaveChanges();
        var album = new Album { ArtistId = artist.Id, Title = "Test Album", Year = 2024 };
        db.Albums.Add(album);
        db.SaveChanges();
        var track = new Track { AlbumId = album.Id, Title = "Test Track", Position = 1 };
        db.Tracks.Add(track);
        var user = new User { Username = "tester", PasswordHash = "x", Email = "t@t" };
        db.Users.Add(user);
        db.SaveChanges();
    }

    [Fact]
    public void Liking_a_track_persists_and_is_detected()
    {
        Assert.False(_svc.IsTrackLiked(userId: 1, trackId: 1));
        _svc.LikeTrack(userId: 1, trackId: 1);
        Assert.True(_svc.IsTrackLiked(userId: 1, trackId: 1));
    }

    [Fact]
    public void Liking_twice_is_idempotent()
    {
        _svc.LikeTrack(1, 1);
        _svc.LikeTrack(1, 1);
        Assert.Single(_svc.GetLikedTrackIds(1));
    }

    [Fact]
    public void Unliking_removes_the_row()
    {
        _svc.LikeTrack(1, 1);
        _svc.UnlikeTrack(1, 1);
        Assert.False(_svc.IsTrackLiked(1, 1));
        Assert.Empty(_svc.GetLikedTrackIds(1));
    }

    [Fact]
    public void Album_likes_independent_from_track_likes()
    {
        _svc.LikeTrack(1, 1);
        Assert.False(_svc.IsAlbumLiked(1, 1));
        _svc.LikeAlbum(1, 1);
        Assert.True(_svc.IsAlbumLiked(1, 1));
        Assert.Single(_svc.GetLikedAlbumIds(1));
    }

    [Fact]
    public void Changed_event_fires_on_like_and_unlike()
    {
        int fired = 0;
        _svc.Changed += (_, _) => fired++;
        _svc.LikeTrack(1, 1);
        _svc.UnlikeTrack(1, 1);
        _svc.LikeAlbum(1, 1);
        _svc.UnlikeAlbum(1, 1);
        Assert.Equal(4, fired);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MUSICAPP_DB_PATH", null);
        try { File.Delete(_dbPath); } catch { }
    }
}
