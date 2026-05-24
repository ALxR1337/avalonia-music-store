using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class AdminViewModel : ViewModelBase
{
    private readonly ICatalogService _catalog;
    private readonly IFileDialogService _files;

    [ObservableProperty] private int _totalProducts;
    [ObservableProperty] private int _totalOrders;
    [ObservableProperty] private decimal _grossRevenue;
    [ObservableProperty] private DateTime _revenueFrom = DateTime.UtcNow.AddMonths(-1).Date;
    [ObservableProperty] private DateTime _revenueTo = DateTime.UtcNow.Date;
    [ObservableProperty] private decimal _periodRevenue;
    [ObservableProperty] private int _periodOrderCount;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private Order? _expandedOrder;

    public AdminViewModel(ICatalogService catalog, IFileDialogService files)
    {
        _catalog = catalog;
        _files = files;

        Products = new ObservableCollection<Product>();
        Orders = new ObservableCollection<Order>();
        TopSelling = new ObservableCollection<Product>();
        Users = new ObservableCollection<User>();

        Reload();
        RecalcPeriodRevenue();
    }

    public ObservableCollection<Product> Products { get; }
    public ObservableCollection<Order> Orders { get; }
    public ObservableCollection<Product> TopSelling { get; }
    public ObservableCollection<User> Users { get; }
    public IReadOnlyList<OrderStatus> OrderStatuses { get; } = Enum.GetValues<OrderStatus>();
    public IReadOnlyList<UserRole> UserRoles { get; } = Enum.GetValues<UserRole>();

    private void Reload()
    {
        Products.Clear();
        foreach (var p in _catalog.Products) Products.Add(p);

        Orders.Clear();
        foreach (var o in _catalog.Orders) Orders.Add(o);

        TopSelling.Clear();
        foreach (var p in _catalog.Products.OrderByDescending(p => p.SalesCount).Take(10)) TopSelling.Add(p);

        Users.Clear();
        foreach (var u in _catalog.GetUsers()) Users.Add(u);

        TotalProducts = Products.Count;
        TotalOrders = Orders.Count;
        GrossRevenue = Orders.Sum(o => o.TotalAmount);
    }

    private void RecalcPeriodRevenue()
    {
        var report = _catalog.RevenueForPeriod(RevenueFrom, RevenueTo);
        PeriodRevenue = report.Total;
        PeriodOrderCount = report.OrderCount;
    }

    partial void OnRevenueFromChanged(DateTime value) => RecalcPeriodRevenue();
    partial void OnRevenueToChanged(DateTime value) => RecalcPeriodRevenue();

    // === Product CRUD ===

    [RelayCommand]
    private async Task AddProductAsync() => await ShowProductDialog(existing: null);

    [RelayCommand]
    private async Task EditProductAsync(Product? product)
    {
        if (product is null) return;
        await ShowProductDialog(product);
    }

    [RelayCommand]
    private void DeleteProduct(Product? product)
    {
        if (product is null) return;
        _catalog.SetProductActive(product.Id, false);
        StatusMessage = $"Товар #{product.Id} деактивовано.";
        Reload();
    }

    private async Task ShowProductDialog(Product? existing)
    {
        var owner = OwnerWindow();
        if (owner is null) return;

        var vm = new ProductEditViewModel(_catalog, _files, existing);
        var window = new Views.ProductEditWindow { DataContext = vm };
        var result = await window.ShowDialog<bool?>(owner);

        if (result == true)
        {
            StatusMessage = existing is null ? "Товар створено." : $"Товар #{existing.Id} оновлено.";
            Reload();
        }
    }

    // === Order ops ===

    [RelayCommand]
    private void ChangeOrderStatus(Order? order)
    {
        if (order is null) return;
        _catalog.UpdateOrderStatus(order.Id, order.Status);
        StatusMessage = $"Статус замовлення #{order.Id} оновлено: {order.Status}.";
    }

    [RelayCommand]
    private void ToggleOrderDetails(Order? order)
    {
        ExpandedOrder = ExpandedOrder?.Id == order?.Id ? null : order;
    }

    // === User ops ===

    [RelayCommand]
    private void ChangeUserRole(User? user)
    {
        if (user is null) return;
        _catalog.SetUserRole(user.Id, user.Role);
        StatusMessage = $"Користувача #{user.Id} переведено в роль {user.Role}.";
    }

    // === Exports ===

    [RelayCommand]
    private async Task ExportOrdersExcelAsync()
    {
        var path = await _files.SaveFileAsync("Зберегти Excel",
            $"orders_{DateTime.UtcNow:yyyyMMdd}.xlsx",
            new[] { new FileFilter("Excel", new[] { "*.xlsx" }) });
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            _catalog.ExportOrdersToExcel(path);
            StatusMessage = $"Замовлення експортовано: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не вдалось зберегти Excel: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportProductsCsvAsync()
    {
        var path = await _files.SaveFileAsync("Зберегти CSV",
            $"products_{DateTime.UtcNow:yyyyMMdd}.csv",
            new[] { new FileFilter("CSV", new[] { "*.csv" }) });
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            _catalog.ExportProductsToCsv(path);
            StatusMessage = $"Товари експортовано: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не вдалось зберегти CSV: {ex.Message}";
        }
    }

    private static Window? OwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
