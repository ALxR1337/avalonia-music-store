using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public sealed class GenreTile
{
    public Genre Genre { get; init; } = null!;
    public string Name => Genre.Name;
    public int AlbumCount { get; init; }
    public string? CoverPath { get; init; }
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);
    public string CountLabel => UkrainianPlural.Albums(AlbumCount);
}

internal static class UkrainianPlural
{
    public static string Albums(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        string word;
        if (mod100 >= 11 && mod100 <= 14) word = "альбомів";
        else if (mod10 == 1) word = "альбом";
        else if (mod10 >= 2 && mod10 <= 4) word = "альбоми";
        else word = "альбомів";
        return $"{n} {word}";
    }
}

/// <summary>
/// A labelled catalog entry-point that simply opens the search page with a
/// pre-baked structured query (e.g. "ціна:..500", "рейтинг:>=4.5").
/// </summary>
public sealed record SearchShortcut(string Title, string Subtitle, string Query);

public partial class CatalogViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IPlayerService _player;
    private readonly ICartService _cart;
    private readonly DispatcherTimer _toastTimer;

    public CatalogViewModel(
        ICatalogService catalog,
        INavigationService nav,
        IPlayerService player,
        ICartService cart)
    {
        _nav = nav;
        _player = player;
        _cart = cart;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastMessage = null; };

        Genres = new ObservableCollection<GenreTile>(BuildGenreTiles(catalog));
        NewArrivals = new ObservableCollection<NewArrivalAlbum>(catalog.GetNewArrivalAlbums(8));
        Artists = new ObservableCollection<Artist>(BuildFeaturedArtists(catalog));
        PriceRanges = new ObservableCollection<SearchShortcut>(BuildPriceRanges(catalog));
        RatingShortcuts = new ObservableCollection<SearchShortcut>(BuildRatingShortcuts(catalog));
    }

    // Featured artists = those carrying the deepest catalog, so the row leads
    // with names the shop actually stocks rather than an arbitrary slice.
    private static IEnumerable<Artist> BuildFeaturedArtists(ICatalogService catalog)
    {
        var albumsPerArtist = catalog.Albums
            .GroupBy(a => a.ArtistId)
            .ToDictionary(g => g.Key, g => g.Count());

        return catalog.Artists
            .OrderByDescending(a => albumsPerArtist.TryGetValue(a.Id, out var c) ? c : 0)
            .ThenBy(a => a.Name)
            .Take(12)
            .ToList();
    }

    // The price tiers are cut from the live catalog (terciles of active product
    // prices, snapped to 50 ₴) rather than hardcoded, so each chip always points
    // at a non-empty slice of the store and carries an album count. Counts use
    // "any edition in range" — the same predicate the search page filters by.
    private static IEnumerable<SearchShortcut> BuildPriceRanges(ICatalogService catalog)
    {
        var active = catalog.Products.Where(p => p.IsActive && p.PriceCents > 0).ToList();
        if (active.Count == 0)
            yield break;

        var prices = active.Select(p => p.Price).OrderBy(p => p).ToList();
        var lower = SnapTo50(prices[prices.Count / 3]);
        var upper = SnapTo50(prices[prices.Count * 2 / 3]);
        if (upper <= lower) upper = lower + 50;

        int CountAlbums(decimal? from, decimal? to) => active
            .Where(p => (from is not decimal f || p.Price >= f)
                     && (to is not decimal t || p.Price <= t))
            .Select(p => p.AlbumId).Distinct().Count();

        var tiers = new (string Title, string Flavor, string Query, int Count)[]
        {
            ($"До {lower:0} ₴", "доступні видання", $"ціна:..{lower:0}", CountAlbums(null, lower)),
            ($"{lower:0}–{upper:0} ₴", "золота середина", $"ціна:{lower:0}..{upper:0}", CountAlbums(lower, upper)),
            ("Преміум", $"{upper:0} ₴ і вище", $"ціна:{upper:0}..", CountAlbums(upper, null)),
        };

        foreach (var t in tiers)
        {
            if (t.Count == 0) continue;
            yield return new SearchShortcut(t.Title, $"{t.Flavor} · {UkrainianPlural.Albums(t.Count)}", t.Query);
        }
    }

    private static decimal SnapTo50(decimal price) =>
        Math.Round(price / 50m, MidpointRounding.AwayFromZero) * 50m;

    // Like the price tiers: each chip carries a live album count and an empty
    // bucket is culled, so a shortcut never leads to a dead-end results page.
    private static IEnumerable<SearchShortcut> BuildRatingShortcuts(ICatalogService catalog)
    {
        var active = catalog.Products.Where(p => p.IsActive).ToList();

        int CountAlbums(double min) => active
            .Where(p => p.Rating >= min)
            .Select(p => p.AlbumId).Distinct().Count();

        var tiers = new (string Title, string Flavor, string Query, int Count)[]
        {
            ("4.5★ і вище", "найкраще оцінені", "рейтинг:>=4.5", CountAlbums(4.5)),
            ("4.0★ і вище", "перевірений звук", "рейтинг:>=4.0", CountAlbums(4.0)),
        };

        foreach (var t in tiers)
        {
            if (t.Count == 0) continue;
            yield return new SearchShortcut(t.Title, $"{t.Flavor} · {UkrainianPlural.Albums(t.Count)}", t.Query);
        }
    }

    private static IEnumerable<GenreTile> BuildGenreTiles(ICatalogService catalog)
    {
        var albumsByGenre = catalog.Albums
            .SelectMany(a =>
            {
                var ids = a.AlbumGenres?
                    .Where(ag => ag.Genre is not null)
                    .Select(ag => ag.GenreId)
                    .ToList() ?? new List<int>();
                if (ids.Count == 0) ids.Add(a.GenreId);
                return ids.Select(gid => (GenreId: gid, Album: a));
            })
            .GroupBy(x => x.GenreId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Album).ToList());

        // Crossover albums (tagged with several genres) used to be the *first*
        // covered album of more than one genre, so e.g. Hip-Hop and
        // Experimental got identical tile art. Keep covers unique greedily:
        // prefer an album whose PRIMARY genre is this tile, then any cover not
        // already used; reuse one only when the genre has no other option.
        var usedCovers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var genre in catalog.Genres)
        {
            albumsByGenre.TryGetValue(genre.Id, out var albums);
            var candidates = (albums ?? new List<Album>())
                .Where(a => !string.IsNullOrWhiteSpace(a.CoverPath))
                .OrderByDescending(a => a.GenreId == genre.Id)
                .Select(a => a.CoverPath!)
                .ToList();
            var cover = candidates.FirstOrDefault(c => !usedCovers.Contains(c))
                        ?? candidates.FirstOrDefault();
            if (cover is not null) usedCovers.Add(cover);

            yield return new GenreTile
            {
                Genre = genre,
                AlbumCount = albums?.Count ?? 0,
                CoverPath = cover,
            };
        }
    }

    public ObservableCollection<GenreTile> Genres { get; }
    public ObservableCollection<NewArrivalAlbum> NewArrivals { get; }
    public ObservableCollection<Artist> Artists { get; }
    public ObservableCollection<SearchShortcut> PriceRanges { get; }
    public ObservableCollection<SearchShortcut> RatingShortcuts { get; }

    // Section visibility gates: a heading must not hang over an empty row
    // (possible when every product is deactivated). Computed once — the
    // collections are built in the constructor and never mutated afterwards.
    public bool HasGenres => Genres.Count > 0;
    public bool HasArtists => Artists.Count > 0;
    public bool HasNewArrivals => NewArrivals.Count > 0;
    public bool HasPriceRanges => PriceRanges.Count > 0;
    public bool HasRatingShortcuts => RatingShortcuts.Count > 0;
    public bool HasShortcuts => HasPriceRanges || HasRatingShortcuts;

    public Product? FeaturedProduct => NewArrivals.FirstOrDefault()?.PrimaryProduct;

    // The hero CTA names what it will actually play instead of a mystery
    // "Слухати зараз"; the bare fallback only shows when nothing is playable
    // (and then the command's CanExecute disables the button anyway).
    public string FeaturedTitle => FeaturedProduct?.Album?.Title is { Length: > 0 } title
        ? $"Слухати: {title}"
        : "Слухати зараз";

    // Toast feedback for shelf-level actions (add to cart). Mirrors the admin
    // status toast: floats over the page, auto-hides, dismissable.
    [ObservableProperty] private string? _toastMessage;

    private void ShowToast(string message)
    {
        ToastMessage = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    private void DismissToast()
    {
        _toastTimer.Stop();
        ToastMessage = null;
    }

    [RelayCommand]
    private void OpenProduct(Product? product)
    {
        if (product is not null)
            _nav.NavigateTo(NavTarget.Product, product.Id);
    }

    // Quoted like the artist query below: an admin-created multi-word genre
    // («New Age») must stay one field value, not split into filter + free text.
    [RelayCommand]
    private void OpenGenre(Genre genre) =>
        _nav.NavigateTo(NavTarget.SearchResults, $"жанр:\"{genre.Name}\"");

    [RelayCommand]
    private void OpenArtist(Artist artist) =>
        _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{artist.Name}\"");

    // Generic entry-point for the price/rating shortcut tiles: the structured
    // query is baked into the SearchShortcut, the search page lifts it into filters.
    [RelayCommand]
    private void OpenSearch(string query) =>
        _nav.NavigateTo(NavTarget.SearchResults, query);

    [RelayCommand(CanExecute = nameof(CanQuickPreview))]
    private void QuickPreview(Product? product)
    {
        if (product?.Album is { Tracks.Count: > 0 } album)
            _player.PlaySample(album.Tracks[0]);
    }

    private static bool CanQuickPreview(Product? product) =>
        product?.Album is { Tracks.Count: > 0 };

    [RelayCommand]
    private void AddToCart(Product? product)
    {
        if (product is null) return;

        var title = product.Album?.Title ?? "Товар";
        var format = product.FormatBadge;

        // CartService.Add silently refuses out-of-stock items — the shelf must
        // tell the user instead of pretending the click worked.
        if (product.Stock <= 0)
        {
            ShowToast($"«{title}» ({format}) — немає в наявності");
            return;
        }

        _cart.Add(product);
        ShowToast($"«{title}» ({format}) додано в кошик");
    }
}
