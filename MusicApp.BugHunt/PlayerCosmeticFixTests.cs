using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Regression guards for Wave Player-Cosmetic-Fixes:
//   - the seeded LastTrackId is restored paused on login ("continue where you
//     left off") and the mini-player surfaces it;
//   - Ukrainian track-count pluralization;
//   - hour-long durations format as h:mm:ss;
//   - «Показати повністю» logic (clamped/expanded);
//   - the mini-player mirrors shuffle/repeat state.
public class PlayerCosmeticFixTests
{
    [AvaloniaFact]
    public void Last_track_is_restored_paused_on_login()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");

        // DbSeeder gives demo a PlayerSettings row with a LastTrackId.
        Assert.NotNull(h.Player!.CurrentTrack);
        Assert.False(h.Player.IsPlaying);

        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        Assert.True(shell.IsMiniPlayerVisible);
        // The bar must actually show the restored track, not an empty shell —
        // the VM is built after the restore's MediaOpened already fired.
        Assert.False(string.IsNullOrWhiteSpace(shell.MiniPlayer!.TrackTitle));
        Assert.Equal(shell.MiniPlayer.TrackTitle, h.Player.CurrentTrack!.Title);
        Assert.NotNull(shell.MiniPlayer.CurrentAlbum);
        h.RunStep("cosmetic-01-restored-last-track", () => { });
    }

    [Fact]
    public void Track_count_uses_ukrainian_plurals()
    {
        Assert.Equal("1 трек", PlayerViewModel.FormatTrackCount(1));
        Assert.Equal("3 треки", PlayerViewModel.FormatTrackCount(3));
        Assert.Equal("5 треків", PlayerViewModel.FormatTrackCount(5));
        Assert.Equal("11 треків", PlayerViewModel.FormatTrackCount(11));
        Assert.Equal("22 треки", PlayerViewModel.FormatTrackCount(22));
        Assert.Equal("114 треків", PlayerViewModel.FormatTrackCount(114));
    }

    [Fact]
    public void Hour_long_durations_format_as_h_mm_ss()
    {
        Assert.Equal("1:15:30", MiniPlayerViewModel.Format(new TimeSpan(1, 15, 30)));
        Assert.Equal("3:08", MiniPlayerViewModel.Format(new TimeSpan(0, 3, 8)));
        Assert.Equal("0:00", MiniPlayerViewModel.Format(TimeSpan.Zero));
    }

    [AvaloniaFact]
    public void Description_toggle_shows_only_when_clamped_or_expanded()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.Nav!.NavigateTo(NavTarget.Player);
        var album = h.Catalog!.Albums.First(a => !string.IsNullOrWhiteSpace(a.Description));
        (h.Nav.CurrentView as PlayerViewModel)!.OpenAlbumCommand.Execute(album);
        Dispatcher.UIThread.RunJobs();
        var pvm = (PlayerViewModel)h.Nav.CurrentView!;

        pvm.IsDescriptionClamped = false;
        Assert.False(pvm.ShowDescriptionToggle);   // short text → no dead button

        pvm.IsDescriptionClamped = true;
        Assert.True(pvm.ShowDescriptionToggle);
        Assert.Equal("Показати повністю", pvm.DescriptionToggleText);

        pvm.IsDescriptionExpanded = true;
        Assert.True(pvm.ShowDescriptionToggle);    // expanded → always collapsible
        Assert.Equal("Згорнути", pvm.DescriptionToggleText);
    }

    [AvaloniaFact]
    public void Mini_player_mirrors_shuffle_state()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        var mini = shell.MiniPlayer!;

        var had = h.Player!.ShuffleMode;
        try
        {
            h.Player.ShuffleMode = !had;
            Assert.Equal(!had, mini.IsShuffleOn);
        }
        finally
        {
            h.Player.ShuffleMode = had;   // shared DB — restore the seeded state
        }
        Assert.Equal(had, mini.IsShuffleOn);
    }
}
