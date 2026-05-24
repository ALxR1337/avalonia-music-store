using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace MusicApp.Models;

public class Product
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public Album? Album { get; set; }
    public ProductFormat Format { get; set; }

    // Money stored as integer cents (kopiykas) to dodge SQLite's loose DECIMAL
    // affinity. Price is a decimal facade for existing callers/XAML.
    public long PriceCents { get; set; }

    [NotMapped]
    public decimal Price
    {
        get => PriceCents / 100m;
        set => PriceCents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }

    public int Stock { get; set; }
    public int ReleaseYear { get; set; }
    public string? Label { get; set; }
    public bool IsActive { get; set; } = true;

    // Maintained by SQLite triggers on Reviews / OrderItems — do not assign
    // from application code.
    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public int SalesCount { get; set; }

    public string FormatBadge => Format == ProductFormat.Vinyl ? "LP" : "CD";
}
