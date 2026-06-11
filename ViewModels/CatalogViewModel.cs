using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    public string CountLabel
    {
        get
        {
            var n = AlbumCount;
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

    public CatalogViewModel(
        ICatalogService catalog,
        INavigationService nav,
        IPlayerService player,
        ICartService cart)
    {
        _nav = nav;
        _player = player;
        _cart = cart;

        Genres = new ObservableCollection<GenreTile>(BuildGenreTiles(catalog));
        NewArrivals = new ObservableCollection<NewArrivalAlbum>(catalog.GetNewArrivalAlbums(8));
        Artists = new ObservableCollection<Artist>(BuildFeaturedArtists(catalog));
        PriceRanges = new ObservableCollection<SearchShortcut>(BuildPriceRanges());
        RatingShortcuts = new ObservableCollection<SearchShortcut>(BuildRatingShortcuts());
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

    private static IEnumerable<SearchShortcut> BuildPriceRanges() => new[]
    {
        new SearchShortcut("До 500 ₴", "доступні видання", "ціна:..500"),
        new SearchShortcut("500–1000 ₴", "золота середина", "ціна:500..1000"),
        new SearchShortcut("Преміум", "1000 ₴ і вище", "ціна:1000.."),
    };

    private static IEnumerable<SearchShortcut> BuildRatingShortcuts() => new[]
    {
        new SearchShortcut("4.5★ і вище", "найкраще оцінені", "рейтинг:>=4.5"),
        new SearchShortcut("4.0★ і вище", "перевірений звук", "рейтинг:>=4.0"),
    };

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

        foreach (var genre in catalog.Genres)
        {
            albumsByGenre.TryGetValue(genre.Id, out var albums);
            var cover = albums?
                .Where(a => !string.IsNullOrWhiteSpace(a.CoverPath))
                .Select(a => a.CoverPath)
                .FirstOrDefault();
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

    public Product? FeaturedProduct => NewArrivals.FirstOrDefault()?.PrimaryProduct;

    [RelayCommand]
    private void OpenProduct(Product product) =>
        _nav.NavigateTo(NavTarget.Product, product.Id);

    [RelayCommand]
    private void OpenGenre(Genre genre) =>
        _nav.NavigateTo(NavTarget.SearchResults, $"жанр:{genre.Name}");

    [RelayCommand]
    private void OpenArtist(Artist artist) =>
        _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{artist.Name}\"");

    // Generic entry-point for the price/rating shortcut tiles: the structured
    // query is baked into the SearchShortcut, the search page lifts it into filters.
    [RelayCommand]
    private void OpenSearch(string query) =>
        _nav.NavigateTo(NavTarget.SearchResults, query);

    [RelayCommand]
    private void QuickPreview(Product? product)
    {
        if (product?.Album is { Tracks.Count: > 0 } album)
            _player.PlaySample(album.Tracks[0]);
    }

    [RelayCommand]
    private void AddToCart(Product product) => _cart.Add(product);
}
