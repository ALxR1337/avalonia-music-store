using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MusicApp.Models;

namespace MusicApp.Data;

public class MusicStoreDbContext : DbContext
{
    public MusicStoreDbContext() { }

    public MusicStoreDbContext(DbContextOptions<MusicStoreDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();
    public DbSet<PlayerSettings> PlayerSettings => Set<PlayerSettings>();
    public DbSet<SearchHistory> SearchHistory => Set<SearchHistory>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<AlbumGenre> AlbumGenres => Set<AlbumGenre>();
    public DbSet<TrackLike> TrackLikes => Set<TrackLike>();
    public DbSet<AlbumLike> AlbumLikes => Set<AlbumLike>();

    public static string DefaultDbDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicStore");

    public static string DefaultDbPath => Path.Combine(DefaultDbDirectory, "store.db");

    public static string ResolveDbPath()
    {
        var overridden = Environment.GetEnvironmentVariable("MUSICAPP_DB_PATH");
        return string.IsNullOrEmpty(overridden) ? DefaultDbPath : overridden;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured) return;

        var path = ResolveDbPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        optionsBuilder.UseSqlite($"Data Source={path}");
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        var formatConverter = new EnumToStringConverter<ProductFormat>();
        var orderStatusConverter = new EnumToStringConverter<OrderStatus>();
        var userRoleConverter = new EnumToStringConverter<UserRole>();
        var repeatModeConverter = new EnumToStringConverter<RepeatMode>();

        mb.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(120).IsRequired();
            e.Property(u => u.Email).HasMaxLength(120);
            e.Property(u => u.Role).HasConversion(userRoleConverter).HasMaxLength(16);
        });

        mb.Entity<Artist>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(120).IsRequired();
            e.Property(a => a.Aliases).HasMaxLength(240);
            e.Property(a => a.Country).HasMaxLength(64);
            e.Property(a => a.PhotoPath).HasMaxLength(260);
            e.HasIndex(a => a.Name);
        });

        mb.Entity<Genre>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(64).IsRequired().UseCollation("NOCASE");
            e.HasIndex(g => g.Name).IsUnique();
        });

        mb.Entity<Album>(e =>
        {
            e.Property(a => a.Title).HasMaxLength(200).IsRequired();
            e.Property(a => a.CoverPath).HasMaxLength(260);
            e.HasOne(a => a.Artist)
                .WithMany()
                .HasForeignKey(a => a.ArtistId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(a => a.Tracks)
                .WithOne()
                .HasForeignKey(t => t.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.Title);
            e.Ignore(a => a.Genre);
            e.Ignore(a => a.GenreId);
        });

        mb.Entity<Track>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.SamplePath).HasMaxLength(260);
            e.Property(t => t.FullPath).HasMaxLength(260);
            e.Property(t => t.SampleStartSeconds).HasDefaultValue(0);
            e.HasIndex(t => new { t.AlbumId, t.Position }).IsUnique();
            e.Ignore(t => t.DurationDisplay);
        });

        mb.Entity<Product>(e =>
        {
            e.Property(p => p.Label).HasMaxLength(120);
            e.Property(p => p.Format).HasConversion(formatConverter).HasMaxLength(8);
            e.HasOne(p => p.Album)
                .WithMany()
                .HasForeignKey(p => p.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.AlbumId, p.Format }).IsUnique();
            e.HasIndex(p => p.IsActive);
            e.Ignore(p => p.Price);
            e.Ignore(p => p.FormatBadge);
        });

        mb.Entity<CartItem>(e =>
        {
            e.HasOne(c => c.Product)
                .WithMany()
                .HasForeignKey(c => c.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.UserId, c.ProductId }).IsUnique();
            e.Ignore(c => c.LineTotal);
        });

        mb.Entity<Order>(e =>
        {
            e.Property(o => o.Status).HasConversion(orderStatusConverter).HasMaxLength(16);
            e.Property(o => o.Currency).HasMaxLength(8).IsRequired();
            e.Property(o => o.UserEmail).HasMaxLength(120);
            e.Property(o => o.ShippingAddress).HasMaxLength(400);
            e.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(o => o.UserId);
            e.HasIndex(o => o.Status);
            e.Ignore(o => o.TotalAmount);
        });

        mb.Entity<OrderItem>(e =>
        {
            e.Property(i => i.ProductTitle).HasMaxLength(240).IsRequired();
            e.Property(i => i.AlbumTitle).HasMaxLength(200).IsRequired();
            e.Property(i => i.ArtistName).HasMaxLength(120).IsRequired();
            e.Property(i => i.FormatLabel).HasMaxLength(8).IsRequired();
            e.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Ignore(i => i.UnitPrice);
            e.Ignore(i => i.LineTotal);
        });

        mb.Entity<Review>(e =>
        {
            e.Property(r => r.Text).HasMaxLength(2000).IsRequired();
            e.Property(r => r.UserDisplayName).HasMaxLength(80);
            e.HasOne<Product>()
                .WithMany()
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.ProductId);
            e.HasIndex(r => r.UserId);
            // Product is populated client-side (CatalogService.GetReviewsByUser); EF should not own it.
            e.Ignore(r => r.Product);
        });

        mb.Entity<Playlist>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(120).IsRequired();
            e.HasMany(p => p.Tracks)
                .WithOne(pt => pt.Playlist!)
                .HasForeignKey(pt => pt.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.UserId);
        });

        mb.Entity<PlaylistTrack>(e =>
        {
            e.HasKey(pt => new { pt.PlaylistId, pt.TrackId });
            e.HasOne(pt => pt.Track)
                .WithMany()
                .HasForeignKey(pt => pt.TrackId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(pt => new { pt.PlaylistId, pt.Position }).IsUnique();
        });

        mb.Entity<PlayerSettings>(e =>
        {
            e.HasKey(s => s.UserId);
            e.Property(s => s.UserId).ValueGeneratedNever();
            e.Property(s => s.RepeatMode).HasConversion(repeatModeConverter).HasMaxLength(8);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Track>()
                .WithMany()
                .HasForeignKey(s => s.LastTrackId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<SearchHistory>(e =>
        {
            e.Property(s => s.Query).HasMaxLength(500).IsRequired();
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.UserId, s.ExecutedAt });
        });

        mb.Entity<SavedSearch>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(120).IsRequired();
            e.Property(s => s.QueryJson).IsRequired();
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.UserId);
        });

        mb.Entity<Wishlist>(e =>
        {
            e.HasOne(w => w.Product)
                .WithMany()
                .HasForeignKey(w => w.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(w => new { w.UserId, w.ProductId }).IsUnique();
        });

        mb.Entity<AlbumGenre>(e =>
        {
            e.HasKey(ag => new { ag.AlbumId, ag.GenreId });
            e.HasOne(ag => ag.Album)
                .WithMany(a => a.AlbumGenres)
                .HasForeignKey(ag => ag.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ag => ag.Genre)
                .WithMany()
                .HasForeignKey(ag => ag.GenreId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(ag => ag.GenreId);
            // Only one primary per album. NULL where IsPrimary=false, partial unique
            // index keeps a single TRUE row per AlbumId.
            e.HasIndex(ag => ag.AlbumId)
                .IsUnique()
                .HasFilter("\"IsPrimary\" = 1");
        });

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
    }
}
