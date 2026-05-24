using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class ProductEditWindow : Window
{
    public ProductEditWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ProductEditViewModel vm)
            vm.CloseRequested += () => Close(vm.DialogResult);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
