using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class MiniPlayerView : UserControl
{
    public MiniPlayerView() => InitializeComponent();

    private void OnSeekPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MiniPlayerViewModel vm) vm.IsScrubbing = true;
    }

    private void OnSeekPointerReleased(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MiniPlayerViewModel vm && sender is Slider slider)
        {
            vm.IsScrubbing = false;
            vm.CommitSeek(slider.Value);
        }
    }
}
