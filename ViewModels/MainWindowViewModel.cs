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
    [ObservableProperty] private NavTarget _currentSection = NavTarget.Catalog;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isMiniPlayerVisible;
    [ObservableProperty] private MiniPlayerViewModel? _miniPlayer;
    [ObservableProperty] private string _userDisplayName = "Гість";
    [ObservableProperty] private string _userEmail = string.Empty;
    [ObservableProperty] private string _userRoleLabel = "Гість";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isGuest = true;
    [ObservableProperty] private bool _isAutocompleteOpen;
    [ObservableProperty] private int _cartCount;
    [ObservableProperty] private bool _isSidebarCollapsed;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _isCoverFullscreen;
    [ObservableProperty] private MusicApp.Models.Album? _fullscreenCoverAlbum;
    [ObservableProperty] private bool _isLoginVisible;
    [ObservableProperty] private bool _isUserMenuOpen;

    public ObservableCollection<AutocompleteHit> Suggestions { get; } = new();

    /// <summary>Sub-VM for the in-app login overlay card.</summary>
    public LoginViewModel Login { get; }

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

        Login = new LoginViewModel(auth);
        Login.RequestClose += () => IsLoginVisible = false;

        _nav.CurrentViewChanged += (_, vm) =>
        {
            CurrentView = vm;
            CurrentTarget = _nav.CurrentTarget;
            CurrentSection = _nav.CurrentSection;
            CanGoBack = _nav.CanGoBack;
            CanGoForward = _nav.CanGoForward;
            // Capture the target offset by value now (before the new page lays
            // out) so the View can restore it once layout settles. Fresh pages
            // carry 0 → open at the top; back/forward carry the saved position.
            RestoreScroll?.Invoke(_nav.CurrentScrollOffset);
        };

        RefreshUserInfo();
        _auth.CurrentUserChanged += (_, _) => RefreshUserInfo();

        _player.MediaOpened += (_, _) => IsMiniPlayerVisible = true;

        CartCount = _cart.ItemCount;
        _cart.CartChanged += (_, _) => CartCount = _cart.ItemCount;

        // initial landing screen
        _nav.NavigateTo(NavTarget.Catalog);
    }

    public bool HasCartItems => CartCount > 0;
    // Highlight follows the active *section*, not the raw target, so detail
    // pages (Product) keep their parent tab lit.
    public bool IsCatalogActive => CurrentSection == NavTarget.Catalog;
    public bool IsCartActive => CurrentSection == NavTarget.Cart;
    public bool IsOrdersActive => CurrentSection == NavTarget.Orders;
    public bool IsProfileActive => CurrentSection == NavTarget.Profile;
    public bool IsPlayerActive => CurrentSection == NavTarget.Player;
    public bool IsAdminActive => CurrentSection == NavTarget.Admin;
    public bool IsSearchActive => CurrentSection == NavTarget.SearchResults;

    /// <summary>Raised when navigation settles; carries the scroll offset the
    /// content area should restore (0 for fresh pages). The View applies it
    /// after the new page lays out.</summary>
    public event Action<double>? RestoreScroll;

    /// <summary>Called by the View as the content area scrolls, so the current
    /// page's position is remembered for back/forward.</summary>
    public void NotifyScroll(double offsetY) => _nav.SaveScroll(offsetY);

    partial void OnCartCountChanged(int value) => OnPropertyChanged(nameof(HasCartItems));

    partial void OnCurrentSectionChanged(NavTarget value)
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
        IsUserMenuOpen = false;
        if (Enum.TryParse<NavTarget>(target, out var t))
            _nav.NavigateTo(t);
    }

    [RelayCommand]
    private void ToggleUserMenu() => IsUserMenuOpen = !IsUserMenuOpen;

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
            // Open the product page for the first product of this album (track hits
            // are re-keyed onto their album upstream). Reached through the search
            // box, so it belongs to the Пошук tab.
            var product = System.Linq.Enumerable.FirstOrDefault(_catalog.Products, p => p.AlbumId == albumId);
            if (product is not null)
            {
                _nav.NavigateTo(NavTarget.Product, product.Id, NavTarget.SearchResults);
                return;
            }
        }
        if (hit.Kind == "artist")
        {
            // Surface the artist's albums via the artist facet (same as a catalog
            // artist tile), not a fuzzy free-text search of the name.
            _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{hit.Text}\"");
            return;
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

    // Triggered by the global Space hotkey (see MainWindow.OnGlobalKeyDown).
    [RelayCommand]
    private void TogglePlayPause() => _player.TogglePlayPause();

    [RelayCommand]
    private void OpenCoverFullscreen(MusicApp.Models.Album? album)
    {
        if (album is null) return;
        FullscreenCoverAlbum = album;
        IsCoverFullscreen = true;
    }

    [RelayCommand]
    private void CloseCoverFullscreen()
    {
        IsCoverFullscreen = false;
        FullscreenCoverAlbum = null;
    }

    [RelayCommand]
    private void ExpandMiniPlayer()
    {
        IsMiniPlayerVisible = false;
        // Land on the album currently playing if there is one — otherwise on
        // the library grid.
        _nav.NavigateTo(NavTarget.Player, _player.CurrentAlbum);
    }

    // Shows the login overlay with a clean form — used at startup (when no
    // "remember me" session was restored) and by the title-bar "Увійти" button.
    public void ShowLogin()
    {
        IsUserMenuOpen = false;
        Login.Reset();
        IsLoginVisible = true;
    }

    [RelayCommand]
    private void OpenLogin() => ShowLogin();

    [RelayCommand]
    private void CloseLogin() => IsLoginVisible = false;

    // Opens the login overlay pre-switched to the registration form — used by the
    // "Реєстрація" item in the guest account menu.
    [RelayCommand]
    private void OpenRegister()
    {
        IsUserMenuOpen = false;
        Login.Reset();
        Login.IsRegistering = true;
        IsLoginVisible = true;
    }

    [RelayCommand]
    private void Logout()
    {
        IsUserMenuOpen = false;
        _auth.Logout();
        // Drop back to the catalog so the user isn't stranded on a now-empty
        // Profile/Admin screen after losing their session.
        _nav.NavigateTo(NavTarget.Catalog);
    }

    // Surfaced from the account menu: go to the Profile screen and reveal its
    // inline change-password panel there — no separate window.
    [RelayCommand]
    private void ChangePassword()
    {
        IsUserMenuOpen = false;
        if (IsGuest) return;
        _nav.NavigateTo(NavTarget.Profile);
        if (CurrentView is ProfileViewModel profile)
            profile.OpenPasswordPanel();
    }

    // Mirrors auth state into the title-bar account chip + menu header.
    private void RefreshUserInfo()
    {
        UserDisplayName = _auth.CurrentUser?.Username ?? "Гість";
        UserEmail = _auth.CurrentUser?.Email ?? string.Empty;
        UserRoleLabel = _auth.CurrentUser?.Role switch
        {
            MusicApp.Models.UserRole.Admin => "Адміністратор",
            MusicApp.Models.UserRole.Customer => "Покупець",
            _ => "Гість"
        };
        IsAdmin = _auth.IsAdmin;
        IsGuest = !_auth.IsAuthenticated;
    }
}
