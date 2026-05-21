using System;
using System.Collections.Generic;
using MusicApp.ViewModels;

namespace MusicApp.Services;

public class NavigationService : INavigationService
{
    private readonly Dictionary<NavTarget, Func<object?, ViewModelBase>> _factories = new();
    private readonly Stack<(NavTarget Target, ViewModelBase View)> _history = new();

    public event EventHandler<ViewModelBase>? CurrentViewChanged;

    public ViewModelBase? CurrentView { get; private set; }
    public NavTarget CurrentTarget { get; private set; } = NavTarget.Catalog;

    public void Register(NavTarget target, Func<object?, ViewModelBase> factory)
        => _factories[target] = factory;

    public void NavigateTo(NavTarget target, object? parameter = null)
    {
        if (!_factories.TryGetValue(target, out var factory))
            throw new InvalidOperationException($"No factory registered for {target}");

        var vm = factory(parameter);
        if (CurrentView is not null)
            _history.Push((CurrentTarget, CurrentView));

        CurrentTarget = target;
        CurrentView = vm;
        CurrentViewChanged?.Invoke(this, vm);
    }

    public void GoBack()
    {
        if (_history.Count == 0) return;
        var (target, view) = _history.Pop();
        CurrentTarget = target;
        CurrentView = view;
        CurrentViewChanged?.Invoke(this, view);
    }
}
