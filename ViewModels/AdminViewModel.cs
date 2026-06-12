using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicApp.Models;
using MusicApp.Services;

namespace MusicApp.ViewModels;

public partial class AdminViewModel : ViewModelBase
{
    private readonly ICatalogService _catalog;
    private readonly IFileDialogService _files;
    private readonly IAuthService _auth;
    private readonly DispatcherTimer _statusTimer;

    // Full unfiltered data; the bindable collections below hold the filtered view.
    private List<AdminProductRow> _allProducts = new();
    private List<AdminOrderRow> _allOrders = new();
    private List<AdminUserRow> _allUsers = new();

    public AdminViewModel(ICatalogService catalog, IFileDialogService files, IAuthService auth)
    {
        _catalog = catalog;
        _files = files;
        _auth = auth;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusMessage = null; };

        Reload();
        RecalcPeriodRevenue();
    }

    // === Sections ===

    [ObservableProperty] private AdminSection _activeSection = AdminSection.Overview;

    public bool IsSectionOverview => ActiveSection == AdminSection.Overview;
    public bool IsSectionProducts => ActiveSection == AdminSection.Products;
    public bool IsSectionOrders => ActiveSection == AdminSection.Orders;
    public bool IsSectionUsers => ActiveSection == AdminSection.Users;

    partial void OnActiveSectionChanged(AdminSection value)
    {
        OnPropertyChanged(nameof(IsSectionOverview));
        OnPropertyChanged(nameof(IsSectionProducts));
        OnPropertyChanged(nameof(IsSectionOrders));
        OnPropertyChanged(nameof(IsSectionUsers));
    }

    [RelayCommand]
    private void SelectSection(string section)
    {
        if (Enum.TryParse<AdminSection>(section, ignoreCase: true, out var s))
            ActiveSection = s;
    }

    // === KPI ===

    [ObservableProperty] private int _totalProducts;
    [ObservableProperty] private int _totalOrders;
    [ObservableProperty] private int _newOrdersCount;
    [ObservableProperty] private decimal _grossRevenue;

    // KPI cards double as navigation: each one opens the section (and filter)
    // that explains its number.
    [RelayCommand]
    private void OpenKpi(string target)
    {
        switch (target)
        {
            case "Products":
                ActiveSection = AdminSection.Products;
                break;
            case "Orders":
                OrderFilter = null;
                ActiveSection = AdminSection.Orders;
                break;
            case "NewOrders":
                OrderFilter = OrderStatus.New;
                ActiveSection = AdminSection.Orders;
                break;
            case "Revenue":
                OrderFilter = OrderStatus.Completed;
                ActiveSection = AdminSection.Orders;
                break;
        }
    }

    // === Status toast ===
    // Success messages auto-hide after 5 s; errors stay until dismissed so the
    // admin can actually read what went wrong.

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isStatusError;

    private void ShowStatus(string message, bool isError = false)
    {
        IsStatusError = isError;
        StatusMessage = message;
        _statusTimer.Stop();
        if (!isError) _statusTimer.Start();
    }

    [RelayCommand]
    private void DismissStatus()
    {
        _statusTimer.Stop();
        StatusMessage = null;
    }

    // === Products (search + state filter + CRUD) ===

    public ObservableCollection<AdminProductRow> Products { get; } = new();

    [ObservableProperty] private string _productSearch = string.Empty;
    [ObservableProperty] private string _productCountLabel = string.Empty;
    [ObservableProperty] private bool _hasNoProducts;
    [ObservableProperty] private ProductStateFilter _productState = ProductStateFilter.All;

    public bool IsProductFilterAll => ProductState == ProductStateFilter.All;
    public bool IsProductFilterActive => ProductState == ProductStateFilter.Active;
    public bool IsProductFilterInactive => ProductState == ProductStateFilter.Inactive;

    partial void OnProductSearchChanged(string value) => ApplyProductFilter();

    partial void OnProductStateChanged(ProductStateFilter value)
    {
        OnPropertyChanged(nameof(IsProductFilterAll));
        OnPropertyChanged(nameof(IsProductFilterActive));
        OnPropertyChanged(nameof(IsProductFilterInactive));
        ApplyProductFilter();
    }

    [RelayCommand]
    private void FilterProducts(string filter)
    {
        if (Enum.TryParse<ProductStateFilter>(filter, ignoreCase: true, out var f))
            ProductState = f;
    }

    private void ApplyProductFilter()
    {
        var query = ProductSearch.Trim();
        IEnumerable<AdminProductRow> source = _allProducts;

        if (ProductState == ProductStateFilter.Active)
            source = source.Where(r => r.Product.IsActive);
        else if (ProductState == ProductStateFilter.Inactive)
            source = source.Where(r => !r.Product.IsActive);

        if (query.Length > 0)
            source = source.Where(r =>
                Matches(r.Product.Album?.Title) || Matches(r.Product.Album?.Artist?.Name) || Matches(r.Product.Label));

        Products.Clear();
        foreach (var r in source) Products.Add(r);

        HasNoProducts = Products.Count == 0;

        var inactive = _allProducts.Count(r => !r.Product.IsActive);
        ProductCountLabel = query.Length > 0 || ProductState != ProductStateFilter.All
            ? $"Знайдено: {Products.Count} із {_allProducts.Count}"
            : Plural(Products.Count, "товар", "товари", "товарів")
              + (inactive > 0 ? $" · {Plural(inactive, "неактивний", "неактивні", "неактивних")}" : "");

        bool Matches(string? text) =>
            text?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    [RelayCommand]
    private void ClearProductSearch() => ProductSearch = string.Empty;

    // Empty-state escape hatch: one click back to the full list.
    [RelayCommand]
    private void ResetProductFilters()
    {
        ProductSearch = string.Empty;
        ProductState = ProductStateFilter.All;
    }

    [RelayCommand]
    private void AddProduct() => OpenEditor(existing: null);

    [RelayCommand]
    private void EditProduct(AdminProductRow? row)
    {
        if (row is null) return;
        OpenEditor(row.Product);
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
                Reload();
                ShowStatus(existing is null ? "Товар створено." : $"Товар #{existing.Id} оновлено.");
            }
        };
        StatusMessage = null;
        EditingProduct = vm;
    }

    // Deactivation is two-step: the trash button only arms the inline
    // confirmation; the actual write happens in ConfirmDeactivateProduct.
    [RelayCommand]
    private void RequestDeactivateProduct(AdminProductRow? row)
    {
        if (row is null) return;
        foreach (var r in _allProducts) r.IsConfirmingDeactivation = false;
        row.IsConfirmingDeactivation = true;
    }

    [RelayCommand]
    private void CancelDeactivateProduct(AdminProductRow? row)
    {
        if (row is null) return;
        row.IsConfirmingDeactivation = false;
    }

    [RelayCommand]
    private void ConfirmDeactivateProduct(AdminProductRow? row)
    {
        if (row is null) return;
        _catalog.SetProductActive(row.Product.Id, false);
        Reload();
        ShowStatus($"Товар #{row.Product.Id} деактивовано і прихований з каталогу покупця.");
    }

    // Re-activation is the undo path — no confirmation needed.
    [RelayCommand]
    private void ActivateProduct(AdminProductRow? row)
    {
        if (row is null) return;
        _catalog.SetProductActive(row.Product.Id, true);
        Reload();
        ShowStatus($"Товар #{row.Product.Id} знову активний у каталозі.");
    }

    // Inline product editor. Non-null while the editor panel is shown.
    [ObservableProperty] private ProductEditViewModel? _editingProduct;

    public bool IsEditingProduct => EditingProduct is not null;
    partial void OnEditingProductChanged(ProductEditViewModel? value) =>
        OnPropertyChanged(nameof(IsEditingProduct));

    // === Orders (status filter + search + per-row expand) ===

    public ObservableCollection<AdminOrderRow> OrderRows { get; } = new();
    public IReadOnlyList<OrderStatus> OrderStatuses { get; } = Enum.GetValues<OrderStatus>();

    // null = show all statuses.
    [ObservableProperty] private OrderStatus? _orderFilter;
    [ObservableProperty] private string _orderSearch = string.Empty;
    [ObservableProperty] private bool _hasNoOrders;

    public bool IsOrderFilterAll => OrderFilter is null;
    public bool IsOrderFilterNew => OrderFilter == OrderStatus.New;
    public bool IsOrderFilterProcessing => OrderFilter == OrderStatus.Processing;
    public bool IsOrderFilterCompleted => OrderFilter == OrderStatus.Completed;
    public bool IsOrderFilterCancelled => OrderFilter == OrderStatus.Cancelled;

    partial void OnOrderFilterChanged(OrderStatus? value)
    {
        OnPropertyChanged(nameof(IsOrderFilterAll));
        OnPropertyChanged(nameof(IsOrderFilterNew));
        OnPropertyChanged(nameof(IsOrderFilterProcessing));
        OnPropertyChanged(nameof(IsOrderFilterCompleted));
        OnPropertyChanged(nameof(IsOrderFilterCancelled));
        ApplyOrderFilter();
    }

    partial void OnOrderSearchChanged(string value) => ApplyOrderFilter();

    [RelayCommand]
    private void FilterOrders(string filter) =>
        OrderFilter = Enum.TryParse<OrderStatus>(filter, ignoreCase: true, out var s) ? s : null;

    [RelayCommand]
    private void ClearOrderSearch() => OrderSearch = string.Empty;

    [RelayCommand]
    private void ResetOrderFilters()
    {
        OrderSearch = string.Empty;
        OrderFilter = null;
    }

    private void ApplyOrderFilter()
    {
        var query = OrderSearch.Trim();
        IEnumerable<AdminOrderRow> source = _allOrders
            .Where(r => OrderFilter is null || r.Order.Status == OrderFilter);
        if (query.Length > 0)
            source = source.Where(r =>
                r.Order.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Buyer.Contains(query, StringComparison.OrdinalIgnoreCase));

        OrderRows.Clear();
        foreach (var row in source) OrderRows.Add(row);
        HasNoOrders = OrderRows.Count == 0;
    }

    // Picking a status in the row's ComboBox persists immediately — there is no
    // intermediate "selected but unsaved" state the admin could miss.
    private void PersistOrderStatus(AdminOrderRow row, OrderStatus status)
    {
        _catalog.UpdateOrderStatus(row.Order.Id, status);
        row.Order.Status = status;
        RecalcKpis();
        RecalcPeriodRevenue();
        ShowStatus($"Замовлення #{row.Order.Id}: статус змінено на «{UkStatus(status)}».");
    }

    // Rows expand independently so two orders can be compared side by side.
    [RelayCommand]
    private void ToggleOrderDetails(AdminOrderRow? row)
    {
        if (row is null) return;
        row.IsExpanded = !row.IsExpanded;
    }

    // === Users ===

    public ObservableCollection<AdminUserRow> Users { get; } = new();

    // Guest is the implicit not-logged-in role; assigning it to a registered
    // account makes no sense, so it is not offered.
    public IReadOnlyList<UserRole> UserRoles { get; } =
        Enum.GetValues<UserRole>().Where(r => r != UserRole.Guest).ToList();

    [ObservableProperty] private string _userSearch = string.Empty;

    partial void OnUserSearchChanged(string value) => ApplyUserFilter();

    [RelayCommand]
    private void ClearUserSearch() => UserSearch = string.Empty;

    private void ApplyUserFilter()
    {
        var query = UserSearch.Trim();
        IEnumerable<AdminUserRow> source = _allUsers;
        if (query.Length > 0)
            source = source.Where(r =>
                r.User.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (r.User.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        Users.Clear();
        foreach (var r in source) Users.Add(r);
    }

    [RelayCommand]
    private void ChangeUserRole(AdminUserRow? row)
    {
        if (row is null || !row.HasUnsavedRole) return;

        // Lockout guard: the logged-in admin cannot demote their own account.
        if (row.User.Id == _auth.CurrentUser?.Id && row.SelectedRole != UserRole.Admin)
        {
            row.Revert();
            ShowStatus("Не можна зняти роль адміністратора з власного облікового запису.", isError: true);
            return;
        }

        _catalog.SetUserRole(row.User.Id, row.SelectedRole);
        row.AcceptRole();
        ShowStatus($"Користувача «{row.User.Username}» переведено в роль «{UkRole(row.SelectedRole)}».");
    }

    [RelayCommand]
    private void RevertUserRole(AdminUserRow? row) => row?.Revert();

    // === Overview: top selling + period revenue ===

    public ObservableCollection<TopSellingRow> TopSelling { get; } = new();

    // Defaults match the pre-selected «Цей місяць» chip: calendar month start.
    [ObservableProperty]
    private DateTime _revenueFrom = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    [ObservableProperty] private DateTime _revenueTo = DateTime.UtcNow.Date;
    [ObservableProperty] private decimal _periodRevenue;
    [ObservableProperty] private int _periodOrderCount;
    [ObservableProperty] private StatsPeriod _activePeriod = StatsPeriod.Month;

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

    private void RecalcPeriodRevenue()
    {
        var report = _catalog.RevenueForPeriod(RevenueFrom, RevenueTo);
        PeriodRevenue = report.Total;
        PeriodOrderCount = report.OrderCount;
        OnPropertyChanged(nameof(PeriodLabel));
    }

    // An inverted range ("Від" later than "До") yields a meaningless zero —
    // surface a warning instead of leaving the admin guessing.
    public bool IsPeriodRangeInvalid => RevenueFrom > RevenueTo;

    // Editing either date by hand drops the quick-period selection to "Custom".
    partial void OnRevenueFromChanged(DateTime value)
    {
        OnPropertyChanged(nameof(IsPeriodRangeInvalid));
        if (_applyingPreset) return;
        ActivePeriod = StatsPeriod.Custom;
        RecalcPeriodRevenue();
    }

    partial void OnRevenueToChanged(DateTime value)
    {
        OnPropertyChanged(nameof(IsPeriodRangeInvalid));
        if (_applyingPreset) return;
        ActivePeriod = StatsPeriod.Custom;
        RecalcPeriodRevenue();
    }

    // Picks a quick period and rewrites the From/To range. Periods are
    // calendar-based (this week starts Monday, this month on the 1st, …) —
    // matching what the labels promise, not rolling N-day windows.
    [RelayCommand]
    private void SelectPeriod(string period)
    {
        if (!Enum.TryParse<StatsPeriod>(period, ignoreCase: true, out var p)) return;

        var today = DateTime.UtcNow.Date;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var quarterStart = new DateTime(today.Year, today.Month - (today.Month - 1) % 3, 1);
        var yearStart = new DateTime(today.Year, 1, 1);
        var (from, to) = p switch
        {
            StatsPeriod.Today   => (today, today),
            StatsPeriod.Week    => (weekStart, today),
            StatsPeriod.Month   => (monthStart, today),
            StatsPeriod.Quarter => (quarterStart, today),
            StatsPeriod.Year    => (yearStart, today),
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
        => _allOrders.Count > 0 ? _allOrders.Min(r => r.Order.CreatedAt).Date : DateTime.UtcNow.Date;

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
            ShowStatus($"Замовлення експортовано: {path}");
        }
        catch (Exception ex)
        {
            ShowStatus($"Не вдалось зберегти Excel: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task ExportProductsExcelAsync()
    {
        var path = await _files.SaveFileAsync("Зберегти Excel",
            $"products_{DateTime.UtcNow:yyyyMMdd}.xlsx",
            new[] { new FileFilter("Excel", new[] { "*.xlsx" }) });
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            _catalog.ExportProductsToExcel(path);
            ShowStatus($"Товари експортовано: {path}");
        }
        catch (Exception ex)
        {
            ShowStatus($"Не вдалось зберегти Excel: {ex.Message}", isError: true);
        }
    }

    // === Data ===

    private void Reload()
    {
        // Newest first so a freshly added product is visible at the top of the
        // list instead of below the fold.
        _allProducts = _catalog.Products
            .OrderByDescending(p => p.Id)
            .Select(p => new AdminProductRow(p))
            .ToList();
        ApplyProductFilter();

        _allOrders = _catalog.Orders.Select(o => new AdminOrderRow(o, PersistOrderStatus)).ToList();
        ApplyOrderFilter();

        TopSelling.Clear();
        var rank = 1;
        var byAlbum = _allProducts
            .GroupBy(r => r.Product.AlbumId)
            .Select(g => new
            {
                Title = g.First().Product.Album?.Title ?? $"Альбом #{g.Key}",
                Artist = g.First().Product.Album?.Artist?.Name ?? "",
                Units = g.Sum(r => r.Product.SalesCount),
                Revenue = g.Sum(r => r.Product.SalesCount * r.Product.Price),
            })
            .Where(a => a.Units > 0)
            .OrderByDescending(a => a.Units)
            .ThenByDescending(a => a.Revenue)
            .Take(10);
        foreach (var a in byAlbum)
            TopSelling.Add(new TopSellingRow(rank++, a.Title, a.Artist, a.Units, a.Revenue));

        // Reuse existing user rows so an in-progress (unsaved) role selection
        // survives unrelated reloads (e.g. saving a product).
        _allUsers = _catalog.GetUsers()
            .Select(u => _allUsers.FirstOrDefault(r => r.User.Id == u.Id) ?? new AdminUserRow(u))
            .ToList();
        ApplyUserFilter();

        RecalcKpis();
    }

    private void RecalcKpis()
    {
        TotalProducts = _allProducts.Count;
        TotalOrders = _allOrders.Count;
        NewOrdersCount = _allOrders.Count(r => r.Order.Status == OrderStatus.New);
        // Revenue counts only fulfilled (Completed) orders, matching RevenueForPeriod and
        // the SalesCount aggregate — New/Processing/Cancelled are not realised income.
        GrossRevenue = _allOrders
            .Where(r => r.Order.Status == OrderStatus.Completed)
            .Sum(r => r.Order.TotalAmount);
    }

    private static string UkStatus(OrderStatus s) => s switch
    {
        OrderStatus.New => "Нове",
        OrderStatus.Processing => "В обробці",
        OrderStatus.Completed => "Виконано",
        OrderStatus.Cancelled => "Скасовано",
        _ => s.ToString(),
    };

    private static string UkRole(UserRole r) => r switch
    {
        UserRole.Guest => "Гість",
        UserRole.Customer => "Покупець",
        UserRole.Admin => "Адміністратор",
        _ => r.ToString(),
    };

    private static string Plural(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = mod10 == 1 && mod100 != 11 ? one
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? few
            : many;
        return $"{n} {word}";
    }
}

/// <summary>Admin screen sections (chip-tab switcher).</summary>
public enum AdminSection
{
    Overview,
    Products,
    Orders,
    Users,
}

/// <summary>Product activity filter for the admin products table.</summary>
public enum ProductStateFilter
{
    All,
    Active,
    Inactive,
}

/// <summary>Quick revenue periods for the overview section.</summary>
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

/// <summary>
/// Product row wrapper: carries the inline deactivation-confirmation state so
/// destructive actions take two clicks instead of one.
/// </summary>
public partial class AdminProductRow : ObservableObject
{
    public AdminProductRow(Product product) => Product = product;

    public Product Product { get; }

    [ObservableProperty] private bool _isConfirmingDeactivation;
}

/// <summary>
/// Order row wrapper: a status picked in the row's ComboBox is persisted
/// immediately via the owner-supplied callback (no unsaved limbo state).
/// Also tracks the inline-details expansion.
/// </summary>
public partial class AdminOrderRow : ObservableObject
{
    private readonly Action<AdminOrderRow, OrderStatus>? _persistStatus;

    public AdminOrderRow(Order order, Action<AdminOrderRow, OrderStatus>? persistStatus = null)
    {
        Order = order;
        _persistStatus = persistStatus;
        _selectedStatus = order.Status;
    }

    public Order Order { get; }

    [ObservableProperty] private OrderStatus _selectedStatus;
    [ObservableProperty] private bool _isExpanded;

    partial void OnSelectedStatusChanged(OrderStatus value)
    {
        if (value != Order.Status)
            _persistStatus?.Invoke(this, value);
    }

    public string Buyer => Order.UserEmail ?? $"користувач #{Order.UserId}";
}

/// <summary>User row wrapper: tracks an unsaved role selection.</summary>
public partial class AdminUserRow : ObservableObject
{
    private UserRole _savedRole;

    public AdminUserRow(User user)
    {
        User = user;
        _savedRole = user.Role;
        _selectedRole = user.Role;
    }

    public User User { get; }

    [ObservableProperty] private UserRole _selectedRole;

    public bool HasUnsavedRole => SelectedRole != _savedRole;
    partial void OnSelectedRoleChanged(UserRole value) =>
        OnPropertyChanged(nameof(HasUnsavedRole));

    public void AcceptRole()
    {
        User.Role = SelectedRole;
        _savedRole = SelectedRole;
        OnPropertyChanged(nameof(HasUnsavedRole));
    }

    public void Revert() => SelectedRole = _savedRole;
}

/// <summary>Per-album aggregate for the overview "top selling" table — units and
/// realised revenue summed across the album's LP/CD products.</summary>
public sealed record TopSellingRow(int Rank, string AlbumTitle, string ArtistName, int Units, decimal Revenue);
