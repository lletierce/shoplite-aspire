namespace ShopLite.ApiService.Orders;

// Ce que le client envoie
public record PlaceOrderRequest(int ProductId, int Quantity);

// Le message déposé dans RabbitMQ
public record OrderPlaced(Guid OrderId, int ProductId, int Quantity, DateTime PlacedAt);