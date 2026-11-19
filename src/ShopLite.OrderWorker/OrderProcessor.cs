using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ShopLite.OrderWorker;

public class OrderProcessor(IConnection connection, ILogger<OrderProcessor> logger) : BackgroundService
{
    private const string QueueName = "orders";

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
            logger.LogInformation("Traitement de la commande {OrderId} : {Quantity} x produit {ProductId}",
                                  order.OrderId, order.Quantity, order.ProductId);

            // Simule un traitement long : paiement, email de confirmation...
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            // Confirmation à RabbitMQ : le message peut être supprimé de la file
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            logger.LogInformation("Commande {OrderId} traitée", order.OrderId);
        };

        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer,
                                        cancellationToken: stoppingToken);

        // Le consommateur travaille en tâche de fond : on attend simplement l'arrêt du service
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}