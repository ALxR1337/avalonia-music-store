namespace MusicApp.Models;

// Explicit join with ordering, so a playlist has a deterministic playback order.
public class PlaylistTrack
{
    public int PlaylistId { get; set; }
    public Playlist? Playlist { get; set; }
    public int TrackId { get; set; }
    public Track? Track { get; set; }
    public int Position { get; set; }
}
