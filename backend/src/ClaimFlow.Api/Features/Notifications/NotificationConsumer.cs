using System.Text.Json;
using ClaimFlow.Api.Infrastructure.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Features.Notifications;

/// <summary>
/// Reads claim events from Kafka and turns them into notifications.
///
/// - Manual commit, AFTER the notification is saved: the bookmark only moves forward on success
///   (at-least-once; duplicates are handled by NotificationHandler).
/// - A temporary failure (e.g. database down): re-read the same message after a pause, don't skip it.
/// - An unreadable message: log and skip, so one bad message can't block everything behind it.
///   (A real system would move it to a "dead-letter" topic for investigation.)
/// </summary>
public sealed class NotificationConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<NotificationConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    // Consume() BLOCKS its thread while waiting for messages. Running the loop via Task.Run keeps it off
    // the startup path, so the web API starts and serves requests normally.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var kafka = options.Value;
        await KafkaTopics.EnsureClaimEventsTopicAsync(kafka, logger, ct);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = kafka.NotificationsConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest, // a brand-new group starts from the oldest event: nothing missed
            EnableAutoCommit = false                    // we move the bookmark ourselves, only after success
        })
            .SetLogHandler((_, log) => logger.LogDebug("Kafka consumer: {Message}", log.Message))
            .SetErrorHandler((_, error) => logger.LogWarning("Kafka consumer error: {Reason}", error.Reason))
            .Build();

        consumer.Subscribe(kafka.ClaimEventsTopic);
        logger.LogInformation("Notification consumer subscribed to {Topic} as group {Group}",
            kafka.ClaimEventsTopic, kafka.NotificationsConsumerGroup);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning("Kafka consume failed: {Reason}", ex.Error.Reason);
                    continue;
                }

                try
                {
                    var claimEvent = JsonSerializer.Deserialize<ClaimEvent>(result.Message.Value, EventJson.Options)
                        ?? throw new JsonException("Empty event payload.");

                    await using (var scope = scopeFactory.CreateAsyncScope())
                    {
                        await scope.ServiceProvider.GetRequiredService<NotificationHandler>().HandleAsync(claimEvent, ct);
                    }

                    consumer.Commit(result);
                }
                catch (JsonException ex)
                {
                    logger.LogError(ex, "Skipping unreadable message at {Position}", result.TopicPartitionOffset);
                    consumer.Commit(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to handle event at {Position}; retrying in {Delay}s",
                        result.TopicPartitionOffset, RetryDelay.TotalSeconds);
                    consumer.Seek(result.TopicPartitionOffset); // read the same message again
                    await Task.Delay(RetryDelay, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            consumer.Close(); // leave the group cleanly so partitions are handed over quickly
        }
    }
}
