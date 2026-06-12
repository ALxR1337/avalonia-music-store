using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.Models;
using MusicApp.Services;
using MusicApp.Services.Search;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Covers the "important"-tier catalog UX-audit fixes: quoted genre queries,
/// hero CTA naming its album, out-of-stock indication on shelf cards, and
/// scroll chevrons dropping out of the tab order.
/// </summary>
public class CatalogImportantFixTests
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
    public void Genre_tile_opens_search_with_quoted_genre_filter()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var genre = h.Catalog!.Genres.First();
        cvm.OpenGenreCommand.Execute(genre);
        Dispatcher.UIThread.RunJobs();

        var svm = h.Nav!.CurrentView as SearchResultsViewModel;
        Assert.NotNull(svm);
        Assert.Contains(genre.Name, svm!.SelectedGenres);
    }

    [Fact]
    public void Quoted_genre_keeps_multiword_name_as_one_field_value()
    {
        // The tile bakes жанр:"…" — for an admin-created multi-word genre the
        // whole name must stay one field value, with no tail leaking into
        // free text (unquoted жанр:New Age would split into filter + word).
        var q = SearchQueryParser.Parse("жанр:\"New Age\"");
        var term = Assert.Single(q.Terms);
        var phrase = Assert.IsType<FieldPhraseTerm>(term);
        Assert.Equal("genre", phrase.Field);
        Assert.Equal("New Age", phrase.Phrase);
    }

    [AvaloniaFact]
    public void Hero_cta_names_the_album_it_will_play()
    {
        var h = new Harness();
        var cvm = OpenCatalog(h);

        var featured = cvm.NewArrivals.First();
        Assert.StartsWith("Слухати:", cvm.FeaturedTitle);
        Assert.Contains(featured.Album.Title, cvm.FeaturedTitle);
        Assert.True(cvm.QuickPreviewCommand.CanExecute(cvm.FeaturedProduct));
    }

    [AvaloniaFact]
    public void Out_of_stock_edition_row_is_disabled_and_says_so()
    {
        var h = new Harness();
        h.OpenMainWindow();

        // Drain one edition's stock in the in-memory cache before the page is
        // built, then open the catalog tall enough to realize the shelf.
        var victim = h.Catalog!.GetNewArrivalAlbums().First(a => a.HasVinyl).Vinyl!;
        victim.Stock = 0;

        h.SetWindowSize(1400, 2000);
        h.RunStep("fix-out-of-stock-row", () => h.Nav!.NavigateTo(NavTarget.Catalog));

        var shelf = h.Find<ScrollViewer>("NewArrivalsScroller");
        var row = shelf.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Classes.Contains("ghost") && ReferenceEquals(b.CommandParameter, victim));
        Assert.False(row.IsEnabled);
        Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "немає" && t.IsVisible);

        // In-stock siblings advertise the buy action instead.
        var inStock = shelf.GetVisualDescendants()
            .OfType<Button>()
            .First(b => b.Classes.Contains("ghost")
                     && b.CommandParameter is Product { Stock: > 0 });
        Assert.True(inStock.IsEnabled);
    }

    [AvaloniaFact]
    public void Scroll_chevrons_are_not_keyboard_tab_stops()
    {
        var h = new Harness();
        OpenCatalog(h);

        foreach (var name in new[]
                 {
                     "GenreScrollLeft", "GenreScrollRight",
                     "ArtistScrollLeft", "ArtistScrollRight",
                     "NewArrivalsScrollLeft", "NewArrivalsScrollRight",
                 })
        {
            var chevron = h.Find<Button>(name);
            Assert.False(chevron.Focusable, $"{name} must not be focusable");
        }
    }
}
