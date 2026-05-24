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
    public int SampleStartSeconds { get; set; }

    public string DurationDisplay
    {
        get
        {
            if (Duration <= TimeSpan.Zero) return string.Empty;
            return Duration.TotalHours >= 1
                ? Duration.ToString(@"h\:mm\:ss")
                : Duration.ToString(@"m\:ss");
        }
    }
}
