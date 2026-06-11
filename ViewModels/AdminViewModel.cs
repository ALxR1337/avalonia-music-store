using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
    [ObservableProperty] private StatsPeriod _activePeriod = StatsPeriod.Month;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private Order? _expandedOrder;

    // True while a preset is rewriting the From/To dates, so the date-change
    // handlers don't bounce the selection back to "Custom".
    private bool _applyingPreset;

    // Chip-tab "active" flags — one per quick period.
    public bool IsPeriodToday => ActivePeriod == StatsPeriod.Today;
    public bool IsPeriodWeek => ActivePeriod == StatsPeriod.Week;
    public bool IsPeriodMonth => ActivePeriod == StatsPeriod.Month;
    public bool IsPeriodQuarter => ActivePeriod == StatsPeriod.Quarter;
    public bool IsPeriodYear => ActivePeriod == StatsPeriod.Year;
    public bool IsPeriodAllTime => ActivePeriod == StatsPeriod.AllTime;
    public bool IsPeriodCustom => ActivePeriod == StatsPeriod.Custom;

    public string PeriodLabel => ActivePeriod switch
    {
        StatsPeriod.AllTime => "За весь час",
        _ => $"{RevenueFrom:dd.MM.yyyy} — {RevenueTo:dd.MM.yyyy}",
    };

    partial void OnActivePeriodChanged(StatsPeriod value)
    {
        OnPropertyChanged(nameof(IsPeriodToday));
        OnPropertyChanged(nameof(IsPeriodWeek));
        OnPropertyChanged(nameof(IsPeriodMonth));
        OnPropertyChanged(nameof(IsPeriodQuarter));
        OnPropertyChanged(nameof(IsPeriodYear));
        OnPropertyChanged(nameof(IsPeriodAllTime));
        OnPropertyChanged(nameof(IsPeriodCustom));
        OnPropertyChanged(nameof(PeriodLabel));
    }

    // Inline product editor (replaces the old modal window). Non-null while the
    // editor panel is shown.
    [ObservableProperty] private ProductEditViewModel? _editingProduct;

    public bool IsEditingProduct => EditingProduct is not null;
    partial void OnEditingProductChanged(ProductEditViewModel? value) =>
        OnPropertyChanged(nameof(IsEditingProduct));

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
        // Revenue counts only fulfilled (Completed) orders, matching RevenueForPeriod and
        // the SalesCount aggregate — New/Processing/Cancelled are not realised income.
        GrossRevenue = Orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalAmount);
    }

    private void RecalcPeriodRevenue()
    {
        var report = _catalog.RevenueForPeriod(RevenueFrom, RevenueTo);
        PeriodRevenue = report.Total;
        PeriodOrderCount = report.OrderCount;
        OnPropertyChanged(nameof(PeriodLabel));
    }

    // Editing either date by hand drops the quick-period selection to "Custom".
    partial void OnRevenueFromChanged(DateTime value)
    {
        if (_applyingPreset) return;
        ActivePeriod = StatsPeriod.Custom;
        RecalcPeriodRevenue();
    }

    partial void OnRevenueToChanged(DateTime value)
    {
        if (_applyingPreset) return;
        ActivePeriod = StatsPeriod.Custom;
        RecalcPeriodRevenue();
    }

    // Picks a quick period (Today/Week/Month/...) and rewrites the From/To range.
    [RelayCommand]
    private void SelectPeriod(string period)
    {
        if (!Enum.TryParse<StatsPeriod>(period, ignoreCase: true, out var p)) return;

        var today = DateTime.UtcNow.Date;
        var (from, to) = p switch
        {
            StatsPeriod.Today   => (today, today),
            StatsPeriod.Week    => (today.AddDays(-6), today),
            StatsPeriod.Month   => (today.AddMonths(-1), today),
            StatsPeriod.Quarter => (today.AddMonths(-3), today),
            StatsPeriod.Year    => (today.AddYears(-1), today),
            StatsPeriod.AllTime => (EarliestOrderDate(), today),
            _ => (RevenueFrom, RevenueTo),
        };

        // Rewrite both dates without tripping the "Custom" fallback, then
        // recompute once against the final range.
        _applyingPreset = true;
        RevenueFrom = from;
        RevenueTo = to;
        _applyingPreset = false;

        ActivePeriod = p;
        RecalcPeriodRevenue();
    }

    private DateTime EarliestOrderDate()
        => Orders.Count > 0 ? Orders.Min(o => o.CreatedAt).Date : DateTime.UtcNow.Date;

    // === Product CRUD ===

    [RelayCommand]
    private void AddProduct() => OpenEditor(existing: null);

    [RelayCommand]
    private void EditProduct(Product? product)
    {
        if (product is null) return;
        OpenEditor(product);
    }

    // Opens the inline editor panel; Save/Cancel close it via CloseRequested.
    private void OpenEditor(Product? existing)
    {
        var vm = new ProductEditViewModel(_catalog, _files, existing);
        vm.CloseRequested += () =>
        {
            var saved = vm.DialogResult;
            EditingProduct = null;
            if (saved)
            {
                StatusMessage = existing is null ? "Товар створено." : $"Товар #{existing.Id} оновлено.";
                Reload();
            }
        };
        StatusMessage = null;
        EditingProduct = vm;
    }

    [RelayCommand]
    private void DeleteProduct(Product? product)
    {
        if (product is null) return;
        _catalog.SetProductActive(product.Id, false);
        StatusMessage = $"Товар #{product.Id} деактивовано.";
        Reload();
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
}

/// <summary>Quick revenue periods for the statistics tab.</summary>
public enum StatsPeriod
{
    Today,
    Week,
    Month,
    Quarter,
    Year,
    AllTime,
    Custom,
}
