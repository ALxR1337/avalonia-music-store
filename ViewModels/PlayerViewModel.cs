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
    private readonly Action? _requestLogin;

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
    // Set by PlayerView after layout: whether the 3-line clamp actually hides
    // text. Drives the visibility of «Показати повністю» so a one-line
    // description doesn't render a do-nothing button.
    [ObservableProperty] private bool _isDescriptionClamped;
    [ObservableProperty] private Album? _selectedAlbum;
    [ObservableProperty] private bool _isAlbumOwned;
    [ObservableProperty] private bool _isAlbumLiked;

    private int? _albumProductId;

    // Reviews are aggregated across every product of the album (LP + CD share
    // one review feed on this page); the form posts to the primary product.
    private const int InitialReviewLimit = 3;
    private readonly System.Collections.Generic.List<Review> _allAlbumReviews = new();

    [ObservableProperty] private bool _showAllReviews;
    [ObservableProperty] private bool _hasMoreReviews;
    [ObservableProperty] private string _albumRatingLabel = "";
    [ObservableProperty] private bool _canLeaveReview;
    [ObservableProperty] private string _newReviewText = string.Empty;
    [ObservableProperty] private int _newReviewRating = 5;
    [ObservableProperty] private string _reviewMessage = string.Empty;

    [ObservableProperty] private bool _isShuffleOn;
    [ObservableProperty] private RepeatMode _repeatMode;


    public PlayerViewModel(
        IPlayerService player,
        ICatalogService catalog,
        IAuthService auth,
        ILikesService likes,
        INavigationService nav,
        IFileDialogService? files = null,
        Album? initialAlbum = null,
        Action? requestLogin = null)
    {
        _player = player;
        _catalog = catalog;
        _auth = auth;
        _likes = likes;
        _nav = nav;
        _files = files;
        _requestLogin = requestLogin;

        PurchasedAlbums = new ObservableCollection<Album>();
        PurchasedAlbums.CollectionChanged += OnPurchasedAlbumsChanged;
        LibraryItems = new ObservableCollection<object>();
        Tracks = new ObservableCollection<TrackRowViewModel>();
        MoreFromArtist = new ObservableCollection<Album>();
        Reviews = new ObservableCollection<Review>();

        ReloadPurchasedAlbums();
        _auth.CurrentUserChanged += (_, _) => { ReloadPurchasedAlbums(); Refresh(); };
        // Track-advance and pause/play get the light-weight update: the full
        // Refresh() (DB lookups, tracklist rebuild, description-expand reset)
        // runs only when the page's album context changes.
        _player.MediaOpened += (_, _) => OnPlaybackChanged();
        _player.PlaybackStateChanged += (_, _) => OnPlaybackChanged();
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
    // The library grid: purchased albums followed by the "play a local file"
    // tile, so the tile flows in the same WrapPanel instead of dropping onto
    // its own row below the grid.
    public ObservableCollection<object> LibraryItems { get; }
    public ObservableCollection<TrackRowViewModel> Tracks { get; }
    public ObservableCollection<Album> MoreFromArtist { get; }
    public ObservableCollection<Review> Reviews { get; }

    public bool HasReviews => _allAlbumReviews.Count > 0;

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

    // Honest state for an album outside the user's library: the play actions
    // fall back to 30-second samples and the page says so instead of letting
    // the purchase gate in PlayerService swallow the click silently.
    public bool ShowSampleHint => HasSelectedAlbum && !IsAlbumOwned;
    public bool ShowBuyCta => HasSelectedAlbum && !IsAlbumOwned && HasAlbumProduct;

    // The header play button is a play/pause toggle for the album that is
    // actually sounding — pressing it mid-playback must not restart track 1.
    public bool IsSelectedAlbumPlaying =>
        _player.IsPlaying
        && SelectedAlbum is not null
        && _player.CurrentAlbum?.Id == SelectedAlbum.Id;

    public string PlayButtonTip => IsSelectedAlbumPlaying
        ? "Пауза"
        : IsAlbumOwned ? "Слухати альбом" : "Слухати семпли (30 с)";

    public string ShuffleTooltip => IsShuffleOn ? "Перемішування ввімкнено" : "Перемішати";
    public string RepeatTooltip => RepeatMode switch
    {
        Models.RepeatMode.All => "Повтор: весь альбом",
        Models.RepeatMode.One => "Повтор: один трек",
        _ => "Повтор: вимкнено",
    };

    public bool ShowDescriptionToggle => IsDescriptionExpanded || IsDescriptionClamped;
    public string DescriptionToggleText => IsDescriptionExpanded ? "Згорнути" : "Показати повністю";

    partial void OnIsShuffleOnChanged(bool value) => OnPropertyChanged(nameof(ShuffleTooltip));
    partial void OnRepeatModeChanged(RepeatMode value) => OnPropertyChanged(nameof(RepeatTooltip));
    partial void OnIsDescriptionClampedChanged(bool value) => OnPropertyChanged(nameof(ShowDescriptionToggle));
    partial void OnIsDescriptionExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDescriptionToggle));
        OnPropertyChanged(nameof(DescriptionToggleText));
    }

    public string MoreFromArtistTitle => string.IsNullOrEmpty(ArtistName) ? "Більше від артиста" : $"Більше від {ArtistName}";

    private void OnPurchasedAlbumsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasPurchasedAlbums));

    private void ReloadPurchasedAlbums()
    {
        PurchasedAlbums.Clear();
        LibraryItems.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        foreach (var a in _catalog.GetPurchasedAlbums(userId))
        {
            PurchasedAlbums.Add(a);
            LibraryItems.Add(a);
        }
        LibraryItems.Add(AddLocalFileTile.Instance);
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
        AlbumTrackCount = album is null ? "" : FormatTrackCount(album.Tracks.Count);
        AlbumTotalDuration = album is null
            ? ""
            : FormatTotal(TimeSpan.FromTicks(album.Tracks.Sum(x => x.Duration.Ticks)));
        AlbumDescription = album?.Description ?? "";
        HasDescription = !string.IsNullOrWhiteSpace(AlbumDescription);
        IsDescriptionExpanded = false;
        _albumProductId = album is null ? null : _catalog.GetPrimaryProductId(album.Id);
        IsAlbumOwned = album is not null
            && _catalog.IsAlbumPurchased(album.Id, _auth.CurrentUser?.Id ?? 0);

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
        OnPropertyChanged(nameof(ShowSampleHint));
        OnPropertyChanged(nameof(ShowBuyCta));
        OnPropertyChanged(nameof(IsSelectedAlbumPlaying));
        OnPropertyChanged(nameof(PlayButtonTip));
        OnPropertyChanged(nameof(MoreFromArtistTitle));

        RebuildTracks(album);
        ReloadMoreFromArtist(album);
        ReloadReviews(album);
        RefreshLikeStates();
    }

    partial void OnSelectedAlbumChanged(Album? value) => Refresh();

    // What play/pause and track-advance can actually change on this page:
    // the current-track markers and the header play button state. Running the
    // full Refresh() here used to collapse the expanded description, rebuild
    // (and de-focus) every tracklist row and re-query the catalog on every
    // pause.
    private void OnPlaybackChanged()
    {
        TrackTitle = _player.CurrentTrack?.Title ?? "—";
        var album = SelectedAlbum;
        var playingAlbumId = _player.CurrentAlbum?.Id ?? 0;
        var currentTrackId = album is not null && playingAlbumId == album.Id
            ? _player.CurrentTrack?.Id ?? 0
            : 0;
        foreach (var row in Tracks)
            row.IsCurrent = row.Track.Id == currentTrackId;
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(IsSelectedAlbumPlaying));
        OnPropertyChanged(nameof(PlayButtonTip));
    }

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

    private void ReloadReviews(Album? album)
    {
        _allAlbumReviews.Clear();
        if (album is not null)
            _allAlbumReviews.AddRange(_catalog.GetReviewsForAlbum(album.Id)
                .OrderByDescending(r => r.CreatedAt));
        ShowAllReviews = false;
        RenderReviews();

        var (avg, count) = album is null ? (0d, 0) : _catalog.GetAlbumRating(album.Id);
        AlbumRatingLabel = count > 0
            ? $"★ {avg:0.0} · {Converters.UkrainianPluralConverter.Format(count, "відгук", "відгуки", "відгуків")}"
            : "";
        ReviewMessage = string.Empty;

        var user = _auth.CurrentUser;
        CanLeaveReview = album is not null
            && user is { Role: not UserRole.Guest, Id: > 0 }
            && IsAlbumOwned;
    }

    private void RenderReviews()
    {
        Reviews.Clear();
        var slice = ShowAllReviews ? _allAlbumReviews : _allAlbumReviews.Take(InitialReviewLimit);
        foreach (var r in slice) Reviews.Add(r);
        HasMoreReviews = !ShowAllReviews && _allAlbumReviews.Count > InitialReviewLimit;
        OnPropertyChanged(nameof(HasReviews));
    }

    partial void OnShowAllReviewsChanged(bool value) => RenderReviews();

    private void RefreshLikeStates()
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        var likedTracks = userId > 0
            ? _likes.GetLikedTrackIds(userId).ToHashSet()
            : new System.Collections.Generic.HashSet<int>();
        foreach (var row in Tracks)
            row.IsLiked = likedTracks.Contains(row.Track.Id);
        IsAlbumLiked = userId > 0
            && SelectedAlbum is not null
            && _likes.IsAlbumLiked(userId, SelectedAlbum.Id);
    }

    private static string FormatTotal(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} год {ts.Minutes} хв";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes} хв";
        // Sub-minute albums showed "0 хв"; empty hides the field entirely.
        return ts.TotalSeconds >= 1 ? $"{ts.Seconds} с" : "";
    }

    // Ukrainian plural: 1 трек / 2–4 треки / 5+ треків (11–14 → треків).
    internal static string FormatTrackCount(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = mod10 == 1 && mod100 != 11 ? "трек"
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? "треки"
            : "треків";
        return $"{n} {word}";
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
        // This album is already loaded in the player: toggle pause/resume
        // instead of restarting from track 1. Exception: the user owns it but
        // it was loaded as samples — fall through and upgrade to full playback.
        var isCurrent = _player.CurrentAlbum?.Id == SelectedAlbum.Id && _player.CurrentTrack is not null;
        if (isCurrent && !(IsAlbumOwned && _player.IsSampleMode))
        {
            _player.TogglePlayPause();
            return;
        }
        if (IsAlbumOwned)
        {
            _player.PlayAlbum(SelectedAlbum);
            return;
        }
        // Not in the user's library: play the 30-second previews instead of
        // letting the purchase-gated PlayAlbum swallow the click silently.
        if (SelectedAlbum.Tracks.Count > 0)
            _player.PlaySample(SelectedAlbum.Tracks[0]);
    }

    [RelayCommand]
    private void PlayTrack(TrackRowViewModel row)
    {
        var album = SelectedAlbum;
        if (album is null || row is null) return;
        if (IsAlbumOwned) _player.PlayAlbum(album, row.Index);
        else _player.PlaySample(row.Track);
    }

    [RelayCommand]
    private void ToggleTrackLike(TrackRowViewModel row)
    {
        if (row is null) return;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0)
        {
            // Guests see active-looking hearts; a silent no-op reads as
            // "broken" — invite them to log in instead.
            _requestLogin?.Invoke();
            return;
        }
        if (row.IsLiked) _likes.UnlikeTrack(userId, row.Track.Id);
        else _likes.LikeTrack(userId, row.Track.Id);
    }

    [RelayCommand]
    private void ToggleAlbumLike()
    {
        if (SelectedAlbum is null) return;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0)
        {
            _requestLogin?.Invoke();
            return;
        }
        if (IsAlbumLiked) _likes.UnlikeAlbum(userId, SelectedAlbum.Id);
        else _likes.LikeAlbum(userId, SelectedAlbum.Id);
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
        if (album is null) return;
        // Clicking a specific album opens THAT album, not a generic artist
        // search: owned → straight into the player; in the catalog → its
        // product page; only album-less leftovers fall back to the search.
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId > 0 && _catalog.IsAlbumPurchased(album.Id, userId))
        {
            _nav.NavigateTo(NavTarget.Player, album);
            return;
        }
        if (_catalog.GetPrimaryProductId(album.Id) is int pid and > 0)
        {
            _nav.NavigateTo(NavTarget.Product, pid);
            return;
        }
        var name = album.Artist?.Name ?? ArtistName;
        if (!string.IsNullOrWhiteSpace(name))
            _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{name}\"");
    }

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    [RelayCommand]
    private void ToggleShowAllReviews() => ShowAllReviews = !ShowAllReviews;

    [RelayCommand]
    private void SubmitReview()
    {
        var album = SelectedAlbum;
        if (album is null) return;
        var user = _auth.CurrentUser;
        if (user is null || user.Role == UserRole.Guest || user.Id <= 0)
        {
            _requestLogin?.Invoke();
            return;
        }
        if (!CanLeaveReview)
        {
            ReviewMessage = "Лише власники альбому можуть залишити відгук.";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewReviewText))
        {
            ReviewMessage = "Введіть текст відгуку.";
            return;
        }
        if (_albumProductId is not int pid || pid <= 0)
        {
            ReviewMessage = "Альбом недоступний у каталозі.";
            return;
        }
        _catalog.AddReview(pid, user.Id, user.Username, NewReviewText, NewReviewRating);
        NewReviewText = string.Empty;
        NewReviewRating = 5;
        ReloadReviews(album);
        ReviewMessage = "Дякуємо! Ваш відгук додано.";
    }

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

// Marker item: renders the "play a local file" tile as the last cell of the
// library grid (same WrapPanel slot as album cards) — see PlayerView's
// ItemsControl.DataTemplates.
public sealed class AddLocalFileTile
{
    public static readonly AddLocalFileTile Instance = new();
    private AddLocalFileTile() { }
}
