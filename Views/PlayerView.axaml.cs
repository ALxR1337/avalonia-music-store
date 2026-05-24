using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class PlayerView : UserControl
{
    public PlayerView() => InitializeComponent();

    private void OnSeekPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm) vm.IsScrubbing = true;
    }

    // Bound to both PointerReleased and PointerCaptureLost — whichever fires first
    // ends the drag and commits the seek. RoutedEventArgs is the common base so a
    // single handler signature works for both events.
    private void OnSeekPointerReleased(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm && sender is Slider slider)
        {
            vm.IsScrubbing = false;
            vm.CommitSeek(slider.Value);
        }
    }
}
