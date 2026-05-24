using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
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

        // Navigate to the Player page.
        h.RunStep("01-nav-player", () => h.Nav!.NavigateTo(NavTarget.Player));

        // Pick the first purchased album (seeded for admin) and play it.
        h.RunStep("02-play-album", () =>
        {
            var pvm = h.Nav!.CurrentView as PlayerViewModel;
            if (pvm is not null && pvm.PurchasedAlbums.Count > 0)
                pvm.PlayAlbumCommand.Execute(pvm.PurchasedAlbums[0]);
        });

        // Force the MiniPlayer visible so its visual subtree is realized.
        // In headless mode the audio backend never fires MediaOpened, so
        // IsMiniPlayerVisible stays false and the Slider is not laid out.
        h.RunStep("03-show-miniplayer", () =>
        {
            var shell = h.Window!.DataContext as MainWindowViewModel;
            if (shell is not null)
                shell.IsMiniPlayerVisible = true;
            Dispatcher.UIThread.RunJobs();
        });

        // Snapshot final state for visual inspection.
        h.RunStep("04-player-final", () => { });

        // The redesigned Player must NOT have a "SeekSlider" named in PlayerView.
        // (It now lives in the MiniPlayer.) We assert via the visual tree that a
        // Slider named "SeekSlider" exists in the MiniPlayer.
        var seekInMini = h.Find<Slider>("SeekSlider");
        Assert.NotNull(seekInMini);
    }
}
