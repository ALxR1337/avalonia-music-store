using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class OrdersViewModel : ViewModelBase
{
    [ObservableProperty] private Order? _expandedOrder;

    public OrdersViewModel(ICatalogService catalog, IAuthService auth)
    {
        var userId = auth.CurrentUser?.Id ?? 0;
        var rows = auth.IsAdmin
            ? catalog.Orders
            : catalog.GetOrdersFor(userId);
        Orders = new ObservableCollection<Order>(rows.OrderByDescending(o => o.CreatedAt));
    }

    public ObservableCollection<Order> Orders { get; }

    [RelayCommand]
    private void ToggleDetails(Order? order)
    {
        ExpandedOrder = ExpandedOrder?.Id == order?.Id ? null : order;
    }
}
