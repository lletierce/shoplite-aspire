using ShopLite.OrderWorker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("messaging");
builder.Services.AddHostedService<OrderProcessor>();

var host = builder.Build();
host.Run();