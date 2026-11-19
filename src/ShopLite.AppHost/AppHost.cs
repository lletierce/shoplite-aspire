var builder = DistributedApplication.CreateBuilder(args);
var cache = builder.AddRedis("cache");

// 1. Le serveur PostgreSQL (un conteneur Docker)
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()   // les données survivent aux redémarrages
    .WithPgAdmin();     // interface web pour explorer la base

// 2. Une base de données sur ce serveur
var catalogDb = postgres.AddDatabase("catalogdb");

// 3. L'API reçoit les services et attend qu'ils soient prêts
var apiService = builder.AddProject<Projects.ShopLite_ApiService>("apiservice")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.ShopLite_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);



builder.Build().Run();
