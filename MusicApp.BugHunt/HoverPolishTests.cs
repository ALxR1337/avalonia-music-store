using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace MusicApp.BugHunt;

// Visual capture + regression for the accent hover/focus polish:
//   * the active sidebar item must keep its orange accent pipe on pointer-over.
//     Fluent's own :pointerover rule rewrites the ContentPresenter BorderBrush,
//     which used to wipe the bar — ControlStyles re-asserts it explicitly now.
//   * the primary CTA must change only its fill on hover/focus — no stray ring.
// These also emit screenshots; eyeball the PNGs under bug-hunt/artifacts.
public class HoverPolishTests
{
    private static void Pseudo(Control c, string name, bool on) =>
        ((Avalonia.Controls.IPseudoClasses)c.Classes).Set(name, on);

    private static ContentPresenter Part(Button b) =>
        b.GetVisualDescendants().OfType<ContentPresenter>()
            .First(c => c.Name == "PART_ContentPresenter");

    [AvaloniaFact]
    public void Active_nav_item_keeps_accent_pipe_on_hover()
    {
        var h = new Harness();
        h.OpenMainWindow();

        var nav = h.Window!.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("nav-item") && b.Classes.Contains("active"));

        h.RunStep("hover-nav-active-rest", () => { });
        h.RunStep("hover-nav-active-over", () => Pseudo(nav, ":pointerover", true));

        // The rendered left bar lives on the ContentPresenter border. On hover it
        // must still be the accent colour, not the Fluent theme's grey override.
        var brush = Part(nav).BorderBrush as ISolidColorBrush;
        Assert.Equal(Color.Parse("#E07B39"), brush?.Color);
    }

    [AvaloniaFact]
    public void Primary_cta_shows_no_border_on_hover_or_focus()
    {
        var h = new Harness();
        h.OpenMainWindow();

        var primary = h.Window!.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("primary"));

        h.RunStep("hover-primary-rest", () => { });
        h.RunStep("hover-primary-over", () => Pseudo(primary, ":pointerover", true));

        // The label must keep the theme's OnAccent (near-black) on hover —
        // Fluent's :pointerover applies a light ButtonForegroundPointerOver
        // that would otherwise wash it out.
        var label = primary.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == "Увійти");
        var fg = label.GetValue(TextBlock.ForegroundProperty) as ISolidColorBrush;
        Assert.Equal(Color.Parse("#FF121212"), fg?.Color);

        h.RunStep("hover-primary-focus", () =>
        {
            Pseudo(primary, ":pointerover", false);
            Pseudo(primary, ":focus", true);
            Pseudo(primary, ":focus-visible", true);
            primary.Focus();
        });

        // Whatever pseudo-state is active, the border stays invisible: either a
        // transparent brush or zero thickness. No orange/grey ring.
        var part = Part(primary);
        var border = part.BorderBrush as ISolidColorBrush;
        var transparent = border is null || border.Color.A == 0;
        var zeroThickness = part.BorderThickness.Top == 0 && part.BorderThickness.Left == 0
                            && part.BorderThickness.Right == 0 && part.BorderThickness.Bottom == 0;
        Assert.True(transparent || zeroThickness,
            $"Primary CTA shows a ring: BorderBrush={part.BorderBrush}, Thickness={part.BorderThickness}");
        Assert.Null(primary.FocusAdorner);
    }
}
