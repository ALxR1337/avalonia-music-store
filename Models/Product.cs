namespace MusicApp.Models;

public class Product
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public Album? Album { get; set; }
    public ProductFormat Format { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int ReleaseYear { get; set; }
    public string? Label { get; set; }
    public bool IsActive { get; set; } = true;

    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public int SalesCount { get; set; }

    public string FormatBadge => Format == ProductFormat.Vinyl ? "LP" : "CD";
}
