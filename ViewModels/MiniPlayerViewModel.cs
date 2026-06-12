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
    [ObservableProperty] private bool _isShuffleOn;
    [ObservableProperty] private MusicApp.Models.RepeatMode _repeatMode;

    // Remembered for unmute: toggling mute restores the last audible level.
    private double _lastAudibleVolume = 0.7;

    // While the user is dragging the slider, the timer-driven Progress writes are
    // suppressed so the thumb does not fight the pointer.
    public bool IsScrubbing { get; set; }

    // Distinguishes Progress writes that mirror the player position from
    // user-initiated ones (keyboard arrows on the slider) — only the latter
    // commit a seek.
    private bool _progressFromPlayer;

    public MiniPlayerViewModel(IPlayerService player, MainWindowViewModel shell)
    {
        _player = player;
        _shell = shell;
        Volume = _player.Volume;

        _player.MediaOpened += (_, _) =>
        {
            Refresh();
            NextCommand.NotifyCanExecuteChanged();
            PreviousCommand.NotifyCanExecuteChanged();
        };
        _player.PositionChanged += (_, _) => Refresh();
        _player.PlaybackStateChanged += (_, _) => IsPlaying = _player.IsPlaying;
        // Volume can change without the slider: another user logs in and their
        // persisted level is applied. Keep the slider honest.
        _player.VolumeChanged += (_, _) => Volume = _player.Volume;

        IsShuffleOn = _player.ShuffleMode;
        RepeatMode = _player.RepeatMode;
        _player.ShuffleModeChanged += (_, _) => IsShuffleOn = _player.ShuffleMode;
        _player.RepeatModeChanged += (_, _) => RepeatMode = _player.RepeatMode;

        // A login-restored "continue where you left off" track loads before
        // this VM exists — its MediaOpened already fired, so sync up now.
        if (_player.CurrentTrack is not null) Refresh();
    }

    public bool IsMuted => Volume <= 0.001;
    public string ShuffleTooltip => IsShuffleOn ? "Перемішування ввімкнено" : "Перемішати";
    public string RepeatTooltip => RepeatMode switch
    {
        MusicApp.Models.RepeatMode.All => "Повтор: весь альбом",
        MusicApp.Models.RepeatMode.One => "Повтор: один трек",
        _ => "Повтор: вимкнено",
    };

    partial void OnVolumeChanged(double value)
    {
        _player.Volume = value;
        if (value > 0.001) _lastAudibleVolume = value;
        OnPropertyChanged(nameof(IsMuted));
    }

    partial void OnIsShuffleOnChanged(bool value) => OnPropertyChanged(nameof(ShuffleTooltip));
    partial void OnRepeatModeChanged(MusicApp.Models.RepeatMode value) => OnPropertyChanged(nameof(RepeatTooltip));

    partial void OnProgressChanged(double value)
    {
        // Keyboard arrows on the slider change Value with no pointer events,
        // so the press/release commit flow never fires — commit here instead.
        // Pointer scrubs (IsScrubbing) and player-driven mirror writes skip.
        if (_progressFromPlayer || IsScrubbing) return;
        CommitSeek(value);
    }

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
            _progressFromPlayer = true;
            Progress = _player.Duration.TotalSeconds <= 0
                ? 0
                : Math.Clamp(_player.Position.TotalSeconds / _player.Duration.TotalSeconds * 100.0, 0, 100);
            _progressFromPlayer = false;
        }
        IsPlaying = _player.IsPlaying;
    }

    // Hour-long tracks need h:mm:ss — "75:30" reads as a typo.
    internal static string Format(TimeSpan ts) => ts.TotalHours >= 1
        ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
        : $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";

    public void CommitSeek(double progressPercent)
    {
        var duration = _player.Duration;
        if (duration.TotalSeconds <= 0) return;
        var ms = duration.TotalMilliseconds * (Math.Clamp(progressPercent, 0, 100) / 100.0);
        _player.Seek(TimeSpan.FromMilliseconds(ms));
    }

    // No album queue (e.g. a single local file) → nothing to skip to; grey the
    // buttons out instead of leaving active-looking dead controls.
    private bool HasQueue => _player.CurrentAlbum is not null;

    [RelayCommand] private void PlayPause() => _player.TogglePlayPause();
    [RelayCommand(CanExecute = nameof(HasQueue))] private void Next() => _player.Next();
    [RelayCommand(CanExecute = nameof(HasQueue))] private void Previous() => _player.Previous();
    [RelayCommand] private void ToggleShuffle() => _player.ShuffleMode = !_player.ShuffleMode;
    [RelayCommand] private void CycleRepeat() => _player.RepeatMode = _player.RepeatMode switch
    {
        MusicApp.Models.RepeatMode.Off => MusicApp.Models.RepeatMode.All,
        MusicApp.Models.RepeatMode.All => MusicApp.Models.RepeatMode.One,
        _ => MusicApp.Models.RepeatMode.Off,
    };
    [RelayCommand] private void ToggleMute() => Volume = IsMuted ? _lastAudibleVolume : 0;
    [RelayCommand] private void Expand() => _shell.ExpandMiniPlayerCommand.Execute(null);
    [RelayCommand] private void Close() => _shell.CloseMiniPlayerCommand.Execute(null);
}
