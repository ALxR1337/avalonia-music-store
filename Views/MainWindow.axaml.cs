using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tunneling: we get the key before any focused control (e.g. an icon
        // Button that still has focus from the last click) can act on it.
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Escape closes the fullscreen cover overlay first — anything else
        // (modal-like) can chain in here later.
        if (e.Key == Key.Escape && vm.IsCoverFullscreen)
        {
            vm.CloseCoverFullscreenCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space) return;

        // Don't steal the space character from text inputs.
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        vm.TogglePlayPauseCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCoverBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.CloseCoverFullscreenCommand.Execute(null);
    }

    // Swallow clicks on the image itself so they don't bubble to the backdrop
    // and close the overlay.
    private void OnCoverImagePressed(object? sender, PointerPressedEventArgs e)
        => e.Handled = true;

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is MainWindowViewModel vm)
            vm.SubmitSearchCommand.Execute(null);
    }

    // Collapse sidebar to a 90px icon column when the window is narrower than 1376px.
    private void OnRootSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsSidebarCollapsed = e.NewSize.Width < 1376;
    }
}
