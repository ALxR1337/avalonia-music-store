using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Price filter end-to-end after the re-pricing redesign: the seeded grid
/// populates every catalog tier (so the premium chip actually returns albums),
/// and search cards read "від N ₴" while several editions qualify but pin to
/// the exact edition price once a format is chosen.
/// </summary>
public class PriceFilterTests
{
    private static CatalogViewModel OpenCatalog(Harness h)
    {
        h.OpenMainWindow();
        h.Nav!.NavigateTo(NavTarget.Catalog);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<CatalogViewModel>(h.Nav!.CurrentView);
    }

    [AvaloniaFact]
    public void Premium_chip_returns_albums_priced_into_its_tier()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var premium = cvm.PriceRanges.Last();           // "ціна:N.."
        var bound = decimal.Parse(
            premium.Query.Replace("ціна:", "").Replace("..", ""), CultureInfo.InvariantCulture);
        cvm.OpenSearchCommand.Execute(premium.Query);
        Dispatcher.UIThread.RunJobs();

        var svm = Assert.IsType<SearchResultsViewModel>(h.Nav!.CurrentView);
        Assert.True(svm.TotalCount > 0,
            $"Premium tier (від {bound:0} ₴) must not be an empty bucket.");
        Assert.All(svm.Albums, a => Assert.True(a.Price >= bound,
            $"«{a.Album.Title}» card shows {a.Price:0} ₴, below the {bound:0} ₴ tier floor."));
    }

    [AvaloniaFact]
    public void Card_shows_from_price_until_a_format_pins_the_edition()
    {
        var h = new Harness();
        h.OpenMainWindow();
        // Browse mode (year range lifts into filters, no text query) → every album,
        // with both its editions passing the filters.
        h.Nav!.NavigateTo(NavTarget.SearchResults, "рік:1900..2100");
        Dispatcher.UIThread.RunJobs();

        var svm = Assert.IsType<SearchResultsViewModel>(h.Nav!.CurrentView);
        Assert.True(svm.TotalCount > 0);
        // Vinyl and CD never share a price in the seed, so every card is a "від".
        Assert.All(svm.Albums, a => Assert.StartsWith("від ", a.PriceLabel));

        svm.SelectedFormatLabel = "CD";
        Dispatcher.UIThread.RunJobs();

        Assert.True(svm.TotalCount > 0);
        Assert.All(svm.Albums, a =>
        {
            Assert.False(a.PriceIsFrom, $"«{a.Album.Title}» still reads 'від' with a format pinned.");
            Assert.Equal(ProductFormat.CD, a.PrimaryProduct!.Format);
        });
    }
}
