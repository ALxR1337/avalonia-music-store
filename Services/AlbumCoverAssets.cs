using System;
using Avalonia.Platform;

namespace MusicApp.Services;

/// <summary>
/// Maps an album (artist + title) to a bundled cover asset under
/// <c>Assets/covers/</c> — used to fill in covers for the few seeded albums
/// whose local folder shipped no usable cover image. Mirrors
/// <see cref="ArtistPhotoAssets"/>: returns the avares-relative path only when
/// the asset is actually embedded, so a miss leaves <c>CoverPath</c> null and
/// the existing placeholder shows instead of an empty tile.
/// </summary>
public static class AlbumCoverAssets
{
    public static string? For(string? artist, string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var slug = AssetSlug.Of($"{artist} {title}");
        if (slug.Length == 0)
            return null;

        var relative = $"Assets/covers/{slug}.jpg";
        try
        {
            if (AssetLoader.Exists(new Uri($"avares://MusicApp/{relative}")))
                return relative;
        }
        catch
        {
        }

        return null;
    }
}
