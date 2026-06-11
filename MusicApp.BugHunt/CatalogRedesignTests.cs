using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Covers the catalog home redesign: the "Популярні цього місяця" grid is gone,
/// replaced by an artists carousel plus price/rating shortcut tiles that open the
/// search page pre-filtered via the structured-query DSL (ціна:.., рейтинг:>=, виконавець:).
/// </summary>
public class CatalogRedesignTests
{
    private static CatalogViewModel OpenCatalog(Harness h)
    {
        h.OpenMainWindow();
        h.Nav!.NavigateTo(NavTarget.Catalog);
        Dispatcher.UIThread.RunJobs();
        var cvm = h.Nav!.CurrentView as CatalogViewModel;
        Assert.NotNull(cvm);
        return cvm!;
    }

    [AvaloniaFact]
    public void Catalog_renders_artists_and_shortcut_rows()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1400, 1100);
        h.RunStep("00-catalog-redesign", () =>
        {
            h.Nav!.NavigateTo(NavTarget.Catalog);
        });

        var cvm = h.Nav!.CurrentView as CatalogViewModel;
        Assert.NotNull(cvm);

        // The new rows are populated from seeded data / static shortcuts.
        Assert.NotEmpty(cvm!.Artists);
        Assert.Equal(3, cvm.PriceRanges.Count);
        Assert.Equal(2, cvm.RatingShortcuts.Count);
    }

    [AvaloniaFact]
    public void Price_shortcut_opens_search_filtered_by_price()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var upTo500 = cvm.PriceRanges.First();          // "ціна:..500"
        cvm.OpenSearchCommand.Execute(upTo500.Query);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        Assert.Null(svm!.PriceFrom);
        Assert.Equal(500m, svm.PriceTo);
        Assert.Contains("Ціна", svm.HeaderLabel);
        Assert.Contains("500", svm.HeaderLabel);
    }

    [AvaloniaFact]
    public void Premium_price_shortcut_lifts_lower_bound_only()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var premium = cvm.PriceRanges.Last();           // "ціна:1000.."
        cvm.OpenSearchCommand.Execute(premium.Query);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        Assert.Equal(1000m, svm!.PriceFrom);
        Assert.Null(svm.PriceTo);
    }

    [AvaloniaFact]
    public void Rating_shortcut_opens_search_filtered_by_min_rating()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var topRated = cvm.RatingShortcuts.First();      // "рейтинг:>=4.5"
        cvm.OpenSearchCommand.Execute(topRated.Query);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        Assert.Equal(4.5, svm!.MinRating);
        Assert.Contains("Рейтинг", svm.HeaderLabel);
    }

    [AvaloniaFact]
    public void Artist_tile_opens_search_scoped_to_that_artist()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var artist = cvm.Artists.First();
        cvm.OpenArtistCommand.Execute(artist);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        // The artist restriction is lifted into the SelectedArtists facet (so it
        // surfaces that artist's albums), and the header reads it back as a
        // friendly label rather than raw artist:"…".
        Assert.Contains(artist.Name, svm!.SelectedArtists);
        Assert.Equal($"Виконавець: {artist.Name}", svm.HeaderLabel);
    }

    [AvaloniaFact]
    public void New_arrivals_is_a_horizontal_shelf()
    {
        var h = OpenCatalogForShelf();
        var scroller = h.Find<ScrollViewer>("NewArrivalsScroller");
        // Horizontal carousel: the strip of cards is wider than the viewport, so it
        // actually scrolls sideways (matching the genres/artists rows above).
        Assert.True(
            scroller.Extent.Width > scroller.Viewport.Width,
            $"Expected the shelf to overflow horizontally (extent {scroller.Extent.Width} > viewport {scroller.Viewport.Width}).");
    }

    [AvaloniaFact]
    public void New_arrival_card_opens_album_when_clicking_empty_padding()
    {
        var h = OpenCatalogForShelf();
        var (card, album) = FirstDualFormatCard(h);

        // Click the bare top-right padding of the card — no cover, no play button,
        // no price row there. The whole tile is the hit-target, so this opens the album.
        ClickAt(h, card, new Point(193, 60));

        var pvm = Assert.IsType<ProductViewModel>(h.Nav!.CurrentView);
        Assert.Equal(album.PrimaryProduct.Id, pvm.Product!.Id);
    }

    [AvaloniaFact]
    public void New_arrival_inner_format_button_keeps_its_own_target()
    {
        var h = OpenCatalogForShelf();
        var (card, album) = FirstDualFormatCard(h);

        // The nested "CD" price button must win the click over the card-wide button:
        // clicking it navigates to the CD edition, not the primary (vinyl) one.
        var cdButton = card.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Classes.Contains("ghost")
                     && b.CommandParameter is Product { Format: ProductFormat.CD });
        ClickAt(h, cdButton, new Point(cdButton.Bounds.Width / 2, cdButton.Bounds.Height / 2));

        var pvm = Assert.IsType<ProductViewModel>(h.Nav!.CurrentView);
        Assert.Equal(album.Cd!.Id, pvm.Product!.Id);
        Assert.NotEqual(album.PrimaryProduct.Id, pvm.Product!.Id);
    }

    // Opens the catalog in a window tall enough that the whole page (incl. the bottom
    // "Нові надходження" shelf) gets measured/arranged, so hit-testing & Extent work.
    private static Harness OpenCatalogForShelf()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1400, 2000);
        h.RunStep("06-new-arrivals-shelf", () => h.Nav!.NavigateTo(NavTarget.Catalog));
        return h;
    }

    private static (Button card, NewArrivalAlbum album) FirstDualFormatCard(Harness h)
    {
        var scroller = h.Find<ScrollViewer>("NewArrivalsScroller");
        var card = scroller.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Classes.Contains("album-card")
                     && b.DataContext is NewArrivalAlbum { HasVinyl: true, HasCd: true });
        return (card, (NewArrivalAlbum)card.DataContext!);
    }

    // Simulate a real left-click (press + release) at a control-local point, routed
    // through the headless input pipeline so hit-testing decides which control wins.
    private static void ClickAt(Harness h, Visual target, Point local)
    {
        var window = h.Window!;
        var p = target.TranslatePoint(local, window)
            ?? throw new InvalidOperationException("Target is not in the window's visual tree.");
        window.MouseMove(p);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
