using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.Services.Search;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

/// <summary>
/// Regression tests for Wave Search-UX-Audit: the critical and important
/// fixes around the search pipeline, facets, suggestions and saved queries.
/// </summary>
public class SearchCriticalFixTests
{
    private static SearchResultsViewModel Srvm(Harness h) =>
        (SearchResultsViewModel)h.Nav!.CurrentView!;

    [AvaloniaFact]
    public void Search_service_fixes_hold()
    {
        var h = new Harness();
        h.OpenMainWindow();
        var search = new SearchService(new MusicStoreDbContextFactory());

        // -- 1. Hostile input must not throw (used to crash the app with an
        //       unhandled SqliteException straight from the search box).
        foreach (var q in new[] { "++", "(((", "\"", ":", "-", "AC/DC", "*", "()" })
        {
            var ex = Record.Exception(() => search.Search(q));
            Assert.Null(ex);
        }

        using var db = new MusicStoreDbContextFactory().CreateDbContext();

        // -- 2. Autocomplete survives a second word.
        var spacedTrack = db.Tracks.AsEnumerable()
            .FirstOrDefault(t => t.Title.Contains(' ') && t.Title.Length >= 5);
        Assert.NotNull(spacedTrack);
        var hits = search.Autocomplete(spacedTrack!.Title);
        Assert.NotEmpty(hits);
        // Track rows must say they are tracks and name the album they open.
        var trackHit = hits.FirstOrDefault(x => x.Kind == "track");
        if (trackHit is not null)
        {
            Assert.NotNull(trackHit.Subtitle);
            Assert.Contains("трек", trackHit.Subtitle!);
        }
        // Every suggestion carries a localized subtitle, never a raw kind.
        Assert.All(hits, x => Assert.False(string.IsNullOrEmpty(x.Subtitle)));

        // -- 3. Did-you-mean: no echo for a correct (case-different) name…
        var artists = db.Artists.Select(a => a.Name).ToList();
        var single = artists.First(a => !a.Contains(' ') && a.Length >= 5);
        Assert.Null(search.Search(single.ToLowerInvariant()).DidYouMean);

        // …a one-char typo in a single word still corrects…
        var typo1 = single.Remove(2, 1);
        Assert.Equal(single, search.Search(typo1).DidYouMean);

        // …and a multi-word typo finally corrects too (first-token-only bug).
        var multi = artists.First(a => a.Contains(' ') && a.Split(' ')[0].Length > 3);
        var words = multi.Split(' ');
        words[0] = words[0].Remove(1, 1);
        Assert.Equal(multi, search.Search(string.Join(" ", words)).DidYouMean);

        // -- 4. Top result: absent in browse mode and for single-result pages,
        //       present when a text query has an actual ranking to crown.
        Assert.Null(search.Search("").TopResult);
        var the = search.Search("the");
        if (the.TotalCount >= 2) Assert.NotNull(the.TopResult);
        var oneHit = search.Search(single.ToLowerInvariant());
        if (oneHit.TotalCount == 1) Assert.Null(oneHit.TopResult);

        // -- 5. Facet counts honour the price filter instead of promising
        //       albums the results below refuse to show; buckets stay visible.
        var dead = search.Search("", new SearchFilters(PriceTo: 1m));
        Assert.Equal(0, dead.TotalCount);
        var genreGroup = dead.Facets.FirstOrDefault(f => f.Field == "genre");
        Assert.NotNull(genreGroup);
        Assert.All(genreGroup!.Buckets, b => Assert.Equal(0, b.Count));

        // -- 6. Artist facet is no longer capped at 12: «Показати всі (N)»
        //       must really mean all artists with purchasable albums.
        var browse = search.Search("");
        var artistGroup = browse.Facets.FirstOrDefault(f => f.Field == "artist");
        Assert.NotNull(artistGroup);
        var expectedArtists = db.Products
            .Where(p => p.IsActive)
            .Select(p => p.Album!.Artist!.Name)
            .Distinct()
            .Count();
        Assert.Equal(expectedArtists, artistGroup!.Buckets.Count);
    }

    [AvaloniaFact]
    public void Search_page_state_fixes_hold()
    {
        var h = new Harness();
        h.OpenMainWindow();
        var vm = (MainWindowViewModel)h.Window!.DataContext!;

        // -- 7. Sidebar «Пошук» re-click keeps the current results page alive.
        h.Nav!.NavigateTo(NavTarget.SearchResults, "death");
        var page = h.Nav.CurrentView;
        vm.NavigateCommand.Execute("SearchResults");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(page, h.Nav.CurrentView);

        // -- 8. Title-bar box mirrors the page: free text shows up, a pure-DSL
        //       navigation (genre tile) clears the stale text.
        Assert.Equal("death", vm.SearchQuery);
        h.Nav.NavigateTo(NavTarget.SearchResults, "жанр:Rock");
        Assert.Equal(string.Empty, vm.SearchQuery);

        // -- 9. «Чи мали ви на увазі» also updates the box.
        using (var db = new MusicStoreDbContextFactory().CreateDbContext())
        {
            var single = db.Artists.Select(a => a.Name).AsEnumerable()
                .First(a => !a.Contains(' ') && a.Length >= 5);
            h.Nav.NavigateTo(NavTarget.SearchResults, single.Remove(2, 1));
            var r = Srvm(h);
            Assert.True(r.HasDidYouMean);
            r.ApplyDidYouMeanCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(single, vm.SearchQuery);
        }

        // -- 10. Format facet from a DSL query clears on the FIRST click.
        h.Nav.NavigateTo(NavTarget.SearchResults, "формат:lp");
        var fmt = Srvm(h);
        var bucket = fmt.Facets.First(f => f.Field == "format")
            .VisibleBuckets.First(b => b.IsActive);
        fmt.ToggleFacetCommand.Execute(bucket);
        Assert.Null(fmt.SelectedFormatLabel);

        // -- 11. Guest save: the button is disabled instead of a silent no-op.
        Assert.False(fmt.SaveQueryCommand.CanExecute(null));

        // -- 12. No-results page offers a way out that works.
        h.Nav.NavigateTo(NavTarget.SearchResults, "qqqqqqq");
        var none = Srvm(h);
        Assert.Equal(0, none.TotalCount);
        none.ShowAllCommand.Execute(null);
        Assert.True(none.TotalCount > 0);
        Assert.Equal(string.Empty, none.Query);

        // -- 13. Picking a suggestion must not re-open the popup later: the
        //        debounce timer armed by typing is cancelled by the pick.
        vm.SearchQuery = "de";
        System.Threading.Thread.Sleep(350);
        Dispatcher.UIThread.RunJobs();
        if (vm.Suggestions.Count > 0)
        {
            vm.PickSuggestionCommand.Execute(vm.Suggestions[0]);
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(350);
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsAutocompleteOpen);
        }
    }

    [AvaloniaFact]
    public void Search_cosmetic_fixes_hold()
    {
        var h = new Harness();
        h.OpenMainWindow();
        var vm = (MainWindowViewModel)h.Window!.DataContext!;
        var search = new SearchService(new MusicStoreDbContextFactory());

        // -- 17/19. Browse mode is honest about itself, rating 0 = no filter.
        h.Nav!.NavigateTo(NavTarget.SearchResults);
        var page = Srvm(h);
        Assert.Equal("Усі альбоми", page.HeaderLabel);
        Assert.StartsWith("Альбомів у каталозі", page.CountLabel);
        var total = page.TotalCount;
        page.MinRating = 0.0;
        Assert.Empty(page.ActiveFilterChips);
        Assert.Equal("Усі альбоми", page.HeaderLabel);
        Assert.Equal(total, page.TotalCount);

        // -- 18. Open-ended range chips say «від»/«до» instead of «…».
        page.MinRating = null;
        page.YearFrom = 1990;
        Assert.Contains(page.ActiveFilterChips, c => c.Label == "Рік: від 1990");
        page.YearFrom = null;
        page.PriceTo = 600m;
        Assert.Contains(page.ActiveFilterChips, c => c.Label == "Ціна: до 600 ₴");
        page.PriceTo = null;

        // -- 22. The single-checkbox «Наявність» group hides its header.
        Assert.False(page.Facets.First(f => f.Field == "stock").ShowTitle);
        Assert.True(page.Facets.First(f => f.Field == "genre").ShowTitle);

        // -- 24. Punctuation separates words: a parenthesised track title finds
        //        its album instead of gluing into a token FTS never indexed.
        using (var db = new MusicStoreDbContextFactory().CreateDbContext())
        {
            var spiky = db.Tracks.AsEnumerable().First(t => t.Title.Contains('('));
            Assert.True(search.Search(spiky.Title).TotalCount >= 1);
        }

        // -- 19b. Enter in the empty box behaves like the sidebar tab…
        h.Nav.NavigateTo(NavTarget.Catalog);
        vm.SearchQuery = string.Empty;
        vm.SubmitSearchCommand.Execute(null);
        Assert.IsType<SearchResultsViewModel>(h.Nav.CurrentView);
        // …but never wipes an already-open results page.
        var current = h.Nav.CurrentView;
        vm.SubmitSearchCommand.Execute(null);
        Assert.Same(current, h.Nav.CurrentView);
    }

    [AvaloniaFact]
    public void History_and_saved_queries_fixes_hold()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        var uid = h.Auth!.CurrentUser!.Id;
        var factory = new MusicStoreDbContextFactory();

        int HistoryRows()
        {
            using var db = factory.CreateDbContext();
            return db.SearchHistory.Count(r => r.UserId == uid && r.Query == "death");
        }

        var historyBefore = HistoryRows();
        int savedBefore;
        using (var db = factory.CreateDbContext())
            savedBefore = db.SavedSearches.Count(s => s.UserId == uid);

        try
        {
            // -- 14. One search + three facet toggles = ONE history row.
            h.Nav!.NavigateTo(NavTarget.SearchResults, "death");
            var page = Srvm(h);
            for (var i = 0; i < 3; i++)
            {
                var bucket = page.Facets.FirstOrDefault(f => f.Field == "genre")?
                    .VisibleBuckets.FirstOrDefault();
                if (bucket is null) break;
                page.ToggleFacetCommand.Execute(bucket);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(historyBefore + 1, HistoryRows());

            // -- 15. History actually surfaces somewhere: recent queries feed
            //        the empty-box suggestions.
            var search = new SearchService(factory);
            Assert.Contains("death", search.RecentQueries(uid));

            // -- 16. Saving twice keeps one row, tells the user, and names it
            //        after the readable header instead of the raw DSL.
            page.SaveQueryCommand.Execute(null);
            Assert.Equal("Збережено у профілі ✓", page.SaveQueryFeedback);
            page.SaveQueryCommand.Execute(null);
            Assert.Equal("Цей запит уже збережено", page.SaveQueryFeedback);

            using var check = factory.CreateDbContext();
            var rows = check.SavedSearches.Where(s => s.UserId == uid).ToList();
            Assert.Equal(savedBefore + 1, rows.Count);
            var added = rows.OrderByDescending(s => s.Id).First();
            Assert.Equal(page.HeaderLabel, added.Name);
            Assert.NotEqual(added.Name, added.QueryJson);
        }
        finally
        {
            // The BugHunt DB is shared by every test in this process — drop the
            // rows this test created so later tests see the seeded baseline.
            using var db = factory.CreateDbContext();
            db.SearchHistory.RemoveRange(
                db.SearchHistory.Where(r => r.UserId == uid && r.Query == "death")
                    .OrderByDescending(r => r.Id).Take(Math.Max(0, HistoryRows() - historyBefore)));
            db.SavedSearches.RemoveRange(
                db.SavedSearches.Where(s => s.UserId == uid)
                    .OrderByDescending(s => s.Id).Take(1));
            db.SaveChanges();
        }
    }
}
