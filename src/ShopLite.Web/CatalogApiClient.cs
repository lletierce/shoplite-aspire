namespace ShopLite.Web;

public class CatalogApiClient(HttpClient httpClient)
{
    public async Task<ProductDto[]> GetProductsAsync(CancellationToken cancellationToken = default)
        => await httpClient.GetFromJsonAsync<ProductDto[]>("/api/products", cancellationToken) ?? [];
}

public record ProductDto(int Id, string Name, string? Description, decimal Price, int Stock);