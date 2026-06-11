using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MusicApp.Converters;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Verifies the artist avatars wired up from the Deezer artist API: the seed
/// backfill stamps every catalog artist with a bundled <c>Assets/artists/*.jpg</c>
/// path, and the cover converter resolves each one to a real embedded bitmap
/// (so the "Виконавці" row shows photos, not letter placeholders).
/// </summary>
public class ArtistAvatarTests
{
    [AvaloniaFact]
    public void Every_seeded_artist_has_a_loadable_avatar()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1400, 1100);
        h.RunStep("00-artist-avatars", () => h.Nav!.NavigateTo(NavTarget.Catalog));

        var cvm = h.Nav!.CurrentView as CatalogViewModel;
        Assert.NotNull(cvm);
        Assert.NotEmpty(cvm!.Artists);

        foreach (var artist in cvm.Artists)
        {
            Assert.False(string.IsNullOrWhiteSpace(artist.PhotoPath),
                $"{artist.Name} has no PhotoPath after the backfill.");

            var image = CoverPathToImageConverter.Instance.Convert(
                artist.PhotoPath, typeof(Bitmap), null, CultureInfo.InvariantCulture);
            Assert.True(image is Bitmap,
                $"{artist.Name} avatar '{artist.PhotoPath}' did not resolve to an embedded bitmap.");
        }
    }

    [AvaloniaFact]
    public void Backfill_resolves_assets_for_all_seeded_artists()
    {
        var h = new Harness();
        h.OpenMainWindow();
        Dispatcher.UIThread.RunJobs();

        // Every artist in the seeded DB must map to a bundled avatar asset; a
        // null here would mean a filename/slug mismatch with Assets/artists/.
        var missing = h.Catalog!.Artists
            .Where(a => ArtistPhotoAssets.For(a.Name) is null)
            .Select(a => a.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "Artists with no matching avatar asset: " + string.Join(", ", missing));
    }
}
