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
    private const int InitialReviewLimit = 3;

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
    [ObservableProperty] private Album? _currentAlbumForCover;

    [ObservableProperty] private bool _isAlbumLiked;
    [ObservableProperty] private bool _isShuffleOn;
    [ObservableProperty] private RepeatMode _repeatMode;

    [ObservableProperty] private double _avgRating;
    [ObservableProperty] private int _reviewCount;
    [ObservableProperty] private bool _showAllReviews;
    [ObservableProperty] private bool _hasMoreReviews;
    [ObservableProperty] private bool _canLeaveReview;
    [ObservableProperty] private bool _isReviewFormOpen;
    [ObservableProperty] private string _newReviewText = string.Empty;
    [ObservableProperty] private int _newReviewRating = 5;
    [ObservableProperty] private string? _reviewMessage;

    public PlayerViewModel(
        IPlayerService player,
        ICatalogService catalog,
        IAuthService auth,
        ILikesService likes,
        INavigationService nav,
        IFileDialogService? files = null)
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
        Reviews = new ObservableCollection<Review>();
        MoreFromArtist = new ObservableCollection<Album>();
        AllReviews = new System.Collections.Generic.List<Review>();

        ReloadPurchasedAlbums();
        _auth.CurrentUserChanged += (_, _) => { ReloadPurchasedAlbums(); Refresh(); };
        _player.MediaOpened += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => Refresh();
        _player.ShuffleModeChanged += (_, _) => IsShuffleOn = _player.ShuffleMode;
        _player.RepeatModeChanged += (_, _) => RepeatMode = _player.RepeatMode;
        _likes.Changed += (_, _) => RefreshLikeStates();

        IsShuffleOn = _player.ShuffleMode;
        RepeatMode = _player.RepeatMode;

        Refresh();
    }

    public ObservableCollection<Album> PurchasedAlbums { get; }
    public ObservableCollection<TrackRowViewModel> Tracks { get; }
    public ObservableCollection<Review> Reviews { get; }
    public ObservableCollection<Album> MoreFromArtist { get; }

    private System.Collections.Generic.List<Review> AllReviews { get; }

    public bool HasTrack => _player.CurrentTrack is not null;
    public bool HasAlbum => _player.CurrentAlbum is not null;
    public bool HasPurchasedAlbums => PurchasedAlbums.Count > 0;
    public bool HasArtist => _player.CurrentAlbum?.Artist is not null;
    public bool HasMoreFromArtist => MoreFromArtist.Count > 0;
    public bool HasReviews => Reviews.Count > 0;
    public string RatingLabel => ReviewCount == 0 ? "Немає відгуків" : $"★ {AvgRating:0.0} ({ReviewCount})";

    // Header title: prefer album title when an album is playing; for local files
    // (no album context) we fall back to the track title.
    public string HeaderTitle => HasAlbum ? AlbumTitle : TrackTitle;

    // Single-line "2018 · Hip-Hop · 15 треків · 24 хв" — only renders the parts present.
    public string MetadataLine
    {
        get
        {
            if (!HasAlbum) return "";
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(AlbumYear)) parts.Add(AlbumYear);
            if (!string.IsNullOrEmpty(AlbumGenre)) parts.Add(AlbumGenre);
            if (!string.IsNullOrEmpty(AlbumTrackCount)) parts.Add(AlbumTrackCount);
            if (!string.IsNullOrEmpty(AlbumTotalDuration)) parts.Add(AlbumTotalDuration);
            return string.Join("  ·  ", parts);
        }
    }

    public string NowPlayingLabel => HasTrack ? $"Зараз грає: {TrackTitle}" : "";
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
        var album = _player.CurrentAlbum;
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
        CurrentAlbumForCover = album;

        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(HasAlbum));
        OnPropertyChanged(nameof(HasArtist));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(MetadataLine));
        OnPropertyChanged(nameof(NowPlayingLabel));
        OnPropertyChanged(nameof(MoreFromArtistTitle));

        RebuildTracks(album);
        ReloadReviews(album);
        ReloadMoreFromArtist(album);
        RefreshLikeStates();
        ReloadCanLeaveReview(album);
    }

    private void RebuildTracks(Album? album)
    {
        Tracks.Clear();
        if (album is null) return;
        var currentTrackId = _player.CurrentTrack?.Id ?? 0;
        for (int i = 0; i < album.Tracks.Count; i++)
        {
            var row = new TrackRowViewModel(album.Tracks[i], i)
            {
                IsCurrent = album.Tracks[i].Id == currentTrackId
            };
            Tracks.Add(row);
        }
    }

    private void ReloadReviews(Album? album)
    {
        AllReviews.Clear();
        Reviews.Clear();
        AvgRating = 0;
        ReviewCount = 0;
        if (album is null) { RaiseReviewStateChanges(); return; }
        var (avg, count) = _catalog.GetAlbumRating(album.Id);
        AvgRating = avg;
        ReviewCount = count;
        AllReviews.AddRange(_catalog.GetReviewsForAlbum(album.Id));
        RenderReviews();
        RaiseReviewStateChanges();
    }

    private void RenderReviews()
    {
        Reviews.Clear();
        var slice = ShowAllReviews ? AllReviews : AllReviews.Take(InitialReviewLimit);
        foreach (var r in slice) Reviews.Add(r);
        HasMoreReviews = !ShowAllReviews && AllReviews.Count > InitialReviewLimit;
        OnPropertyChanged(nameof(HasReviews));
    }

    partial void OnShowAllReviewsChanged(bool value) => RenderReviews();
    partial void OnAvgRatingChanged(double value) => OnPropertyChanged(nameof(RatingLabel));
    partial void OnReviewCountChanged(int value) => OnPropertyChanged(nameof(RatingLabel));

    private void RaiseReviewStateChanges()
    {
        OnPropertyChanged(nameof(HasReviews));
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
        var album = _player.CurrentAlbum;
        IsAlbumLiked = userId > 0 && album is not null && _likes.IsAlbumLiked(userId, album.Id);
    }

    private void ReloadCanLeaveReview(Album? album)
    {
        var user = _auth.CurrentUser;
        CanLeaveReview = album is not null
            && user is { Role: not UserRole.Guest, Id: > 0 }
            && _catalog.IsAlbumPurchased(album.Id, user.Id);
    }

    private static string FormatTotal(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} год {ts.Minutes} хв";
        return $"{(int)ts.TotalMinutes} хв";
    }

    [RelayCommand]
    private void PlayAlbum(Album album) => _player.PlayAlbum(album);

    [RelayCommand]
    private void PlayTrack(TrackRowViewModel row)
    {
        var album = _player.CurrentAlbum;
        if (album is null || row is null) return;
        _player.PlayAlbum(album, row.Index);
    }

    [RelayCommand]
    private void ToggleAlbumLike()
    {
        var userId = _auth.CurrentUser?.Id ?? 0;
        var album = _player.CurrentAlbum;
        if (userId <= 0 || album is null) return;
        if (IsAlbumLiked) _likes.UnlikeAlbum(userId, album.Id);
        else _likes.LikeAlbum(userId, album.Id);
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
        var name = _player.CurrentAlbum?.Artist?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;
        _nav.NavigateTo(NavTarget.SearchResults, $"виконавець:\"{name}\"");
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
    private void ToggleReviewForm() => IsReviewFormOpen = !IsReviewFormOpen;

    [RelayCommand]
    private void ToggleShowAllReviews() => ShowAllReviews = !ShowAllReviews;

    [RelayCommand]
    private void SubmitReview()
    {
        var album = _player.CurrentAlbum;
        if (album is null) { ReviewMessage = "Немає альбому."; return; }
        var user = _auth.CurrentUser;
        if (user is null || user.Role == UserRole.Guest) { ReviewMessage = "Лише авторизовані."; return; }
        if (!CanLeaveReview) { ReviewMessage = "Лише покупці цього альбому можуть залишити відгук."; return; }
        if (string.IsNullOrWhiteSpace(NewReviewText)) { ReviewMessage = "Введіть текст відгуку."; return; }
        var productId = _catalog.GetPrimaryProductId(album.Id);
        if (productId is null) { ReviewMessage = "Альбом не має продукту для відгуку."; return; }

        _catalog.AddReview(productId.Value, user.Id, user.Username, NewReviewText, NewReviewRating);
        NewReviewText = string.Empty;
        NewReviewRating = 5;
        ReviewMessage = "Дякуємо! Ваш відгук додано.";
        IsReviewFormOpen = false;
        ReloadReviews(album);
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
