using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
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
    private readonly ICartService? _cart;

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

    // Destructive actions arm an inline "Видалити? Так/Ні" pair on their row
    // instead of deleting on the first click — one mouse-slip near the 32px
    // icons must not cost the user a written review or a saved query.
    [ObservableProperty] private Review? _reviewPendingDelete;
    [ObservableProperty] private SavedSearchSummary? _savedSearchPendingDelete;

    // Status banner under the header. Successes auto-hide after 5 s; errors
    // stay until dismissed so the user can actually read what went wrong
    // (same contract as the admin toast).
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isStatusError;
    private readonly DispatcherTimer _statusTimer;

    // Inline "change password" panel (replaces the old modal window).
    [ObservableProperty] private bool _isPasswordPanelOpen;

    /// <summary>Form for the inline change-password panel.</summary>
    public ChangePasswordViewModel PasswordForm { get; }

    public ProfileViewModel(IAuthService auth, ICatalogService catalog,
        ISearchService? search = null, INavigationService? nav = null,
        ICartService? cart = null)
    {
        _auth = auth;
        _catalog = catalog;
        _search = search;
        _nav = nav;
        _cart = cart;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusMessage = null; };

        PasswordForm = new ChangePasswordViewModel(auth);
        PasswordForm.RequestClose += ok =>
        {
            IsPasswordPanelOpen = false;
            if (ok) ShowStatus("Пароль змінено.");
        };

        Username = auth.CurrentUser?.Username ?? "Гість";
        Email = auth.CurrentUser?.Email ?? string.Empty;
        RoleLabel = auth.CurrentUser?.Role switch
        {
            UserRole.Admin => "Адміністратор",
            UserRole.Customer => "Покупець",
            _ => "Гість"
        };

        StatusFilterOptions = new[] { new OrderStatusOption(null, "Усі статуси") }
            .Concat(Enum.GetValues<OrderStatus>()
                .Select(s => new OrderStatusOption(s, OrderStatusLabels.Ua(s))))
            .ToArray();
        _selectedStatusOption = StatusFilterOptions[0];

        AllOrders = new ObservableCollection<Order>();
        Orders = new ObservableCollection<Order>();
        MyReviews = new ObservableCollection<Review>();
        SavedSearches = new ObservableCollection<SavedSearchSummary>();
        WishlistItems = new ObservableCollection<Product>();

        catalog.WishlistChanged += (_, _) => ReloadWishlist();

        ReloadAll();
    }

    public ObservableCollection<Order> AllOrders { get; }
    public ObservableCollection<Order> Orders { get; }
    public ObservableCollection<Review> MyReviews { get; }
    public ObservableCollection<SavedSearchSummary> SavedSearches { get; }
    public ObservableCollection<Product> WishlistItems { get; }

    public bool IsAuthenticated => _auth.CurrentUser is { Role: not UserRole.Guest, Id: > 0 };
    public bool HasEmail => !string.IsNullOrEmpty(_auth.CurrentUser?.Email);

    /// <summary>«Усі статуси» + the four statuses, labelled in Ukrainian like the row chips.</summary>
    public OrderStatusOption[] StatusFilterOptions { get; }

    [ObservableProperty] private OrderStatusOption? _selectedStatusOption;

    partial void OnSelectedStatusOptionChanged(OrderStatusOption? value)
    {
        if (value is not null) OrderStatusFilter = value.Value;
    }

    private void ReloadAll()
    {
        ReloadOrders();
        ReloadReviews();
        ReloadSavedSearches();
        ReloadWishlist();
    }

    private void ReloadOrders()
    {
        AllOrders.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId > 0)
            foreach (var o in _catalog.GetOrdersFor(userId).OrderByDescending(o => o.CreatedAt))
                AllOrders.Add(o);
        ApplyOrderFilter();
    }

    /// <summary>No orders at all — the list never had anything to show.</summary>
    public bool HasNoOrders => AllOrders.Count == 0;

    /// <summary>Orders exist but the active status filter matched none of them.</summary>
    public bool HasOrdersButNoneMatch => AllOrders.Count > 0 && Orders.Count == 0;

    private void ApplyOrderFilter()
    {
        Orders.Clear();
        IEnumerable<Order> source = AllOrders;
        if (OrderStatusFilter is OrderStatus s) source = source.Where(o => o.Status == s);
        foreach (var o in source) Orders.Add(o);
        // A row hidden by the filter must not stay silently expanded.
        if (ExpandedOrder is not null && Orders.All(o => o.Id != ExpandedOrder.Id))
            ExpandedOrder = null;
        OnPropertyChanged(nameof(HasNoOrders));
        OnPropertyChanged(nameof(HasOrdersButNoneMatch));
    }

    partial void OnOrderStatusFilterChanged(OrderStatus? value)
    {
        ApplyOrderFilter();
        // Keep the ComboBox in sync when the filter is set programmatically
        // («Скинути», tests); record equality makes the round-trip a no-op.
        SelectedStatusOption = Array.Find(StatusFilterOptions, o => o.Value == value)
                               ?? StatusFilterOptions[0];
    }

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

    // The rating stars in the inline editor; parameter comes from XAML as "1".."5".
    [RelayCommand]
    private void SetEditingRating(string? rating)
    {
        if (int.TryParse(rating, out var r)) EditingReviewRating = Math.Clamp(r, 1, 5);
    }

    [RelayCommand]
    private void OpenReviewProduct(Review? review)
    {
        if (review is null || _nav is null) return;
        _nav.NavigateTo(NavTarget.Product, review.ProductId);
    }

    [RelayCommand]
    private void CancelEditReview()
    {
        EditingReview = null;
        EditingReviewText = string.Empty;
        EditingReviewRating = 5;
    }

    private bool CanSaveEditReview() => !string.IsNullOrWhiteSpace(EditingReviewText);

    partial void OnEditingReviewTextChanged(string value) =>
        SaveEditReviewCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSaveEditReview))]
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
        ShowStatus("Відгук оновлено.");
    }

    [RelayCommand]
    private void RequestDeleteReview(Review? review) => ReviewPendingDelete = review;

    [RelayCommand]
    private void CancelDeleteReview() => ReviewPendingDelete = null;

    [RelayCommand]
    private void DeleteReview(Review? review)
    {
        ReviewPendingDelete = null;
        if (review is null) return;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        if (_catalog.DeleteReview(review.Id, userId))
        {
            ReloadReviews();
            var title = review.Product?.Album?.Title;
            ShowStatus(title is null ? "Відгук видалено." : $"Відгук про «{title}» видалено.");
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

    // «Сповіщати» (NotifyOnNew) deliberately has no UI: nothing in the app
    // delivers notifications, so the checkbox was a promise that never fired.
    // The schema column stays; wire a command back up here if a notifier lands.

    [RelayCommand]
    private void RequestDeleteSavedSearch(SavedSearchSummary? summary) => SavedSearchPendingDelete = summary;

    [RelayCommand]
    private void CancelDeleteSavedSearch() => SavedSearchPendingDelete = null;

    [RelayCommand]
    private void DeleteSavedSearch(SavedSearchSummary? summary)
    {
        SavedSearchPendingDelete = null;
        if (summary is null || _search is null) return;
        _search.DeleteSavedSearch(summary.Saved.Id);
        ReloadSavedSearches();
        ShowStatus($"Запит «{summary.Saved.Name}» видалено.");
    }

    // === Wishlist (saved albums) ===

    private void ReloadWishlist()
    {
        WishlistItems.Clear();
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        foreach (var p in _catalog.GetWishlistProducts(userId)) WishlistItems.Add(p);
    }

    [RelayCommand]
    private void OpenWishlistItem(Product? product)
    {
        if (product is null || _nav is null) return;
        _nav.NavigateTo(NavTarget.Product, product.Id);
    }

    [RelayCommand]
    private void RemoveWishlistItem(Product? product)
    {
        if (product is null) return;
        var userId = _auth.CurrentUser?.Id ?? 0;
        if (userId <= 0) return;
        // WishlistChanged from the catalog reloads the collection.
        _catalog.RemoveFromWishlist(userId, product.Id);
    }

    [RelayCommand]
    private void AddWishlistItemToCart(Product? product)
    {
        if (product is null || _cart is null || product.Stock <= 0) return;
        _cart.Add(product);
        ShowStatus($"«{product.Album?.Title}» додано до кошика.");
    }

    // === Password change (inline panel) ===

    // Toggles the inline change-password panel under the profile header. Called
    // from the profile button and (via the shell) from the title-bar menu.
    // The form is wiped only on the closed→open transition: a second press
    // collapses the panel instead of silently discarding half-typed input.
    [RelayCommand]
    public void OpenPasswordPanel()
    {
        if (!IsAuthenticated)
        {
            ShowStatus("Гість не може змінити пароль.", isError: true);
            return;
        }
        if (IsPasswordPanelOpen)
        {
            IsPasswordPanelOpen = false;
            return;
        }
        PasswordForm.Reset();
        DismissStatus();
        IsPasswordPanelOpen = true;
    }

    // === Status banner ===

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
}

/// <summary>One entry of the order-status filter dropdown; null Value = no filter.</summary>
public sealed record OrderStatusOption(OrderStatus? Value, string Label);

public partial class ChangePasswordViewModel : ViewModelBase
{
    private readonly IAuthService _auth;

    [ObservableProperty] private string _oldPassword = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string? _error;

    public ChangePasswordViewModel(IAuthService auth) { _auth = auth; }

    public event Action<bool>? RequestClose;

    // Clears the fields before the panel is shown again.
    public void Reset()
    {
        OldPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        Error = null;
    }

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
        if (NewPassword == OldPassword)
        {
            Error = "Новий пароль має відрізнятися від поточного.";
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
