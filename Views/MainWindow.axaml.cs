using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    // True while we programmatically restore a scroll position, so the
    // ScrollChanged events that fires during page re-layout don't overwrite
    // the remembered offset with a transient 0.
    private bool _restoring;

    public MainWindow()
    {
        InitializeComponent();
        // Tunneling: we get the key before any focused control (e.g. an icon
        // Button that still has focus from the last click) can act on it.
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);

        ContentScroll.ScrollChanged += OnContentScrolled;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.RestoreScroll -= OnRestoreScroll;
        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null) _vm.RestoreScroll += OnRestoreScroll;
    }

    // Remember the live scroll position so back/forward can return to it.
    private void OnContentScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_restoring) return;
        _vm?.NotifyScroll(ContentScroll.Offset.Y);
    }

    // Apply the offset the navigation settled on (0 for fresh pages). Deferred
    // to Loaded priority so the new page has measured/arranged first — otherwise
    // the offset clamps against a stale extent.
    private void OnRestoreScroll(double offsetY)
    {
        _restoring = true;
        Dispatcher.UIThread.Post(() =>
        {
            ContentScroll.Offset = ContentScroll.Offset.WithY(offsetY);
            _vm?.NotifyScroll(offsetY);
            _restoring = false;
        }, DispatcherPriority.Loaded);
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Login overlay is the top-most modal: Esc dismisses it. Returning early
        // for any other key keeps the global Space play/pause hotkey (below) from
        // firing behind the modal, while still letting Space/Enter/typing fall
        // through to the card's TextBoxes (this is the tunnel pass — unmarked
        // keys continue to the focused control).
        if (vm.IsLoginVisible)
        {
            if (e.Key == Key.Escape)
            {
                vm.CloseLoginCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        // Escape closes the fullscreen cover overlay first — anything else
        // (modal-like) can chain in here later.
        if (e.Key == Key.Escape && vm.IsCoverFullscreen)
        {
            vm.CloseCoverFullscreenCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Hardware media keys: act wherever focus is, but only when a track
        // is loaded — otherwise let them pass through untouched.
        switch (e.Key)
        {
            case Key.MediaPlayPause when vm.HasLoadedTrack:
                vm.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.MediaNextTrack when vm.HasLoadedTrack:
                vm.MiniPlayer?.NextCommand.Execute(null);
                e.Handled = true;
                return;
            case Key.MediaPreviousTrack when vm.HasLoadedTrack:
                vm.MiniPlayer?.PreviousCommand.Execute(null);
                e.Handled = true;
                return;
        }

        if (e.Key != Key.Space) return;

        // Don't steal the space character from text inputs.
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        // Nothing loaded → nothing to toggle: let Space reach the focused
        // control instead of dying in a no-op. Once playback starts, the
        // Spotify-style global pause deliberately wins over focused buttons
        // (see Space_toggles_play_pause_globally_when_no_textbox_focused).
        if (!vm.HasLoadedTrack) return;

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

    private void OnLoginBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.CloseLoginCommand.Execute(null);
    }

    // Clicks on the card must not bubble to the backdrop and dismiss the overlay.
    private void OnLoginCardPressed(object? sender, PointerPressedEventArgs e)
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
        if (DataContext is not MainWindowViewModel vm) return;
        switch (e.Key)
        {
            case Key.Enter:
                vm.SubmitOrPickHighlighted();
                e.Handled = true;
                break;
            case Key.Down when vm.IsAutocompleteOpen:
                vm.MoveSuggestionHighlight(1);
                e.Handled = true;
                break;
            case Key.Up when vm.IsAutocompleteOpen:
                vm.MoveSuggestionHighlight(-1);
                e.Handled = true;
                break;
            case Key.Escape when vm.IsAutocompleteOpen:
                vm.CloseAutocomplete();
                e.Handled = true;
                break;
            case Key.Escape when !string.IsNullOrEmpty(vm.SearchQuery):
                vm.ClearSearchCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnSearchGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OnSearchBoxFocused();
    }

    private void OnSearchClear(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ClearSearchCommand.Execute(null);
        SearchBox.Focus();
    }

    // Collapse sidebar to a 72px icon column when the window is narrower than 1100px.
    private void OnRootSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsSidebarCollapsed = e.NewSize.Width < 1100;
    }
}
