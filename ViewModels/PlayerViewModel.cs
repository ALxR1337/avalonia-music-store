using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class PlayerViewModel : ViewModelBase
{
    private readonly IPlayerService _player;

    [ObservableProperty] private string _trackTitle = "—";
    [ObservableProperty] private string _albumTitle = "";
    [ObservableProperty] private string _artistName = "";
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _volume = 0.7;
    [ObservableProperty] private Album? _selectedLibraryAlbum;

    public PlayerViewModel(IPlayerService player, ICatalogService catalog)
    {
        _player = player;
        Volume = _player.Volume;

        // pretend the first two albums were "purchased" — would normally come from Orders
        PurchasedAlbums = new ObservableCollection<Album>(catalog.Albums.Take(2));

        _player.MediaOpened += (_, _) => Refresh();
        _player.PositionChanged += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => IsPlaying = _player.IsPlaying;

        Refresh();
    }

    public ObservableCollection<Album> PurchasedAlbums { get; }

    partial void OnVolumeChanged(double value) => _player.Volume = value;

    private void Refresh()
    {
        var t = _player.CurrentTrack;
        TrackTitle = t?.Title ?? "—";
        AlbumTitle = _player.CurrentAlbum?.Title ?? "";
        ArtistName = _player.CurrentAlbum?.Artist?.Name ?? "";
        PositionText = Fmt(_player.Position);
        DurationText = Fmt(_player.Duration);
        Progress = _player.Duration.TotalSeconds <= 0
            ? 0
            : _player.Position.TotalSeconds / _player.Duration.TotalSeconds * 100.0;
        IsPlaying = _player.IsPlaying;
    }

    private static string Fmt(TimeSpan ts) => $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";

    [RelayCommand]
    private void PlayAlbum(Album album) => _player.PlayAlbum(album);

    [RelayCommand] private void PlayPause() => _player.TogglePlayPause();
    [RelayCommand] private void Next() => _player.Next();
    [RelayCommand] private void Previous() => _player.Previous();
}
