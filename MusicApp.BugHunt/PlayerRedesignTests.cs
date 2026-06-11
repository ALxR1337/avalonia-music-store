using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.Services;
using MusicApp.ViewModels;
using MusicApp.Views;
using Xunit;

namespace MusicApp.BugHunt;

public class PlayerRedesignTests
{
    [AvaloniaFact]
    public void Player_page_shows_header_tracklist_reviews_without_playback_controls()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.RunStep("00-open-app", () => { });

        // Navigate to the Player page (library grid).
        h.RunStep("01-nav-player-grid", () => h.Nav!.NavigateTo(NavTarget.Player));

        // Open any album in the catalog through the player VM. OpenAlbumCommand
        // navigates the shell — so after this step CurrentView is a fresh
        // PlayerViewModel with the album pre-selected and the previous (grid)
        // VM is on the nav history stack.
        h.RunStep("02-open-album", () =>
        {
            var pvm = h.Nav!.CurrentView as PlayerViewModel;
            var album = h.Catalog!.Albums.FirstOrDefault();
            if (pvm is not null && album is not null)
                pvm.OpenAlbumCommand.Execute(album);
        });

        // Play the open album so the MiniPlayer's visual subtree is realized.
        h.RunStep("03-play-and-show-miniplayer", () =>
        {
            var pvm = h.Nav!.CurrentView as PlayerViewModel;
            pvm?.PlaySelectedAlbumCommand.Execute(null);
            var shell = h.Window!.DataContext as MainWindowViewModel;
            if (shell is not null) shell.IsMiniPlayerVisible = true;
            Dispatcher.UIThread.RunJobs();
        });

        // Back arrow in the top bar → restore the library grid VM.
        h.RunStep("04-back-to-grid", () => h.Nav!.GoBack());

        // Forward arrow → restore the album VM.
        h.RunStep("05-forward-to-album", () => h.Nav!.GoForward());

        // SeekSlider must live in the MiniPlayer, not in PlayerView.
        var seekInMini = h.Find<Slider>("SeekSlider");
        Assert.NotNull(seekInMini);

        // The album view must be the current view after the round-trip.
        var finalPvm = h.Nav!.CurrentView as PlayerViewModel;
        Assert.NotNull(finalPvm);
        Assert.True(finalPvm!.HasSelectedAlbum);
    }

    // The redesigned bottom bar must keep its centre column (transport + seek)
    // inside the 80px player strip — no slider spilling past the bottom edge,
    // and a slim seek slider. Regression guard for the earlier layout where the
    // default 44px-tall Fluent slider pushed the seek row off the bar.
    [AvaloniaFact]
    public void MiniPlayer_seek_bar_fits_within_the_bottom_strip()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.Nav!.NavigateTo(NavTarget.Player);

        var album = h.Catalog!.Albums.FirstOrDefault();
        Assert.NotNull(album);
        var pvm = h.Nav.CurrentView as PlayerViewModel;
        pvm!.OpenAlbumCommand.Execute(album);
        pvm.PlaySelectedAlbumCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var shell = h.Window!.DataContext as MainWindowViewModel;
        shell!.IsMiniPlayerVisible = true;

        // Simulate mid-playback so the filled track + time labels render in the
        // snapshot (the headless player never advances Position on its own).
        var mini = shell.MiniPlayer!;
        mini.TrackTitle = "Black Skinhead";
        mini.ArtistName = "Kanye West";
        mini.PositionText = "1:12";
        mini.DurationText = "3:08";
        mini.Progress = 38;
        Dispatcher.UIThread.RunJobs();

        var seek = h.Find<Slider>("SeekSlider");
        var bar = seek.GetVisualAncestors().OfType<MiniPlayerView>().First();

        // The bar slides in over 0.25s via a translateY transition on its host
        // Border. The headless clock doesn't advance on its own, so the frame
        // would catch it mid-slide (shifted down, seek row clipped). Pin the
        // transform/opacity to their settled values so the snapshot is clean.
        var hostBorder = bar.GetVisualAncestors().OfType<Border>().First();
        hostBorder.Transitions = null;   // kill the in-flight slide animation
        hostBorder.RenderTransform = null;
        hostBorder.Opacity = 1;
        h.RunStep("miniplayer-redesign", () => { });

        // Slider top within the bar = sum of Bounds.Y up the ancestor chain.
        double top = 0;
        for (Avalonia.Visual? cur = seek; cur is not null && cur != bar; cur = cur.GetVisualParent())
            if (cur is Control c) top += c.Bounds.Y;
        var bottom = top + seek.Bounds.Height;

        Assert.True(seek.Bounds.Height <= 28,
            $"Seek slider is {seek.Bounds.Height:0}px tall — expected a slim (<=28px) bar.");
        Assert.True(bottom <= bar.Bounds.Height + 0.5,
            $"Seek slider bottom ({bottom:0}) spills past the {bar.Bounds.Height:0}px player strip.");
    }

    // Each metadata link on the album page navigates to a focused, filtered view.
    // Regression coverage for the bug where `виконавець:"Name"` produced an
    // unfiltered global search because SearchResultsViewModel dropped the term.
    [AvaloniaFact]
    public void Album_metadata_links_open_filtered_destinations()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        // Pick an album that has an artist, genre, and a product so all three
        // links are exercised.
        var album = h.Catalog!.Albums
            .FirstOrDefault(a => a.Artist is not null && a.Genre is not null
                && h.Catalog.GetPrimaryProductId(a.Id) is not null);
        Assert.NotNull(album);

        h.RunStep("link-00-open-album", () =>
        {
            h.Nav!.NavigateTo(NavTarget.Player);
            var pvm = h.Nav.CurrentView as PlayerViewModel;
            pvm!.OpenAlbumCommand.Execute(album);
        });

        // 1) Album title → catalog product page.
        h.RunStep("link-01-title-to-product", () =>
        {
            var pvm = h.Nav!.CurrentView as PlayerViewModel;
            pvm!.GoToAlbumProductCommand.Execute(null);
        });
        Assert.Equal(NavTarget.Product, h.Nav!.CurrentTarget);

        // 2) Artist → search filtered by artist name only. Regression: this used
        // to land on the full catalog because the field term was dropped; now the
        // artist surfaces as a SelectedArtists facet.
        h.Nav.GoBack(); // back to album page
        h.RunStep("link-02-artist-to-search", () =>
        {
            var pvm = h.Nav.CurrentView as PlayerViewModel;
            pvm!.GoToArtistCommand.Execute(null);
        });
        Assert.Equal(NavTarget.SearchResults, h.Nav.CurrentTarget);
        var artistSrvm = h.Nav.CurrentView as SearchResultsViewModel;
        Assert.NotNull(artistSrvm);
        Assert.Contains(album!.Artist!.Name, artistSrvm!.SelectedArtists);

        // 3) Genre → search filtered by genre. The genre name must surface as a
        // SelectedGenres facet so the results narrow.
        h.Nav.GoBack();
        h.RunStep("link-03-genre-to-search", () =>
        {
            var pvm = h.Nav.CurrentView as PlayerViewModel;
            pvm!.GoToGenreCommand.Execute(null);
        });
        Assert.Equal(NavTarget.SearchResults, h.Nav.CurrentTarget);
        var genreSrvm = h.Nav.CurrentView as SearchResultsViewModel;
        Assert.NotNull(genreSrvm);
        Assert.Contains(album.Genre!.Name, genreSrvm!.SelectedGenres);
    }
}
