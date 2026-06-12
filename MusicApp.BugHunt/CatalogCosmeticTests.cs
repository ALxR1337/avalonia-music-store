using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.Services;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Cosmetic-tier catalog audit fixes: wheel scroll-chaining through the
/// horizontal shelves, album counts on genre tiles, and tile-aligned paging.
/// </summary>
public class CatalogCosmeticTests
{
    [AvaloniaFact]
    public void Wheel_over_a_shelf_scrolls_the_page_vertically()
    {
        var h = new Harness();
        var window = h.OpenMainWindow();
        h.SetWindowSize(1280, 800);
        h.Nav!.NavigateTo(NavTarget.Catalog);
        Dispatcher.UIThread.RunJobs();

        var page = h.Find<ScrollViewer>("ContentScroll");
        Assert.Equal(0, page.Offset.Y);

        // Wheel down with the pointer over the genres shelf: the inner
        // horizontal ScrollViewer cannot consume a vertical wheel, so the
        // event must chain to the page scroller instead of dying inside.
        var shelf = h.Find<ScrollViewer>("GenreScroller");
        var hit = shelf.TranslatePoint(new Point(shelf.Bounds.Width / 2, shelf.Bounds.Height / 2), window);
        Assert.NotNull(hit);
        window.MouseWheel(hit!.Value, new Vector(0, -1), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(page.Offset.Y > 0,
            $"Expected the page to scroll down past the shelf (Offset.Y={page.Offset.Y}).");
    }

    [AvaloniaFact]
    public void Genre_tiles_show_album_counts()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.Nav!.NavigateTo(NavTarget.Catalog);
        Dispatcher.UIThread.RunJobs();

        var shelf = h.Find<ScrollViewer>("GenreScroller");
        var countLabels = shelf.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Text is { } s && s.Contains("альбом"))
            .ToList();
        Assert.NotEmpty(countLabels);
    }

    [AvaloniaFact]
    public void Genre_tiles_use_distinct_covers()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.Nav!.NavigateTo(NavTarget.Catalog);
        Dispatcher.UIThread.RunJobs();

        var cvm = (MusicApp.ViewModels.CatalogViewModel)h.Nav!.CurrentView!;
        // Crossover albums must not give two genres the same tile art:
        // every genre with a cover picks one no other tile uses.
        var covers = cvm.Genres
            .Where(g => g.HasCover)
            .Select(g => g.CoverPath!)
            .ToList();
        Assert.NotEmpty(covers);
        Assert.Equal(covers.Count, covers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // The chevron animation runs on a DispatcherTimer that never ticks under
    // Avalonia.Headless, so the paging *math* is pinned directly instead:
    // a page is always a whole number of tiles, never a fraction that would
    // leave a card cut at the row's edge.
    [Theory]
    [InlineData(976, 168, 840)]  // artists at 1280-wide window → 5 whole tiles
    [InlineData(976, 214, 856)]  // genres/arrivals at the same width → 4 tiles
    [InlineData(100, 214, 214)]  // viewport narrower than a tile → still 1 tile
    public void Chevron_page_step_is_a_whole_number_of_tiles(
        double viewport, double stride, double expected)
    {
        var step = MusicApp.Views.CatalogView.WholeTilePageStep(viewport, stride);
        Assert.Equal(expected, step);
        Assert.Equal(0, step % stride);
    }
}
