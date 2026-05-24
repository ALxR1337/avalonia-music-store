using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;

namespace MusicApp.Services;

public class LikesService : ILikesService
{
    private readonly IDbContextFactory<MusicStoreDbContext> _dbFactory;

    public LikesService(IDbContextFactory<MusicStoreDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public event EventHandler? Changed;

    public bool IsTrackLiked(int userId, int trackId)
    {
        if (userId <= 0) return false;
        using var db = _dbFactory.CreateDbContext();
        return db.TrackLikes.AsNoTracking().Any(x => x.UserId == userId && x.TrackId == trackId);
    }

    public void LikeTrack(int userId, int trackId)
    {
        if (userId <= 0 || trackId <= 0) return;
        using var db = _dbFactory.CreateDbContext();
        if (db.TrackLikes.Any(x => x.UserId == userId && x.TrackId == trackId)) return;
        db.TrackLikes.Add(new TrackLike { UserId = userId, TrackId = trackId, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UnlikeTrack(int userId, int trackId)
    {
        if (userId <= 0) return;
        using var db = _dbFactory.CreateDbContext();
        var row = db.TrackLikes.FirstOrDefault(x => x.UserId == userId && x.TrackId == trackId);
        if (row is null) return;
        db.TrackLikes.Remove(row);
        db.SaveChanges();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<int> GetLikedTrackIds(int userId)
    {
        if (userId <= 0) return Array.Empty<int>();
        using var db = _dbFactory.CreateDbContext();
        return db.TrackLikes.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.TrackId)
            .ToList();
    }

    public bool IsAlbumLiked(int userId, int albumId)
    {
        if (userId <= 0) return false;
        using var db = _dbFactory.CreateDbContext();
        return db.AlbumLikes.AsNoTracking().Any(x => x.UserId == userId && x.AlbumId == albumId);
    }

    public void LikeAlbum(int userId, int albumId)
    {
        if (userId <= 0 || albumId <= 0) return;
        using var db = _dbFactory.CreateDbContext();
        if (db.AlbumLikes.Any(x => x.UserId == userId && x.AlbumId == albumId)) return;
        db.AlbumLikes.Add(new AlbumLike { UserId = userId, AlbumId = albumId, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UnlikeAlbum(int userId, int albumId)
    {
        if (userId <= 0) return;
        using var db = _dbFactory.CreateDbContext();
        var row = db.AlbumLikes.FirstOrDefault(x => x.UserId == userId && x.AlbumId == albumId);
        if (row is null) return;
        db.AlbumLikes.Remove(row);
        db.SaveChanges();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<int> GetLikedAlbumIds(int userId)
    {
        if (userId <= 0) return Array.Empty<int>();
        using var db = _dbFactory.CreateDbContext();
        return db.AlbumLikes.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.AlbumId)
            .ToList();
    }
}
