using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MusicApp.Views;

public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => HookViewportPinning();
        // Leaving the page (or closing the window) must not leave the toast's
        // 5-second auto-hide timer running against a dead visual tree.
        DetachedFromVisualTree += (_, _) =>
            (DataContext as ViewModels.AdminViewModel)?.DismissStatusCommand.Execute(null);
    }

    // The page scrolls as a whole (MainWindow's ContentScroll), so a
    // Bottom-aligned child sits at the bottom of the *content*, which on long
    // tables is far below the fold. Translate the toast so it stays pinned to
    // the bottom of the visible viewport instead.
    private void HookViewportPinning()
    {
        var scroll = this.FindAncestorOfType<ScrollViewer>();
        if (scroll is null) return;

        void Update()
        {
            var toast = this.FindControl<Border>("StatusToast");
            if (toast is null) return;
            var delta = scroll.Offset.Y + scroll.Viewport.Height - Bounds.Height;
            toast.RenderTransform = new TranslateTransform(0, Math.Min(0, delta));
        }

        scroll.ScrollChanged += (_, _) => Update();
        SizeChanged += (_, _) => Update();
        Update();
    }
}
