using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ChangePasswordViewModel vm)
            vm.RequestClose += ok => Close(ok);
    }
}
