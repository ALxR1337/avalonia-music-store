using System;
using System.Collections.ObjectModel;
using MusicApp.Models;

namespace MusicApp.Services;

public interface ICartService
{
    event EventHandler? CartChanged;

    ObservableCollection<CartItem> Items { get; }
    int ItemCount { get; }
    decimal Total { get; }

    void Add(Product product, int quantity = 1);
    void Remove(CartItem item);
    void UpdateQuantity(CartItem item, int quantity);
    void Clear();
    Order Checkout(string? shippingAddress = null, string? comment = null);
}
