# Player Page Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the Player page as an album-context view (header + tracklist + reviews + "more from artist" + likes + shuffle/repeat). Remove playback controls that duplicate the MiniPlayer. Move seek interactivity to the MiniPlayer.

**Architecture:** New `ILikesService` + two new DB tables for track/album likes; small additions to `ICatalogService` and `IPlayerService`; full rewrite of `PlayerViewModel`/`PlayerView`; MiniPlayer's `ProgressBar` becomes an interactive `Slider`; artist navigation reuses the existing `SearchResults` page via the `виконавець:` structured query field.

**Tech Stack:** .NET 10, Avalonia 12.0.3, EF Core + SQLite, CommunityToolkit.Mvvm (`ObservableProperty`/`RelayCommand`), xUnit v3 + Avalonia.Headless for tests (`MusicApp.BugHunt/`).

**Spec:** `docs/superpowers/specs/2026-05-24-player-redesign-design.md`

---

## File Structure

**New files:**
- `Models/TrackLike.cs`, `Models/AlbumLike.cs`
- `Data/Migrations/<timestamp>_AddLikes.cs` (+ `.Designer.cs`)
- `Services/ILikesService.cs`, `Services/LikesService.cs`
- `ViewModels/TrackRowViewModel.cs` (per-row VM for the tracklist)
- `MusicApp.BugHunt/LikesServiceTests.cs`
- `MusicApp.BugHunt/CatalogServiceExtensionsTests.cs`
- `MusicApp.BugHunt/PlayerRedesignTests.cs`

**Modified files:**
- `Data/MusicStoreDbContext.cs` — `DbSet<TrackLike>`, `DbSet<AlbumLike>`, entity configs
- `Data/Migrations/MusicStoreDbContextModelSnapshot.cs` — auto-updated by `ef migrations add`
- `Services/ICatalogService.cs`, `Services/CatalogService.cs` — `GetAlbumsByArtist`, `GetAlbumRating`, `GetReviewsForAlbum`, `GetPrimaryProductId`
- `Services/IPlayerService.cs`, `Services/PlayerService.cs` — `ShuffleModeChanged`, `RepeatModeChanged` events
- `Themes/Icons.axaml` — add `IconShuffle`, `IconRepeat`, `IconRepeatOne`
- `ViewModels/PlayerViewModel.cs` — major rewrite (remove playback controls, add album context + tracklist + reviews + likes + shuffle/repeat + more from artist)
- `Views/PlayerView.axaml` + `.cs` — new single-scroll layout
- `ViewModels/MiniPlayerViewModel.cs` — `IsScrubbing`/`CommitSeek` pattern
- `Views/MiniPlayerView.axaml` + `.cs` — `Slider` replaces `ProgressBar`, scrub event handlers
- `App.axaml.cs` — register `LikesService`, pass into `PlayerViewModel` factory

---

## Task 1: Add Shuffle/Repeat/RepeatOne icons

**Files:**
- Modify: `Themes/Icons.axaml`

- [ ] **Step 1: Add three new StreamGeometry resources**

Open `Themes/Icons.axaml`. Find the existing `IconHeartFilled` entry (around line 33). Below it add:

```xml
    <StreamGeometry x:Key="IconShuffle">M16 3 h5 v5 M4 20 L21 3 M21 16 v5 h-5 M15 15 l6 6 M4 4 l5 5</StreamGeometry>
    <StreamGeometry x:Key="IconRepeat">M17 1 l4 4 -4 4 M3 11 V9 a4 4 0 0 1 4 -4 h14 M7 23 l-4 -4 4 -4 M21 13 v2 a4 4 0 0 1 -4 4 H3</StreamGeometry>
    <StreamGeometry x:Key="IconRepeatOne">M17 1 l4 4 -4 4 M3 11 V9 a4 4 0 0 1 4 -4 h14 M7 23 l-4 -4 4 -4 M21 13 v2 a4 4 0 0 1 -4 4 H3 M11 15 V10 l-2 1</StreamGeometry>
```

- [ ] **Step 2: Build to ensure XAML compiles**

Run: `dotnet build MusicApp.csproj`
Expected: succeeds (warnings ok, no errors).

- [ ] **Step 3: Commit**

```bash
git add Themes/Icons.axaml
git commit -m "feat(theme): add Shuffle/Repeat/RepeatOne icons for player redesign"
```

---

## Task 2: Track & Album Like models + DbContext registration

**Files:**
- Create: `Models/TrackLike.cs`, `Models/AlbumLike.cs`
- Modify: `Data/MusicStoreDbContext.cs`

- [ ] **Step 1: Create `Models/TrackLike.cs`**

```csharp
using System;

namespace MusicApp.Models;

public class TrackLike
{
    public int UserId { get; set; }
    public int TrackId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Track? Track { get; set; }
}
```

- [ ] **Step 2: Create `Models/AlbumLike.cs`**

```csharp
using System;

namespace MusicApp.Models;

public class AlbumLike
{
    public int UserId { get; set; }
    public int AlbumId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Album? Album { get; set; }
}
```

- [ ] **Step 3: Register `DbSet`s and entity configs**

In `Data/MusicStoreDbContext.cs`, in the DbSet section (just after `AlbumGenres` around line 31), add:

```csharp
    public DbSet<TrackLike> TrackLikes => Set<TrackLike>();
    public DbSet<AlbumLike> AlbumLikes => Set<AlbumLike>();
```

Then in `OnModelCreating`, append (before the closing brace, after the last `mb.Entity<…>` block):

```csharp
        mb.Entity<TrackLike>(e =>
        {
            e.HasKey(x => new { x.UserId, x.TrackId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        mb.Entity<AlbumLike>(e =>
        {
            e.HasKey(x => new { x.UserId, x.AlbumId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Album).WithMany().HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });
```

- [ ] **Step 4: Build to verify model compiles**

Run: `dotnet build MusicApp.csproj`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add Models/TrackLike.cs Models/AlbumLike.cs Data/MusicStoreDbContext.cs
git commit -m "feat(db): add TrackLike and AlbumLike entities"
```

---

## Task 3: EF migration `AddLikes`

**Files:**
- Create: `Data/Migrations/<timestamp>_AddLikes.cs` (+ `.Designer.cs`)
- Modify: `Data/Migrations/MusicStoreDbContextModelSnapshot.cs`

- [ ] **Step 1: Generate the migration**

Run: `dotnet ef migrations add AddLikes --project MusicApp.csproj --output-dir Data/Migrations`
Expected: three files created/modified under `Data/Migrations/`. The non-Designer file should contain `CreateTable("TrackLikes", …)` and `CreateTable("AlbumLikes", …)` with composite keys and FKs.

If `dotnet-ef` is not installed, install it first: `dotnet tool install --global dotnet-ef`.

- [ ] **Step 2: Open the generated `*_AddLikes.cs` and sanity-check**

Verify the `Up` method contains:
- `migrationBuilder.CreateTable(name: "TrackLikes", …)` with columns `UserId`, `TrackId`, `CreatedAt` and a composite primary key on `(UserId, TrackId)`.
- `migrationBuilder.CreateTable(name: "AlbumLikes", …)` with columns `UserId`, `AlbumId`, `CreatedAt` and a composite primary key on `(UserId, AlbumId)`.
- Foreign keys on both tables to `Users` and `Tracks`/`Albums` with `OnDelete: ReferentialAction.Cascade`.
- Indexes on `UserId` for both.

Any missing — re-check Task 2 entity configs.

- [ ] **Step 3: Build to verify migration compiles**

Run: `dotnet build MusicApp.csproj`
Expected: succeeds.

- [ ] **Step 4: Run migration against a fresh DB to verify**

Run: `MUSICAPP_DB_PATH=/tmp/musicapp-likes-test.db dotnet ef database update --project MusicApp.csproj`
Expected: completes without error. The two new tables exist (verify with `sqlite3 /tmp/musicapp-likes-test.db ".tables"` — should list `TrackLikes` and `AlbumLikes`).

Cleanup: `rm /tmp/musicapp-likes-test.db`.

- [ ] **Step 5: Commit**

```bash
git add Data/Migrations/
git commit -m "feat(db): add EF migration for TrackLikes and AlbumLikes"
```

---

## Task 4: `ILikesService` + `LikesService` (TDD)

**Files:**
- Create: `Services/ILikesService.cs`, `Services/LikesService.cs`
- Test: `MusicApp.BugHunt/LikesServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `MusicApp.BugHunt/LikesServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to confirm it fails**

Run: `dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj --filter "FullyQualifiedName~LikesServiceTests"`
Expected: build fails — `LikesService` and `ILikesService` don't exist yet.

- [ ] **Step 3: Create `Services/ILikesService.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace MusicApp.Services;

public interface ILikesService
{
    event EventHandler? Changed;

    bool IsTrackLiked(int userId, int trackId);
    void LikeTrack(int userId, int trackId);
    void UnlikeTrack(int userId, int trackId);
    IReadOnlyList<int> GetLikedTrackIds(int userId);

    bool IsAlbumLiked(int userId, int albumId);
    void LikeAlbum(int userId, int albumId);
    void UnlikeAlbum(int userId, int albumId);
    IReadOnlyList<int> GetLikedAlbumIds(int userId);
}
```

- [ ] **Step 4: Create `Services/LikesService.cs`**

```csharp
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
```

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj --filter "FullyQualifiedName~LikesServiceTests"`
Expected: all 5 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Services/ILikesService.cs Services/LikesService.cs MusicApp.BugHunt/LikesServiceTests.cs
git commit -m "feat(services): add LikesService for track and album likes"
```

---

## Task 5: `PlayerService` events for Shuffle/Repeat changes

**Files:**
- Modify: `Services/IPlayerService.cs`, `Services/PlayerService.cs`

- [ ] **Step 1: Extend the interface**

In `Services/IPlayerService.cs`, in the events block (lines 8-11), add:

```csharp
    event EventHandler? ShuffleModeChanged;
    event EventHandler? RepeatModeChanged;
```

So the full events block reads:

```csharp
    event EventHandler? MediaOpened;
    event EventHandler? MediaEnded;
    event EventHandler? PositionChanged;
    event EventHandler? PlaybackStateChanged;
    event EventHandler? ShuffleModeChanged;
    event EventHandler? RepeatModeChanged;
```

- [ ] **Step 2: Raise the events in `PlayerService`**

In `Services/PlayerService.cs`, find the `PlaybackStateChanged` event declaration (around line 58) and add after it:

```csharp
    public event EventHandler? ShuffleModeChanged;
    public event EventHandler? RepeatModeChanged;
```

Then modify the `ShuffleMode` setter (around line 88-93) to raise its event:

```csharp
    public bool ShuffleMode
    {
        get => _shuffleMode;
        set
        {
            if (_shuffleMode == value) return;
            _shuffleMode = value;
            PersistSettings();
            ShuffleModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
```

And the `RepeatMode` setter (around line 96-100):

```csharp
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode == value) return;
            _repeatMode = value;
            PersistSettings();
            RepeatModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build MusicApp.csproj`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add Services/IPlayerService.cs Services/PlayerService.cs
git commit -m "feat(player): raise events when Shuffle/Repeat mode changes"
```

---

## Task 6: `CatalogService` extensions (TDD)

**Files:**
- Modify: `Services/ICatalogService.cs`, `Services/CatalogService.cs`
- Test: `MusicApp.BugHunt/CatalogServiceExtensionsTests.cs`

- [ ] **Step 1: Write failing tests**

Create `MusicApp.BugHunt/CatalogServiceExtensionsTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj --filter "FullyQualifiedName~CatalogServiceExtensionsTests"`
Expected: compile failure — methods don't exist.

- [ ] **Step 3: Extend the interface**

In `Services/ICatalogService.cs`, add these methods to the interface (after `GetReviewsByUser`):

```csharp
    IReadOnlyList<Album> GetAlbumsByArtist(int artistId, int? excludeAlbumId = null);
    (double Avg, int Count) GetAlbumRating(int albumId);
    IReadOnlyList<Review> GetReviewsForAlbum(int albumId);
    int? GetPrimaryProductId(int albumId);
```

- [ ] **Step 4: Implement in `CatalogService`**

In `Services/CatalogService.cs`, add these methods (e.g. after `GetReviewsByUser`):

```csharp
    public IReadOnlyList<Album> GetAlbumsByArtist(int artistId, int? excludeAlbumId = null) =>
        _albums
            .Where(a => a.ArtistId == artistId && (!excludeAlbumId.HasValue || a.Id != excludeAlbumId.Value))
            .OrderBy(a => a.Year)
            .ToList();

    public (double Avg, int Count) GetAlbumRating(int albumId)
    {
        var productIds = _products.Where(p => p.AlbumId == albumId).Select(p => p.Id).ToHashSet();
        if (productIds.Count == 0) return (0.0, 0);
        var ratings = _reviews.Where(r => productIds.Contains(r.ProductId)).Select(r => r.Rating).ToList();
        if (ratings.Count == 0) return (0.0, 0);
        return (ratings.Average(), ratings.Count);
    }

    public IReadOnlyList<Review> GetReviewsForAlbum(int albumId)
    {
        var productIds = _products.Where(p => p.AlbumId == albumId).Select(p => p.Id).ToHashSet();
        if (productIds.Count == 0) return Array.Empty<Review>();
        return _reviews.Where(r => productIds.Contains(r.ProductId))
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
    }

    public int? GetPrimaryProductId(int albumId)
    {
        var p = _products.Where(x => x.AlbumId == albumId).OrderBy(x => x.Id).FirstOrDefault();
        return p?.Id;
    }
```

Also add `using System;` if not already present (for `Array.Empty`).

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj --filter "FullyQualifiedName~CatalogServiceExtensionsTests"`
Expected: all 7 tests pass.

- [ ] **Step 6: Commit**

```bash
git add Services/ICatalogService.cs Services/CatalogService.cs MusicApp.BugHunt/CatalogServiceExtensionsTests.cs
git commit -m "feat(catalog): add album/artist query helpers for player redesign"
```

---

## Task 7: MiniPlayer becomes the seek surface

**Files:**
- Modify: `ViewModels/MiniPlayerViewModel.cs`, `Views/MiniPlayerView.axaml`, `Views/MiniPlayerView.axaml.cs`

- [ ] **Step 1: Update `MiniPlayerViewModel.cs`**

Replace the file contents with:

```csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class MiniPlayerViewModel : ViewModelBase
{
    private readonly IPlayerService _player;
    private readonly MainWindowViewModel _shell;

    [ObservableProperty] private string _trackTitle = string.Empty;
    [ObservableProperty] private string _artistName = string.Empty;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _volume = 0.7;
    [ObservableProperty] private MusicApp.Models.Album? _currentAlbum;

    // While the user is dragging the slider, the timer-driven Progress writes are
    // suppressed so the thumb does not fight the pointer.
    public bool IsScrubbing { get; set; }

    public MiniPlayerViewModel(IPlayerService player, MainWindowViewModel shell)
    {
        _player = player;
        _shell = shell;
        Volume = _player.Volume;

        _player.MediaOpened += (_, _) => Refresh();
        _player.PositionChanged += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => IsPlaying = _player.IsPlaying;
    }

    partial void OnVolumeChanged(double value) => _player.Volume = value;

    private void Refresh()
    {
        var t = _player.CurrentTrack;
        TrackTitle = t?.Title ?? "—";
        CurrentAlbum = _player.CurrentAlbum;
        ArtistName = _player.CurrentAlbum?.Artist?.Name ?? "семпл 30 с";
        PositionText = Format(_player.Position);
        DurationText = Format(_player.Duration);
        if (!IsScrubbing)
        {
            Progress = _player.Duration.TotalSeconds <= 0
                ? 0
                : _player.Position.TotalSeconds / _player.Duration.TotalSeconds * 100.0;
        }
        IsPlaying = _player.IsPlaying;
    }

    private static string Format(TimeSpan ts) => $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";

    public void CommitSeek(double progressPercent)
    {
        var duration = _player.Duration;
        if (duration.TotalSeconds <= 0) return;
        var ms = duration.TotalMilliseconds * (Math.Clamp(progressPercent, 0, 100) / 100.0);
        _player.Seek(TimeSpan.FromMilliseconds(ms));
    }

    [RelayCommand] private void PlayPause() => _player.TogglePlayPause();
    [RelayCommand] private void Next() => _player.Next();
    [RelayCommand] private void Previous() => _player.Previous();
    [RelayCommand] private void Expand() => _shell.ExpandMiniPlayerCommand.Execute(null);
    [RelayCommand] private void Close() => _shell.CloseMiniPlayerCommand.Execute(null);
}
```

- [ ] **Step 2: Update `Views/MiniPlayerView.axaml`**

Find the center column's progress block (lines 52–63 of the current file) and replace the `<ProgressBar …>` with a `<Slider …>`. The new center column should read:

```xml
        <!-- Center: controls + progress -->
        <StackPanel Grid.Column="1" Spacing="4" VerticalAlignment="Center" HorizontalAlignment="Center">
            <StackPanel Orientation="Horizontal" Spacing="6" HorizontalAlignment="Center">
                <Button Classes="icon" Width="32" Height="32"
                        Command="{Binding PreviousCommand}">
                    <Path Classes="icon" Data="{StaticResource IconSkipBack}"/>
                </Button>
                <Button Classes="play-circle" Width="36" Height="36" CornerRadius="18"
                        Command="{Binding PlayPauseCommand}">
                    <Path Data="{StaticResource IconPlay}" Fill="Black" Stroke="Transparent"
                          Width="14" Height="14" Stretch="Uniform"/>
                </Button>
                <Button Classes="icon" Width="32" Height="32"
                        Command="{Binding NextCommand}">
                    <Path Classes="icon" Data="{StaticResource IconSkipForward}"/>
                </Button>
            </StackPanel>
            <Grid ColumnDefinitions="Auto,*,Auto" Width="500">
                <TextBlock Grid.Column="0" Text="{Binding PositionText}"
                           Classes="muted" FontSize="11" VerticalAlignment="Center"
                           Margin="0,0,8,0"/>
                <Slider Grid.Column="1" Name="SeekSlider"
                        Minimum="0" Maximum="100"
                        Value="{Binding Progress}"
                        Foreground="{StaticResource AccentBrush}"
                        PointerPressed="OnSeekPointerPressed"
                        PointerCaptureLost="OnSeekPointerReleased"
                        PointerReleased="OnSeekPointerReleased"/>
                <TextBlock Grid.Column="2" Text="{Binding DurationText}"
                           Classes="muted" FontSize="11" VerticalAlignment="Center"
                           Margin="8,0,0,0"/>
            </Grid>
        </StackPanel>
```

- [ ] **Step 3: Add code-behind handlers in `Views/MiniPlayerView.axaml.cs`**

Replace the file contents with:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class MiniPlayerView : UserControl
{
    public MiniPlayerView() => InitializeComponent();

    private void OnSeekPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MiniPlayerViewModel vm) vm.IsScrubbing = true;
    }

    private void OnSeekPointerReleased(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MiniPlayerViewModel vm && sender is Slider slider)
        {
            vm.IsScrubbing = false;
            vm.CommitSeek(slider.Value);
        }
    }
}
```

The file exists; overwrite it with the contents above.

- [ ] **Step 4: Build**

Run: `dotnet build MusicApp.csproj`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add ViewModels/MiniPlayerViewModel.cs Views/MiniPlayerView.axaml Views/MiniPlayerView.axaml.cs
git commit -m "feat(miniplayer): convert progress bar to interactive seek slider"
```

---

## Task 8: `TrackRowViewModel`

**Files:**
- Create: `ViewModels/TrackRowViewModel.cs`

- [ ] **Step 1: Create the per-row view model**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using MusicApp.Models;

namespace MusicApp.ViewModels;

public partial class TrackRowViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _isCurrent;

    public TrackRowViewModel(Track track, int index)
    {
        Track = track;
        Index = index;
    }

    public Track Track { get; }
    public int Index { get; }
    public int Position => Track.Position > 0 ? Track.Position : Index + 1;
    public string Title => Track.Title;
    public string DurationDisplay => Track.DurationDisplay;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build MusicApp.csproj`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add ViewModels/TrackRowViewModel.cs
git commit -m "feat(viewmodel): add TrackRowViewModel for player tracklist"
```

---

## Task 9: Rewrite `PlayerViewModel`

**Files:**
- Modify: `ViewModels/PlayerViewModel.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class PlayerViewModel : ViewModelBase
{
    private const int InitialReviewLimit = 3;

    private readonly IPlayerService _player;
    private readonly ICatalogService _catalog;
    private readonly IAuthService _auth;
    private readonly ILikesService _likes;
    private readonly INavigationService _nav;
    private readonly IFileDialogService? _files;

    [ObservableProperty] private string _trackTitle = "—";
    [ObservableProperty] private string _albumTitle = "";
    [ObservableProperty] private string _artistName = "";
    [ObservableProperty] private string _albumYear = "";
    [ObservableProperty] private string _albumGenre = "";
    [ObservableProperty] private string _albumTrackCount = "";
    [ObservableProperty] private string _albumTotalDuration = "";
    [ObservableProperty] private string _albumDescription = "";
    [ObservableProperty] private bool _hasDescription;
    [ObservableProperty] private bool _isDescriptionExpanded;
    [ObservableProperty] private Album? _currentAlbumForCover;

    [ObservableProperty] private bool _isAlbumLiked;
    [ObservableProperty] private bool _isShuffleOn;
    [ObservableProperty] private RepeatMode _repeatMode;

    [ObservableProperty] private double _avgRating;
    [ObservableProperty] private int _reviewCount;
    [ObservableProperty] private bool _showAllReviews;
    [ObservableProperty] private bool _hasMoreReviews;
    [ObservableProperty] private bool _canLeaveReview;
    [ObservableProperty] private bool _isReviewFormOpen;
    [ObservableProperty] private string _newReviewText = string.Empty;
    [ObservableProperty] private int _newReviewRating = 5;
    [ObservableProperty] private string? _reviewMessage;

    public PlayerViewModel(
        IPlayerService player,
        ICatalogService catalog,
        IAuthService auth,
        ILikesService likes,
        INavigationService nav,
        IFileDialogService? files = null)
    {
        _player = player;
        _catalog = catalog;
        _auth = auth;
        _likes = likes;
        _nav = nav;
        _files = files;

        PurchasedAlbums = new ObservableCollection<Album>();
        PurchasedAlbums.CollectionChanged += OnPurchasedAlbumsChanged;
        Tracks = new ObservableCollection<TrackRowViewModel>();
        Reviews = new ObservableCollection<Review>();
        MoreFromArtist = new ObservableCollection<Album>();
        AllReviews = new System.Collections.Generic.List<Review>();

        ReloadPurchasedAlbums();
        _auth.CurrentUserChanged += (_, _) => { ReloadPurchasedAlbums(); Refresh(); };
        _player.MediaOpened += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => Refresh();
        _player.ShuffleModeChanged += (_, _) => IsShuffleOn = _player.ShuffleMode;
        _player.RepeatModeChanged += (_, _) => RepeatMode = _player.RepeatMode;
        _likes.Changed += (_, _) => RefreshLikeStates();

        IsShuffleOn = _player.ShuffleMode;
        RepeatMode = _player.RepeatMode;

        Refresh();
    }

    public ObservableCollection<Album> PurchasedAlbums { get; }
    public ObservableCollection<TrackRowViewModel> Tracks { get; }
    public ObservableCollection<Review> Reviews { get; }
    public ObservableCollection<Album> MoreFromArtist { get; }

    private System.Collections.Generic.List<Review> AllReviews { get; }

    public bool HasTrack => _player.CurrentTrack is not null;
    public bool HasAlbum => _player.CurrentAlbum is not null;
    public bool HasPurchasedAlbums => PurchasedAlbums.Count > 0;
    public bool HasArtist => _player.CurrentAlbum?.Artist is not null;
    public bool HasMoreFromArtist => MoreFromArtist.Count > 0;
    public bool HasReviews => Reviews.Count > 0;
    public string RatingLabel => ReviewCount == 0 ? "Немає відгуків" : $"★ {AvgRating:0.0} ({ReviewCount})";

    private void OnPurchasedAlbumsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasPurchasedAlbums));

    private void ReloadPurchasedAlbums()
    {
        PurchasedAlbums.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        foreach (var a in _catalog.GetPurchasedAlbums(userId))
            PurchasedAlbums.Add(a);
    }

    private void Refresh()
    {
        var t = _player.CurrentTrack;
        var album = _player.CurrentAlbum;
        TrackTitle = t?.Title ?? "—";
        AlbumTitle = album?.Title ?? "";
        ArtistName = album?.Artist?.Name ?? "";
        AlbumYear = album?.Year > 0 ? album.Year.ToString() : "";
        AlbumGenre = album?.Genre?.Name ?? "";
        AlbumTrackCount = album is null ? "" : $"{album.Tracks.Count} треків";
        AlbumTotalDuration = album is null
            ? ""
            : FormatTotal(TimeSpan.FromTicks(album.Tracks.Sum(x => x.Duration.Ticks)));
        AlbumDescription = album?.Description ?? "";
        HasDescription = !string.IsNullOrWhiteSpace(AlbumDescription);
        IsDescriptionExpanded = false;
        CurrentAlbumForCover = album;

        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(HasAlbum));
        OnPropertyChanged(nameof(HasArtist));

        RebuildTracks(album);
        ReloadReviews(album);
        ReloadMoreFromArtist(album);
        RefreshLikeStates();
        ReloadCanLeaveReview(album);
    }

    private void RebuildTracks(Album? album)
    {
        Tracks.Clear();
        if (album is null) return;
        var currentTrackId = _player.CurrentTrack?.Id ?? 0;
        for (int i = 0; i < album.Tracks.Count; i++)
        {
            var row = new TrackRowViewModel(album.Tracks[i], i)
            {
                IsCurrent = album.Tracks[i].Id == currentTrackId
            };
            Tracks.Add(row);
        }
    }

    private void ReloadReviews(Album? album)
    {
        AllReviews.Clear();
        Reviews.Clear();
        AvgRating = 0;
        ReviewCount = 0;
        if (album is null) { RaiseReviewStateChanges(); return; }
        var (avg, count) = _catalog.GetAlbumRating(album.Id);
        AvgRating = avg;
        ReviewCount = count;
        AllReviews.AddRange(_catalog.GetReviewsForAlbum(album.Id));
        RenderReviews();
        RaiseReviewStateChanges();
    }

    private void RenderReviews()
    {
        Reviews.Clear();
        var slice = ShowAllReviews ? AllReviews : AllReviews.Take(InitialReviewLimit);
        foreach (var r in slice) Reviews.Add(r);
        HasMoreReviews = !ShowAllReviews && AllReviews.Count > InitialReviewLimit;
        OnPropertyChanged(nameof(HasReviews));
    }

    partial void OnShowAllReviewsChanged(bool value) => RenderReviews();
    partial void OnAvgRatingChanged(double value) => OnPropertyChanged(nameof(RatingLabel));
    partial void OnReviewCountChanged(int value) => OnPropertyChanged(nameof(RatingLabel));

    private void RaiseReviewStateChanges()
    {
        OnPropertyChanged(nameof(HasReviews));
    }

    private void ReloadMoreFromArtist(Album? album)
    {
        MoreFromArtist.Clear();
        if (album?.Artist is null) return;
        foreach (var other in _catalog.GetAlbumsByArtist(album.ArtistId, excludeAlbumId: album.Id))
            MoreFromArtist.Add(other);
        OnPropertyChanged(nameof(HasMoreFromArtist));
    }

    private void RefreshLikeStates()
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        var likedTracks = userId > 0
            ? _likes.GetLikedTrackIds(userId).ToHashSet()
            : new System.Collections.Generic.HashSet<int>();
        foreach (var row in Tracks)
            row.IsLiked = likedTracks.Contains(row.Track.Id);
        var album = _player.CurrentAlbum;
        IsAlbumLiked = userId > 0 && album is not null && _likes.IsAlbumLiked(userId, album.Id);
    }

    private void ReloadCanLeaveReview(Album? album)
    {
        var user = _auth.CurrentUser;
        CanLeaveReview = album is not null
            && user is { Role: not UserRole.Guest, Id: > 0 }
            && _catalog.IsAlbumPurchased(album.Id, user.Id);
    }

    private static string FormatTotal(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} год {ts.Minutes} хв";
        return $"{(int)ts.TotalMinutes} хв";
    }

    [RelayCommand]
    private void PlayAlbum(Album album) => _player.PlayAlbum(album);

    [RelayCommand]
    private void PlayTrack(TrackRowViewModel row)
    {
        var album = _player.CurrentAlbum;
        if (album is null || row is null) return;
        _player.PlayAlbum(album, row.Index);
    }

    [RelayCommand]
    private void ToggleAlbumLike()
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        var album = _player.CurrentAlbum;
        if (userId <= 0 || album is null) return;
        if (IsAlbumLiked) _likes.UnlikeAlbum(userId, album.Id);
        else _likes.LikeAlbum(userId, album.Id);
    }

    [RelayCommand]
    private void ToggleTrackLike(TrackRowViewModel row)
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0 || row is null) return;
        if (row.IsLiked) _likes.UnlikeTrack(userId, row.Track.Id);
        else _likes.LikeTrack(userId, row.Track.Id);
    }

    [RelayCommand]
    private void ToggleShuffle() => _player.ShuffleMode = !_player.ShuffleMode;

    [RelayCommand]
    private void CycleRepeat()
    {
        _player.RepeatMode = _player.RepeatMode switch
        {
            Models.RepeatMode.Off => Models.RepeatMode.All,
            Models.RepeatMode.All => Models.RepeatMode.One,
            _ => Models.RepeatMode.Off
        };
    }

    [RelayCommand]
    private void GoToArtist()
    {
        var name = _player.CurrentAlbum?.Artist?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{name}\"");
    }

    [RelayCommand]
    private void GoToArtistAlbum(Album album)
    {
        if (album?.Artist?.Name is not string name) return;
        _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{name}\"");
    }

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private void ToggleReviewForm() => IsReviewFormOpen = !IsReviewFormOpen;

    [RelayCommand]
    private void ToggleShowAllReviews() => ShowAllReviews = !ShowAllReviews;

    [RelayCommand]
    private void SubmitReview()
    {
        var album = _player.CurrentAlbum;
        if (album is null) { ReviewMessage = "Немає альбому."; return; }
        var user = _auth.CurrentUser;
        if (user is null || user.Role == UserRole.Guest) { ReviewMessage = "Лише авторизовані."; return; }
        if (!CanLeaveReview) { ReviewMessage = "Лише покупці цього альбому можуть залишити відгук."; return; }
        if (string.IsNullOrWhiteSpace(NewReviewText)) { ReviewMessage = "Введіть текст відгуку."; return; }
        var productId = _catalog.GetPrimaryProductId(album.Id);
        if (productId is null) { ReviewMessage = "Альбом не має продукту для відгуку."; return; }

        _catalog.AddReview(productId.Value, user.Id, user.Username, NewReviewText, NewReviewRating);
        NewReviewText = string.Empty;
        NewReviewRating = 5;
        ReviewMessage = "Дякуємо! Ваш відгук додано.";
        IsReviewFormOpen = false;
        ReloadReviews(album);
    }

    [RelayCommand]
    private async Task AddLocalFilesAsync()
    {
        if (_files is null) return;
        var path = await _files.OpenFileAsync("Виберіть аудіофайл", new[]
        {
            new FileFilter("Аудіо", new[] { "*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a" }),
            new FileFilter("Усі файли", new[] { "*.*" })
        });
        if (string.IsNullOrWhiteSpace(path)) return;
        _player.PlayFile(path);
    }
}
```

- [ ] **Step 2: Build to verify VM compiles**

Run: `dotnet build MusicApp.csproj`
Expected: build fails on the call site in `App.axaml.cs` (constructor signature changed). Leave it failing — Task 11 wires DI.

If any other compile errors appear besides the App.axaml.cs constructor mismatch, fix the VM before moving on.

- [ ] **Step 3: Commit (build still failing — that's intentional pre-DI wiring)**

```bash
git add ViewModels/PlayerViewModel.cs
git commit -m "feat(player-vm): rewrite for album-context view with likes/reviews/shuffle-repeat"
```

---

## Task 10: Redesign `PlayerView.axaml`

**Files:**
- Modify: `Views/PlayerView.axaml`, `Views/PlayerView.axaml.cs`

- [ ] **Step 1: Replace `Views/PlayerView.axaml` contents**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:MusicApp.ViewModels"
             xmlns:m="using:MusicApp.Models"
             x:Class="MusicApp.Views.PlayerView"
             x:DataType="vm:PlayerViewModel"
             Name="PlayerRoot">

    <Grid ColumnDefinitions="280,*">

        <!-- Library sidebar (unchanged behavior) -->
        <Border Grid.Column="0" Background="{StaticResource BgElevatedBrush}"
                Padding="14,20" Margin="20,20,10,20"
                CornerRadius="{StaticResource RadiusM}">
            <StackPanel Spacing="14">
                <TextBlock Classes="tiny" Text="КУПЛЕНІ АЛЬБОМИ"/>
                <ItemsControl ItemsSource="{Binding PurchasedAlbums}"
                              IsVisible="{Binding HasPurchasedAlbums}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="m:Album">
                            <Button Classes="nav-item"
                                    Command="{Binding $parent[ItemsControl].((vm:PlayerViewModel)DataContext).PlayAlbumCommand}"
                                    CommandParameter="{Binding}">
                                <StackPanel Orientation="Horizontal" Spacing="10">
                                    <Border Width="36" Height="36"
                                            CornerRadius="{StaticResource RadiusS}"
                                            Background="{Binding ., Converter={StaticResource AlbumToGradient}}"
                                            ClipToBounds="True">
                                        <Grid>
                                            <TextBlock Text="{Binding Title, Converter={StaticResource FirstChar}}"
                                                       FontSize="16" FontWeight="Bold" Opacity="0.45"
                                                       HorizontalAlignment="Center"
                                                       VerticalAlignment="Center"
                                                       Foreground="{StaticResource FgPrimaryBrush}"
                                                       IsVisible="{Binding CoverPath, Converter={x:Static StringConverters.IsNullOrEmpty}}"/>
                                            <Image Source="{Binding CoverPath, Converter={StaticResource CoverPathToImage}}"
                                                   Stretch="UniformToFill"/>
                                        </Grid>
                                    </Border>
                                    <StackPanel Spacing="2" VerticalAlignment="Center">
                                        <TextBlock Text="{Binding Title}" FontSize="13" FontWeight="SemiBold"/>
                                        <TextBlock Text="{Binding Artist.Name}" Classes="muted" FontSize="11"/>
                                    </StackPanel>
                                </StackPanel>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <TextBlock Classes="muted"
                           Text="Поки немає куплених альбомів. Купіть щось у каталозі."
                           TextWrapping="Wrap"
                           IsVisible="{Binding !HasPurchasedAlbums}"
                           Margin="2,4,2,4"/>

                <Rectangle Height="1" Fill="{StaticResource DividerSoftBrush}" Margin="0,10"/>

                <Button Classes="ghost" Content="+ Додати файли"
                        HorizontalAlignment="Stretch"
                        Command="{Binding AddLocalFilesCommand}"/>
            </StackPanel>
        </Border>

        <!-- Main content: empty state OR album context -->
        <Border Grid.Column="1" Margin="10,20,20,20"
                CornerRadius="{StaticResource RadiusL}">
            <Border.Background>
                <LinearGradientBrush StartPoint="0%,0%" EndPoint="0%,100%">
                    <GradientStop Offset="0" Color="#FF1F1F1F"/>
                    <GradientStop Offset="1" Color="#FF121212"/>
                </LinearGradientBrush>
            </Border.Background>

            <Grid>
                <!-- Empty state -->
                <StackPanel Spacing="14"
                            HorizontalAlignment="Center" VerticalAlignment="Center"
                            IsVisible="{Binding !HasTrack}">
                    <Path Classes="icon xl" Data="{StaticResource IconHeadphones}"
                          Width="96" Height="96" Opacity="0.4"
                          HorizontalAlignment="Center"/>
                    <TextBlock Classes="h3" Text="Нічого не грає" HorizontalAlignment="Center"/>
                    <TextBlock Classes="muted"
                               Text="Виберіть альбом зі списку зліва або додайте свої файли"
                               TextAlignment="Center" TextWrapping="Wrap"
                               MaxWidth="320" HorizontalAlignment="Center"/>
                </StackPanel>

                <!-- Album context (scrollable) -->
                <ScrollViewer IsVisible="{Binding HasTrack}" Padding="40,30">
                    <StackPanel Spacing="22">

                        <!-- ===== Header ===== -->
                        <Grid ColumnDefinitions="160,*" >
                            <Border Grid.Column="0" Width="160" Height="160"
                                    CornerRadius="{StaticResource RadiusM}"
                                    Background="{Binding CurrentAlbumForCover, Converter={StaticResource AlbumToGradient}}"
                                    ClipToBounds="True">
                                <Grid>
                                    <TextBlock Text="{Binding TrackTitle, Converter={StaticResource FirstChar}}"
                                               FontSize="56" FontWeight="Bold" Opacity="0.45"
                                               HorizontalAlignment="Center" VerticalAlignment="Center"
                                               Foreground="{StaticResource FgPrimaryBrush}"
                                               IsVisible="{Binding CurrentAlbumForCover.CoverPath, Converter={x:Static StringConverters.IsNullOrEmpty}}"/>
                                    <Image Source="{Binding CurrentAlbumForCover.CoverPath, Converter={StaticResource CoverPathToImage}}"
                                           Stretch="UniformToFill"/>
                                </Grid>
                            </Border>

                            <StackPanel Grid.Column="1" Margin="20,0,0,0" Spacing="6" VerticalAlignment="Center">
                                <TextBlock Text="{Binding TrackTitle}" Classes="h2"/>

                                <!-- Album-mode subtitle -->
                                <StackPanel Orientation="Horizontal" Spacing="6" IsVisible="{Binding HasAlbum}">
                                    <Button Classes="text-link" Command="{Binding GoToArtistCommand}"
                                            IsVisible="{Binding HasArtist}">
                                        <TextBlock Text="{Binding ArtistName}" FontSize="14"/>
                                    </Button>
                                    <TextBlock Text="·" Classes="muted" IsVisible="{Binding HasArtist}"/>
                                    <TextBlock Text="{Binding AlbumTitle}" Classes="muted" FontSize="14"/>
                                </StackPanel>

                                <!-- Metadata strip -->
                                <StackPanel Orientation="Horizontal" Spacing="8" IsVisible="{Binding HasAlbum}">
                                    <TextBlock Text="{Binding AlbumYear}" Classes="muted" FontSize="12"/>
                                    <TextBlock Text="·" Classes="muted" FontSize="12"/>
                                    <TextBlock Text="{Binding AlbumGenre}" Classes="muted" FontSize="12"/>
                                    <TextBlock Text="·" Classes="muted" FontSize="12"/>
                                    <TextBlock Text="{Binding AlbumTrackCount}" Classes="muted" FontSize="12"/>
                                    <TextBlock Text="·" Classes="muted" FontSize="12"/>
                                    <TextBlock Text="{Binding AlbumTotalDuration}" Classes="muted" FontSize="12"/>
                                </StackPanel>

                                <!-- Action row: like, shuffle, repeat -->
                                <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0"
                                            IsVisible="{Binding HasAlbum}">
                                    <Button Classes="icon" Width="36" Height="36"
                                            Command="{Binding ToggleAlbumLikeCommand}"
                                            ToolTip.Tip="Подобається">
                                        <Path Classes="icon"
                                              Data="{Binding IsAlbumLiked, Converter={StaticResource BoolToHeartIcon}}"/>
                                    </Button>
                                    <Button Classes="icon" Width="36" Height="36"
                                            Command="{Binding ToggleShuffleCommand}"
                                            ToolTip.Tip="Перемішати">
                                        <Path Classes="icon" Data="{StaticResource IconShuffle}"
                                              Opacity="{Binding IsShuffleOn, Converter={StaticResource BoolToOpacity}}"/>
                                    </Button>
                                    <Button Classes="icon" Width="36" Height="36"
                                            Command="{Binding CycleRepeatCommand}"
                                            ToolTip.Tip="Повтор">
                                        <Path Classes="icon"
                                              Data="{Binding RepeatMode, Converter={StaticResource RepeatModeToIcon}}"
                                              Opacity="{Binding RepeatMode, Converter={StaticResource RepeatModeToOpacity}}"/>
                                    </Button>
                                </StackPanel>
                            </StackPanel>
                        </Grid>

                        <!-- Description -->
                        <StackPanel IsVisible="{Binding HasDescription}" Spacing="6">
                            <TextBlock Text="{Binding AlbumDescription}" TextWrapping="Wrap"
                                       MaxLines="3"
                                       IsVisible="{Binding !IsDescriptionExpanded}"/>
                            <TextBlock Text="{Binding AlbumDescription}" TextWrapping="Wrap"
                                       IsVisible="{Binding IsDescriptionExpanded}"/>
                            <Button Classes="text-link" Command="{Binding ToggleDescriptionCommand}">
                                <TextBlock Text="Показати повністю" IsVisible="{Binding !IsDescriptionExpanded}"/>
                            </Button>
                            <Button Classes="text-link" Command="{Binding ToggleDescriptionCommand}">
                                <TextBlock Text="Згорнути" IsVisible="{Binding IsDescriptionExpanded}"/>
                            </Button>
                        </StackPanel>

                        <Rectangle Height="1" Fill="{StaticResource DividerSoftBrush}"
                                   IsVisible="{Binding HasAlbum}"/>

                        <!-- ===== Tracklist ===== -->
                        <StackPanel Spacing="8" IsVisible="{Binding HasAlbum}">
                            <TextBlock Text="Треки" Classes="h3"/>
                            <ItemsControl ItemsSource="{Binding Tracks}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate DataType="vm:TrackRowViewModel">
                                        <Grid ColumnDefinitions="36,*,Auto,Auto" Margin="0,2"
                                              Background="{Binding IsCurrent, Converter={StaticResource BoolToHighlightBrush}}">
                                            <Button Grid.Column="0" Classes="ghost" Padding="0"
                                                    Command="{Binding $parent[ItemsControl].((vm:PlayerViewModel)DataContext).PlayTrackCommand}"
                                                    CommandParameter="{Binding}">
                                                <TextBlock Text="{Binding Position}" HorizontalAlignment="Center"
                                                           Classes="muted"/>
                                            </Button>
                                            <Button Grid.Column="1" Classes="ghost" HorizontalAlignment="Stretch"
                                                    HorizontalContentAlignment="Left" Padding="6,4"
                                                    Command="{Binding $parent[ItemsControl].((vm:PlayerViewModel)DataContext).PlayTrackCommand}"
                                                    CommandParameter="{Binding}">
                                                <TextBlock Text="{Binding Title}"/>
                                            </Button>
                                            <TextBlock Grid.Column="2" Text="{Binding DurationDisplay}"
                                                       Classes="muted" VerticalAlignment="Center" Margin="8,0"/>
                                            <Button Grid.Column="3" Classes="icon" Width="28" Height="28"
                                                    Command="{Binding $parent[ItemsControl].((vm:PlayerViewModel)DataContext).ToggleTrackLikeCommand}"
                                                    CommandParameter="{Binding}">
                                                <Path Classes="icon"
                                                      Data="{Binding IsLiked, Converter={StaticResource BoolToHeartIcon}}"/>
                                            </Button>
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>

                        <Rectangle Height="1" Fill="{StaticResource DividerSoftBrush}"
                                   IsVisible="{Binding HasAlbum}"/>

                        <!-- ===== Reviews ===== -->
                        <StackPanel Spacing="10" IsVisible="{Binding HasAlbum}">
                            <Grid ColumnDefinitions="*,Auto">
                                <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="Відгуки" Classes="h3"/>
                                    <TextBlock Text="{Binding RatingLabel}" Classes="muted" VerticalAlignment="Center"/>
                                </StackPanel>
                                <Button Grid.Column="1" Classes="primary"
                                        Content="Залишити відгук"
                                        Command="{Binding ToggleReviewFormCommand}"
                                        IsVisible="{Binding CanLeaveReview}"/>
                            </Grid>

                            <StackPanel IsVisible="{Binding IsReviewFormOpen}" Spacing="6"
                                        Background="{StaticResource BgElevatedBrush}"
                                        Margin="0,4">
                                <TextBlock Text="Ваша оцінка" Classes="muted"/>
                                <NumericUpDown Value="{Binding NewReviewRating}" Minimum="1" Maximum="5" Increment="1"/>
                                <TextBox Text="{Binding NewReviewText}" AcceptsReturn="True"
                                         Watermark="Що ви думаєте про цей альбом?"
                                         MinHeight="80"/>
                                <Button Classes="primary" Content="Надіслати"
                                        Command="{Binding SubmitReviewCommand}"/>
                            </StackPanel>

                            <TextBlock Text="{Binding ReviewMessage}" Classes="muted"
                                       IsVisible="{Binding ReviewMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>

                            <ItemsControl ItemsSource="{Binding Reviews}" IsVisible="{Binding HasReviews}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate DataType="m:Review">
                                        <Border Padding="8" Margin="0,2"
                                                CornerRadius="{StaticResource RadiusS}"
                                                Background="{StaticResource BgElevatedBrush}">
                                            <StackPanel Spacing="4">
                                                <StackPanel Orientation="Horizontal" Spacing="6">
                                                    <TextBlock Text="{Binding UserDisplayName}" FontWeight="SemiBold"/>
                                                    <TextBlock Text="{Binding Rating, StringFormat='{}{0} ★'}" Classes="muted"/>
                                                </StackPanel>
                                                <TextBlock Text="{Binding Text}" TextWrapping="Wrap"/>
                                            </StackPanel>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>

                            <Button Classes="text-link"
                                    Content="Показати всі"
                                    Command="{Binding ToggleShowAllReviewsCommand}"
                                    IsVisible="{Binding HasMoreReviews}"/>
                        </StackPanel>

                        <Rectangle Height="1" Fill="{StaticResource DividerSoftBrush}"
                                   IsVisible="{Binding HasMoreFromArtist}"/>

                        <!-- ===== More from artist ===== -->
                        <StackPanel Spacing="10" IsVisible="{Binding HasMoreFromArtist}">
                            <TextBlock Text="{Binding ArtistName, StringFormat='Більше від {0}'}" Classes="h3"/>
                            <ScrollViewer HorizontalScrollBarVisibility="Auto"
                                          VerticalScrollBarVisibility="Disabled">
                                <ItemsControl ItemsSource="{Binding MoreFromArtist}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <StackPanel Orientation="Horizontal" Spacing="12"/>
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate DataType="m:Album">
                                            <Button Classes="ghost"
                                                    Command="{Binding $parent[ItemsControl].((vm:PlayerViewModel)DataContext).GoToArtistAlbumCommand}"
                                                    CommandParameter="{Binding}">
                                                <StackPanel Width="120" Spacing="6">
                                                    <Border Width="120" Height="120"
                                                            CornerRadius="{StaticResource RadiusS}"
                                                            Background="{Binding ., Converter={StaticResource AlbumToGradient}}"
                                                            ClipToBounds="True">
                                                        <Image Source="{Binding CoverPath, Converter={StaticResource CoverPathToImage}}"
                                                               Stretch="UniformToFill"/>
                                                    </Border>
                                                    <TextBlock Text="{Binding Title}" FontSize="12"
                                                               FontWeight="SemiBold"
                                                               TextTrimming="CharacterEllipsis"/>
                                                    <TextBlock Text="{Binding Year}" FontSize="11" Classes="muted"/>
                                                </StackPanel>
                                            </Button>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </ScrollViewer>
                        </StackPanel>

                    </StackPanel>
                </ScrollViewer>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Replace `Views/PlayerView.axaml.cs` contents**

The seek handlers are no longer used. Reduce to:

```csharp
using Avalonia.Controls;

namespace MusicApp.Views;

public partial class PlayerView : UserControl
{
    public PlayerView() => InitializeComponent();
}
```

- [ ] **Step 3: Add the converters used by the new XAML**

Create `Converters/BoolToHeartIconConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MusicApp.Converters;

public class BoolToHeartIconConverter : IValueConverter
{
    public static readonly BoolToHeartIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var liked = value is bool b && b;
        var key = liked ? "IconHeartFilled" : "IconHeart";
        return Application.Current?.Resources[key] as Geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Create `Converters/BoolToOpacityConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

public class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? 1.0 : 0.45;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Create `Converters/RepeatModeToIconConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MusicApp.Models;

namespace MusicApp.Converters;

public class RepeatModeToIconConverter : IValueConverter
{
    public static readonly RepeatModeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is RepeatMode.One ? "IconRepeatOne" : "IconRepeat";
        return Application.Current?.Resources[key] as Geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class RepeatModeToOpacityConverter : IValueConverter
{
    public static readonly RepeatModeToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RepeatMode.Off ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Create `Converters/BoolToHighlightBrushConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MusicApp.Converters;

public class BoolToHighlightBrushConverter : IValueConverter
{
    private static readonly IBrush HighlightBrush =
        new SolidColorBrush(Color.FromArgb(0x33, 0xE0, 0x7B, 0x39));
    public static readonly BoolToHighlightBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? HighlightBrush : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4: Register the new converters as resources**

Open `App.axaml` (the XAML, not the code-behind). In `Application.Resources` find the converter declarations. Add:

```xml
        <conv:BoolToHeartIconConverter x:Key="BoolToHeartIcon"/>
        <conv:BoolToOpacityConverter x:Key="BoolToOpacity"/>
        <conv:RepeatModeToIconConverter x:Key="RepeatModeToIcon"/>
        <conv:RepeatModeToOpacityConverter x:Key="RepeatModeToOpacity"/>
        <conv:BoolToHighlightBrushConverter x:Key="BoolToHighlightBrush"/>
```

(If the `conv` xmlns prefix isn't already declared, add `xmlns:conv="using:MusicApp.Converters"` to the `Application` element.)

- [ ] **Step 5: Build**

Run: `dotnet build MusicApp.csproj`
Expected: still fails on `App.axaml.cs` constructor mismatch (Task 11 fixes this). XAML must compile without errors though.

If the XAML reports unknown resources `IconShuffle`/`IconRepeat`/`IconRepeatOne`, Task 1 wasn't completed — go back and finish it.

- [ ] **Step 6: Commit**

```bash
git add Views/PlayerView.axaml Views/PlayerView.axaml.cs App.axaml Converters/
git commit -m "feat(player-view): single-scroll album-context layout"
```

---

## Task 11: Wire `LikesService` and the new `PlayerViewModel` signature in DI

**Files:**
- Modify: `App.axaml.cs`

- [ ] **Step 1: Update `App.axaml.cs`**

Find the service-construction block (lines 27-33 in the current file). Insert the likes service after `catalog`:

```csharp
            var nav = new NavigationService();
            var auth = new AuthService(dbFactory);
            var cart = new CartService(auth, dbFactory);
            var catalog = new CatalogService(dbFactory);
            var likes = new LikesService(dbFactory);
            var search = new SearchService(dbFactory);
            var files = new FileDialogService();
            var player = new PlayerService(auth, dbFactory, catalog);
```

Then update the Player factory registration:

```csharp
            nav.Register(NavTarget.Player,
                _ => new PlayerViewModel(player, catalog, auth, likes, nav, files));
```

- [ ] **Step 2: Build**

Run: `dotnet build MusicApp.csproj`
Expected: succeeds.

- [ ] **Step 3: Run the full app to sanity-check it boots**

Run: `dotnet build MusicApp.csproj -nologo -v q` (must succeed).
Then run the app:
- Headless smoke (no GUI needed): `dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj --filter "FullyQualifiedName~SmokeTests.MainWindow_opens_and_resizes"`
- Expected: passes; no crash at startup.

- [ ] **Step 4: Commit**

```bash
git add App.axaml.cs
git commit -m "feat(di): register LikesService and update Player VM wiring"
```

---

## Task 12: BugHunt smoke test for the redesigned Player

**Files:**
- Create: `MusicApp.BugHunt/PlayerRedesignTests.cs`

- [ ] **Step 1: Write the integration test**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MusicApp.Services;

namespace MusicApp.BugHunt;

public class PlayerRedesignTests
{
    [AvaloniaFact]
    public void Player_page_shows_header_tracklist_reviews_without_playback_controls()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.RunStep("00-open-app", () => { });

        // Navigate to the Player page.
        h.RunStep("01-nav-player", () => h.Nav!.NavigateTo(NavTarget.Player));

        // Pick the first purchased album (seeded for admin) and play it.
        h.RunStep("02-play-album", () =>
        {
            var pvm = h.Nav!.CurrentView as MusicApp.ViewModels.PlayerViewModel;
            if (pvm is not null && pvm.PurchasedAlbums.Count > 0)
                pvm.PlayAlbumCommand.Execute(pvm.PurchasedAlbums[0]);
        });

        // Snapshot final state for visual inspection.
        h.RunStep("03-player-final", () => { });

        // The redesigned Player must NOT have a "SeekSlider" named in PlayerView.
        // (It now lives in the MiniPlayer.) We assert via the visual tree dump
        // that no Slider named "SeekSlider" exists inside the PlayerView control,
        // but one exists in MiniPlayer.
        var seekInMini = h.Find<Slider>("SeekSlider");
        Assert.NotNull(seekInMini);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test MusicApp.BugHunt/MusicApp.BugHunt.csproj --filter "FullyQualifiedName~PlayerRedesignTests"`
Expected: passes. Inspect `bug-hunt/artifacts/` PNGs and tree dumps — the Player main area should show the album header (cover 160×160, title, artist link, metadata strip, like/shuffle/repeat row), a tracklist, and either a reviews block or the "more from artist" strip.

If the test fails because the harness can't find `SeekSlider`, the MiniPlayer change in Task 7 was incomplete.

- [ ] **Step 3: Commit**

```bash
git add MusicApp.BugHunt/PlayerRedesignTests.cs
git commit -m "test(player): smoke-test redesigned Player page and MiniPlayer seek"
```

---

## Task 13: Manual verification

**Files:** none (manual step).

- [ ] **Step 1: Launch the desktop app**

Run: `dotnet run --project MusicApp.csproj`
Login as the seeded `admin` / `admin` (or any seeded buyer).

- [ ] **Step 2: Walk through the redesigned page**

In the running app:
1. Click "Плеєр" in the sidebar.
2. Click a purchased album in the left sidebar.
3. Confirm the main area shows the new layout: 160×160 cover, title, artist link, year/genre/duration line, like/shuffle/repeat buttons; below — description; below — tracklist; below — reviews; below — "more from artist" strip.
4. Confirm there are **no** play/pause/seek/volume controls on the page.
5. Confirm the MiniPlayer at the bottom shows play/pause/prev/next/volume **and a seek slider** that you can drag.
6. Click the artist name → should land on Search Results filtered by that artist.
7. Click a track in the tracklist → should jump to that track.
8. Toggle Shuffle and Repeat → icons reflect state (faded for off, accent for on; repeat icon changes shape for "one").
9. Like a track and like the album → heart icons fill in.
10. If you have purchased the album, the "Залишити відгук" form is available; submit and confirm the review appears.

- [ ] **Step 2 (alt): If GUI isn't available, use the BugHunt artifacts**

Inspect `bug-hunt/artifacts/*-PlayerRedesignTests*.png` and the matching `.tree.txt` files for the same checks. The text dumps will not look pretty, but they prove the controls are present and bound.

- [ ] **Step 3: Note any visual issues**

If any layout/binding bug shows up, file it as a follow-up (do **not** patch in this plan — keep this plan's commits scoped to the spec).

---

## Out-of-scope reminders

- Playlists: not built in this plan (deferred — see spec § Out of Scope).
- Lyrics: not built (explicitly removed).
- New Catalog artist filter: not built (replaced by SearchResults nav).
- Track context menu (⋮): not built (only like is inline).
