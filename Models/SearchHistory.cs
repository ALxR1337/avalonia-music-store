using System;

namespace MusicApp.Models;

public class SearchHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Query { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public int ResultCount { get; set; }
}
