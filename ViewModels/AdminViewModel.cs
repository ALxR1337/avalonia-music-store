using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class AdminViewModel : ViewModelBase
{
    [ObservableProperty] private int _totalProducts;
    [ObservableProperty] private int _totalOrders;
    [ObservableProperty] private decimal _grossRevenue;

    public AdminViewModel(ICatalogService catalog)
    {
        Products = new ObservableCollection<Product>(catalog.Products);
        Orders = new ObservableCollection<Order>(catalog.Orders);
        TopSelling = new ObservableCollection<Product>(catalog.Products
            .OrderByDescending(p => p.SalesCount).Take(10));

        TotalProducts = Products.Count;
        TotalOrders = Orders.Count;
        GrossRevenue = Orders.Sum(o => o.TotalAmount);
    }

    public ObservableCollection<Product> Products { get; }
    public ObservableCollection<Order> Orders { get; }
    public ObservableCollection<Product> TopSelling { get; }
}
