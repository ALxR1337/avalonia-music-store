using System;

namespace MusicApp.Models;

public class SavedSearch
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string QueryJson { get; set; } = string.Empty;
    public bool NotifyOnNew { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
