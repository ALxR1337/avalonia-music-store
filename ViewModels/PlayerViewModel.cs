using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private readonly IFileDialogService? _files;

    [ObservableProperty] private string _trackTitle = "—";
    [ObservableProperty] private string _albumTitle = "";
    [ObservableProperty] private string _artistName = "";
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _volume = 0.7;
    [ObservableProperty] private Album? _selectedLibraryAlbum;
    [ObservableProperty] private Album? _currentAlbumForCover;

    // When the user is dragging the progress slider, suppress timer-driven Progress writes
    // so the thumb doesn't fight the user's drag.
    public bool IsScrubbing { get; set; }

    public PlayerViewModel(IPlayerService player, ICatalogService catalog, IAuthService auth,
        IFileDialogService? files = null)
    {
        _player = player;
        _catalog = catalog;
        _auth = auth;
        _files = files;
        Volume = _player.Volume;

        PurchasedAlbums = new ObservableCollection<Album>();
        PurchasedAlbums.CollectionChanged += OnPurchasedAlbumsChanged;
        ReloadPurchasedAlbums();

        _auth.CurrentUserChanged += (_, _) => ReloadPurchasedAlbums();
        _player.MediaOpened += (_, _) => Refresh();
        _player.PositionChanged += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => IsPlaying = _player.IsPlaying;

        Refresh();
    }

    public ObservableCollection<Album> PurchasedAlbums { get; }

    public bool HasTrack => _player.CurrentTrack is not null;
    public bool HasPurchasedAlbums => PurchasedAlbums.Count > 0;

    private void OnPurchasedAlbumsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasPurchasedAlbums));

    private void ReloadPurchasedAlbums()
    {
        PurchasedAlbums.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        foreach (var a in _catalog.GetPurchasedAlbums(userId))
            PurchasedAlbums.Add(a);
    }

    partial void OnVolumeChanged(double value) => _player.Volume = value;

    private void Refresh()
    {
        var t = _player.CurrentTrack;
        TrackTitle = t?.Title ?? "—";
        AlbumTitle = _player.CurrentAlbum?.Title ?? "";
        ArtistName = _player.CurrentAlbum?.Artist?.Name ?? "";
        CurrentAlbumForCover = _player.CurrentAlbum;
        PositionText = Fmt(_player.Position);
        DurationText = Fmt(_player.Duration);
        if (!IsScrubbing)
        {
            Progress = _player.Duration.TotalSeconds <= 0
                ? 0
                : _player.Position.TotalSeconds / _player.Duration.TotalSeconds * 100.0;
        }
        IsPlaying = _player.IsPlaying;
        OnPropertyChanged(nameof(HasTrack));
    }

    private static string Fmt(TimeSpan ts) => $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";

    // Called by the view when the scrub-slider's drag completes.
    public void CommitSeek(double progressPercent)
    {
        var duration = _player.Duration;
        if (duration.TotalSeconds <= 0) return;
        var ms = duration.TotalMilliseconds * (Math.Clamp(progressPercent, 0, 100) / 100.0);
        _player.Seek(TimeSpan.FromMilliseconds(ms));
    }

    [RelayCommand]
    private void PlayAlbum(Album album) => _player.PlayAlbum(album);

    [RelayCommand] private void PlayPause() => _player.TogglePlayPause();
    [RelayCommand] private void Next() => _player.Next();
    [RelayCommand] private void Previous() => _player.Previous();

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
