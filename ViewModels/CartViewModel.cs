using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class CartViewModel : ViewModelBase
{
    private readonly ICartService _cart;
    private readonly INavigationService _nav;

    [ObservableProperty] private decimal _total;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string? _flashMessage;

    public CartViewModel(ICartService cart, INavigationService nav)
    {
        _cart = cart;
        _nav = nav;
        Items = cart.Items;
        cart.CartChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<CartItem> Items { get; }

    private void Refresh()
    {
        Total = _cart.Total;
        ItemCount = _cart.ItemCount;
        IsEmpty = _cart.Items.Count == 0;
    }

    [RelayCommand]
    private void Increase(CartItem item) => _cart.UpdateQuantity(item, item.Quantity + 1);

    [RelayCommand]
    private void Decrease(CartItem item) => _cart.UpdateQuantity(item, item.Quantity - 1);

    [RelayCommand]
    private void Remove(CartItem item) => _cart.Remove(item);

    [RelayCommand]
    private void Checkout()
    {
        if (_cart.Items.Count == 0) return;
        var order = _cart.Checkout();
        FlashMessage = $"Замовлення створено. Сума: {order.TotalAmount:0} ₴";
        _nav.NavigateTo(NavTarget.Orders);
    }
}
