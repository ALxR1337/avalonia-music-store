using System;

namespace MusicApp.Models;

public class Review
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Rating { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Not mapped: populated by ICatalogService.GetReviewsByUser so the profile UI
    // can show which product/album the review belongs to without an extra round-trip.
    public Product? Product { get; set; }
}
