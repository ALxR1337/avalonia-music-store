namespace MusicApp.Models;

public sealed class NewArrivalAlbum
{
    public Album Album { get; init; } = null!;
    public Product? Vinyl { get; init; }
    public Product? Cd { get; init; }

    public bool HasVinyl => Vinyl is not null;
    public bool HasCd => Cd is not null;

    public Product? PrimaryProduct => Vinyl ?? Cd;
}
