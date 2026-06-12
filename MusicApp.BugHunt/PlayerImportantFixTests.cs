using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Regression guards for Wave Player-Important-Fixes:
//   - the header play button toggles the current album instead of restarting;
//   - pause no longer collapses the description or rebuilds the tracklist;
//   - the mini-player volume slider follows service-side volume changes;
//   - album like round-trips; a guest's like click opens the login overlay;
//   - "More from artist" tiles open the album (player/product), not a search;
//   - hardware media keys drive the player.
public class PlayerImportantFixTests
{
    private static PlayerViewModel OpenAlbum(Harness h, MusicApp.Models.Album album)
    {
        h.Nav!.NavigateTo(NavTarget.Player);
        (h.Nav.CurrentView as PlayerViewModel)!.OpenAlbumCommand.Execute(album);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<PlayerViewModel>(h.Nav.CurrentView);
    }

    [AvaloniaFact]
    public void Play_button_toggles_current_album_instead_of_restarting()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var owned = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id)
            .First(a => a.Tracks.Count > 1);
        var pvm = OpenAlbum(h, owned);

        pvm.PlayTrackCommand.Execute(pvm.Tracks[1]);
        Dispatcher.UIThread.RunJobs();
        var playingId = h.Player!.CurrentTrack!.Id;
        Assert.Equal(owned.Tracks[1].Id, playingId);

        // Pressing the big play again must toggle pause, not restart track 1.
        pvm.PlaySelectedAlbumCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(playingId, h.Player.CurrentTrack!.Id);

        h.Player.Stop();
    }

    [AvaloniaFact]
    public void Pause_keeps_description_expanded_and_tracklist_rows_intact()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var owned = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id)
            .First(a => a.Tracks.Count > 0);
        var pvm = OpenAlbum(h, owned);

        pvm.PlaySelectedAlbumCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        pvm.IsDescriptionExpanded = true;
        var firstRow = pvm.Tracks[0];

        // Stop raises PlaybackStateChanged synchronously — the old full
        // Refresh() collapsed the description and rebuilt every row on it.
        h.Player!.Stop();
        Dispatcher.UIThread.RunJobs();

        Assert.True(pvm.IsDescriptionExpanded);
        Assert.Same(firstRow, pvm.Tracks[0]);
    }

    [AvaloniaFact]
    public void Service_volume_change_reaches_miniplayer_slider()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        var mini = shell.MiniPlayer!;

        try
        {
            // Simulates "another user's persisted volume gets applied".
            h.Player!.Volume = 0.37;
            Assert.Equal(0.37, mini.Volume, 2);
        }
        finally
        {
            h.Player!.Volume = 0;   // keep the shared suite muted
        }
    }

    [AvaloniaFact]
    public void Album_like_round_trips_through_likes_service()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var owned = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id).First();
        var pvm = OpenAlbum(h, owned);

        var before = pvm.IsAlbumLiked;
        try
        {
            pvm.ToggleAlbumLikeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(!before, pvm.IsAlbumLiked);
        }
        finally
        {
            // Shared seeded DB per run — restore the original like state.
            if (pvm.IsAlbumLiked != before)
            {
                pvm.ToggleAlbumLikeCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
            }
        }
        Assert.Equal(before, pvm.IsAlbumLiked);
    }

    [AvaloniaFact]
    public void Guest_like_click_opens_login_overlay()
    {
        var h = new Harness();
        h.OpenMainWindow();   // guest — no login
        var album = h.Catalog!.Albums.First(a => a.Tracks.Count > 0);
        var pvm = OpenAlbum(h, album);
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        Assert.False(shell.IsLoginVisible);

        pvm.ToggleTrackLikeCommand.Execute(pvm.Tracks[0]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.IsLoginVisible);
    }

    [AvaloniaFact]
    public void Artist_album_tile_routes_to_player_or_product()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var purchased = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id);
        var ownedIds = purchased.Select(a => a.Id).ToHashSet();
        var owned = purchased.First();
        var unownedWithProduct = h.Catalog.Albums.First(a =>
            !ownedIds.Contains(a.Id) && h.Catalog.GetPrimaryProductId(a.Id) is > 0);

        var pvm = OpenAlbum(h, owned);

        // Unowned + in catalog → its product page (was: generic artist search).
        pvm.GoToArtistAlbumCommand.Execute(unownedWithProduct);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<ProductViewModel>(h.Nav!.CurrentView);

        // Owned → straight into the player with that album selected.
        h.Nav.NavigateTo(NavTarget.Player);
        var grid = (PlayerViewModel)h.Nav.CurrentView!;
        grid.GoToArtistAlbumCommand.Execute(owned);
        Dispatcher.UIThread.RunJobs();
        var opened = Assert.IsType<PlayerViewModel>(h.Nav.CurrentView);
        Assert.Equal(owned.Id, opened.SelectedAlbum?.Id);
    }

    [AvaloniaFact]
    public void Media_next_key_advances_to_the_next_track()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var hadShuffle = h.Player!.ShuffleMode;
        h.Player.ShuffleMode = false;   // deterministic linear order
        try
        {
            var owned = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id)
                .First(a => a.Tracks.Count > 1);
            // Start playback directly: the header button is a toggle for an
            // already-loaded album (e.g. the login-restored last track), which
            // would make the starting track order-dependent.
            h.Player.PlayAlbum(owned);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(owned.Tracks[0].Id, h.Player.CurrentTrack!.Id);

            h.Window!.KeyPress(Key.MediaNextTrack, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(owned.Tracks[1].Id, h.Player.CurrentTrack!.Id);
        }
        finally
        {
            h.Player.Stop();
            h.Player.ShuffleMode = hadShuffle;
        }
    }
}
