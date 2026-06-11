using System;
using Avalonia.Platform;

namespace MusicApp.Services;

/// <summary>
/// Maps an artist name to a bundled avatar asset under <c>Assets/artists/</c>
/// (sourced from the Deezer public artist API). Returns the avares-relative
/// path (e.g. <c>"Assets/artists/bob-dylan.jpg"</c>) — the shape
/// <see cref="Converters.CoverPathToImageConverter"/> expects — but only when
/// the asset is actually embedded in the assembly. A missing asset yields
/// <c>null</c> so callers never persist a dangling path, which would hide the
/// letter-placeholder fallback and leave an empty circle.
/// </summary>
public static class ArtistPhotoAssets
{
    public static string? For(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var relative = $"Assets/artists/{AssetSlug.Of(name)}.jpg";
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
