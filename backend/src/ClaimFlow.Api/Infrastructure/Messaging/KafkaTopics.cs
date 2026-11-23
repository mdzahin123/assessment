using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace ClaimFlow.Api.Infrastructure.Messaging;

/// <summary>
/// Creates the claim-events topic explicitly (with our partition count) instead of relying on
/// auto-creation. Keeps retrying while Kafka is unreachable: the API itself works without Kafka,
/// events simply wait in the outbox until Kafka is back.
/// </summary>
public static class KafkaTopics
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    public static async Task EnsureClaimEventsTopicAsync(KafkaOptions kafka, ILogger logger, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers })
                    .SetLogHandler((_, _) => { }) // connection noise is reported via the warning below instead
                    .Build();
                await admin.CreateTopicsAsync(
                    [new TopicSpecification { Name = kafka.ClaimEventsTopic, NumPartitions = kafka.ClaimEventsPartitions, ReplicationFactor = 1 }],
                    new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                logger.LogInformation("Created Kafka topic {Topic} with {Partitions} partitions", kafka.ClaimEventsTopic, kafka.ClaimEventsPartitions);
                return;
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                return; // already there: nothing to do
            }
            catch (KafkaException ex)
            {
                logger.LogWarning("Kafka not reachable yet ({Reason}); retrying in {Delay}s", ex.Error.Reason, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, ct);
            }
        }
    }
}
