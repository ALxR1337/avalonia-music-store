using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MusicApp.Views;

public partial class CatalogView : UserControl
{
    private readonly ScrollRow _genreRow;
    private readonly ScrollRow _artistRow;
    private readonly ScrollRow _newArrivalsRow;

    public CatalogView()
    {
        InitializeComponent();
        _genreRow = new ScrollRow(GenreScroller, GenreScrollLeft, GenreScrollRight, GenreFadeLeft, GenreFadeRight);
        _artistRow = new ScrollRow(ArtistScroller, ArtistScrollLeft, ArtistScrollRight, ArtistFadeLeft, ArtistFadeRight);
        _newArrivalsRow = new ScrollRow(NewArrivalsScroller, NewArrivalsScrollLeft, NewArrivalsScrollRight, NewArrivalsFadeLeft, NewArrivalsFadeRight);
        Loaded += (_, _) =>
        {
            _genreRow.Refresh();
            _artistRow.Refresh();
            _newArrivalsRow.Refresh();
        };
    }

    private void OnGenreScrollLeft(object? sender, RoutedEventArgs e) => _genreRow.ScrollByPage(-1);
    private void OnGenreScrollRight(object? sender, RoutedEventArgs e) => _genreRow.ScrollByPage(1);
    private void OnArtistScrollLeft(object? sender, RoutedEventArgs e) => _artistRow.ScrollByPage(-1);
    private void OnArtistScrollRight(object? sender, RoutedEventArgs e) => _artistRow.ScrollByPage(1);
    private void OnNewArrivalsScrollLeft(object? sender, RoutedEventArgs e) => _newArrivalsRow.ScrollByPage(-1);
    private void OnNewArrivalsScrollRight(object? sender, RoutedEventArgs e) => _newArrivalsRow.ScrollByPage(1);

    /// <summary>
    /// A horizontal carousel: chevron buttons + edge-fade vignettes whose
    /// visibility tracks scroll position, with an ease-out animated jump per click.
    /// Shared by the "Перегляд за жанрами" and "Виконавці" rows.
    /// </summary>
    private sealed class ScrollRow
    {
        // Roughly three genre tiles' worth (200 + 14 margin) — feels natural on a click.
        private const double ScrollStep = 642;
        private const double ScrollAnimMs = 350;

        private readonly ScrollViewer _sv;
        private readonly Button _left;
        private readonly Button _right;
        private readonly Control _fadeLeft;
        private readonly Control _fadeRight;

        private DispatcherTimer? _animTimer;
        private double _animStart;
        private double _animTarget;
        private DateTime _animStartedAt;

        public ScrollRow(ScrollViewer sv, Button left, Button right, Control fadeLeft, Control fadeRight)
        {
            _sv = sv;
            _left = left;
            _right = right;
            _fadeLeft = fadeLeft;
            _fadeRight = fadeRight;

            _sv.ScrollChanged += (_, _) => Refresh();
            _sv.LayoutUpdated += (_, _) => Refresh();
            Refresh();
        }

        public void ScrollByPage(int direction) => AnimateScrollBy(direction * ScrollStep);

        public void Refresh()
        {
            var maxX = Math.Max(0, _sv.Extent.Width - _sv.Viewport.Width);
            var canLeft = _sv.Offset.X > 0.5;
            var canRight = _sv.Offset.X < maxX - 0.5;
            _left.IsVisible = canLeft;
            _right.IsVisible = canRight;
            ToggleClass(_fadeLeft, "show", canLeft);
            ToggleClass(_fadeRight, "show", canRight);
        }

        private void AnimateScrollBy(double dx)
        {
            var max = Math.Max(0, _sv.Extent.Width - _sv.Viewport.Width);
            _animStart = _sv.Offset.X;
            _animTarget = Math.Clamp(_sv.Offset.X + dx, 0, max);
            _animStartedAt = DateTime.UtcNow;

            _animTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _animTimer.Tick -= OnAnimTick;
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - _animStartedAt).TotalMilliseconds;
            var t = Math.Clamp(elapsed / ScrollAnimMs, 0.0, 1.0);
            // ease-out cubic — fast at start, settles smoothly
            var eased = 1.0 - Math.Pow(1.0 - t, 3);
            var x = _animStart + (_animTarget - _animStart) * eased;
            _sv.Offset = new Vector(x, _sv.Offset.Y);
            if (t >= 1.0) _animTimer?.Stop();
        }

        private static void ToggleClass(Control? c, string name, bool on)
        {
            if (c is null) return;
            if (on) { if (!c.Classes.Contains(name)) c.Classes.Add(name); }
            else { c.Classes.Remove(name); }
        }
    }
}
