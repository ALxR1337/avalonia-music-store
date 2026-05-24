using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using MusicApp.Data;
using MusicApp.Models;

namespace MusicApp.Services;

public class PlayerService : IPlayerService, IDisposable
{
    private const int SampleLengthSeconds = 30;

    private readonly IAuthService? _auth;
    private readonly IDbContextFactory<MusicStoreDbContext>? _dbFactory;
    private readonly ICatalogService? _catalog;
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mp;

    private int _currentIndex = -1;
    private bool _isSampleMode;
    private long _sampleEndMs;
    private double _volume = 0.7;
    private bool _disposed;
    private bool _settingsLoaded;

    public PlayerService(
        IAuthService? auth = null,
        IDbContextFactory<MusicStoreDbContext>? dbFactory = null,
        ICatalogService? catalog = null)
    {
        _auth = auth;
        _dbFactory = dbFactory;
        _catalog = catalog;

        LibVlcInitializer.EnsureInitialized();
        _libVlc = new LibVLC();
        _mp = new MediaPlayer(_libVlc) { Volume = (int)(_volume * 100) };

        _mp.TimeChanged += OnTimeChanged;
        _mp.EndReached += OnEndReached;
        _mp.Playing += (_, _) => OnUi(() => { IsPlaying = true; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); });
        _mp.Paused += (_, _) => OnUi(() => { IsPlaying = false; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); });
        _mp.Stopped += (_, _) => OnUi(() => { IsPlaying = false; PlaybackStateChanged?.Invoke(this, EventArgs.Empty); });

        if (_auth is not null)
        {
            _auth.CurrentUserChanged += (_, _) => LoadSettingsForCurrentUser();
            LoadSettingsForCurrentUser();
        }
    }

    public event EventHandler? MediaOpened;
    public event EventHandler? MediaEnded;
    public event EventHandler? PositionChanged;
    public event EventHandler? PlaybackStateChanged;

    public Track? CurrentTrack { get; private set; }
    public Album? CurrentAlbum { get; private set; }
    public bool IsPlaying { get; private set; }
    public TimeSpan Position => TimeSpan.FromMilliseconds(_mp.Time < 0 ? 0 : _mp.Time);
    public TimeSpan Duration
    {
        get
        {
            if (_isSampleMode) return TimeSpan.FromSeconds(SampleLengthSeconds);
            var ms = _mp.Length;
            if (ms > 0) return TimeSpan.FromMilliseconds(ms);
            return CurrentTrack?.Duration ?? TimeSpan.Zero;
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(clamped - _volume) < 0.001) return;
            _volume = clamped;
            _mp.Volume = (int)(_volume * 100);
            PersistSettings();
        }
    }

    private bool _shuffleMode;
    public bool ShuffleMode
    {
        get => _shuffleMode;
        set { if (_shuffleMode == value) return; _shuffleMode = value; PersistSettings(); }
    }

    private RepeatMode _repeatMode = RepeatMode.Off;
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set { if (_repeatMode == value) return; _repeatMode = value; PersistSettings(); }
    }

    public void PlayAlbum(Album album, int startTrackIndex = 0)
    {
        if (album.Tracks.Count == 0) return;
        if (!IsAlbumPlayable(album.Id)) return;
        CurrentAlbum = album;
        _currentIndex = Math.Clamp(startTrackIndex, 0, album.Tracks.Count - 1);
        StartTrack(album.Tracks[_currentIndex], sampleMode: false);
    }

    private bool IsAlbumPlayable(int albumId)
    {
        if (_catalog is null || _auth is null) return true;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return false;
        return _catalog.IsAlbumPurchased(albumId, userId);
    }

    public void PlaySample(Track track)
    {
        CurrentAlbum = null;
        StartTrack(track, sampleMode: true);
    }

    // Plays a file from disk (user's local library) as full track — bypasses the purchase gate
    // because the file is the user's own. Surfaces it through the same MediaOpened event so the
    // mini-player and Now-Playing UI light up.
    public void PlayFile(string path, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        CurrentAlbum = null;
        var track = new Track
        {
            Id = 0,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title,
            FullPath = path
        };
        StartTrack(track, sampleMode: false);
    }

    private void StartTrack(Track track, bool sampleMode)
    {
        CurrentTrack = track;
        _isSampleMode = sampleMode;

        var path = sampleMode
            ? (track.SamplePath ?? track.FullPath)
            : (track.FullPath ?? track.SamplePath);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            // No real audio — surface the open so UI updates, but don't try to play.
            IsPlaying = false;
            _sampleEndMs = 0;
            MediaOpened?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        using var media = new Media(_libVlc, new Uri(Path.GetFullPath(path)));
        _mp.Media = media;

        if (sampleMode)
        {
            var startMs = (long)Math.Max(0, track.SampleStartSeconds) * 1000L;
            _sampleEndMs = startMs + SampleLengthSeconds * 1000L;
            _mp.Play();
            if (startMs > 0) _mp.Time = startMs;
        }
        else
        {
            _sampleEndMs = 0;
            _mp.Play();
        }

        PersistLastTrack(track.Id);
        MediaOpened?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack is null) return;
        if (_mp.IsPlaying) _mp.Pause();
        else _mp.Play();
    }

    public void Stop()
    {
        _mp.Stop();
        _sampleEndMs = 0;
    }

    public void Next()
    {
        if (CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % CurrentAlbum.Tracks.Count;
        StartTrack(CurrentAlbum.Tracks[_currentIndex], sampleMode: false);
    }

    public void Previous()
    {
        if (CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + CurrentAlbum.Tracks.Count) % CurrentAlbum.Tracks.Count;
        StartTrack(CurrentAlbum.Tracks[_currentIndex], sampleMode: false);
    }

    public void Seek(TimeSpan position)
    {
        if (CurrentTrack is null) return;
        var ms = (long)Math.Clamp(position.TotalMilliseconds, 0, Duration.TotalMilliseconds);
        _mp.Time = ms;
        OnUi(() => PositionChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (_isSampleMode && _sampleEndMs > 0 && e.Time >= _sampleEndMs)
        {
            OnUi(() =>
            {
                _mp.Stop();
                MediaEnded?.Invoke(this, EventArgs.Empty);
            });
            return;
        }
        OnUi(() => PositionChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        OnUi(() =>
        {
            MediaEnded?.Invoke(this, EventArgs.Empty);
            if (_isSampleMode) return;
            switch (_repeatMode)
            {
                case RepeatMode.One when CurrentTrack is not null:
                    StartTrack(CurrentTrack, sampleMode: false);
                    break;
                case RepeatMode.All:
                case RepeatMode.Off:
                    Next();
                    break;
            }
        });
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void LoadSettingsForCurrentUser()
    {
        if (_dbFactory is null || _auth is null) return;
        var user = _auth.CurrentUser;
        if (user is null || user.Role == UserRole.Guest) { _settingsLoaded = false; return; }

        try
        {
            using var db = _dbFactory.CreateDbContext();
            var s = db.PlayerSettings.AsNoTracking().FirstOrDefault(p => p.UserId == user.Id);
            if (s is not null)
            {
                _volume = Math.Clamp(s.Volume / 100.0, 0.0, 1.0);
                _mp.Volume = s.Volume;
                _shuffleMode = s.ShuffleMode;
                _repeatMode = s.RepeatMode;
            }
            _settingsLoaded = true;
        }
        catch
        {
            _settingsLoaded = false;
        }
    }

    private void PersistSettings(int? lastTrackId = null)
    {
        if (!_settingsLoaded || _dbFactory is null || _auth?.CurrentUser is null) return;
        var user = _auth.CurrentUser;
        if (user.Role == UserRole.Guest || user.Id <= 0) return;

        try
        {
            using var db = _dbFactory.CreateDbContext();
            var s = db.PlayerSettings.FirstOrDefault(p => p.UserId == user.Id);
            var isNew = s is null;
            s ??= new PlayerSettings { UserId = user.Id };
            s.Volume = (int)(_volume * 100);
            s.RepeatMode = _repeatMode;
            s.ShuffleMode = _shuffleMode;
            if (lastTrackId.HasValue) s.LastTrackId = lastTrackId.Value;
            if (isNew) db.PlayerSettings.Add(s);
            db.SaveChanges();
        }
        catch
        {
            // Persistence failures should not crash playback.
        }
    }

    private void PersistLastTrack(int trackId)
    {
        // FK PlayerSettings.LastTrackId → Tracks(Id). Synthetic tracks (PlayFile)
        // use Id = 0; persisting that would violate the FK.
        if (trackId <= 0) return;
        PersistSettings(trackId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _mp.Stop(); } catch { }
        _mp.Dispose();
        _libVlc.Dispose();
    }
}
