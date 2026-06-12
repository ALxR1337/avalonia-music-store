using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Regression guards for Wave Player-Critical-Fixes:
//   - shuffle actually shuffles (was a persisted-but-ignored flag);
//   - repeat-off stops at the album end (was looping forever like repeat-all);
//   - mini-player ✕ stops playback (was hiding the bar over running audio);
//   - mini-player ⛶ keeps the bar visible (was removing the app's only
//     transport controls);
//   - an unowned album page plays 30s samples and shows a buy CTA (was a
//     silent no-op behind the purchase gate).
public class PlayerCriticalFixTests
{
    // Tracks carry no file paths on purpose: StartTrack takes its no-media
    // early-out, so the queue logic is exercised without touching LibVLC audio.
    private static Album MakeAlbum(int trackCount)
    {
        var album = new Album { Id = 900, ArtistId = 1, Title = "Test Album" };
        for (int i = 0; i < trackCount; i++)
            album.Tracks.Add(new Track { Id = 901 + i, AlbumId = 900, Position = i + 1, Title = $"T{i + 1}" });
        return album;
    }

    [AvaloniaFact]
    public void Shuffle_visits_every_track_once_per_cycle_and_wraps_to_anchor()
    {
        using var svc = new PlayerService();
        var album = MakeAlbum(5);
        svc.PlayAlbum(album);
        svc.ShuffleMode = true;

        var anchor = svc.CurrentTrack!.Id;
        var visited = new List<int> { anchor };
        for (int i = 0; i < 4; i++)
        {
            svc.Next();
            visited.Add(svc.CurrentTrack!.Id);
        }

        // One pass over the shuffle walk covers every track exactly once…
        Assert.Equal(5, visited.Distinct().Count());

        // …and the next step wraps back to the anchor track.
        svc.Next();
        Assert.Equal(anchor, svc.CurrentTrack!.Id);
    }

    [AvaloniaFact]
    public void Repeat_off_stops_at_album_end_instead_of_looping()
    {
        using var svc = new PlayerService();
        var album = MakeAlbum(3);
        svc.RepeatMode = RepeatMode.Off;
        svc.PlayAlbum(album, startTrackIndex: 1);

        // Mid-album: the natural track end still advances.
        svc.HandleTrackEnded();
        Assert.Equal(album.Tracks[2].Id, svc.CurrentTrack!.Id);

        // Last track: playback stops on the final track instead of wrapping.
        svc.HandleTrackEnded();
        Assert.Equal(album.Tracks[2].Id, svc.CurrentTrack!.Id);
        Assert.False(svc.IsPlaying);

        // Repeat-all keeps the old wrap-around behaviour.
        svc.RepeatMode = RepeatMode.All;
        svc.HandleTrackEnded();
        Assert.Equal(album.Tracks[0].Id, svc.CurrentTrack!.Id);
    }

    [AvaloniaFact]
    public void Unowned_album_page_plays_samples_and_offers_buy_cta()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.Nav!.NavigateTo(NavTarget.Player);

        // Derive an album the demo customer does NOT own from the seed itself
        // (hardcoding titles drifts whenever DbSeeder's order list changes).
        // It must have tracks (sample fallback) and a product (buy CTA).
        var ownedIds = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id)
            .Select(a => a.Id).ToHashSet();
        var unowned = h.Catalog.Albums.First(a =>
            !ownedIds.Contains(a.Id)
            && a.Tracks.Count > 0
            && h.Catalog.GetPrimaryProductId(a.Id) is > 0);
        (h.Nav.CurrentView as PlayerViewModel)!.OpenAlbumCommand.Execute(unowned);
        Dispatcher.UIThread.RunJobs();

        var pvm = Assert.IsType<PlayerViewModel>(h.Nav.CurrentView);
        Assert.False(pvm.IsAlbumOwned);
        Assert.True(pvm.ShowSampleHint);
        Assert.True(pvm.ShowBuyCta);
        h.RunStep("critfix-01-unowned-album", () => { });

        pvm.PlaySelectedAlbumCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Player!.IsSampleMode);
        Assert.NotNull(h.Player.CurrentTrack);
        Assert.Equal(unowned.Id, h.Player.CurrentAlbum?.Id);

        h.Player.Stop();
    }

    [AvaloniaFact]
    public void Owned_album_page_plays_full_tracks_without_sample_hint()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.Nav!.NavigateTo(NavTarget.Player);

        var owned = h.Catalog!.GetPurchasedAlbums(h.Auth!.CurrentUser!.Id)
            .First(a => a.Tracks.Count > 0);
        (h.Nav.CurrentView as PlayerViewModel)!.OpenAlbumCommand.Execute(owned);
        Dispatcher.UIThread.RunJobs();

        var pvm = Assert.IsType<PlayerViewModel>(h.Nav.CurrentView);
        Assert.True(pvm.IsAlbumOwned);
        Assert.False(pvm.ShowSampleHint);
        Assert.False(pvm.ShowBuyCta);
        h.RunStep("critfix-02-owned-album", () => { });

        pvm.PlaySelectedAlbumCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Player!.IsSampleMode);
        Assert.Equal(owned.Id, h.Player.CurrentAlbum?.Id);

        h.Player.Stop();
    }

    [AvaloniaFact]
    public void Expand_keeps_bar_visible_and_close_stops_playback()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var shell = (MainWindowViewModel)h.Window!.DataContext!;

        var track = h.Catalog!.Albums.SelectMany(a => a.Tracks).First();
        h.Player!.PlaySample(track);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsMiniPlayerVisible);

        // ⛶ navigates to the Player page but must NOT take the bar away — it
        // is the only pause/seek surface in the app.
        shell.ExpandMiniPlayerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsMiniPlayerVisible);
        Assert.IsType<PlayerViewModel>(h.Nav!.CurrentView);
        h.RunStep("critfix-03-expanded-bar-still-visible", () => { });

        // ✕ hides the bar AND silences the audio.
        shell.CloseMiniPlayerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsMiniPlayerVisible);
        Assert.False(h.Player.IsPlaying);
    }
}
