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
