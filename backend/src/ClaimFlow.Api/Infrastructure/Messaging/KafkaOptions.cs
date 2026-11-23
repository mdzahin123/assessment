namespace ClaimFlow.Api.Infrastructure.Messaging;

/// <summary>Kafka settings, read from the "Kafka" section of appsettings.json.</summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ClaimEventsTopic { get; set; } = "claim-events";
    public int ClaimEventsPartitions { get; set; } = 3;
    public string NotificationsConsumerGroup { get; set; } = "claimflow-notifications";
}
