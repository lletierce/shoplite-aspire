namespace ShopLite.OrderWorker;

public record OrderPlaced(Guid OrderId, int ProductId, int Quantity, DateTime PlacedAt);