using System;
using MusicApp.ViewModels;

namespace MusicApp.Services;

public enum NavTarget
{
    Catalog,
    SearchResults,
    Product,
    Cart,
    Profile,
    Orders,
    Player,
    Admin
}

public interface INavigationService
{
    event EventHandler<ViewModelBase>? CurrentViewChanged;

    ViewModelBase? CurrentView { get; }
    NavTarget CurrentTarget { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    void NavigateTo(NavTarget target, object? parameter = null);
    void GoBack();
    void GoForward();
}
