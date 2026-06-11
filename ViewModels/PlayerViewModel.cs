using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class PlayerViewModel : ViewModelBase
{
    private readonly IPlayerService _player;
    private readonly ICatalogService _catalog;
    private readonly IAuthService _auth;
    private readonly ILikesService _likes;
    private readonly INavigationService _nav;
    private readonly IFileDialogService? _files;

    [ObservableProperty] private string _trackTitle = "—";
    [ObservableProperty] private string _albumTitle = "";
    [ObservableProperty] private string _artistName = "";
    [ObservableProperty] private string _albumYear = "";
    [ObservableProperty] private string _albumGenre = "";
    [ObservableProperty] private string _albumTrackCount = "";
    [ObservableProperty] private string _albumTotalDuration = "";
    [ObservableProperty] private string _albumDescription = "";
    [ObservableProperty] private bool _hasDescription;
    [ObservableProperty] private bool _isDescriptionExpanded;
    [ObservableProperty] private Album? _selectedAlbum;

    private int? _albumProductId;

    [ObservableProperty] private bool _isShuffleOn;
    [ObservableProperty] private RepeatMode _repeatMode;


    public PlayerViewModel(
        IPlayerService player,
        ICatalogService catalog,
        IAuthService auth,
        ILikesService likes,
        INavigationService nav,
        IFileDialogService? files = null,
        Album? initialAlbum = null)
    {
        _player = player;
        _catalog = catalog;
        _auth = auth;
        _likes = likes;
        _nav = nav;
        _files = files;

        PurchasedAlbums = new ObservableCollection<Album>();
        PurchasedAlbums.CollectionChanged += OnPurchasedAlbumsChanged;
        Tracks = new ObservableCollection<TrackRowViewModel>();
        MoreFromArtist = new ObservableCollection<Album>();

        ReloadPurchasedAlbums();
        _auth.CurrentUserChanged += (_, _) => { ReloadPurchasedAlbums(); Refresh(); };
        _player.MediaOpened += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => Refresh();
        _player.ShuffleModeChanged += (_, _) => IsShuffleOn = _player.ShuffleMode;
        _player.RepeatModeChanged += (_, _) => RepeatMode = _player.RepeatMode;
        _likes.Changed += (_, _) => RefreshLikeStates();

        IsShuffleOn = _player.ShuffleMode;
        RepeatMode = _player.RepeatMode;

        // Skip the change notification — Refresh() below sees the field directly,
        // so we don't double-fire navigation/refresh wiring.
        _selectedAlbum = initialAlbum;
        Refresh();
    }

    public ObservableCollection<Album> PurchasedAlbums { get; }
    public ObservableCollection<TrackRowViewModel> Tracks { get; }
    public ObservableCollection<Album> MoreFromArtist { get; }

    public bool HasTrack => _player.CurrentTrack is not null;
    public bool HasSelectedAlbum => SelectedAlbum is not null;
    public bool HasPurchasedAlbums => PurchasedAlbums.Count > 0;
    public bool HasArtist => SelectedAlbum?.Artist is not null;
    public bool HasMoreFromArtist => MoreFromArtist.Count > 0;

    public string HeaderTitle => HasSelectedAlbum ? AlbumTitle : TrackTitle;

    public bool HasYear => !string.IsNullOrEmpty(AlbumYear);
    public bool HasGenre => !string.IsNullOrEmpty(AlbumGenre);
    public bool HasTrackCount => !string.IsNullOrEmpty(AlbumTrackCount);
    public bool HasTotalDuration => !string.IsNullOrEmpty(AlbumTotalDuration);

    // Each separator is visible only when this part renders AND at least one part
    // after it renders too — keeps the row tidy when fields are missing.
    public bool ShowYearSeparator => HasYear && (HasGenre || HasTrackCount || HasTotalDuration);
    public bool ShowGenreSeparator => HasGenre && (HasTrackCount || HasTotalDuration);
    public bool ShowTrackCountSeparator => HasTrackCount && HasTotalDuration;

    public bool HasAlbumProduct => _albumProductId is > 0;

    public string MoreFromArtistTitle => string.IsNullOrEmpty(ArtistName) ? "Більше від артиста" : $"Більше від {ArtistName}";

    private void OnPurchasedAlbumsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasPurchasedAlbums));

    private void ReloadPurchasedAlbums()
    {
        PurchasedAlbums.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        foreach (var a in _catalog.GetPurchasedAlbums(userId))
            PurchasedAlbums.Add(a);
    }

    private void Refresh()
    {
        var t = _player.CurrentTrack;
        var album = SelectedAlbum;
        TrackTitle = t?.Title ?? "—";
        AlbumTitle = album?.Title ?? "";
        ArtistName = album?.Artist?.Name ?? "";
        AlbumYear = album?.Year > 0 ? album.Year.ToString() : "";
        AlbumGenre = album?.Genre?.Name ?? "";
        AlbumTrackCount = album is null ? "" : $"{album.Tracks.Count} треків";
        AlbumTotalDuration = album is null
            ? ""
            : FormatTotal(TimeSpan.FromTicks(album.Tracks.Sum(x => x.Duration.Ticks)));
        AlbumDescription = album?.Description ?? "";
        HasDescription = !string.IsNullOrWhiteSpace(AlbumDescription);
        IsDescriptionExpanded = false;
        _albumProductId = album is null ? null : _catalog.GetPrimaryProductId(album.Id);

        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(HasSelectedAlbum));
        OnPropertyChanged(nameof(HasArtist));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HasYear));
        OnPropertyChanged(nameof(HasGenre));
        OnPropertyChanged(nameof(HasTrackCount));
        OnPropertyChanged(nameof(HasTotalDuration));
        OnPropertyChanged(nameof(ShowYearSeparator));
        OnPropertyChanged(nameof(ShowGenreSeparator));
        OnPropertyChanged(nameof(ShowTrackCountSeparator));
        OnPropertyChanged(nameof(HasAlbumProduct));
        OnPropertyChanged(nameof(MoreFromArtistTitle));

        RebuildTracks(album);
        ReloadMoreFromArtist(album);
        RefreshLikeStates();
    }

    partial void OnSelectedAlbumChanged(Album? value) => Refresh();

    private void RebuildTracks(Album? album)
    {
        Tracks.Clear();
        if (album is null) return;
        var playingAlbumId = _player.CurrentAlbum?.Id ?? 0;
        var currentTrackId = playingAlbumId == album.Id ? _player.CurrentTrack?.Id ?? 0 : 0;
        for (int i = 0; i < album.Tracks.Count; i++)
        {
            var row = new TrackRowViewModel(album.Tracks[i], i)
            {
                IsCurrent = album.Tracks[i].Id == currentTrackId
            };
            Tracks.Add(row);
        }
    }

    private void ReloadMoreFromArtist(Album? album)
    {
        MoreFromArtist.Clear();
        if (album?.Artist is null) return;
        foreach (var other in _catalog.GetAlbumsByArtist(album.ArtistId, excludeAlbumId: album.Id))
            MoreFromArtist.Add(other);
        OnPropertyChanged(nameof(HasMoreFromArtist));
    }

    private void RefreshLikeStates()
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        var likedTracks = userId > 0
            ? _likes.GetLikedTrackIds(userId).ToHashSet()
            : new System.Collections.Generic.HashSet<int>();
        foreach (var row in Tracks)
            row.IsLiked = likedTracks.Contains(row.Track.Id);
    }

    private static string FormatTotal(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} год {ts.Minutes} хв";
        return $"{(int)ts.TotalMinutes} хв";
    }

    [RelayCommand]
    private void OpenAlbum(Album album)
    {
        if (album is null) return;
        // Push a new navigation entry so the top-bar back/forward arrows can
        // step between the library grid and the open album just like browser
        // history. The Player factory passes `album` through as initialAlbum.
        _nav.NavigateTo(NavTarget.Player, album);
    }

    [RelayCommand]
    private void PlaySelectedAlbum()
    {
        if (SelectedAlbum is null) return;
        _player.PlayAlbum(SelectedAlbum);
    }

    [RelayCommand]
    private void PlayTrack(TrackRowViewModel row)
    {
        var album = SelectedAlbum;
        if (album is null || row is null) return;
        _player.PlayAlbum(album, row.Index);
    }

    [RelayCommand]
    private void ToggleTrackLike(TrackRowViewModel row)
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0 || row is null) return;
        if (row.IsLiked) _likes.UnlikeTrack(userId, row.Track.Id);
        else _likes.LikeTrack(userId, row.Track.Id);
    }

    [RelayCommand]
    private void ToggleShuffle() => _player.ShuffleMode = !_player.ShuffleMode;

    [RelayCommand]
    private void CycleRepeat()
    {
        _player.RepeatMode = _player.RepeatMode switch
        {
            Models.RepeatMode.Off => Models.RepeatMode.All,
            Models.RepeatMode.All => Models.RepeatMode.One,
            _ => Models.RepeatMode.Off
        };
    }

    [RelayCommand]
    private void GoToArtist()
    {
        var name = SelectedAlbum?.Artist?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{name}\"");
    }

    [RelayCommand]
    private void GoToAlbumProduct()
    {
        if (_albumProductId is int pid && pid > 0)
            _nav.NavigateTo(NavTarget.Product, pid);
    }

    [RelayCommand]
    private void GoToGenre()
    {
        var name = SelectedAlbum?.Genre?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        _nav.NavigateTo(NavTarget.SearchResults, $"жанр:\"{name}\"");
    }

    [RelayCommand]
    private void GoToArtistAlbum(Album album)
    {
        if (album?.Artist?.Name is not string name) return;
        _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{name}\"");
    }

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private async Task AddLocalFilesAsync()
    {
        if (_files is null) return;
        var path = await _files.OpenFileAsync("Виберіть аудіофайл", new[]
        {
            new FileFilter("Аудіо", new[] { "*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a" }),
            new FileFilter("Усі файли", new[] { "*.*" })
        });
        if (string.IsNullOrWhiteSpace(path)) return;
        _player.PlayFile(path);
    }
}
