using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class SearchResultsView : UserControl
{
    private ScrollViewer? _outer;

    public SearchResultsView() => InitializeComponent();

    // The window's content ScrollViewer measures pages with infinite height, so
    // the facet sidebar could never stay put while results scroll. Locking the
    // page to the outer viewport's height moves scrolling into ResultsScroll
    // and keeps the filters permanently in view.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _outer = this.FindAncestorOfType<ScrollViewer>();
        if (_outer is not null)
        {
            _outer.PropertyChanged += OnOuterPropertyChanged;
            UpdateHeight();
        }

        // Restore the results-column position after layout settles (the VM
        // survives in nav history; the view is rebuilt on back/forward).
        if (DataContext is SearchResultsViewModel vm && vm.SavedScrollOffset > 0)
        {
            var offset = vm.SavedScrollOffset;
            Dispatcher.UIThread.Post(
                () => ResultsScroll.Offset = new Vector(0, offset),
                DispatcherPriority.Loaded);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is SearchResultsViewModel vm)
            vm.SavedScrollOffset = ResultsScroll.Offset.Y;
        if (_outer is not null)
        {
            _outer.PropertyChanged -= OnOuterPropertyChanged;
            _outer = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOuterPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ViewportProperty) UpdateHeight();
    }

    private void UpdateHeight()
    {
        if (_outer is null) return;
        var h = _outer.Viewport.Height - Root.Margin.Top - Root.Margin.Bottom;
        Root.Height = h > 0 ? h : double.NaN;
    }
}
