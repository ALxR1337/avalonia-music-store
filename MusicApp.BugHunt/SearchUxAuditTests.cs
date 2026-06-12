using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.ViewModels;

namespace MusicApp.BugHunt;

/// <summary>
/// Screenshot + behaviour sweep for the Search UX audit. Assertion-free on
/// purpose — artifacts land in bug-hunt/artifacts/, VM-level observations are
/// appended to search-ux-audit.log in the same directory.
/// </summary>
public class SearchUxAuditTests
{
    private static readonly string LogPath =
        Path.Combine(Harness.ArtifactsDir, "search-ux-audit.log");

    private static void Log(string line)
    {
        Directory.CreateDirectory(Harness.ArtifactsDir);
        File.AppendAllText(LogPath, line + "\n");
    }

    private static SearchResultsViewModel Srvm(Harness h) =>
        (SearchResultsViewModel)h.Nav!.CurrentView!;

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    // Let the 200ms autocomplete DispatcherTimer fire, then drain the queue.
    private static void WaitDebounce()
    {
        Thread.Sleep(350);
        Pump();
    }

    [AvaloniaFact]
    public void Search_ux_audit_guest()
    {
        Log($"==== guest sweep {DateTime.Now:O} ====");
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);
        var vm = (MainWindowViewModel)h.Window!.DataContext!;

        // 01 — sidebar «Пошук» with no query: browse mode.
        h.RunStep("ux-srch-01-browse-empty", () => h.Nav!.NavigateTo(NavTarget.SearchResults));
        var browse = Srvm(h);
        Log($"01 browse: Header='{browse.HeaderLabel}' TotalCount={browse.TotalCount} " +
            $"HasTopResult={browse.HasTopResult} TopResultLabel='{browse.TopResultLabel}'");

        // 02 — autocomplete: does the popup open, what do suggestions carry?
        vm.SearchQuery = "de";
        WaitDebounce();
        Log($"02 autocomplete 'de': IsOpen={vm.IsAutocompleteOpen} " +
            $"suggestions=[{string.Join("; ", vm.Suggestions.Select(s => $"{s.Text}|kind={s.Kind}"))}]");
        h.Snapshot("ux-srch-02-autocomplete-open");

        // 03 — Enter right after typing (inside the debounce window): does the
        // pending timer reopen the popup over the results page?
        vm.SearchQuery = "death";
        vm.SubmitSearchCommand.Execute(null);
        Pump();
        Log($"03 submit 'death': IsOpen right after Enter={vm.IsAutocompleteOpen}");
        WaitDebounce();
        Log($"03 submit 'death': IsOpen 350ms later={vm.IsAutocompleteOpen}  (true => popup over results)");
        h.RunStep("ux-srch-03-results-death", () => vm.IsAutocompleteOpen = false);
        var death = Srvm(h);
        Log($"03 results: Header='{death.HeaderLabel}' Total={death.TotalCount} " +
            $"DidYouMean='{death.DidYouMean}' chips=[{string.Join(", ", death.ActiveFilterChips.Select(c => c.Label))}]");

        // 04 — pick a suggestion: same pending-timer race after PickSuggestion.
        vm.SearchQuery = "de";
        WaitDebounce();
        if (vm.Suggestions.Count > 0)
        {
            var pick = vm.Suggestions[0];
            vm.PickSuggestionCommand.Execute(pick);
            Pump();
            Log($"04 pick '{pick.Text}' ({pick.Kind}): IsOpen right after={vm.IsAutocompleteOpen} nav={h.Nav!.CurrentView!.GetType().Name}");
            WaitDebounce();
            Log($"04 pick: IsOpen 350ms later={vm.IsAutocompleteOpen}  (true => popup over product page)");
            h.Snapshot("ux-srch-04-after-pick");
        }
        else Log("04 pick: no suggestions for 'de' — skipped");

        // 05 — did-you-mean quality probes against real seeded names.
        using (var db = new MusicStoreDbContextFactory().CreateDbContext())
        {
            var artists = db.Artists.Select(a => a.Name).ToList();
            Log($"05 seeded artists: {string.Join(", ", artists)}");

            var single = artists.FirstOrDefault(a => !a.Contains(' ') && a.Length >= 5);
            if (single is not null)
            {
                // 05a correct-but-rare query: does it "suggest" the same name back?
                h.Nav!.NavigateTo(NavTarget.SearchResults, single.ToLowerInvariant());
                var r = Srvm(h);
                Log($"05a exact '{single.ToLowerInvariant()}': Total={r.TotalCount} DidYouMean='{r.DidYouMean}'");

                // 05b real one-char typo in a single word.
                var typo = single.Remove(2, 1);
                h.Nav!.NavigateTo(NavTarget.SearchResults, typo);
                r = Srvm(h);
                Log($"05b typo '{typo}': Total={r.TotalCount} DidYouMean='{r.DidYouMean}'");
                h.Snapshot("ux-srch-05-did-you-mean");

                // 05c apply did-you-mean: does the title-bar box follow?
                if (r.HasDidYouMean)
                {
                    vm.SearchQuery = typo; // simulate the user having typed it
                    Pump();
                    r.ApplyDidYouMeanCommand.Execute(null);
                    Pump();
                    Log($"05c apply: page Query='{r.Query}' title-bar SearchQuery='{vm.SearchQuery}'");
                }
            }

            var multi = artists.FirstOrDefault(a => a.Contains(' '));
            if (multi is not null)
            {
                // 05d one-char typo in a multi-word name.
                var words = multi.Split(' ');
                words[0] = words[0].Length > 3 ? words[0].Remove(1, 1) : words[0] + "x";
                var typo = string.Join(" ", words);
                h.Nav!.NavigateTo(NavTarget.SearchResults, typo);
                var r = Srvm(h);
                Log($"05d multi-word typo '{typo}' (real: '{multi}'): Total={r.TotalCount} DidYouMean='{r.DidYouMean}'");
            }
        }

        // 06 — zero FTS hits: the whole dynamic facet sidebar disappears.
        h.RunStep("ux-srch-06-no-results", () => h.Nav!.NavigateTo(NavTarget.SearchResults, "qqqqqqq"));
        var none = Srvm(h);
        Log($"06 no-results: Total={none.TotalCount} FacetGroups={none.Facets.Count} DidYouMean='{none.DidYouMean}'");

        // 07 — numeric dead-end on browse: price kills everything, facet counts stay.
        h.Nav!.NavigateTo(NavTarget.SearchResults);
        var dead = Srvm(h);
        dead.PriceTo = 1m;
        Pump();
        var genreCounts = dead.Facets.FirstOrDefault(f => f.Field == "genre")?
            .VisibleBuckets.Take(4).Select(b => $"{b.Label}({b.Count})");
        Log($"07 priceTo=1: Total={dead.TotalCount} FacetGroups={dead.Facets.Count} " +
            $"genre counts=[{string.Join(", ", genreCounts ?? Enumerable.Empty<string>())}]");
        h.Snapshot("ux-srch-07-price-deadend");

        // 08 — MinRating=0: a no-op filter that still produces a chip + header.
        dead.PriceTo = null;
        dead.MinRating = 0.0;
        Pump();
        Log($"08 minRating=0: Header='{dead.HeaderLabel}' chips=[{string.Join(", ", dead.ActiveFilterChips.Select(c => c.Label))}] Total={dead.TotalCount}");
        h.Snapshot("ux-srch-08-rating-zero-chip");

        // 09 — format facet toggle after a DSL query: does clicking the active
        // bucket actually clear it?
        h.Nav!.NavigateTo(NavTarget.SearchResults, "формат:lp");
        var fmt = Srvm(h);
        Log($"09 формат:lp → SelectedFormatLabel='{fmt.SelectedFormatLabel}'");
        var bucket = fmt.Facets.FirstOrDefault(f => f.Field == "format")?
            .VisibleBuckets.FirstOrDefault(b => b.IsActive);
        if (bucket is not null)
        {
            fmt.ToggleFacetCommand.Execute(bucket);
            Pump();
            Log($"09 click active bucket '{bucket.Label}' → SelectedFormatLabel='{fmt.SelectedFormatLabel}' (non-null => первый клик не снял фильтр)");
        }
        else Log("09 no active format bucket found");

        // 10 — hostile queries: do they crash the search pipeline?
        foreach (var q in new[] { "++", "\"", ":", "-", "(((", "AC/DC" })
        {
            try
            {
                h.Nav!.NavigateTo(NavTarget.SearchResults, q);
                var r = Srvm(h);
                Log($"10 query '{q}': OK Total={r.TotalCount} Header='{r.HeaderLabel}'");
            }
            catch (Exception ex)
            {
                Log($"10 query '{q}': CRASH {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        // 11 — guest SaveQuery: silent no-op?
        h.Nav!.NavigateTo(NavTarget.SearchResults, "death");
        var g = Srvm(h);
        g.SaveQueryCommand.Execute(null);
        Pump();
        using (var db = new MusicStoreDbContextFactory().CreateDbContext())
            Log($"11 guest SaveQuery: rows in SavedSearches={db.SavedSearches.Count()} (guest click => нічого, без фідбеку)");

        // 12 — title-bar box vs page state sync after an external navigation.
        vm.SearchQuery = "death";
        Pump();
        h.Nav!.NavigateTo(NavTarget.SearchResults, "жанр:Rock");
        var rock = Srvm(h);
        Log($"12 genre-tile nav: page Header='{rock.HeaderLabel}' title-bar SearchQuery='{vm.SearchQuery}'");

        // 13 — sidebar «Пошук» click while on results: state wiped?
        vm.NavigateCommand.Execute("SearchResults");
        Pump();
        var wiped = Srvm(h);
        Log($"13 sidebar re-click: Header='{wiped.HeaderLabel}' Total={wiped.TotalCount} (browse => запит/фільтри скинуто)");

        // 14 — layout sweep: results page bottom (sticky?), min size, tall.
        h.Nav!.NavigateTo(NavTarget.SearchResults, "death");
        h.RunStep("ux-srch-14-results-top", () => { });
        h.RunStep("ux-srch-15-results-bottom", () =>
        {
            h.Find<ScrollViewer>("ContentScroll").Offset = new Vector(0, 100000);
            Pump();
        });
        h.SetWindowSize(1024, 640);
        h.RunStep("ux-srch-16-min-top", () =>
        {
            h.Find<ScrollViewer>("ContentScroll").Offset = default;
            Pump();
        });
        h.SetWindowSize(1480, 2300);
        h.RunStep("ux-srch-17-tall-full", () =>
        {
            h.Find<ScrollViewer>("ContentScroll").Offset = default;
            Pump();
        });
        Log("guest sweep done");
    }

    [AvaloniaFact]
    public void Search_ux_audit_logged_in()
    {
        Log($"==== logged-in sweep {DateTime.Now:O} ====");
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");
        h.SetWindowSize(1280, 800);
        var uid = h.Auth!.CurrentUser!.Id;

        int HistoryRows()
        {
            using var db = new MusicStoreDbContextFactory().CreateDbContext();
            return db.SearchHistory.Count(r => r.UserId == uid && r.Query == "death");
        }

        var before = HistoryRows();
        int savedBefore;
        using (var db = new MusicStoreDbContextFactory().CreateDbContext())
            savedBefore = db.SavedSearches.Count(s => s.UserId == uid);

        try
        {
            // 20 — history spam: one search + three facet toggles = how many rows?
            h.Nav!.NavigateTo(NavTarget.SearchResults, "death");
            var r = Srvm(h);
            var afterLoad = HistoryRows();
            for (int i = 0; i < 3; i++)
            {
                var bucket = r.Facets.FirstOrDefault(f => f.Field == "genre")?.VisibleBuckets.FirstOrDefault();
                if (bucket is null) break;
                r.ToggleFacetCommand.Execute(bucket);
                Pump();
            }
            var afterToggles = HistoryRows();
            Log($"20 history 'death': before={before} after-load={afterLoad} after-3-toggles={afterToggles}");

            // 21 — SaveQuery twice: duplicates? what name?
            r.SaveQueryCommand.Execute(null);
            r.SaveQueryCommand.Execute(null);
            Pump();
            using (var db = new MusicStoreDbContextFactory().CreateDbContext())
            {
                var rows = db.SavedSearches.Where(s => s.UserId == uid).ToList();
                Log($"21 SaveQuery×2: rows={rows.Count} names=[{string.Join(" || ", rows.Select(s => s.Name))}]");
            }
            h.Snapshot("ux-srch-21-after-save");
        }
        finally
        {
            // The BugHunt DB is shared by every test in this process — drop the
            // rows this sweep created so later tests see the seeded baseline.
            using var db = new MusicStoreDbContextFactory().CreateDbContext();
            db.SearchHistory.RemoveRange(
                db.SearchHistory.Where(x => x.UserId == uid && x.Query == "death")
                    .OrderByDescending(x => x.Id).Take(Math.Max(0, HistoryRows() - before)));
            var savedNow = db.SavedSearches.Count(s => s.UserId == uid);
            db.SavedSearches.RemoveRange(
                db.SavedSearches.Where(s => s.UserId == uid)
                    .OrderByDescending(s => s.Id).Take(Math.Max(0, savedNow - savedBefore)));
            db.SaveChanges();
        }
        Log("logged-in sweep done");
    }
}
