using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class ProductViewModel : ViewModelBase
{
    private const int InitialReviewLimit = 3;

    private readonly ICatalogService _catalog;
    private readonly ICartService _cart;
    private readonly IPlayerService _player;
    private readonly INavigationService _nav;
    private readonly IAuthService _auth;

    private readonly List<Review> _allReviews = new();

    [ObservableProperty] private Product? _product;
    [ObservableProperty] private bool _showAllReviews;
    [ObservableProperty] private bool _hasMoreReviews;
    [ObservableProperty] private bool _isInWishlist;
    [ObservableProperty] private bool _canLeaveReview;
    [ObservableProperty] private string _newReviewText = string.Empty;
    [ObservableProperty] private int _newReviewRating = 5;
    [ObservableProperty] private string? _reviewMessage;
    [ObservableProperty] private bool _hasVinyl;
    [ObservableProperty] private bool _hasCd;

    public ProductViewModel(
        ICatalogService catalog,
        ICartService cart,
        IPlayerService player,
        INavigationService nav,
        IAuthService auth,
        int productId)
    {
        _catalog = catalog;
        _cart = cart;
        _player = player;
        _nav = nav;
        _auth = auth;

        Product = catalog.GetProduct(productId);
        Tracks = new ObservableCollection<Track>(Product?.Album?.Tracks ?? new List<Track>());
        Reviews = new ObservableCollection<Review>();

        ReloadReviews();
        ReloadFormatAvailability();
        ReloadWishlistState();
        ReloadCanLeaveReview();

        _auth.CurrentUserChanged += (_, _) =>
        {
            ReloadWishlistState();
            ReloadCanLeaveReview();
        };
    }

    public ObservableCollection<Track> Tracks { get; }
    public ObservableCollection<Review> Reviews { get; }

    public string AlbumTitle => Product?.Album?.Title ?? "—";
    public string ArtistName => Product?.Album?.Artist?.Name ?? "—";
    public string GenreLabel => Product?.Album?.Genre?.Name ?? "—";
    public string YearLabel => Product?.Album?.Year.ToString() ?? "";
    public string? AlbumDescription => Product?.Album?.Description;
    public bool HasAlbumDescription => !string.IsNullOrWhiteSpace(AlbumDescription);
    public string? ArtistBio => Product?.Album?.Artist?.Description;
    public bool HasArtistBio => !string.IsNullOrWhiteSpace(ArtistBio);
    public IReadOnlyList<string> AlbumGenreNames =>
        Product?.Album?.AlbumGenres?
            .Where(ag => ag.Genre is not null)
            .Select(ag => ag.Genre!.Name)
            .Distinct()
            .ToList()
        ?? new List<string>();
    public string MetadataLine
    {
        get
        {
            var parts = new List<string> { "Альбом" };
            if (Product?.Album?.Year is int year && year > 0) parts.Add(year.ToString());
            var genres = AlbumGenreNames;
            if (genres.Count > 0) parts.Add(string.Join(", ", genres));
            return string.Join(" · ", parts);
        }
    }
    public string PriceLabel => $"{Product?.Price:0} ₴";
    public string StockLabel => Product is null
        ? "—"
        : Product.Stock > 0 ? $"У наявності: {Product.Stock}" : "Немає в наявності";
    public string RatingLabel => Product?.ReviewCount is int c and > 0
        ? $"★ {Product.Rating:0.0} · {Converters.UkrainianPluralConverter.Format(c, "відгук", "відгуки", "відгуків")}"
        : "Ще немає відгуків";
    public string? CoverPath => Product?.Album?.CoverPath;
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverPath);
    public bool IsVinylSelected => Product?.Format == ProductFormat.Vinyl;
    public bool IsCdSelected => Product?.Format == ProductFormat.CD;
    public string WishlistLabel => IsInWishlist ? "Збережено" : "Зберегти";

    private void ReloadReviews()
    {
        if (Product is null) return;
        _allReviews.Clear();
        _allReviews.AddRange(_catalog.GetReviewsFor(Product.Id).OrderByDescending(r => r.CreatedAt));
        RenderReviews();
    }

    private void RenderReviews()
    {
        Reviews.Clear();
        var slice = ShowAllReviews ? _allReviews : _allReviews.Take(InitialReviewLimit);
        foreach (var r in slice) Reviews.Add(r);
        HasMoreReviews = !ShowAllReviews && _allReviews.Count > InitialReviewLimit;
    }

    partial void OnShowAllReviewsChanged(bool value) => RenderReviews();
    partial void OnIsInWishlistChanged(bool value) => OnPropertyChanged(nameof(WishlistLabel));

    private void ReloadFormatAvailability()
    {
        if (Product?.Album is null) return;
        var siblings = _catalog.Products.Where(p => p.AlbumId == Product.AlbumId).ToList();
        HasVinyl = siblings.Any(p => p.Format == ProductFormat.Vinyl);
        HasCd = siblings.Any(p => p.Format == ProductFormat.CD);
    }

    private void ReloadWishlistState()
    {
        var uid = _auth.CurrentUser?.Id ?? 0;
        IsInWishlist = Product is not null && _catalog.IsInWishlist(uid, Product.Id);
    }

    private void ReloadCanLeaveReview()
    {
        var user = _auth.CurrentUser;
        CanLeaveReview = Product is not null
            && user is { Role: not UserRole.Guest, Id: > 0 }
            && _catalog.IsAlbumPurchased(Product.AlbumId, user.Id);
    }

    [RelayCommand(CanExecute = nameof(CanAddToCart))]
    private void AddToCart()
    {
        if (Product is not null) _cart.Add(Product);
    }

    private bool CanAddToCart() => Product is { Stock: > 0 };

    [RelayCommand]
    private void PlaySample(Track track) => _player.PlaySample(track);

    [RelayCommand]
    private void SelectFormat(string format)
    {
        if (Product is null) return;
        var wanted = format == "LP" ? ProductFormat.Vinyl : ProductFormat.CD;
        if (Product.Format == wanted) return;
        var sibling = _catalog.GetSiblingProduct(Product.Id);
        if (sibling is null) return;
        // In-place swap to the sibling product — NOT a navigation: no history
        // entry, no VM rebuild, no scroll reset. Picking LP vs CD is page state,
        // not a page change.
        LoadProduct(sibling.Id);
    }

    // Points the page at a different product without leaving it. Reviews and
    // wishlist state are per-product (LP and CD are distinct products), so they
    // reload; the album, tracks and cover are shared between formats.
    private void LoadProduct(int productId)
    {
        Product = _catalog.GetProduct(productId);
        ReloadReviews();
        ReloadFormatAvailability();
        ReloadWishlistState();
        ReloadCanLeaveReview();
    }

    // Product drives a large surface of computed labels — refresh them all
    // whenever it is reassigned (format swap, post-review reload).
    partial void OnProductChanged(Product? value)
    {
        OnPropertyChanged(nameof(AlbumTitle));
        OnPropertyChanged(nameof(ArtistName));
        OnPropertyChanged(nameof(GenreLabel));
        OnPropertyChanged(nameof(YearLabel));
        OnPropertyChanged(nameof(AlbumDescription));
        OnPropertyChanged(nameof(HasAlbumDescription));
        OnPropertyChanged(nameof(ArtistBio));
        OnPropertyChanged(nameof(HasArtistBio));
        OnPropertyChanged(nameof(AlbumGenreNames));
        OnPropertyChanged(nameof(MetadataLine));
        OnPropertyChanged(nameof(PriceLabel));
        OnPropertyChanged(nameof(StockLabel));
        OnPropertyChanged(nameof(RatingLabel));
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(IsVinylSelected));
        OnPropertyChanged(nameof(IsCdSelected));
        AddToCartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleShowAllReviews() => ShowAllReviews = !ShowAllReviews;

    [RelayCommand]
    private void ToggleWishlist()
    {
        if (Product is null) return;
        var uid = _auth.CurrentUser?.Id ?? 0;
        if (uid <= 0) { ReviewMessage = "Увійдіть, щоб зберігати товари."; return; }
        if (IsInWishlist) _catalog.RemoveFromWishlist(uid, Product.Id);
        else _catalog.AddToWishlist(uid, Product.Id);
        IsInWishlist = !IsInWishlist;
    }

    [RelayCommand]
    private void SubmitReview()
    {
        if (Product is null) return;
        var user = _auth.CurrentUser;
        if (user is null || user.Role == UserRole.Guest) { ReviewMessage = "Лише авторизовані."; return; }
        if (!CanLeaveReview) { ReviewMessage = "Лише покупці цього альбому можуть залишити відгук."; return; }
        if (string.IsNullOrWhiteSpace(NewReviewText)) { ReviewMessage = "Введіть текст відгуку."; return; }

        _catalog.AddReview(Product.Id, user.Id, user.Username, NewReviewText, NewReviewRating);
        NewReviewText = string.Empty;
        NewReviewRating = 5;
        ReviewMessage = "Дякуємо! Ваш відгук додано.";
        ReloadReviews();
        Product = _catalog.GetProduct(Product.Id) ?? Product;
        OnPropertyChanged(nameof(RatingLabel));
    }
}
