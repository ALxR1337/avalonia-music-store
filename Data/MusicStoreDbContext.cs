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

    public static string DefaultDbDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicStore");

    public static string DefaultDbPath => Path.Combine(DefaultDbDirectory, "store.db");

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured) return;

        Directory.CreateDirectory(DefaultDbDirectory);
        optionsBuilder.UseSqlite($"Data Source={DefaultDbPath}");
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        var formatConverter = new EnumToStringConverter<ProductFormat>();
        var orderStatusConverter = new EnumToStringConverter<OrderStatus>();
        var userRoleConverter = new EnumToStringConverter<UserRole>();

        mb.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email);
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
            e.HasIndex(a => a.Name);
        });

        mb.Entity<Genre>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(64).IsRequired();
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
            e.HasOne(a => a.Genre)
                .WithMany()
                .HasForeignKey(a => a.GenreId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(a => a.Tracks)
                .WithOne()
                .HasForeignKey(t => t.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.Title);
        });

        mb.Entity<Track>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.SamplePath).HasMaxLength(260);
            e.Property(t => t.FullPath).HasMaxLength(260);
            e.Ignore(t => t.DurationDisplay);
        });

        mb.Entity<Product>(e =>
        {
            e.Property(p => p.Price).HasColumnType("DECIMAL(10,2)");
            e.Property(p => p.Label).HasMaxLength(120);
            e.Property(p => p.Format).HasConversion(formatConverter).HasMaxLength(8);
            e.HasOne(p => p.Album)
                .WithMany()
                .HasForeignKey(p => p.AlbumId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.AlbumId, p.Format }).IsUnique();
            e.HasIndex(p => p.IsActive);
            e.Ignore(p => p.FormatBadge);
        });

        mb.Entity<CartItem>(e =>
        {
            e.HasOne(c => c.Product)
                .WithMany()
                .HasForeignKey(c => c.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.UserId, c.ProductId }).IsUnique();
            e.Ignore(c => c.LineTotal);
        });

        mb.Entity<Order>(e =>
        {
            e.Property(o => o.TotalAmount).HasColumnType("DECIMAL(10,2)");
            e.Property(o => o.Status).HasConversion(orderStatusConverter).HasMaxLength(16);
            e.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(o => o.UserId);
            e.HasIndex(o => o.Status);
        });

        mb.Entity<OrderItem>(e =>
        {
            e.Property(i => i.UnitPrice).HasColumnType("DECIMAL(10,2)");
            e.HasOne(i => i.Product)
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Ignore(i => i.LineTotal);
        });

        mb.Entity<Review>(e =>
        {
            e.Property(r => r.Text).HasMaxLength(2000).IsRequired();
            e.Property(r => r.UserDisplayName).HasMaxLength(80);
            e.HasIndex(r => r.ProductId);
            e.HasIndex(r => r.UserId);
        });

        mb.Entity<Playlist>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(120).IsRequired();
            e.HasMany(p => p.Tracks).WithMany();
            e.HasIndex(p => p.UserId);
        });
    }
}
