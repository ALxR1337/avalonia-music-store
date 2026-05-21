using System.Collections.ObjectModel;
using System.Linq;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public class OrdersViewModel : ViewModelBase
{
    public OrdersViewModel(ICatalogService catalog, IAuthService auth)
    {
        var userId = auth.CurrentUser?.Id ?? 0;
        var rows = auth.IsAdmin
            ? catalog.Orders
            : catalog.GetOrdersFor(userId);
        Orders = new ObservableCollection<Order>(rows.OrderByDescending(o => o.CreatedAt));
    }

    public ObservableCollection<Order> Orders { get; }
}
