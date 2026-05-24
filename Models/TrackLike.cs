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
