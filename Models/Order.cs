using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace MusicApp.Models;

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public OrderStatus Status { get; set; } = OrderStatus.New;

    // Money stored as integer cents; TotalAmount is a decimal facade.
    public long TotalAmountCents { get; set; }

    [NotMapped]
    public decimal TotalAmount
    {
        get => TotalAmountCents / 100m;
        set => TotalAmountCents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }

    // Snapshot fields: captured at checkout so the order is self-contained
    // even if the user, address or currency settings later change.
    public string? UserEmail { get; set; }
    public string? ShippingAddress { get; set; }
    public string? Comment { get; set; }
    public string Currency { get; set; } = "UAH";

    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }

    public long UnitPriceCents { get; set; }

    [NotMapped]
    public decimal UnitPrice
    {
        get => UnitPriceCents / 100m;
        set => UnitPriceCents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }

    // Snapshot fields: keep the displayed product information stable even if
    // the underlying Album/Artist/Product is later renamed or removed.
    public string ProductTitle { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string FormatLabel { get; set; } = string.Empty;

    [NotMapped]
    public decimal LineTotal => UnitPrice * Quantity;
}
