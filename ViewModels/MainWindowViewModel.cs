using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.Services.Search;

namespace MusicApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IAuthService _auth;
    private readonly ICartService _cart;
    private readonly IPlayerService _player;
    private readonly ICatalogService _catalog;
    private readonly ISearchService? _search;
    private DispatcherTimer? _autocompleteTimer;

    [ObservableProperty] private ViewModelBase? _currentView;
    [ObservableProperty] private NavTarget _currentTarget = NavTarget.Catalog;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isMiniPlayerVisible;
    [ObservableProperty] private MiniPlayerViewModel? _miniPlayer;
    [ObservableProperty] private string _userDisplayName = "Гість";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isGuest = true;
    [ObservableProperty] private bool _isAutocompleteOpen;
    [ObservableProperty] private int _cartCount;
    [ObservableProperty] private bool _isSidebarCollapsed;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;

    public ObservableCollection<AutocompleteHit> Suggestions { get; } = new();

    public MainWindowViewModel(
        INavigationService nav,
        IAuthService auth,
        ICartService cart,
        IPlayerService player,
        ICatalogService catalog,
        ISearchService? search = null)
    {
        _nav = nav;
        _auth = auth;
        _cart = cart;
        _player = player;
        _catalog = catalog;
        _search = search;

        MiniPlayer = new MiniPlayerViewModel(player, this);

        _nav.CurrentViewChanged += (_, vm) =>
        {
            CurrentView = vm;
            CurrentTarget = _nav.CurrentTarget;
            CanGoBack = _nav.CanGoBack;
            CanGoForward = _nav.CanGoForward;
        };

        UserDisplayName = _auth.CurrentUser?.Username ?? "Гість";
        IsAdmin = _auth.IsAdmin;
        IsGuest = !_auth.IsAuthenticated;

        _auth.CurrentUserChanged += (_, _) =>
        {
            UserDisplayName = _auth.CurrentUser?.Username ?? "Гість";
            IsAdmin = _auth.IsAdmin;
            IsGuest = !_auth.IsAuthenticated;
        };

        _player.MediaOpened += (_, _) => IsMiniPlayerVisible = true;

        CartCount = _cart.ItemCount;
        _cart.CartChanged += (_, _) => CartCount = _cart.ItemCount;

        // initial landing screen
        _nav.NavigateTo(NavTarget.Catalog);
    }

    public bool HasCartItems => CartCount > 0;
    public bool IsCatalogActive => CurrentTarget == NavTarget.Catalog;
    public bool IsCartActive => CurrentTarget == NavTarget.Cart;
    public bool IsOrdersActive => CurrentTarget == NavTarget.Orders;
    public bool IsProfileActive => CurrentTarget == NavTarget.Profile;
    public bool IsPlayerActive => CurrentTarget == NavTarget.Player;
    public bool IsAdminActive => CurrentTarget == NavTarget.Admin;
    public bool IsSearchActive => CurrentTarget == NavTarget.SearchResults;

    partial void OnCartCountChanged(int value) => OnPropertyChanged(nameof(HasCartItems));

    partial void OnCurrentTargetChanged(NavTarget value)
    {
        OnPropertyChanged(nameof(IsCatalogActive));
        OnPropertyChanged(nameof(IsCartActive));
        OnPropertyChanged(nameof(IsOrdersActive));
        OnPropertyChanged(nameof(IsProfileActive));
        OnPropertyChanged(nameof(IsPlayerActive));
        OnPropertyChanged(nameof(IsAdminActive));
        OnPropertyChanged(nameof(IsSearchActive));
    }

    // Design-time ctor — XAML designer only; in production the real services are injected
    public MainWindowViewModel() : this(
        new NavigationService(),
        DesignServices.Auth,
        DesignServices.Cart,
        new PlayerService(),
        DesignServices.Catalog)
    {
    }

    private static class DesignServices
    {
        private static readonly MusicStoreDbContextFactory Factory = new();
        public static readonly AuthService Auth = new(Factory);
        public static readonly CatalogService Catalog = new(Factory);
        public static readonly CartService Cart = new(Auth, Factory);
    }

    [RelayCommand]
    private void Navigate(string target)
    {
        if (Enum.TryParse<NavTarget>(target, out var t))
            _nav.NavigateTo(t);
    }

    [RelayCommand]
    private void GoBack() => _nav.GoBack();

    [RelayCommand]
    private void GoForward() => _nav.GoForward();

    [RelayCommand]
    private void SubmitSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        IsAutocompleteOpen = false;
        _nav.NavigateTo(NavTarget.SearchResults, SearchQuery);
    }

    [RelayCommand]
    private void PickSuggestion(AutocompleteHit hit)
    {
        if (hit is null) return;
        SearchQuery = hit.Text;
        IsAutocompleteOpen = false;
        if (hit.Kind == "album" && hit.EntityId is int albumId)
        {
            // Open the product page for the first product of this album.
            var product = System.Linq.Enumerable.FirstOrDefault(_catalog.Products, p => p.AlbumId == albumId);
            if (product is not null)
            {
                _nav.NavigateTo(NavTarget.Product, product.Id);
                return;
            }
        }
        _nav.NavigateTo(NavTarget.SearchResults, hit.Text);
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (_search is null) return;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
        {
            Suggestions.Clear();
            IsAutocompleteOpen = false;
            return;
        }

        _autocompleteTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _autocompleteTimer.Stop();
        _autocompleteTimer.Tick -= AutocompleteFire;
        _autocompleteTimer.Tick += AutocompleteFire;
        _autocompleteTimer.Start();
    }

    private void AutocompleteFire(object? sender, EventArgs e)
    {
        _autocompleteTimer?.Stop();
        if (_search is null) return;
        var hits = _search.Autocomplete(SearchQuery);
        Suggestions.Clear();
        foreach (var h in hits) Suggestions.Add(h);
        IsAutocompleteOpen = Suggestions.Count > 0;
    }

    [RelayCommand]
    private void CloseMiniPlayer() => IsMiniPlayerVisible = false;

    [RelayCommand]
    private void ExpandMiniPlayer()
    {
        IsMiniPlayerVisible = false;
        _nav.NavigateTo(NavTarget.Player);
    }

    [RelayCommand]
    private void OpenLogin()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) return;

        var loginVm = new LoginViewModel(_auth);
        var loginWindow = new Views.LoginWindow { DataContext = loginVm };
        loginVm.RequestClose += () => loginWindow.Close();
        loginWindow.ShowDialog(desktop.MainWindow!);
    }
}
