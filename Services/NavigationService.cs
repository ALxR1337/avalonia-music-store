using System;
using System.Collections.Generic;
using MusicApp.ViewModels;

namespace MusicApp.Services;

public class NavigationService : INavigationService
{
    // One node in the back/forward timeline. Bundles the live view with the
    // sidebar section it belongs to and the scroll position we left it at, so
    // GoBack/GoForward restore the page exactly as the user remembers it.
    private sealed class NavEntry
    {
        public required NavTarget Target;
        public required NavTarget Section;
        public required ViewModelBase View;
        public double Scroll;
    }

    private readonly Dictionary<NavTarget, Func<object?, ViewModelBase>> _factories = new();
    private readonly Stack<NavEntry> _history = new();
    private readonly Stack<NavEntry> _forward = new();
    private NavEntry? _current;

    public event EventHandler<ViewModelBase>? CurrentViewChanged;

    public ViewModelBase? CurrentView => _current?.View;
    public NavTarget CurrentTarget => _current?.Target ?? NavTarget.Catalog;
    public NavTarget CurrentSection => _current?.Section ?? NavTarget.Catalog;
    public double CurrentScrollOffset => _current?.Scroll ?? 0;
    public bool CanGoBack => _history.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    // Every target owns a sidebar tab except detail pages, which live "inside"
    // whatever section the user came from.
    private static bool IsSection(NavTarget target) => target != NavTarget.Product;

    public void Register(NavTarget target, Func<object?, ViewModelBase> factory)
        => _factories[target] = factory;

    public void NavigateTo(NavTarget target, object? parameter = null, NavTarget? section = null)
    {
        if (!_factories.TryGetValue(target, out var factory))
            throw new InvalidOperationException($"No factory registered for {target}");

        var vm = factory(parameter);
        // An explicit override wins; otherwise a real section becomes the active
        // section itself and a detail page keeps the section it was opened from.
        var resolvedSection = section
            ?? (IsSection(target) ? target : (_current?.Section ?? NavTarget.Catalog));

        if (_current is not null)
            _history.Push(_current);
        _forward.Clear();

        // Fresh page → starts at the top (Scroll defaults to 0).
        _current = new NavEntry { Target = target, Section = resolvedSection, View = vm };
        CurrentViewChanged?.Invoke(this, vm);
    }

    public void GoBack()
    {
        if (_history.Count == 0 || _current is null) return;
        _forward.Push(_current);
        _current = _history.Pop();
        CurrentViewChanged?.Invoke(this, _current.View);
    }

    public void GoForward()
    {
        if (_forward.Count == 0 || _current is null) return;
        _history.Push(_current);
        _current = _forward.Pop();
        CurrentViewChanged?.Invoke(this, _current.View);
    }

    public void SaveScroll(double offsetY)
    {
        if (_current is not null) _current.Scroll = offsetY;
    }
}
