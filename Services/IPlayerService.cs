using System;
using MusicApp.Models;

namespace MusicApp.Services;

public interface IPlayerService
{
    event EventHandler? MediaOpened;
    event EventHandler? MediaEnded;
    event EventHandler? PositionChanged;
    event EventHandler? PlaybackStateChanged;

    Track? CurrentTrack { get; }
    Album? CurrentAlbum { get; }
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    double Volume { get; set; }
    bool ShuffleMode { get; set; }
    RepeatMode RepeatMode { get; set; }

    void PlayAlbum(Album album, int startTrackIndex = 0);
    void PlaySample(Track track);
    void TogglePlayPause();
    void Stop();
    void Next();
    void Previous();
    void Seek(TimeSpan position);
}
