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

        // The new rows are populated from seeded data. All three price tiers must
        // survive the empty-bucket culling — i.e. the seed populates each of them —
        // and every chip advertises how many albums it leads to.
        Assert.NotEmpty(cvm!.Artists);
        Assert.Equal(3, cvm.PriceRanges.Count);
        Assert.All(cvm.PriceRanges, r => Assert.Contains("альбом", r.Subtitle));
        // Rating chips are culled like price tiers when empty, and carry live
        // counts the same way — at least the 4.0 bucket must survive the seed.
        Assert.NotEmpty(cvm.RatingShortcuts);
        Assert.All(cvm.RatingShortcuts, r => Assert.Contains("альбом", r.Subtitle));
    }

    [AvaloniaFact]
    public void Price_shortcut_opens_search_filtered_by_price()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        // The tier bounds are derived from the catalog at runtime, so read the
        // bound back out of the chip's own query ("ціна:..N") instead of pinning it.
        var budget = cvm.PriceRanges.First();
        var bound = ParseBound(budget.Query);
        cvm.OpenSearchCommand.Execute(budget.Query);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        Assert.Null(svm!.PriceFrom);
        Assert.Equal(bound, svm.PriceTo);
        Assert.Contains("Ціна", svm.HeaderLabel);
        Assert.Contains($"{bound:0}", svm.HeaderLabel);
    }

    [AvaloniaFact]
    public void Premium_price_shortcut_lifts_lower_bound_only()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var premium = cvm.PriceRanges.Last();           // "ціна:N.."
        var bound = ParseBound(premium.Query);
        cvm.OpenSearchCommand.Execute(premium.Query);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        Assert.Equal(bound, svm!.PriceFrom);
        Assert.Null(svm.PriceTo);
    }

    // Extracts the single numeric bound from a one-sided chip query
    // ("ціна:..400" or "ціна:850..").
    private static decimal ParseBound(string query) =>
        decimal.Parse(query.Replace("ціна:", "").Replace("..", ""),
            System.Globalization.CultureInfo.InvariantCulture);

    [AvaloniaFact]
    public void Rating_shortcut_opens_search_filtered_by_min_rating()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        // Read the threshold back out of the chip's own query ("рейтинг:>=N"):
        // empty buckets are culled, so the first chip is not pinned to 4.5.
        var topRated = cvm.RatingShortcuts.First();
        var threshold = double.Parse(topRated.Query.Replace("рейтинг:>=", ""),
            System.Globalization.CultureInfo.InvariantCulture);
        cvm.OpenSearchCommand.Execute(topRated.Query);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        Assert.Equal(threshold, svm!.MinRating);
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
        Assert.Equal(album.PrimaryProduct!.Id, pvm.Product!.Id);
    }

    [AvaloniaFact]
    public void New_arrival_inner_format_button_keeps_its_own_target()
    {
        var h = OpenCatalogForShelf();
        var (card, album) = FirstDualFormatCard(h);

        // The nested "CD" price button must win the click over the card-wide
        // button: it adds the CD edition to the cart (Wave Catalog-UX-Audit
        // made price rows the buy action) instead of opening the album page.
        var cdButton = card.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Classes.Contains("ghost")
                     && b.CommandParameter is Product { Format: ProductFormat.CD });
        ClickAt(h, cdButton, new Point(cdButton.Bounds.Width / 2, cdButton.Bounds.Height / 2));

        var cvm = Assert.IsType<CatalogViewModel>(h.Nav!.CurrentView);
        Assert.Contains(h.Cart!.Items, i => i.ProductId == album.Cd!.Id);
        Assert.Contains("додано в кошик", cvm.ToastMessage);
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
