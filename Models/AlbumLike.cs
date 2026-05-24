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
