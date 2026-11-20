using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ShopLite.OrderWorker;

public class OrderProcessor(IConnection connection, ILogger<OrderProcessor> logger) : BackgroundService
{
    private const string QueueName = "orders";

    // Source de traces et de métriques propres à notre métier
    private static readonly ActivitySource ActivitySource = new("ShopLite.Orders");
    private static readonly Meter Meter = new("ShopLite.Orders");
    private static readonly Counter<long> OrdersProcessed =
        Meter.CreateCounter<long>("shoplite.orders.processed", description: "Nombre de commandes traitées");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false,
                                        autoDelete: false, cancellationToken: stoppingToken);

        // Un seul message à la fois : on n'en reçoit pas un nouveau tant que le précédent n'est pas confirmé
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
                                    cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var order = JsonSerializer.Deserialize<OrderPlaced>(ea.Body.Span)!;

            using var activity = ActivitySource.StartActivity("Traitement commande");
            activity?.SetTag("order.id", order.OrderId);
            activity?.SetTag("order.quantity", order.Quantity);

            logger.LogInformation("Traitement de la commande {OrderId} : {Quantity} x produit {ProductId}",
                                  order.OrderId, order.Quantity, order.ProductId);

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);

            OrdersProcessed.Add(1);
            logger.LogInformation("Commande {OrderId} traitée", order.OrderId);
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer,
                                        cancellationToken: stoppingToken);

        // Le consommateur travaille en tâche de fond : on attend simplement l'arrêt du service
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}