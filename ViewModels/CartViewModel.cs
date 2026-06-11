using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class CartViewModel : ViewModelBase
{
    private const int SuggestionCount = 5;

    private enum Stage { Cart, Checkout, Success }

    private readonly ICartService _cart;
    private readonly INavigationService _nav;
    private readonly IAuthService _auth;
    private readonly ICatalogService _catalog;

    private Stage _stage = Stage.Cart;

    [ObservableProperty] private decimal _total;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string? _flashMessage;
    [ObservableProperty] private bool _isGuest;
    [ObservableProperty] private bool _hasSuggestions;

    // Checkout form: pickup from one of the stores, or Nova Poshta delivery.
    [ObservableProperty] private bool _isPickup = true;
    [ObservableProperty] private bool _isNovaPoshta;
    [ObservableProperty] private string? _selectedPickupLocation;
    [ObservableProperty] private string _novaPoshtaCity = "Київ";
    [ObservableProperty] private string _novaPoshtaBranch = string.Empty;
    [ObservableProperty] private string _orderComment = string.Empty;
    [ObservableProperty] private string? _contactEmail;
    [ObservableProperty] private string? _checkoutError;

    public IReadOnlyList<string> PickupLocations { get; } = new[]
    {
        "Поділ — вул. Петра Сагайдачного, 16",
        "Центр — вул. Хрещатик, 22",
        "Золоті Ворота — вул. Ярославів Вал, 15",
        "Печерськ — вул. Велика Васильківська, 72",
    };

    partial void OnIsPickupChanged(bool value)
    {
        if (value) IsNovaPoshta = false;
        CheckoutError = null;
    }

    partial void OnIsNovaPoshtaChanged(bool value)
    {
        if (value) IsPickup = false;
        CheckoutError = null;
    }

    // Success screen
    [ObservableProperty] private Order? _completedOrder;

    public CartViewModel(ICartService cart, INavigationService nav, IAuthService auth, ICatalogService catalog)
    {
        _cart = cart;
        _nav = nav;
        _auth = auth;
        _catalog = catalog;
        Items = cart.Items;
        Suggestions = new ObservableCollection<Product>();
        cart.CartChanged += (_, _) => Refresh();
        auth.CurrentUserChanged += (_, _) => { SetStage(Stage.Cart); Refresh(); };
        Refresh();
    }

    public ObservableCollection<CartItem> Items { get; }
    public ObservableCollection<Product> Suggestions { get; }

    public bool ShowCart => _stage == Stage.Cart;
    public bool ShowCheckout => _stage == Stage.Checkout;
    public bool ShowSuccess => _stage == Stage.Success;

    public string SuggestionsTitle => IsEmpty ? "Можливо, вас зацікавить" : "Купіть також";

    private void SetStage(Stage stage)
    {
        if (_stage == stage) return;
        _stage = stage;
        OnPropertyChanged(nameof(ShowCart));
        OnPropertyChanged(nameof(ShowCheckout));
        OnPropertyChanged(nameof(ShowSuccess));
    }

    private void Refresh()
    {
        Total = _cart.Total;
        ItemCount = _cart.ItemCount;
        IsEmpty = _cart.Items.Count == 0;
        IsGuest = !_auth.IsAuthenticated;

        // The cart can be emptied from elsewhere (logout, another view) while
        // the checkout form is open — there is nothing left to order then.
        if (_stage == Stage.Checkout && IsEmpty)
            SetStage(Stage.Cart);

        RebuildSuggestions();
        OnPropertyChanged(nameof(SuggestionsTitle));
    }

    // «Купіть також»: albums by the same artists first, then same-genre, then
    // store bestsellers — always excluding albums already in the cart. With an
    // empty cart the affinity scores are all zero, so it degrades to plain
    // popularity ("Можливо, вас зацікавить").
    private void RebuildSuggestions()
    {
        var inCartAlbums = _cart.Items
            .Select(i => i.Product?.AlbumId ?? 0)
            .Where(id => id > 0)
            .ToHashSet();
        var cartArtists = _cart.Items
            .Select(i => i.Product?.Album?.ArtistId ?? 0)
            .Where(id => id > 0)
            .ToHashSet();
        var cartGenres = _cart.Items
            .SelectMany(i => i.Product?.Album?.AlbumGenres ?? new System.Collections.Generic.List<AlbumGenre>())
            .Select(ag => ag.GenreId)
            .ToHashSet();

        var picks = _catalog.Products
            .Where(p => p.IsActive && p.Stock > 0 && p.Album is not null && !inCartAlbums.Contains(p.AlbumId))
            .GroupBy(p => p.AlbumId)
            .Select(g => g.OrderBy(p => p.Format == ProductFormat.Vinyl ? 0 : 1).First())
            .OrderByDescending(p =>
                (cartArtists.Contains(p.Album!.ArtistId) ? 2 : 0)
                + (p.Album!.AlbumGenres.Any(ag => cartGenres.Contains(ag.GenreId)) ? 1 : 0))
            .ThenByDescending(p => p.SalesCount)
            .ThenByDescending(p => p.Rating)
            .Take(SuggestionCount)
            .ToList();

        Suggestions.Clear();
        foreach (var p in picks) Suggestions.Add(p);
        HasSuggestions = Suggestions.Count > 0;
    }

    [RelayCommand]
    private void Increase(CartItem item) => _cart.UpdateQuantity(item, item.Quantity + 1);

    [RelayCommand]
    private void Decrease(CartItem item) => _cart.UpdateQuantity(item, item.Quantity - 1);

    [RelayCommand]
    private void Remove(CartItem item) => _cart.Remove(item);

    [RelayCommand]
    private void AddSuggestion(Product? product)
    {
        if (product is not null) _cart.Add(product);
    }

    [RelayCommand]
    private void OpenSuggestion(Product? product)
    {
        if (product is not null) _nav.NavigateTo(NavTarget.Product, product.Id);
    }

    [RelayCommand]
    private void BeginCheckout()
    {
        if (_cart.Items.Count == 0) return;
        if (!_auth.IsAuthenticated)
        {
            FlashMessage = "Будь ласка, увійдіть, щоб оформити замовлення.";
            return;
        }

        FlashMessage = null;
        CheckoutError = null;
        ContactEmail = _auth.CurrentUser?.Email;
        SelectedPickupLocation ??= PickupLocations[0];

        SetStage(Stage.Checkout);
    }

    [RelayCommand]
    private void CancelCheckout()
    {
        CheckoutError = null;
        SetStage(Stage.Cart);
    }

    [RelayCommand]
    private void ConfirmCheckout()
    {
        if (_cart.Items.Count == 0 || !_auth.IsAuthenticated)
        {
            SetStage(Stage.Cart);
            return;
        }

        string address;
        if (IsPickup)
        {
            if (string.IsNullOrWhiteSpace(SelectedPickupLocation))
            {
                CheckoutError = "Оберіть магазин для самовивозу.";
                return;
            }
            address = $"Самовивіз з магазину: {SelectedPickupLocation}";
        }
        else
        {
            var city = NovaPoshtaCity.Trim();
            var branch = NovaPoshtaBranch.Trim();
            if (city.Length == 0 || branch.Length == 0)
            {
                CheckoutError = "Вкажіть місто та відділення Нової Пошти.";
                return;
            }
            if (branch.All(char.IsDigit))
                branch = $"відділення №{branch}";
            address = $"Нова Пошта: {city}, {branch}";
        }

        CheckoutError = null;
        CompletedOrder = _cart.Checkout(address, OrderComment);
        OrderComment = string.Empty;
        SetStage(Stage.Success);
    }

    [RelayCommand]
    private void GoToOrders()
    {
        SetStage(Stage.Cart);
        _nav.NavigateTo(NavTarget.Orders);
    }

    [RelayCommand]
    private void ContinueShopping()
    {
        SetStage(Stage.Cart);
        _nav.NavigateTo(NavTarget.Catalog);
    }
}
