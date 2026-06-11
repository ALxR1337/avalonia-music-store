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
    [ObservableProperty] private MusicApp.Models.Album? _currentAlbum;

    // While the user is dragging the slider, the timer-driven Progress writes are
    // suppressed so the thumb does not fight the pointer.
    public bool IsScrubbing { get; set; }

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
        CurrentAlbum = _player.CurrentAlbum;
        // A 30-second preview keeps the "семпл 30 с" subtitle even though we now
        // resolve its album for the cover art; full playback shows the artist.
        ArtistName = _player.IsSampleMode
            ? "семпл 30 с"
            : (_player.CurrentAlbum?.Artist?.Name ?? "—");
        PositionText = Format(_player.Position);
        DurationText = Format(_player.Duration);
        if (!IsScrubbing)
        {
            // Clamp: the player can report a position slightly past Duration on
            // short samples (e.g. 0:33 over a 0:30 clip), which otherwise drags
            // the thumb past the track end.
            Progress = _player.Duration.TotalSeconds <= 0
                ? 0
                : Math.Clamp(_player.Position.TotalSeconds / _player.Duration.TotalSeconds * 100.0, 0, 100);
        }
        IsPlaying = _player.IsPlaying;
    }

    private static string Format(TimeSpan ts) => $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";

    public void CommitSeek(double progressPercent)
    {
        var duration = _player.Duration;
        if (duration.TotalSeconds <= 0) return;
        var ms = duration.TotalMilliseconds * (Math.Clamp(progressPercent, 0, 100) / 100.0);
        _player.Seek(TimeSpan.FromMilliseconds(ms));
    }

    [RelayCommand] private void PlayPause() => _player.TogglePlayPause();
    [RelayCommand] private void Next() => _player.Next();
    [RelayCommand] private void Previous() => _player.Previous();
    [RelayCommand] private void Expand() => _shell.ExpandMiniPlayerCommand.Execute(null);
    [RelayCommand] private void Close() => _shell.CloseMiniPlayerCommand.Execute(null);
}
