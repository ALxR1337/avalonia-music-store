using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class ProductViewModel : ViewModelBase
{
    private readonly ICartService _cart;
    private readonly IPlayerService _player;

    [ObservableProperty] private Product? _product;
    [ObservableProperty] private bool _isVinylSelected;

    public ProductViewModel(ICatalogService catalog, ICartService cart, IPlayerService player, int productId)
    {
        _cart = cart;
        _player = player;
        Product = catalog.GetProduct(productId);
        IsVinylSelected = Product?.Format == ProductFormat.Vinyl;

        Tracks = new ObservableCollection<Track>(Product?.Album?.Tracks ?? new System.Collections.Generic.List<Track>());
        Reviews = new ObservableCollection<Review>(catalog.GetReviewsFor(productId));
    }

    public ObservableCollection<Track> Tracks { get; }
    public ObservableCollection<Review> Reviews { get; }

    public string AlbumTitle => Product?.Album?.Title ?? "—";
    public string ArtistName => Product?.Album?.Artist?.Name ?? "—";
    public string GenreLabel => Product?.Album?.Genre?.Name ?? "—";
    public string YearLabel => Product?.Album?.Year.ToString() ?? "";
    public string PriceLabel => $"{Product?.Price:0} ₴";
    public string StockLabel => Product is null
        ? "—"
        : Product.Stock > 0 ? $"У наявності: {Product.Stock}" : "Немає в наявності";
    public string RatingLabel => $"★ {Product?.Rating:0.0} · {Product?.ReviewCount} відгуків";

    [RelayCommand]
    private void AddToCart()
    {
        if (Product is not null) _cart.Add(Product);
    }

    [RelayCommand]
    private void PlaySample(Track track) => _player.PlaySample(track);
}
