using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Data;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IAuthService _auth;
    private readonly ICartService _cart;
    private readonly IPlayerService _player;
    private readonly ICatalogService _catalog;

    [ObservableProperty] private ViewModelBase? _currentView;
    [ObservableProperty] private NavTarget _currentTarget = NavTarget.Catalog;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isMiniPlayerVisible;
    [ObservableProperty] private MiniPlayerViewModel? _miniPlayer;
    [ObservableProperty] private string _userDisplayName = "Гість";
    [ObservableProperty] private bool _isAdmin;

    public MainWindowViewModel(
        INavigationService nav,
        IAuthService auth,
        ICartService cart,
        IPlayerService player,
        ICatalogService catalog)
    {
        _nav = nav;
        _auth = auth;
        _cart = cart;
        _player = player;
        _catalog = catalog;

        MiniPlayer = new MiniPlayerViewModel(player, this);

        _nav.CurrentViewChanged += (_, vm) =>
        {
            CurrentView = vm;
            CurrentTarget = _nav.CurrentTarget;
        };

        _auth.CurrentUserChanged += (_, _) =>
        {
            UserDisplayName = _auth.CurrentUser?.Username ?? "Гість";
            IsAdmin = _auth.IsAdmin;
        };

        _player.MediaOpened += (_, _) => IsMiniPlayerVisible = true;

        // initial landing screen
        _nav.NavigateTo(NavTarget.Catalog);
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
    private void SubmitSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        _nav.NavigateTo(NavTarget.SearchResults, SearchQuery);
    }

    [RelayCommand]
    private void CloseMiniPlayer() => IsMiniPlayerVisible = false;

    [RelayCommand]
    private void ExpandMiniPlayer()
    {
        IsMiniPlayerVisible = false;
        _nav.NavigateTo(NavTarget.Player);
    }
}
