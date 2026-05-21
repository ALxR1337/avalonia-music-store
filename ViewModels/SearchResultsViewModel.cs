using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class SearchResultsViewModel : ViewModelBase
{
    private readonly ICatalogService _catalog;
    private readonly INavigationService _nav;
    private readonly IPlayerService _player;
    private readonly ICartService _cart;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _showVinylOnly;
    [ObservableProperty] private bool _showCdOnly;
    [ObservableProperty] private double _maxPrice = 1000;

    public SearchResultsViewModel(
        ICatalogService catalog,
        INavigationService nav,
        IPlayerService player,
        ICartService cart,
        string query)
    {
        _catalog = catalog;
        _nav = nav;
        _player = player;
        _cart = cart;
        Query = query;

        var results = _catalog.SearchProducts(query);
        Albums = new ObservableCollection<Product>(results.Where(p => p.Format == ProductFormat.Vinyl).Take(20));
        Tracks = new ObservableCollection<Track>(
            results.SelectMany(p => p.Album?.Tracks ?? new System.Collections.Generic.List<Track>())
                   .DistinctBy(t => t.Id)
                   .Take(40));
        Artists = new ObservableCollection<Artist>(
            results.Select(p => p.Album?.Artist).OfType<Artist>().DistinctBy(a => a.Id));
        Facets = new ObservableCollection<FacetGroup>
        {
            new() { Title = "Жанр", Values = new ObservableCollection<FacetValue>(
                _catalog.Genres.Select(g => new FacetValue { Label = g.Name, Count = results.Count(p => p.Album?.GenreId == g.Id) })) },
            new() { Title = "Формат", Values = new ObservableCollection<FacetValue>
                {
                    new() { Label = "Вініл LP", Count = results.Count(p => p.Format == ProductFormat.Vinyl) },
                    new() { Label = "CD", Count = results.Count(p => p.Format == ProductFormat.CD) }
                }
            }
        };

        TotalCount = results.Count;
    }

    public ObservableCollection<Product> Albums { get; }
    public ObservableCollection<Track> Tracks { get; }
    public ObservableCollection<Artist> Artists { get; }
    public ObservableCollection<FacetGroup> Facets { get; }

    [RelayCommand]
    private void OpenProduct(Product product) =>
        _nav.NavigateTo(NavTarget.Product, product.Id);

    [RelayCommand]
    private void PlayTrack(Track track) => _player.PlaySample(track);
}

public class FacetGroup
{
    public string Title { get; set; } = string.Empty;
    public ObservableCollection<FacetValue> Values { get; set; } = new();
}

public class FacetValue
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool IsActive { get; set; }
    public string Display => $"{Label} ({Count})";
}
