var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.ShopLite_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.ShopLite_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
