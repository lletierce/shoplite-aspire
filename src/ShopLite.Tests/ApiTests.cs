using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace ShopLite.Tests;

public class ApiTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    // Démarre toute l'application (AppHost + conteneurs) et attend que l'API soit prête
    private static async Task<DistributedApplication> StartAppAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.ShopLite_AppHost>(
                ["--ShopLite:UseDataVolume=false"],
                cts.Token);

        var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        try
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cts.Token);
        }
        catch (Exception ex)
        {
            // En cas d'échec : on récupère les logs de l'API pour comprendre pourquoi
            var logs = await GetResourceLogsAsync(app, "apiservice");
            throw new Exception($"L'API n'a pas démarré. Ses logs :\n{logs}", ex);
        }

        return app;
    }

    // Lit les logs déjà produits par une ressource (pendant 5 secondes maximum)
    private static async Task<string> GetResourceLogsAsync(DistributedApplication app, string resourceName)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = model.Resources.Single(r => r.Name == resourceName);
        var loggerService = app.Services.GetRequiredService<ResourceLoggerService>();

        var logs = new StringBuilder();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var batch in loggerService.WatchAsync(resource).WithCancellation(timeout.Token))
            {
                foreach (var line in batch)
                {
                    logs.AppendLine(line.Content);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal : on arrête de lire au bout de 5 secondes
        }

        return logs.ToString();
    }

    [Fact]
    public async Task GetProducts_ReturnsSeededProducts()
    {
        // Arrange
        await using var app = await StartAppAsync();
        var client = app.CreateHttpClient("apiservice");

        // Act
        var products = await client.GetFromJsonAsync<ProductResponse[]>("/api/products");

        // Assert
        Assert.NotNull(products);
        Assert.NotEmpty(products);
    }

    [Fact]
    public async Task PlaceOrder_WithInvalidQuantity_ReturnsBadRequest()
    {
        // Arrange
        await using var app = await StartAppAsync();
        var client = app.CreateHttpClient("apiservice");

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new { productId = 1, quantity = 0 });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_InvalidatesCatalogCache()
    {
        // Arrange
        await using var app = await StartAppAsync();
        var client = app.CreateHttpClient("apiservice");
        var uniqueName = $"Produit test {Guid.NewGuid():N}";

        await client.GetFromJsonAsync<ProductResponse[]>("/api/products");   // remplit le cache

        // Act
        var created = await client.PostAsJsonAsync("/api/products",
            new { name = uniqueName, price = 10m, stock = 1 });
        created.EnsureSuccessStatusCode();

        var products = await client.GetFromJsonAsync<ProductResponse[]>("/api/products");

        // Assert
        Assert.Contains(products!, p => p.Name == uniqueName);
    }
}

public record ProductResponse(int Id, string Name, decimal Price, int Stock);