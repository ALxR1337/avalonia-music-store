using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace MusicApp.Controls;

// WrapPanel variant for responsive card grids. A plain WrapPanel with
// fixed-width items leaves a dead gutter on the right whenever the available
// width is not a multiple of the item width; this panel instead derives the
// column count from MinItemWidth and stretches every item so each row spans
// the full width.
public sealed class UniformWrapPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(MinItemWidth), 180);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(ColumnSpacing), 12);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(RowSpacing), 12);

    static UniformWrapPanel()
    {
        AffectsMeasure<UniformWrapPanel>(MinItemWidthProperty, ColumnSpacingProperty, RowSpacingProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    private (int Columns, double ItemWidth) ResolveColumns(double width)
    {
        var min = Math.Max(1.0, MinItemWidth);
        var cols = Math.Max(1, (int)Math.Floor((width + ColumnSpacing) / (min + ColumnSpacing)));
        var itemWidth = (width - (cols - 1) * ColumnSpacing) / cols;
        return (cols, itemWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children.Where(c => c.IsVisible).ToList();
        if (children.Count == 0) return default;

        // Unconstrained width (horizontal scroller etc.) — degrade to a single
        // row of MinItemWidth items; this panel is meant for vertical layouts.
        var width = double.IsInfinity(availableSize.Width)
            ? children.Count * (MinItemWidth + ColumnSpacing) - ColumnSpacing
            : availableSize.Width;

        var (cols, itemWidth) = ResolveColumns(width);

        double totalHeight = 0, rowHeight = 0;
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            var isRowEnd = (i + 1) % cols == 0 || i == children.Count - 1;
            if (isRowEnd)
            {
                totalHeight += rowHeight + (i == children.Count - 1 ? 0 : RowSpacing);
                rowHeight = 0;
            }
        }

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(c => c.IsVisible).ToList();
        if (children.Count == 0) return finalSize;

        var (cols, itemWidth) = ResolveColumns(finalSize.Width);

        double y = 0;
        for (var rowStart = 0; rowStart < children.Count; rowStart += cols)
        {
            var row = children.Skip(rowStart).Take(cols).ToList();
            var rowHeight = row.Max(c => c.DesiredSize.Height);
            for (var col = 0; col < row.Count; col++)
                row[col].Arrange(new Rect(col * (itemWidth + ColumnSpacing), y, itemWidth, rowHeight));
            y += rowHeight + RowSpacing;
        }

        return finalSize;
    }
}

// Forces its child into a square (height = width). Used for album-cover
// tiles whose width is fluid (UniformWrapPanel) but which must keep a 1:1
// aspect ratio.
public sealed class Square : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width)
            ? Math.Min(availableSize.Height, 200)
            : availableSize.Width;
        Child?.Measure(new Size(w, w));
        return new Size(w, w);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
