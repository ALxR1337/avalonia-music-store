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
    Player,
    Admin
}

public interface INavigationService
{
    event EventHandler<ViewModelBase>? CurrentViewChanged;

    ViewModelBase? CurrentView { get; }
    NavTarget CurrentTarget { get; }

    /// <summary>
    /// The sidebar section that should appear active. Differs from
    /// <see cref="CurrentTarget"/> for detail pages: a <see cref="NavTarget.Product"/>
    /// inherits the section it was opened from (Catalog, SearchResults, Player…),
    /// so drilling into an album keeps the parent tab highlighted.
    /// </summary>
    NavTarget CurrentSection { get; }

    /// <summary>Saved vertical scroll offset of the page we'd return to via <see cref="GoBack"/>.</summary>
    double CurrentScrollOffset { get; }

    bool CanGoBack { get; }
    bool CanGoForward { get; }

    /// <param name="section">Forces which sidebar tab stays active. Defaults to
    /// the target itself for sections, or the inherited section for detail pages.
    /// Pass it to attribute a detail page to a specific origin — e.g. an album
    /// opened from the search box belongs to the Пошук tab, not wherever the
    /// user happened to be.</param>
    void NavigateTo(NavTarget target, object? parameter = null, NavTarget? section = null);
    void GoBack();
    void GoForward();

    /// <summary>Records the live scroll offset of the current page so back/forward can restore it.</summary>
    void SaveScroll(double offsetY);
}
