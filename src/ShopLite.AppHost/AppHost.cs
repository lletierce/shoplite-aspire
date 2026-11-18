var builder = DistributedApplication.CreateBuilder(args);

// 1. Le serveur PostgreSQL (un conteneur Docker)
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()   // les données survivent aux redémarrages
    .WithPgAdmin();     // interface web pour explorer la base

// 2. Une base de données sur ce serveur
var catalogDb = postgres.AddDatabase("catalogdb");

// 3. L'API reçoit la base et attend qu'elle soit prête
var apiService = builder.AddProject<Projects.ShopLite_ApiService>("apiservice")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.ShopLite_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
