namespace MusicApp.Models;

public enum ProductFormat
{
    Vinyl,
    CD
}

public enum OrderStatus
{
    New,
    Processing,
    Completed,
    Cancelled
}

/// <summary>
/// Single source of the Ukrainian order-status labels — shared by the XAML
/// converter (chips) and view-models that build their own option lists, so the
/// filter dropdown can never drift from the row chips again.
/// </summary>
public static class OrderStatusLabels
{
    public static string Ua(OrderStatus status) => status switch
    {
        OrderStatus.New => "Нове",
        OrderStatus.Processing => "В обробці",
        OrderStatus.Completed => "Виконано",
        OrderStatus.Cancelled => "Скасовано",
        _ => status.ToString()
    };
}

public enum UserRole
{
    Guest,
    Customer,
    Admin
}

public enum RepeatMode
{
    Off,
    All,
    One
}
