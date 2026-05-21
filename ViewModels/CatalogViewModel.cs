using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

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

        Genres = new ObservableCollection<Genre>(catalog.Genres);
        NewArrivals = new ObservableCollection<NewArrivalAlbum>(catalog.GetNewArrivalAlbums(8));
        Popular = new ObservableCollection<Product>(catalog.GetPopular(10));
    }

    public ObservableCollection<Genre> Genres { get; }
    public ObservableCollection<NewArrivalAlbum> NewArrivals { get; }
    public ObservableCollection<Product> Popular { get; }

    [RelayCommand]
    private void OpenProduct(Product product) =>
        _nav.NavigateTo(NavTarget.Product, product.Id);

    [RelayCommand]
    private void OpenGenre(Genre genre) =>
        _nav.NavigateTo(NavTarget.SearchResults, $"жанр:{genre.Name}");

    [RelayCommand]
    private void QuickPreview(Product product)
    {
        if (product.Album is { Tracks.Count: > 0 } album)
            _player.PlaySample(album.Tracks[0]);
    }

    [RelayCommand]
    private void AddToCart(Product product) => _cart.Add(product);
}
