using System.Collections.Generic;

namespace MusicApp.Models;

public class Album
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public int GenreId { get; set; }
    public Genre? Genre { get; set; }
    public string? CoverPath { get; set; }
    public string? Description { get; set; }
    public List<Track> Tracks { get; set; } = new();
}
