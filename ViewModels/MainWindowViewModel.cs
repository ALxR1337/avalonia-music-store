using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Data;
using MusicApp.Services;
using MusicApp.Services.Search;

namespace MusicApp.ViewModels;

// One row of the search-box popup. Wraps the service hit with the keyboard
// highlight flag (↑/↓ move it, Enter picks the highlighted row).
public partial class SuggestionItemViewModel : ObservableObject
{
    public SuggestionItemViewModel(AutocompleteHit hit) => Hit = hit;

    public AutocompleteHit Hit { get; }
    public string Text => Hit.Text;
    public string Kind => Hit.Kind;
    public string? ImagePath => Hit.ImagePath;
    public string? Subtitle => Hit.Subtitle;

    [ObservableProperty] private bool _isHighlighted;
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IAuthService _auth;
    private readonly ICartService _cart;
    private readonly IPlayerService _player;
    private readonly ICatalogService _catalog;
    private readonly ISearchService? _search;
    private DispatcherTimer? _autocompleteTimer;

    // True while SearchQuery is being set programmatically (suggestion pick,
    // sync from the results page) — those writes must not re-arm the debounce
    // timer, or the popup re-opens on top of whatever page came next.
    private bool _suppressSuggestions;
    private int _highlightedSuggestion = -1;
    private SearchResultsViewModel? _currentSearchVm;

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

    public ObservableCollection<SuggestionItemViewModel> Suggestions { get; } = new();

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
            SyncSearchBoxWith(vm as SearchResultsViewModel);
        };

        RefreshUserInfo();
        _auth.CurrentUserChanged += (_, _) => RefreshUserInfo();

        // A track restored from PlayerSettings ("continue where you left off")
        // may already be loaded before this VM exists — surface the bar for it.
        IsMiniPlayerVisible = _player.CurrentTrack is not null;

        _player.MediaOpened += (_, _) => IsMiniPlayerVisible = true;
        // Invariant: whenever audio is audible the bar is on screen — it is the
        // app's only transport surface (the Player page has no controls), so a
        // hidden bar + playing audio would leave the user without pause/seek.
        _player.PlaybackStateChanged += (_, _) =>
        {
            if (_player.IsPlaying) IsMiniPlayerVisible = true;
        };

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
        if (!Enum.TryParse<NavTarget>(target, out var t)) return;
        // Re-clicking «Пошук» while already on the results page must not wipe
        // the user's query and filters — unlike the other sections this page
        // is stateful, and "reset everything" is never what that click means.
        if (t == NavTarget.SearchResults && _nav.CurrentView is SearchResultsViewModel) return;
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
        // Kill the pending debounce tick FIRST — otherwise it fires up to
        // 200ms later and drops the popup on top of the results page.
        _autocompleteTimer?.Stop();
        IsAutocompleteOpen = false;
        // Enter in the EMPTY box opens browse-all, same as the sidebar tab —
        // unless the results page is already showing (keep its state).
        if (string.IsNullOrWhiteSpace(SearchQuery) && _nav.CurrentView is SearchResultsViewModel) return;
        _nav.NavigateTo(NavTarget.SearchResults, SearchQuery);
    }

    [RelayCommand]
    private void PickSuggestion(SuggestionItemViewModel? item)
    {
        if (item is null) return;
        var hit = item.Hit;

        _suppressSuggestions = true;
        SearchQuery = hit.Text;
        _suppressSuggestions = false;
        _autocompleteTimer?.Stop();
        IsAutocompleteOpen = false;

        // An album/track row opens the product page (track hits carry their
        // album's id — the album is the purchasable unit). Reached through the
        // search box, so it belongs to the Пошук tab. Prefer an active edition:
        // suggestions must not land on a deactivated product the search results
        // themselves would hide.
        if (hit.Kind is "album" or "track" && hit.EntityId is int albumId)
        {
            var product = _catalog.Products.FirstOrDefault(p => p.AlbumId == albumId && p.IsActive)
                       ?? _catalog.Products.FirstOrDefault(p => p.AlbumId == albumId);
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
        // History rows and fallbacks run as a plain text search.
        _nav.NavigateTo(NavTarget.SearchResults, hit.Text);
    }

    /// <summary>Enter in the search box: picks the keyboard-highlighted
    /// suggestion if there is one, otherwise submits the typed query.</summary>
    public void SubmitOrPickHighlighted()
    {
        if (IsAutocompleteOpen
            && _highlightedSuggestion >= 0 && _highlightedSuggestion < Suggestions.Count)
            PickSuggestion(Suggestions[_highlightedSuggestion]);
        else
            SubmitSearch();
    }

    /// <summary>↑/↓ in the search box: moves the highlight, wrapping around.</summary>
    public void MoveSuggestionHighlight(int delta)
    {
        if (Suggestions.Count == 0) return;
        var i = _highlightedSuggestion + delta;
        if (i < 0) i = Suggestions.Count - 1;
        if (i >= Suggestions.Count) i = 0;
        for (var j = 0; j < Suggestions.Count; j++)
            Suggestions[j].IsHighlighted = j == i;
        _highlightedSuggestion = i;
    }

    public void CloseAutocomplete()
    {
        _autocompleteTimer?.Stop();
        IsAutocompleteOpen = false;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _autocompleteTimer?.Stop();
        _suppressSuggestions = true;
        SearchQuery = string.Empty;
        _suppressSuggestions = false;
        ResetSuggestions();
        IsAutocompleteOpen = false;
    }

    /// <summary>Focus lands in an (almost) empty search box → offer the
    /// user's recent queries instead of nothing (guests have no history).</summary>
    public void OnSearchBoxFocused()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SearchQuery.Trim().Length < 2)
            ShowHistorySuggestions();
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (_search is null || _suppressSuggestions) return;
        ResetSuggestions();
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2)
        {
            ShowHistorySuggestions();
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
        ResetSuggestions();
        foreach (var h in hits) Suggestions.Add(new SuggestionItemViewModel(h));
        IsAutocompleteOpen = Suggestions.Count > 0;
    }

    private void ShowHistorySuggestions()
    {
        ResetSuggestions();
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (_search is null || userId <= 0)
        {
            IsAutocompleteOpen = false;
            return;
        }
        foreach (var q in _search.RecentQueries(userId))
            Suggestions.Add(new SuggestionItemViewModel(
                new AutocompleteHit(q, "history", null, null, "нещодавній пошук")));
        IsAutocompleteOpen = Suggestions.Count > 0;
    }

    private void ResetSuggestions()
    {
        Suggestions.Clear();
        _highlightedSuggestion = -1;
    }

    // Keeps the title-bar box honest about what the current results page shows:
    // a genre-tile navigation clears it, «Чи мали ви на увазі» updates it —
    // before this, Enter could silently re-run a stale query.
    private void SyncSearchBoxWith(SearchResultsViewModel? searchVm)
    {
        if (_currentSearchVm is not null)
            _currentSearchVm.PropertyChanged -= OnSearchVmPropertyChanged;
        _currentSearchVm = searchVm;
        if (_currentSearchVm is null) return;
        _currentSearchVm.PropertyChanged += OnSearchVmPropertyChanged;
        SetSearchBoxText(_currentSearchVm.Query);
    }

    private void OnSearchVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchResultsViewModel.Query) && _currentSearchVm is not null)
            SetSearchBoxText(_currentSearchVm.Query);
    }

    private void SetSearchBoxText(string text)
    {
        _autocompleteTimer?.Stop();
        _suppressSuggestions = true;
        SearchQuery = text;
        _suppressSuggestions = false;
        IsAutocompleteOpen = false;
    }

    [RelayCommand]
    private void CloseMiniPlayer()
    {
        // ✕ must actually silence the app: hiding the bar while audio keeps
        // playing would leave no visible way to stop it (the Player page has
        // no transport controls).
        _player.Stop();
        IsMiniPlayerVisible = false;
    }

    // Gate for the global hotkeys: media keys and Space only act (and swallow
    // the keystroke) when something is actually loaded in the player.
    public bool HasLoadedTrack => _player.CurrentTrack is not null;

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
        // Land on the album currently playing if there is one — otherwise on
        // the library grid. The bar stays visible: the album page deliberately
        // has no transport controls, so the bar remains the only pause/seek UI.
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
