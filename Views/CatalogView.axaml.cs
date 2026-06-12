using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MusicApp.Views;

public partial class CatalogView : UserControl
{
    private readonly ScrollRow _genreRow;
    private readonly ScrollRow _artistRow;
    private readonly ScrollRow _newArrivalsRow;

    // Tile stride per shelf = tile width + right margin (genre 200+14,
    // artist 150+18, arrival card 200+14). Paging by whole strides keeps
    // tiles aligned to the row's left edge after every chevron click.
    private const double GenreStride = 214;
    private const double ArtistStride = 168;
    private const double ArrivalStride = 214;

    public CatalogView()
    {
        InitializeComponent();
        _genreRow = new ScrollRow(GenreScroller, GenreScrollLeft, GenreScrollRight, GenreFadeLeft, GenreFadeRight, GenreStride);
        _artistRow = new ScrollRow(ArtistScroller, ArtistScrollLeft, ArtistScrollRight, ArtistFadeLeft, ArtistFadeRight, ArtistStride);
        _newArrivalsRow = new ScrollRow(NewArrivalsScroller, NewArrivalsScrollLeft, NewArrivalsScrollRight, NewArrivalsFadeLeft, NewArrivalsFadeRight, ArrivalStride);
        Loaded += (_, _) =>
        {
            _genreRow.Refresh();
            _artistRow.Refresh();
            _newArrivalsRow.Refresh();
        };
        AttachedToVisualTree += (_, _) => HookToastViewportPinning();
        // Leaving the page must not leave the toast's auto-hide timer running
        // against a dead visual tree.
        DetachedFromVisualTree += (_, _) =>
            (DataContext as ViewModels.CatalogViewModel)?.DismissToastCommand.Execute(null);
    }

    // The page scrolls as a whole (MainWindow's ContentScroll), so a
    // Bottom-aligned child sits at the bottom of the *content* — far below the
    // fold. Translate the toast so it stays pinned to the visible viewport.
    private void HookToastViewportPinning()
    {
        var scroll = this.FindAncestorOfType<ScrollViewer>();
        if (scroll is null) return;

        void Update()
        {
            var toast = this.FindControl<Border>("CatalogToast");
            if (toast is null) return;
            var delta = scroll.Offset.Y + scroll.Viewport.Height - Bounds.Height;
            toast.RenderTransform = new TranslateTransform(0, Math.Min(0, delta));
        }

        scroll.ScrollChanged += (_, _) => Update();
        SizeChanged += (_, _) => Update();
        Update();
    }

    // Public so BugHunt can pin the paging math (the animation itself runs on
    // a DispatcherTimer, which never ticks under Avalonia.Headless).
    public static double WholeTilePageStep(double viewportWidth, double tileStride) =>
        Math.Max(1, Math.Floor(viewportWidth / tileStride)) * tileStride;

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
        private const double ScrollAnimMs = 350;

        private readonly ScrollViewer _sv;
        private readonly Button _left;
        private readonly Button _right;
        private readonly Control _fadeLeft;
        private readonly Control _fadeRight;
        private readonly double _tileStride;

        private DispatcherTimer? _animTimer;
        private double _animStart;
        private double _animTarget;
        private DateTime _animStartedAt;

        public ScrollRow(ScrollViewer sv, Button left, Button right, Control fadeLeft, Control fadeRight, double tileStride)
        {
            _sv = sv;
            _left = left;
            _right = right;
            _fadeLeft = fadeLeft;
            _fadeRight = fadeRight;
            _tileStride = tileStride;

            _sv.ScrollChanged += (_, _) => Refresh();
            _sv.LayoutUpdated += (_, _) => Refresh();
            Refresh();
        }

        // One click pages by as many *whole* tiles as fit the viewport, so the
        // row lands tile-aligned instead of cutting a card at the edge.
        public void ScrollByPage(int direction) =>
            AnimateScrollBy(direction * WholeTilePageStep(_sv.Viewport.Width, _tileStride));

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
