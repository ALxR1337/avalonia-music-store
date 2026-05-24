namespace MusicApp.Models;

public class PlayerSettings
{
    public int UserId { get; set; }
    public int Volume { get; set; } = 80;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
    public bool ShuffleMode { get; set; }
    public int? LastTrackId { get; set; }
}
