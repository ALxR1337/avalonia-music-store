using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
        // Focus the first password field when the inline panel opens — the
        // command only flips a flag, so the view owns the focus side effect.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ProfileViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileViewModel.IsPasswordPanelOpen)
            && sender is ProfileViewModel { IsPasswordPanelOpen: true })
            Dispatcher.UIThread.Post(() => PasswordPanel.FocusFirstField());
    }
}
