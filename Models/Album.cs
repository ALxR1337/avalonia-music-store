using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace MusicApp.Models;

public class Album
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? CoverPath { get; set; }
    public string? Description { get; set; }
    public List<Track> Tracks { get; set; } = new();
    public List<AlbumGenre> AlbumGenres { get; set; } = new();

    // Primary genre is the one row in AlbumGenres flagged IsPrimary. Kept as a
    // NotMapped facade so existing callers ("album.Genre", "album.GenreId") keep
    // working after the schema dropped Album.GenreId.
    [NotMapped]
    public Genre? Genre => AlbumGenres?.FirstOrDefault(ag => ag.IsPrimary)?.Genre
                        ?? AlbumGenres?.FirstOrDefault()?.Genre;

    [NotMapped]
    public int GenreId => Genre?.Id ?? 0;
}
