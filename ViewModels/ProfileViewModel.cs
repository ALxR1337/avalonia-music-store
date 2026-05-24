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

public partial class ProfileViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly ICatalogService _catalog;
    private readonly ISearchService? _search;
    private readonly INavigationService? _nav;

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _roleLabel = "";

    // Orders tab
    [ObservableProperty] private OrderStatus? _orderStatusFilter;
    [ObservableProperty] private Order? _expandedOrder;

    // Reviews tab
    [ObservableProperty] private Review? _editingReview;
    [ObservableProperty] private string _editingReviewText = string.Empty;
    [ObservableProperty] private int _editingReviewRating = 5;

    // Status messages (e.g., password change result)
    [ObservableProperty] private string? _statusMessage;

    public ProfileViewModel(IAuthService auth, ICatalogService catalog,
        ISearchService? search = null, INavigationService? nav = null)
    {
        _auth = auth;
        _catalog = catalog;
        _search = search;
        _nav = nav;

        Username = auth.CurrentUser?.Username ?? "Гість";
        Email = string.IsNullOrEmpty(auth.CurrentUser?.Email) ? "не вказано" : auth.CurrentUser.Email!;
        RoleLabel = auth.CurrentUser?.Role switch
        {
            UserRole.Admin => "Адміністратор",
            UserRole.Customer => "Покупець",
            _ => "Гість"
        };

        AllOrders = new ObservableCollection<Order>();
        Orders = new ObservableCollection<Order>();
        MyReviews = new ObservableCollection<Review>();
        SavedSearches = new ObservableCollection<SavedSearchSummary>();

        ReloadAll();
    }

    public ObservableCollection<Order> AllOrders { get; }
    public ObservableCollection<Order> Orders { get; }
    public ObservableCollection<Review> MyReviews { get; }
    public ObservableCollection<SavedSearchSummary> SavedSearches { get; }

    public bool IsAuthenticated => _auth.CurrentUser is { Role: not UserRole.Guest, Id: > 0 };
    public OrderStatus[] StatusFilterOptions { get; } =
        Enum.GetValues<OrderStatus>();

    private void ReloadAll()
    {
        ReloadOrders();
        ReloadReviews();
        ReloadSavedSearches();
    }

    private void ReloadOrders()
    {
        AllOrders.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) { Orders.Clear(); return; }
        foreach (var o in _catalog.GetOrdersFor(userId).OrderByDescending(o => o.CreatedAt))
            AllOrders.Add(o);
        ApplyOrderFilter();
    }

    private void ApplyOrderFilter()
    {
        Orders.Clear();
        IEnumerable<Order> source = AllOrders;
        if (OrderStatusFilter is OrderStatus s) source = source.Where(o => o.Status == s);
        foreach (var o in source) Orders.Add(o);
    }

    partial void OnOrderStatusFilterChanged(OrderStatus? value) => ApplyOrderFilter();

    [RelayCommand]
    private void ClearOrderFilter() => OrderStatusFilter = null;

    [RelayCommand]
    private void ToggleOrderDetails(Order? order)
    {
        ExpandedOrder = ExpandedOrder?.Id == order?.Id ? null : order;
    }

    // === Reviews ===

    private void ReloadReviews()
    {
        MyReviews.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        foreach (var r in _catalog.GetReviewsByUser(userId)) MyReviews.Add(r);
    }

    [RelayCommand]
    private void StartEditReview(Review? review)
    {
        if (review is null) return;
        EditingReview = review;
        EditingReviewText = review.Text;
        EditingReviewRating = review.Rating;
    }

    [RelayCommand]
    private void CancelEditReview()
    {
        EditingReview = null;
        EditingReviewText = string.Empty;
        EditingReviewRating = 5;
    }

    [RelayCommand]
    private void SaveEditReview()
    {
        if (EditingReview is null) return;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;

        _catalog.UpdateReview(EditingReview.Id, userId, EditingReviewText, EditingReviewRating);
        EditingReview = null;
        EditingReviewText = string.Empty;
        EditingReviewRating = 5;
        ReloadReviews();
        StatusMessage = "Відгук оновлено.";
    }

    [RelayCommand]
    private void DeleteReview(Review? review)
    {
        if (review is null) return;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        if (_catalog.DeleteReview(review.Id, userId))
        {
            ReloadReviews();
            StatusMessage = $"Відгук #{review.Id} видалено.";
        }
    }

    // === Saved searches ===

    private void ReloadSavedSearches()
    {
        SavedSearches.Clear();
        if (_search is null) return;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        foreach (var s in _search.ListSavedSearchSummaries(userId))
            SavedSearches.Add(s);
    }

    [RelayCommand]
    private void RunSavedSearch(SavedSearchSummary? summary)
    {
        if (summary is null || _nav is null) return;
        _nav.NavigateTo(NavTarget.SearchResults, summary.Saved.QueryJson);
    }

    [RelayCommand]
    private void ToggleSavedSearchNotify(SavedSearchSummary? summary)
    {
        if (summary is null || _search is null) return;
        var next = !summary.Saved.NotifyOnNew;
        _search.SetSavedSearchNotify(summary.Saved.Id, next);
        ReloadSavedSearches();
    }

    [RelayCommand]
    private void DeleteSavedSearch(SavedSearchSummary? summary)
    {
        if (summary is null || _search is null) return;
        _search.DeleteSavedSearch(summary.Saved.Id);
        ReloadSavedSearches();
    }

    // === Password change ===

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (!IsAuthenticated)
        {
            StatusMessage = "Гість не може змінити пароль.";
            return;
        }
        var owner = OwnerWindow();
        if (owner is null) return;

        var vm = new ChangePasswordViewModel(_auth);
        var window = new Views.ChangePasswordWindow { DataContext = vm };
        var ok = await window.ShowDialog<bool?>(owner);
        if (ok == true) StatusMessage = "Пароль змінено.";
    }

    private static Window? OwnerWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}

public partial class ChangePasswordViewModel : ViewModelBase
{
    private readonly IAuthService _auth;

    [ObservableProperty] private string _oldPassword = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string? _error;

    public ChangePasswordViewModel(IAuthService auth) { _auth = auth; }

    public event Action<bool>? RequestClose;

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    [RelayCommand]
    private void Submit()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 4)
        {
            Error = "Новий пароль має містити щонайменше 4 символи.";
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            Error = "Підтвердження не співпадає.";
            return;
        }
        if (!_auth.TryChangePassword(OldPassword, NewPassword))
        {
            Error = "Невірний старий пароль.";
            return;
        }
        RequestClose?.Invoke(true);
    }
}
