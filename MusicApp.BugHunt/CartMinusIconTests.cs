using Avalonia.Headless.XUnit;
using MusicApp.Services;
using MusicApp.ViewModels;

namespace MusicApp.BugHunt;

/// <summary>
/// Verifies the cart quantity stepper's "−" (decrease) button renders a
/// visible minus glyph. Regression guard for the degenerate-bounds bug:
/// IconMinus was a pure horizontal line (zero-height geometry), which under
/// the global Stretch="Uniform" on Path.icon collapsed the vertical scale to
/// 0 and made the glyph vanish (rendered as a dot). The "+" was unaffected
/// because its geometry has both width and height.
/// </summary>
public class CartMinusIconTests
{
    [AvaloniaFact]
    public void Cart_quantity_stepper_renders_minus_button()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "demo", password: "demo");
        h.SetWindowSize(1280, 800);

        // Push two distinct products into the cart so both stepper rows render.
        h.Nav!.NavigateTo(NavTarget.Product, 1);
        if (h.Nav!.CurrentView is ProductViewModel p1)
            p1.AddToCartCommand.Execute(null);

        h.Nav!.NavigateTo(NavTarget.Product, 2);
        if (h.Nav!.CurrentView is ProductViewModel p2)
            p2.AddToCartCommand.Execute(null);

        h.RunStep("cart-minus-icon", () => h.Nav!.NavigateTo(NavTarget.Cart));
    }
}
