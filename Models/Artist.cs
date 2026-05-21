namespace MusicApp.Models;

public class Artist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Aliases { get; set; }
    public string? Country { get; set; }
    public string? Description { get; set; }
}
