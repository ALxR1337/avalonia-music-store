using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.Services.Search;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Verifies the album-centric search redesign: artist hits surface that artist's
// albums (not an empty profile), genre is multi-select (OR), and track matches
// fold into the owning album instead of standing alone. Drives the real
// SearchService against the harness's seeded + FTS5-indexed isolated DB.
public class SearchRedesignTests
{
    private static (SearchService search, MusicStoreDbContextFactory factory) Boot()
    {
        var h = new Harness();
        h.OpenMainWindow();          // seeds the isolated DB, builds the FTS5 index
        var factory = new MusicStoreDbContextFactory();
        return (new SearchService(factory), factory);
    }

    [AvaloniaFact]
    public void Artist_search_surfaces_that_artists_albums()
    {
        var (search, factory) = Boot();
        using var db = factory.CreateDbContext();

        // Deepest-catalogue artist that actually has purchasable albums.
        var artist = db.Artists.AsNoTracking()
            .Select(a => new
            {
                Artist = a,
                Count = db.Albums.Count(al => al.ArtistId == a.Id
                            && db.Products.Any(p => p.AlbumId == al.Id && p.IsActive))
            })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .First().Artist;

        var expectedAlbumIds = db.Albums.AsNoTracking()
            .Where(al => al.ArtistId == artist.Id
                      && db.Products.Any(p => p.AlbumId == al.Id && p.IsActive))
            .Select(al => al.Id)
            .ToHashSet();

        var results = search.Search($"виконавець:\"{artist.Name}\"");

        // The bug: artist queries returned only the artist row and zero albums.
        Assert.NotEmpty(results.Albums);
        var resultIds = results.Albums.Select(h => h.Album.Id).ToHashSet();
        Assert.True(expectedAlbumIds.IsSubsetOf(resultIds),
            $"очікувані альбоми артиста {artist.Name} мають бути у видачі");
    }

    [AvaloniaFact]
    public void Multiple_genres_return_the_union()
    {
        var (search, factory) = Boot();
        using var db = factory.CreateDbContext();

        var genres = db.Genres.AsNoTracking()
            .Where(g => db.Albums.Any(al => al.AlbumGenres.Any(ag => ag.GenreId == g.Id)
                          && db.Products.Any(p => p.AlbumId == al.Id && p.IsActive)))
            .OrderBy(g => g.Id)
            .Take(2)
            .ToList();
        Assert.Equal(2, genres.Count);

        int CountFor(params string[] g) =>
            search.Search("", new SearchFilters(Genres: g)).Albums.Count;

        var onlyA = CountFor(genres[0].Name);
        var onlyB = CountFor(genres[1].Name);
        var both = search.Search("", new SearchFilters(Genres: new[] { genres[0].Name, genres[1].Name })).Albums;

        // OR semantics: selecting both is a union, never narrower than either alone.
        Assert.True(both.Count >= Math.Max(onlyA, onlyB));
        Assert.All(both, h => Assert.Contains(h.Album.AlbumGenres,
            ag => ag.Genre!.Name == genres[0].Name || ag.Genre!.Name == genres[1].Name));
    }

    [AvaloniaFact]
    public void Genres_in_all_mode_require_every_genre()
    {
        var (search, factory) = Boot();
        using var db = factory.CreateDbContext();

        // An album carrying 2+ genres guarantees the AND (intersection) is non-empty.
        var multi = db.Albums.AsNoTracking()
            .Include(a => a.AlbumGenres).ThenInclude(ag => ag.Genre)
            .Where(a => a.AlbumGenres.Count >= 2
                     && db.Products.Any(p => p.AlbumId == a.Id && p.IsActive))
            .OrderBy(a => a.Id)
            .First();
        var two = multi.AlbumGenres.Select(ag => ag.Genre!.Name).Distinct().Take(2).ToArray();

        var or = search.Search("", new SearchFilters(Genres: two, GenresMatchAll: false)).Albums;
        var and = search.Search("", new SearchFilters(Genres: two, GenresMatchAll: true)).Albums;

        // AND is a subset of OR, includes the co-occurring album, and every AND
        // result carries all the selected genres.
        Assert.True(and.Count <= or.Count);
        Assert.Contains(and, h => h.Album.Id == multi.Id);
        Assert.All(and, h =>
        {
            var names = h.Album.AlbumGenres.Select(ag => ag.Genre!.Name).ToHashSet();
            Assert.All(two, g => Assert.Contains(g, names));
        });
    }

    [AvaloniaFact]
    public void Track_match_folds_into_its_album()
    {
        var (search, factory) = Boot();
        using var db = factory.CreateDbContext();

        var track = db.Tracks.AsNoTracking()
            .Where(t => t.Title.Length > 2
                     && db.Products.Any(p => p.AlbumId == t.AlbumId && p.IsActive))
            .OrderBy(t => t.Id)
            .First();

        var results = search.Search($"трек:\"{track.Title}\"");

        // The owning album appears, annotated with the matched track — there is no
        // standalone-track result type any more (SearchResults exposes only albums).
        var hit = results.Albums.FirstOrDefault(h => h.Album.Id == track.AlbumId);
        Assert.NotNull(hit);
        Assert.Contains(hit!.MatchedTracks, mt => mt.Track.Id == track.Id);
        Assert.True(hit!.HasMatchedTracks);
    }

    [AvaloniaFact]
    public void Genre_combine_toggle_renders_under_the_genre_list()
    {
        var h = new Harness();
        h.OpenMainWindow();
        h.SetWindowSize(1280, 800);

        using var db = new MusicStoreDbContextFactory().CreateDbContext();
        var genres = db.Genres.AsNoTracking()
            .Where(g => db.Albums.Any(al => al.AlbumGenres.Any(ag => ag.GenreId == g.Id)
                          && db.Products.Any(p => p.AlbumId == al.Id && p.IsActive)))
            .OrderBy(g => g.Id).Take(2).ToList();

        // Snapshot lands in bug-hunt/artifacts so the placement can be eyeballed.
        h.RunStep("genre-combine-toggle", () =>
            h.Nav!.NavigateTo(NavTarget.SearchResults, $"жанр:\"{genres[0].Name}\" жанр:\"{genres[1].Name}\""));

        var svm = (SearchResultsViewModel)h.Nav!.CurrentView!;
        Assert.True(svm.CanCombineGenres);   // 2 genres → the any/all combiner is relevant

        // The combiner checkbox is realized and visible (it lives inside the genre
        // facet group, directly under the genre checkboxes).
        var combine = h.Window!.GetVisualDescendants().OfType<CheckBox>()
            .FirstOrDefault(cb => (cb.Content as TextBlock)?.Text?.Contains("усіма обраними") == true);
        Assert.NotNull(combine);
        Assert.True(combine!.IsVisible);
    }
}
