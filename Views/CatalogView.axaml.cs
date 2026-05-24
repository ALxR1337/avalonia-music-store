using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MusicApp.Views;

public partial class CatalogView : UserControl
{
    // Roughly three tiles' worth (160 + 14 margin) — feels natural on a click.
    private const double GenreScrollStep = 522;
    private const double ScrollAnimMs = 350;

    private DispatcherTimer? _genreAnimTimer;
    private double _genreAnimStart;
    private double _genreAnimTarget;
    private DateTime _genreAnimStartedAt;

    public CatalogView()
    {
        InitializeComponent();
        GenreScroller.ScrollChanged += (_, _) => UpdateGenreChevronVisibility();
        GenreScroller.LayoutUpdated += (_, _) => UpdateGenreChevronVisibility();
        Loaded += (_, _) => UpdateGenreChevronVisibility();
    }

    private void OnGenreScrollLeft(object? sender, RoutedEventArgs e)
        => AnimateScrollBy(GenreScroller, -GenreScrollStep);

    private void OnGenreScrollRight(object? sender, RoutedEventArgs e)
        => AnimateScrollBy(GenreScroller, GenreScrollStep);

    private void UpdateGenreChevronVisibility()
    {
        if (GenreScroller is not { } sv) return;
        var maxX = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        var canLeft = sv.Offset.X > 0.5;
        var canRight = sv.Offset.X < maxX - 0.5;
        GenreScrollLeft.IsVisible = canLeft;
        GenreScrollRight.IsVisible = canRight;
        ToggleClass(GenreFadeLeft, "show", canLeft);
        ToggleClass(GenreFadeRight, "show", canRight);
    }

    private static void ToggleClass(Control? c, string name, bool on)
    {
        if (c is null) return;
        if (on) { if (!c.Classes.Contains(name)) c.Classes.Add(name); }
        else { c.Classes.Remove(name); }
    }

    private void AnimateScrollBy(ScrollViewer? sv, double dx)
    {
        if (sv is null) return;
        var max = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
        _genreAnimStart = sv.Offset.X;
        _genreAnimTarget = Math.Clamp(sv.Offset.X + dx, 0, max);
        _genreAnimStartedAt = DateTime.UtcNow;

        _genreAnimTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _genreAnimTimer.Tick -= OnGenreAnimTick;
        _genreAnimTimer.Tick += OnGenreAnimTick;
        _genreAnimTimer.Start();
    }

    private void OnGenreAnimTick(object? sender, EventArgs e)
    {
        if (GenreScroller is not { } sv) { _genreAnimTimer?.Stop(); return; }
        var elapsed = (DateTime.UtcNow - _genreAnimStartedAt).TotalMilliseconds;
        var t = Math.Clamp(elapsed / ScrollAnimMs, 0.0, 1.0);
        // ease-out cubic — fast at start, settles smoothly
        var eased = 1.0 - Math.Pow(1.0 - t, 3);
        var x = _genreAnimStart + (_genreAnimTarget - _genreAnimStart) * eased;
        sv.Offset = new Vector(x, sv.Offset.Y);
        if (t >= 1.0) _genreAnimTimer?.Stop();
    }
}
