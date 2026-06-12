using System;
using Avalonia.Controls;
using MusicApp.ViewModels;

namespace MusicApp.Views;

public partial class PlayerView : UserControl
{
    private TextBlock? _descClamped;
    private TextBlock? _descMeasure;

    public PlayerView()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
    }

    // «Показати повністю» renders only when the 3-line clamp actually hides
    // text: a zero-height measuring twin (same width, no MaxLines) reports the
    // full height to compare against.
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is not PlayerViewModel vm) return;
        _descClamped ??= this.FindControl<TextBlock>("DescClamped");
        _descMeasure ??= this.FindControl<TextBlock>("DescMeasure");
        if (_descClamped is null || _descMeasure is null || !_descClamped.IsVisible) return;
        vm.IsDescriptionClamped = _descMeasure.DesiredSize.Height > _descClamped.DesiredSize.Height + 0.5;
    }
}
