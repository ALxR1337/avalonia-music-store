using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class MiniPlayerViewModel : ViewModelBase
{
    private readonly IPlayerService _player;
    private readonly MainWindowViewModel _shell;

    [ObservableProperty] private string _trackTitle = string.Empty;
    [ObservableProperty] private string _artistName = string.Empty;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _volume = 0.7;

    public MiniPlayerViewModel(IPlayerService player, MainWindowViewModel shell)
    {
        _player = player;
        _shell = shell;
        Volume = _player.Volume;

        _player.MediaOpened += (_, _) => Refresh();
        _player.PositionChanged += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => IsPlaying = _player.IsPlaying;
    }

    partial void OnVolumeChanged(double value) => _player.Volume = value;

    private void Refresh()
    {
        var t = _player.CurrentTrack;
        TrackTitle = t?.Title ?? "—";
        ArtistName = _player.CurrentAlbum?.Artist?.Name ?? "семпл 30 с";
        PositionText = Format(_player.Position);
        DurationText = Format(_player.Duration);
        Progress = _player.Duration.TotalSeconds <= 0
            ? 0
            : _player.Position.TotalSeconds / _player.Duration.TotalSeconds * 100.0;
        IsPlaying = _player.IsPlaying;
    }

    private static string Format(TimeSpan ts) => $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";

    [RelayCommand] private void PlayPause() => _player.TogglePlayPause();
    [RelayCommand] private void Next() => _player.Next();
    [RelayCommand] private void Previous() => _player.Previous();
    [RelayCommand] private void Expand() => _shell.ExpandMiniPlayerCommand.Execute(null);
    [RelayCommand] private void Close() => _shell.CloseMiniPlayerCommand.Execute(null);
}
