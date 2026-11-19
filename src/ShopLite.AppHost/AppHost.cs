var builder = DistributedApplication.CreateBuilder(args);
var cache = builder.AddRedis("cache");

// Le serveur PostgreSQL (un conteneur Docker)
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()   // les données survivent aux redémarrages
    .WithPgAdmin();     // interface web pour explorer la base

// Une base de données sur ce serveur
var catalogDb = postgres.AddDatabase("catalogdb");

// Un serveur RabbitMQ (un conteneur Docker)
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();   // interface web pour voir les files

// L'API reçoit les services et attend qu'ils soient prêts
var apiService = builder.AddProject<Projects.ShopLite_ApiService>("apiservice")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.ShopLite_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddProject<Projects.ShopLite_OrderWorker>("orderworker")
    .WithReference(messaging)
    .WaitFor(messaging);

builder.Build().Run();
