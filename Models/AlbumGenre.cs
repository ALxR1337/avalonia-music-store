namespace MusicApp.Models;

// Join entity for Album↔Genre many-to-many. Exactly one row per album should
// have IsPrimary=true; the rest are secondary tags.
public class AlbumGenre
{
    public int AlbumId { get; set; }
    public Album? Album { get; set; }
    public int GenreId { get; set; }
    public Genre? Genre { get; set; }
    public bool IsPrimary { get; set; }
}
