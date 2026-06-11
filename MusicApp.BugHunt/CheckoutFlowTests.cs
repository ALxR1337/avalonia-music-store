using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MusicApp.Services;
using MusicApp.ViewModels;
using Xunit;

namespace MusicApp.BugHunt;

// Checkout used to be one click: «Оформити замовлення» instantly created the
// order. It is now a two-step flow — an inline form (required shipping
// address, optional comment) followed by a success screen. These tests walk
// the whole flow through the real CartView.
public class CheckoutFlowTests
{
    [AvaloniaFact]
    public void Pickup_checkout_uses_selected_store_as_address()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        var product = h.Catalog!.Products.First(p => p.IsActive && p.Stock > 0);
        h.Cart!.Add(product, 1);
        h.RunStep("checkout-01-cart", () => h.Nav!.NavigateTo(NavTarget.Cart));
        var cvm = Assert.IsType<CartViewModel>(h.Nav!.CurrentView);
        Assert.True(cvm.ShowCart);

        h.RunStep("checkout-02-form", () => cvm.BeginCheckoutCommand.Execute(null));
        Assert.True(cvm.ShowCheckout);
        Assert.False(cvm.ShowCart);

        // Pickup is the default; the first store is preselected.
        Assert.True(cvm.IsPickup);
        Assert.Equal(cvm.PickupLocations[0], cvm.SelectedPickupLocation);

        cvm.SelectedPickupLocation = "Центр — вул. Хрещатик, 22";
        cvm.OrderComment = "Подзвонити заздалегідь";
        h.RunStep("checkout-03-success", () => cvm.ConfirmCheckoutCommand.Execute(null));

        Assert.True(cvm.ShowSuccess);
        Assert.NotNull(cvm.CompletedOrder);
        Assert.Equal("Самовивіз з магазину: Центр — вул. Хрещатик, 22",
            cvm.CompletedOrder!.ShippingAddress);
        Assert.Equal("Подзвонити заздалегідь", cvm.CompletedOrder.Comment);
        Assert.Empty(h.Cart!.Items);

        cvm.GoToOrdersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<OrdersViewModel>(h.Nav!.CurrentView);
    }

    [AvaloniaFact]
    public void Nova_poshta_checkout_requires_city_and_branch()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        var product = h.Catalog!.Products.First(p => p.IsActive && p.Stock > 0);
        h.Cart!.Add(product, 1);
        h.Nav!.NavigateTo(NavTarget.Cart);
        Dispatcher.UIThread.RunJobs();
        var cvm = Assert.IsType<CartViewModel>(h.Nav!.CurrentView);

        cvm.BeginCheckoutCommand.Execute(null);
        cvm.IsNovaPoshta = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(cvm.IsPickup);
        h.Snapshot("checkout-04-nova-poshta-form");

        // An empty branch must not create an order.
        cvm.NovaPoshtaBranch = "  ";
        cvm.ConfirmCheckoutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(cvm.ShowCheckout);
        Assert.False(string.IsNullOrEmpty(cvm.CheckoutError));
        Assert.Null(cvm.CompletedOrder);

        // A digits-only branch number gets the «відділення №» prefix.
        cvm.NovaPoshtaBranch = "25";
        cvm.ConfirmCheckoutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(cvm.ShowSuccess);
        Assert.Equal("Нова Пошта: Київ, відділення №25",
            cvm.CompletedOrder!.ShippingAddress);
    }

    [AvaloniaFact]
    public void Cancel_returns_to_cart_with_items_intact()
    {
        var h = new Harness();
        h.OpenMainWindow(loginAs: "admin", password: "admin");

        var product = h.Catalog!.Products.First(p => p.IsActive && p.Stock > 0);
        h.Cart!.Add(product, 2);
        h.Nav!.NavigateTo(NavTarget.Cart);
        Dispatcher.UIThread.RunJobs();
        var cvm = Assert.IsType<CartViewModel>(h.Nav!.CurrentView);

        cvm.BeginCheckoutCommand.Execute(null);
        Assert.True(cvm.ShowCheckout);

        cvm.CancelCheckoutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(cvm.ShowCart);
        Assert.Equal(2, h.Cart!.ItemCount);
        Assert.Null(cvm.CompletedOrder);
    }
}
