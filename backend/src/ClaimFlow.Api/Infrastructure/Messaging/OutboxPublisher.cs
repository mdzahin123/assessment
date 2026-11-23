using System.Text;
using ClaimFlow.Api.Infrastructure.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Api.Infrastructure.Messaging;

/// <summary>
/// The "courier": every second, sends unpublished outbox messages to Kafka (oldest first) and ticks them off.
///
/// - Kafka down? Sends fail, rows stay unpublished, we retry later. The API is unaffected.
/// - Order: messages go out oldest-first, and a failure STOPS the batch (we never skip ahead),
///   so one claim's events can't overtake each other between the database and Kafka.
/// - At-least-once: a crash after Kafka accepted a message but before we ticked it off means it
///   is sent again later. Consumers handle that by ignoring duplicate event IDs.
/// - Assumes a single API instance. With several, each would publish the same rows (duplicates are
///   still harmless, just wasteful); a row lock (SELECT ... FOR UPDATE SKIP LOCKED) would be the next step.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    TimeProvider time,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafka = options.Value;
        await KafkaTopics.EnsureClaimEventsTopicAsync(kafka, logger, stoppingToken);

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,            // only count a message as sent once the broker has stored it
            EnableIdempotence = true,   // the broker drops duplicates caused by the producer's own network retries
            MessageTimeoutMs = 10_000   // give up on one send after 10s; the row stays unpublished and is retried
        })
            .SetLogHandler((_, log) => logger.LogDebug("Kafka producer: {Message}", log.Message))
            .Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sent = await PublishBatchAsync(producer, kafka.ClaimEventsTopic, stoppingToken);
                if (sent < BatchSize)
                    await Task.Delay(IdleDelay, time, stoppingToken); // nothing (more) to do right now
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // app is shutting down
            }
            catch (Exception ex)
            {
                logger.LogWarning("Outbox publishing paused ({Reason}); retrying in {Delay}s", ex.Message, ErrorDelay.TotalSeconds);
                await Task.Delay(ErrorDelay, time, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(IProducer<string, string> producer, string topic, CancellationToken ct)
    {
        // A fresh scope (and DbContext) per batch: DbContext is not thread-safe and should be short-lived.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var batch = await db.OutboxMessages
            .Where(m => m.PublishedAt == null)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in batch)
        {
            try
            {
                await producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = message.Key,
                    Value = message.Payload,
                    Headers = new Headers { { "event-type", Encoding.UTF8.GetBytes(message.Type) } }
                }, ct);
            }
            catch (ProduceException<string, string> ex)
            {
                message.RecordFailure(ex.Error.Reason);
                await db.SaveChangesAsync(ct);
                throw; // stop here: sending later messages first would break ordering
            }

            message.MarkPublished(time.GetUtcNow());
            await db.SaveChangesAsync(ct); // tick off one at a time, so a crash re-sends at most one message
        }

        if (batch.Count > 0)
            logger.LogInformation("Published {Count} claim event(s) to {Topic}", batch.Count, topic);
        return batch.Count;
    }
}
