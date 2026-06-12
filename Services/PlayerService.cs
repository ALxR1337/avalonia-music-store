using System;
using System.Collections.Generic;
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
    // Shuffle is a random walk over the current album: a permutation of track
    // indices anchored on the playing track. Next/Previous step through it, so
    // every track is visited exactly once per cycle.
    private List<int>? _shuffleOrder;
    private int _shufflePos = -1;
    private readonly Random _shuffleRng = new();
    private bool _isSampleMode;
    private long _sampleStartMs;
    private long _sampleEndMs;
    private double _volume = 0.7;
    private bool _disposed;
    private bool _settingsLoaded;
    private readonly DispatcherTimer _volumePersistDebounce;

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

        _volumePersistDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _volumePersistDebounce.Tick += (_, _) =>
        {
            _volumePersistDebounce.Stop();
            PersistSettings();
        };

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
    public event EventHandler? ShuffleModeChanged;
    public event EventHandler? RepeatModeChanged;
    public event EventHandler? VolumeChanged;

    public Track? CurrentTrack { get; private set; }
    public Album? CurrentAlbum { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsSampleMode => _isSampleMode;
    public TimeSpan Position
    {
        get
        {
            var ms = _mp.Time < 0 ? 0 : _mp.Time;
            // A sample plays a 30s window starting at _sampleStartMs into the clip.
            // Report the position relative to that window so it counts 0:00 → 0:30
            // and stays in step with Duration (the 30s sample length) instead of
            // showing the absolute clip time (e.g. 0:31 over a "0:30" sample).
            if (_isSampleMode)
                ms = Math.Clamp(ms - _sampleStartMs, 0, SampleLengthSeconds * 1000L);
            return TimeSpan.FromMilliseconds(ms);
        }
    }
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
            VolumeChanged?.Invoke(this, EventArgs.Empty);
            // Dragging the slider lands here once per pixel; debounce the
            // SQLite write instead of hitting the DB on every tick.
            _volumePersistDebounce.Stop();
            _volumePersistDebounce.Start();
        }
    }

    private bool _shuffleMode;
    public bool ShuffleMode
    {
        get => _shuffleMode;
        set
        {
            if (_shuffleMode == value) return;
            _shuffleMode = value;
            RebuildShuffleOrder();
            PersistSettings();
            ShuffleModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private RepeatMode _repeatMode = RepeatMode.Off;
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode == value) return;
            _repeatMode = value;
            PersistSettings();
            RepeatModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PlayAlbum(Album album, int startTrackIndex = 0)
    {
        if (album.Tracks.Count == 0) return;
        if (!IsAlbumPlayable(album.Id)) return;
        CurrentAlbum = album;
        _currentIndex = Math.Clamp(startTrackIndex, 0, album.Tracks.Count - 1);
        RebuildShuffleOrder();
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
        // Resolve the owning album so the mini-player can show its cover art and
        // so Next/Previous can step through the album's other previews. Skipping
        // stays in sample mode (StartTrack(sampleMode: _isSampleMode) in
        // Next/Previous), so it can never escalate into a full, unpurchased track.
        CurrentAlbum = _catalog?.GetAlbum(track.AlbumId);
        // Match by Id, not reference: CurrentAlbum is a freshly-fetched instance,
        // so its Track objects differ from the one passed in by the caller.
        _currentIndex = CurrentAlbum?.Tracks.FindIndex(t => t.Id == track.Id) ?? -1;
        RebuildShuffleOrder();
        StartTrack(track, sampleMode: true);
    }

    // Plays a file from disk (user's local library) as full track — bypasses the purchase gate
    // because the file is the user's own. Surfaces it through the same MediaOpened event so the
    // mini-player and Now-Playing UI light up.
    public void PlayFile(string path, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        CurrentAlbum = null;
        RebuildShuffleOrder();
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
            // No real audio — stop whatever was sounding (otherwise the previous
            // track keeps playing under the new track's title) and surface the
            // open so UI updates, but don't try to play.
            _mp.Stop();
            IsPlaying = false;
            _sampleStartMs = 0;
            _sampleEndMs = 0;
            MediaOpened?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        using var media = new Media(_libVlc, new Uri(Path.GetFullPath(path)));
        _mp.Media = media;

        if (sampleMode)
        {
            _sampleStartMs = (long)Math.Max(0, track.SampleStartSeconds) * 1000L;
            _sampleEndMs = _sampleStartMs + SampleLengthSeconds * 1000L;
            _mp.Play();
            if (_sampleStartMs > 0) _mp.Time = _sampleStartMs;
        }
        else
        {
            _sampleStartMs = 0;
            _sampleEndMs = 0;
            _mp.Play();
        }

        PersistLastTrack(track.Id);
        MediaOpened?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (CurrentTrack is null) return;
        if (_mp.IsPlaying)
        {
            _mp.Pause();
            return;
        }
        // Cold "continue where you left off" state (restored on login): the
        // track is surfaced in the UI but no media was loaded yet.
        if (_mp.Media is null)
        {
            StartTrack(CurrentTrack, sampleMode: _isSampleMode);
            return;
        }
        // A stopped/ended sample must relaunch through StartTrack so playback
        // re-enters the 30s window — a raw Play() restarts the file from 0:00,
        // before _sampleStartMs, i.e. outside the preview.
        if (_isSampleMode && _mp.State is VLCState.Stopped or VLCState.Ended)
        {
            StartTrack(CurrentTrack, sampleMode: true);
            return;
        }
        _mp.Play();
    }

    public void Stop()
    {
        _mp.Stop();
        _sampleEndMs = 0;
        // LibVLC raises Stopped asynchronously from its own thread; report the
        // idle state right away so the UI doesn't show "playing" in the gap.
        IsPlaying = false;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        if (CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0) return;
        if (_shuffleMode && _shuffleOrder is { Count: > 0 })
        {
            _shufflePos = (_shufflePos + 1) % _shuffleOrder.Count;
            _currentIndex = _shuffleOrder[_shufflePos];
        }
        else
        {
            _currentIndex = (_currentIndex + 1) % CurrentAlbum.Tracks.Count;
        }
        // Preserve the current mode: skipping a 30s preview steps to the next
        // track's preview, never into a full unpurchased track.
        StartTrack(CurrentAlbum.Tracks[_currentIndex], sampleMode: _isSampleMode);
    }

    public void Previous()
    {
        if (CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0) return;
        if (_shuffleMode && _shuffleOrder is { Count: > 0 })
        {
            _shufflePos = (_shufflePos - 1 + _shuffleOrder.Count) % _shuffleOrder.Count;
            _currentIndex = _shuffleOrder[_shufflePos];
        }
        else
        {
            _currentIndex = (_currentIndex - 1 + CurrentAlbum.Tracks.Count) % CurrentAlbum.Tracks.Count;
        }
        StartTrack(CurrentAlbum.Tracks[_currentIndex], sampleMode: _isSampleMode);
    }

    private void RebuildShuffleOrder()
    {
        if (!_shuffleMode || CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0)
        {
            _shuffleOrder = null;
            _shufflePos = -1;
            return;
        }
        var order = Enumerable.Range(0, CurrentAlbum.Tracks.Count)
            .OrderBy(_ => _shuffleRng.Next())
            .ToList();
        // Anchor the playing track at position 0 so Next() visits every other
        // track before coming back to it.
        if (_currentIndex >= 0 && order.Remove(_currentIndex))
            order.Insert(0, _currentIndex);
        _shuffleOrder = order;
        _shufflePos = 0;
    }

    // "Last track" in play order: the album's final index linearly, or the last
    // slot of the shuffle walk when shuffle is on.
    private bool IsAtEndOfPlayOrder()
    {
        if (CurrentAlbum is null || CurrentAlbum.Tracks.Count == 0) return true;
        if (_shuffleMode && _shuffleOrder is { Count: > 0 })
            return _shufflePos >= _shuffleOrder.Count - 1;
        return _currentIndex >= CurrentAlbum.Tracks.Count - 1;
    }

    public void Seek(TimeSpan position)
    {
        if (CurrentTrack is null) return;
        long ms;
        if (_isSampleMode)
        {
            // Incoming position is sample-relative (0 → 30s); map it back onto the
            // absolute clip window [_sampleStartMs, _sampleEndMs] so scrubbing stays
            // inside the preview instead of jumping to the start of the file.
            var rel = Math.Clamp(position.TotalMilliseconds, 0, SampleLengthSeconds * 1000.0);
            ms = _sampleStartMs + (long)rel;
        }
        else
        {
            ms = (long)Math.Clamp(position.TotalMilliseconds, 0, Duration.TotalMilliseconds);
        }
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

    private void OnEndReached(object? sender, EventArgs e) => OnUi(HandleTrackEnded);

    // internal so the BugHunt harness can exercise the end-of-track decision
    // without waiting for real audio to finish.
    internal void HandleTrackEnded()
    {
        MediaEnded?.Invoke(this, EventArgs.Empty);
        if (_isSampleMode) return;
        switch (_repeatMode)
        {
            case RepeatMode.One when CurrentTrack is not null:
                StartTrack(CurrentTrack, sampleMode: false);
                break;
            case RepeatMode.All:
                Next();
                break;
            case RepeatMode.Off when !IsAtEndOfPlayOrder():
                Next();
                break;
            default:
                // Repeat off at the end of the album: stop instead of wrapping
                // around forever. LibVLC's Ended state never raises Stopped, so
                // report the idle state ourselves.
                IsPlaying = false;
                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
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
                RebuildShuffleOrder();
                // The fields were set directly (no setters → no events); tell
                // the UI so sliders/toggles don't keep the previous user's state.
                VolumeChanged?.Invoke(this, EventArgs.Empty);
                ShuffleModeChanged?.Invoke(this, EventArgs.Empty);
                RepeatModeChanged?.Invoke(this, EventArgs.Empty);
                RestoreLastTrack(s.LastTrackId ?? 0);
            }
            _settingsLoaded = true;
        }
        catch
        {
            _settingsLoaded = false;
        }
    }

    // "Continue where you left off": surface the user's last track in the
    // player paused — audio starts only when they press play. Sample/full mode
    // is re-derived from ownership so a stored preview can't escalate into a
    // full unpurchased track.
    private void RestoreLastTrack(int lastTrackId)
    {
        if (lastTrackId <= 0 || _catalog is null || CurrentTrack is not null) return;
        var album = _catalog.Albums.FirstOrDefault(a => a.Tracks.Any(t => t.Id == lastTrackId));
        var track = album?.Tracks.FirstOrDefault(t => t.Id == lastTrackId);
        if (album is null || track is null) return;
        CurrentAlbum = album;
        _currentIndex = album.Tracks.FindIndex(t => t.Id == lastTrackId);
        _isSampleMode = !IsAlbumPlayable(album.Id);
        CurrentTrack = track;
        RebuildShuffleOrder();
        OnUi(() =>
        {
            MediaOpened?.Invoke(this, EventArgs.Empty);
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        });
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
        if (_volumePersistDebounce.IsEnabled)
        {
            // Flush a pending debounced volume write so closing the app right
            // after a slider drag doesn't lose the setting.
            _volumePersistDebounce.Stop();
            PersistSettings();
        }
        try { _mp.Stop(); } catch { }
        _mp.Dispose();
        _libVlc.Dispose();
    }
}
