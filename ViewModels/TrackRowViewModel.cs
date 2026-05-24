using CommunityToolkit.Mvvm.ComponentModel;
using MusicApp.Models;

namespace MusicApp.ViewModels;

public partial class TrackRowViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLiked;
    [ObservableProperty] private bool _isCurrent;

    public TrackRowViewModel(Track track, int index)
    {
        Track = track;
        Index = index;
    }

    public Track Track { get; }
    public int Index { get; }
    public int Position => Track.Position > 0 ? Track.Position : Index + 1;
    public string Title => Track.Title;
    public string DurationDisplay => Track.DurationDisplay;
}
