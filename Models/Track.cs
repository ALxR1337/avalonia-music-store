using System;

namespace MusicApp.Models;

public class Track
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public int Position { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Lyrics { get; set; }
    public TimeSpan Duration { get; set; }
    public string? SamplePath { get; set; }
    public string? FullPath { get; set; }

    public string DurationDisplay => Duration.ToString(@"m\:ss");
}
