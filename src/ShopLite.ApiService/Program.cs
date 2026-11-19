using Microsoft.EntityFrameworkCore;
using ShopLite.ApiService.Data;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using RabbitMQ.Client;
using ShopLite.ApiService.Orders;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add database context for catalogdb.
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");

// Add Redis distributed cache for caching.
builder.AddRedisDistributedCache("cache");

// Add RabbitMQ client for messaging.
builder.AddRabbitMQClient("messaging");

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Seed the database with sample data if in development environment.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (!await db.Products.AnyAsync())
    {
        db.Products.AddRange(
            new Product { Name = "Clavier mécanique", Price = 89.90m, Stock = 25 },
            new Product { Name = "Écran 27 pouces", Price = 249.00m, Stock = 10 },
            new Product { Name = "Casque audio", Price = 59.99m, Stock = 40 });
        await db.SaveChangesAsync();
    }
}

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.MapGet("/", () => "API service is running.");


// Map product endpoints
const string ProductsCacheKey = "catalog:products";

var products = app.MapGroup("/api/products");

// Liste des produits, avec cache Redis
products.MapGet("/", async (CatalogDbContext db, IDistributedCache cache, CancellationToken ct) =>
{
    // 1. Le cache contient-il déjà la liste ?
    var cached = await cache.GetStringAsync(ProductsCacheKey, ct);
    if (cached is not null)
    {
        return Results.Content(cached, "application/json");
    }

    // 2. Sinon : lecture en base, puis mise en cache pendant 5 minutes
    var list = await db.Products.AsNoTracking().ToListAsync(ct);
    var json = JsonSerializer.Serialize(list, JsonSerializerOptions.Web);

    await cache.SetStringAsync(ProductsCacheKey, json, new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    }, ct);

    return Results.Content(json, "application/json");
});

// Un produit par son id
products.MapGet("/{id:int}", async (int id, CatalogDbContext db) =>
    await db.Products.FindAsync(id) is { } product
        ? Results.Ok(product)
        : Results.NotFound());

// Création d'un produit, puis invalidation du cache
products.MapPost("/", async (Product product, CatalogDbContext db, IDistributedCache cache) =>
{
    db.Products.Add(product);
    await db.SaveChangesAsync();
    
    await cache.RemoveAsync(ProductsCacheKey);   // le catalogue a changé : on vide le cache

    return Results.Created($"/api/products/{product.Id}", product);
});

app.MapPost("/api/orders", async (PlaceOrderRequest request, CatalogDbContext db,
                                  IConnection connection, CancellationToken ct) =>
{
    // 1. Validation : on refuse les commandes incohérentes avant de publier quoi que ce soit
    if (request.Quantity <= 0)
        return Results.BadRequest("La quantité doit être positive.");

    if (!await db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
        return Results.NotFound($"Produit {request.ProductId} introuvable.");

    // 2. Création du message
    var order = new OrderPlaced(Guid.NewGuid(), request.ProductId, request.Quantity, DateTime.UtcNow);

    // 3. Publication dans la file "orders"
    await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
    await channel.QueueDeclareAsync("orders", durable: true, exclusive: false,
                                    autoDelete: false, cancellationToken: ct);

    var body = JsonSerializer.SerializeToUtf8Bytes(order);
    var properties = new BasicProperties { Persistent = true };

    await channel.BasicPublishAsync(exchange: "", routingKey: "orders", mandatory: false,
                                    basicProperties: properties, body: body, cancellationToken: ct);

    // 4. Réponse immédiate : la commande est acceptée, pas encore traitée
    return Results.Accepted($"/api/orders/{order.OrderId}", order);
});

app.MapDefaultEndpoints();

app.Run();