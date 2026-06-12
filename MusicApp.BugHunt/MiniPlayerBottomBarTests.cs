using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.ViewModels;
using MusicApp.Views;
using Xunit;

namespace MusicApp.BugHunt;

// Regression coverage for the three bottom-bar (mini-player) bugs reported on
// the catalog "Слухати зараз" 30-second preview:
//   1. the preview now reports sample-relative position (0:00 → 0:30) and the
//      album cover shows instead of an empty tile;
//   2. the cover art is resolved for samples;
//   3. cover, transport buttons and the volume cluster sit on one vertical
//      centre line, with the seek strip pinned slim along the bottom edge.
public class MiniPlayerBottomBarTests
{
    private static (Harness h, MainWindowViewModel shell) PlayFirstSample()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        var album = h.Catalog!.Albums.First(a => a.Tracks.Count > 0);
        h.Player!.PlaySample(album.Tracks[0]);

        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        shell.IsMiniPlayerVisible = true;
        Dispatcher.UIThread.RunJobs();
        return (h, shell);
    }

    // Bug 2: a 30-second preview used to drop CurrentAlbum, leaving the bottom-bar
    // cover an empty gradient tile. PlaySample now resolves the owning album so
    // the art renders, while the "семпл 30 с" subtitle is preserved.
    [AvaloniaFact]
    public void Sample_preview_shows_cover_art_and_keeps_preview_subtitle()
    {
        var (h, shell) = PlayFirstSample();

        Assert.True(h.Player!.IsSampleMode);
        var mini = shell.MiniPlayer!;
        Assert.NotNull(mini.CurrentAlbum);
        Assert.False(string.IsNullOrWhiteSpace(mini.CurrentAlbum!.CoverPath));
        Assert.Equal("семпл 30 с", mini.ArtistName);
        Assert.Equal("0:30", mini.DurationText);
    }

    // Bug 1 (safety half): skipping during a preview must never escalate into a
    // full, unpurchased track. Next/Previous now step through the album's other
    // 30-second previews — staying in sample mode keeps the purchase gate intact.
    [AvaloniaFact]
    public void Skip_steps_through_previews_without_leaving_sample_mode()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        var album = h.Catalog!.Albums.First(a => a.Tracks.Count >= 2);
        h.Player!.PlaySample(album.Tracks[0]);
        var first = h.Player.CurrentTrack;

        // Forward: moves to the next track's preview, still a 30s sample.
        h.Player.Next();
        Assert.NotSame(first, h.Player.CurrentTrack);
        Assert.Equal(album.Tracks[1].Id, h.Player.CurrentTrack!.Id);
        Assert.True(h.Player.IsSampleMode);

        // Back: returns to the first track's preview, still in sample mode.
        h.Player.Previous();
        Assert.Equal(album.Tracks[0].Id, h.Player.CurrentTrack!.Id);
        Assert.True(h.Player.IsSampleMode);
    }

    private static (Harness h, MiniPlayerView bar) SettledBar()
    {
        var (h, _) = PlayFirstSample();
        var bar = h.Find<Slider>("SeekSlider").GetVisualAncestors().OfType<MiniPlayerView>().First();
        var host = bar.GetVisualAncestors().OfType<Border>().First();
        host.Transitions = null; host.RenderTransform = null; host.Opacity = 1;
        Dispatcher.UIThread.RunJobs();
        return (h, bar);
    }

    // Rendered Y centre of a visual in bar space (TranslatePoint folds in any
    // ancestor render transforms, unlike summing Bounds.Y).
    private static double CenterY(Visual v, MiniPlayerView bar)
    {
        var c = (Control)v;
        return v.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), bar)!.Value.Y;
    }

    // The volume glyph lives inside the mute toggle button of the right
    // cluster (the StackPanel that also hosts the volume slider) — grab the
    // first visible Path there.
    private static Path FindVolumeIcon(MiniPlayerView bar)
    {
        var volumeSlider = bar.GetVisualDescendants().OfType<Slider>().First(s => s.Name != "SeekSlider");
        var cluster = volumeSlider.GetVisualAncestors().OfType<StackPanel>().First();
        return cluster.GetVisualDescendants().OfType<Path>()
            .First(p => p.IsVisible && p.Bounds.Width > 0);
    }

    // Spotify layout: the cover (left) and the volume cluster (right) are each
    // centred on the bar's midline — they are NOT pulled up to the transport row.
    [AvaloniaFact]
    public void Cover_and_volume_cluster_are_centred_on_the_bar()
    {
        var (h, bar) = SettledBar();
        var mid = bar.Bounds.Height / 2;

        var cover = bar.GetVisualDescendants().OfType<Border>().First(b => b.Width == 48 && b.Height == 48);
        var volumeIcon = FindVolumeIcon(bar);

        Assert.True(System.Math.Abs(CenterY(cover, bar) - mid) <= 4,
            $"Cover centre {CenterY(cover, bar):0.#} is not on the bar midline {mid:0.#}.");
        Assert.True(System.Math.Abs(CenterY(volumeIcon, bar) - mid) <= 4,
            $"Volume icon centre {CenterY(volumeIcon, bar):0.#} is not on the bar midline {mid:0.#}.");
    }

    // The centre column is a [transport ; seek] block centred as a group: the
    // transport sits above the midline, the seek below, and their midpoint lands
    // on the bar centre. The seek must also stay slim and inside the strip.
    [AvaloniaFact]
    public void Transport_and_seek_form_a_centred_block()
    {
        var (h, bar) = SettledBar();
        var mid = bar.Bounds.Height / 2;

        var seek = h.Find<Slider>("SeekSlider");
        var play = bar.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("play-circle"));
        var playC = CenterY(play, bar);
        var seekC = CenterY(seek, bar);

        Assert.True(playC < mid, $"Transport ({playC:0.#}) should sit above the bar midline ({mid:0.#}).");
        Assert.True(seekC > mid, $"Seek ({seekC:0.#}) should sit below the bar midline ({mid:0.#}).");

        // The block is centred when the gap above the transport row matches the
        // gap below the seek row (averaging the two row centres is skewed because
        // the rows differ in height).
        var gapTop = playC - play.Bounds.Height / 2;
        var gapBottom = bar.Bounds.Height - (seekC + seek.Bounds.Height / 2);
        Assert.True(System.Math.Abs(gapTop - gapBottom) <= 3,
            $"The transport+seek block is not centred: gapTop={gapTop:0.#}, gapBottom={gapBottom:0.#}.");
        Assert.True(seek.Bounds.Height <= 28,
            $"Seek slider is {seek.Bounds.Height:0}px tall — expected a slim (<=28px) bar.");
        Assert.True(seekC + seek.Bounds.Height / 2 <= bar.Bounds.Height + 0.5,
            "Seek strip spills past the player strip.");
    }

    // The volume slider's visible track lines up with its own icon and the window
    // buttons in the right cluster (the Fluent track-offset fix). Render-aware via
    // the thumb's projected centre.
    [AvaloniaFact]
    public void Volume_track_aligns_with_its_icon_and_buttons()
    {
        var (h, bar) = SettledBar();

        var volumeIcon = FindVolumeIcon(bar);
        var volumeSlider = bar.GetVisualDescendants().OfType<Slider>().First(s => s.Name != "SeekSlider");
        var thumb = volumeSlider.GetVisualDescendants().OfType<Thumb>().First();
        // A right-cluster window button (Expand/Close), used as the row baseline.
        var winButton = bar.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("icon") && b.GetVisualAncestors().OfType<StackPanel>()
                .Any(sp => sp.GetVisualChildren().OfType<Slider>().Any(s => s.Name != "SeekSlider")));

        var iconC = CenterY(volumeIcon, bar);
        var thumbC = CenterY(thumb, bar);
        var btnC = CenterY(winButton, bar);

        Assert.True(System.Math.Abs(iconC - thumbC) <= 3,
            $"Volume icon ({iconC:0.#}) and track ({thumbC:0.#}) are not aligned.");
        Assert.True(System.Math.Abs(btnC - thumbC) <= 3,
            $"Window buttons ({btnC:0.#}) and volume track ({thumbC:0.#}) are not aligned.");
    }

    // The transport button must reflect playback state: a pause glyph while
    // playing, a play glyph while paused. Previously it was a static play icon.
    [AvaloniaFact]
    public void Play_button_swaps_between_play_and_pause_glyphs()
    {
        // No real playback here: live LibVLC posts its Playing event onto the
        // dispatcher at an arbitrary moment and races the manual IsPlaying
        // writes below. The glyph swap is a pure binding, so just show the bar.
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        var shell = (MainWindowViewModel)h.Window!.DataContext!;
        shell.IsMiniPlayerVisible = true;
        Dispatcher.UIThread.RunJobs();

        var bar = h.Find<Slider>("SeekSlider").GetVisualAncestors().OfType<MiniPlayerView>().First();
        var mini = shell.MiniPlayer!;

        var playBtn = bar.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("play-circle"));
        var glyphs = playBtn.GetVisualDescendants().OfType<Path>().ToList();
        Assert.Equal(2, glyphs.Count); // play + pause, one shown at a time

        mini.IsPlaying = false;
        Dispatcher.UIThread.RunJobs();
        var shownWhenPaused = glyphs.Where(p => p.IsVisible).ToList();

        mini.IsPlaying = true;
        Dispatcher.UIThread.RunJobs();
        var shownWhenPlaying = glyphs.Where(p => p.IsVisible).ToList();

        Assert.Single(shownWhenPaused);
        Assert.Single(shownWhenPlaying);
        Assert.NotSame(shownWhenPaused[0], shownWhenPlaying[0]);
    }
}
