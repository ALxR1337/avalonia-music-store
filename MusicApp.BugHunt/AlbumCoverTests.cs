using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MusicApp.Converters;
using MusicApp.Services;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Covers the bundled fallback album art: a handful of seeded albums shipped no
/// usable cover in their local folder, so their covers were fetched and embedded
/// under Assets/covers/ and the seed backfill stamps CoverPath from them.
/// </summary>
public class AlbumCoverTests
{
    // (artist, album title) for every seeded album whose folder had no cover.
    public static readonly (string Artist, string Title)[] GapAlbums =
    {
        ("Bob Dylan", "The Freewheelin' Bob Dylan"),
        ("Daft Punk", "Discovery"),
        ("JPEGMAFIA", "Veteran"),
        ("Kendrick Lamar", "To Pimp A Butterfly"),
        ("The Dave Brubeck Quartet", "Time Out"),
    };

    [AvaloniaFact]
    public void Bundled_cover_asset_resolves_to_a_bitmap_for_each_gap_album()
    {
        var h = new Harness();
        h.OpenMainWindow();           // boots Avalonia so AssetLoader works
        Dispatcher.UIThread.RunJobs();

        foreach (var (artist, title) in GapAlbums)
        {
            var asset = AlbumCoverAssets.For(artist, title);
            Assert.False(string.IsNullOrEmpty(asset),
                $"No bundled cover asset for {artist} — {title}.");

            var image = CoverPathToImageConverter.Instance.Convert(
                asset, typeof(Bitmap), null, CultureInfo.InvariantCulture);
            Assert.True(image is Bitmap,
                $"Cover '{asset}' for {artist} — {title} did not load as an embedded bitmap.");
        }
    }

    [AvaloniaFact]
    public void Backfill_stamps_coverpath_on_the_gap_albums()
    {
        var h = new Harness();
        h.OpenMainWindow();
        Dispatcher.UIThread.RunJobs();

        foreach (var (_, title) in GapAlbums)
        {
            var album = h.Catalog!.Albums.FirstOrDefault(a => a.Title == title);
            Assert.NotNull(album);
            Assert.False(string.IsNullOrWhiteSpace(album!.CoverPath),
                $"'{title}' still has no CoverPath after the backfill.");
        }
    }

    [AvaloniaFact]
    public void Gap_album_product_page_shows_the_bundled_cover()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1400, 1100);

        var album = h.Catalog!.Albums.First(a => a.Title == "Discovery");
        var productId = h.Catalog!.GetPrimaryProductId(album.Id);
        Assert.NotNull(productId);

        h.RunStep("01-gap-album-cover", () =>
            h.Nav!.NavigateTo(NavTarget.Product, productId!.Value));
    }
}
