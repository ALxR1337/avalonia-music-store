using System;
using Avalonia.Threading;
using MusicApp.Models;

namespace MusicApp.Services;

public class PlayerService : IPlayerService
{
    private readonly DispatcherTimer _timer;
    private TimeSpan _position;
    private int _currentIndex = -1;

    public PlayerService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            if (!IsPlaying || CurrentTrack is null) return;
            _position += _timer.Interval;
            if (_position >= Duration)
            {
                MediaEnded?.Invoke(this, EventArgs.Empty);
                Next();
            }
            PositionChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? MediaOpened;
    public event EventHandler? MediaEnded;
    public event EventHandler? PositionChanged;
    public event EventHandler? PlaybackStateChanged;

    public Track? CurrentTrack { get; private set; }
    public Album? CurrentAlbum { get; private set; }
    public bool IsPlaying { get; private set; }
    public TimeSpan Position { get => _position; private set { _position = value; PositionChanged?.Invoke(this, EventArgs.Empty); } }
    public TimeSpan Duration => CurrentTrack?.Duration ?? TimeSpan.Zero;
    public double Volume { get; set; } = 0.7;
    public bool ShuffleMode { get; set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

    public void PlayAlbum(Album album, int startTrackIndex = 0)
    {
        if (album.Tracks.Count == 0) return;
        CurrentAlbum = album;
        _currentIndex = Math.Clamp(startTrackIndex, 0, album.Tracks.Count - 1);
        StartTrack(album.Tracks[_currentIndex]);
    }

    public void PlaySample(Track track)
    {
        CurrentAlbum = null;
        StartTrack(track);
    }

    private void StartTrack(Track track)
    {
        CurrentTrack = track;
        _position = TimeSpan.Zero;
        IsPlaying = true;
        _timer.Start();
        MediaOpened?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack is null) return;
        IsPlaying = !IsPlaying;
        if (IsPlaying) _timer.Start();
        else _timer.Stop();
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        IsPlaying = false;
        _timer.Stop();
        _position = TimeSpan.Zero;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        if (CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % CurrentAlbum.Tracks.Count;
        StartTrack(CurrentAlbum.Tracks[_currentIndex]);
    }

    public void Previous()
    {
        if (CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + CurrentAlbum.Tracks.Count) % CurrentAlbum.Tracks.Count;
        StartTrack(CurrentAlbum.Tracks[_currentIndex]);
    }

    public void Seek(TimeSpan position)
    {
        if (CurrentTrack is null) return;
        _position = TimeSpan.FromMilliseconds(Math.Clamp(position.TotalMilliseconds, 0, Duration.TotalMilliseconds));
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }
}
