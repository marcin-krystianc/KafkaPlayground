using System.ComponentModel;
using Spectre.Console.Cli;

namespace KafkaTool;

public class ProducerConsumerSettings : KafkaSettings
{
    [CommandOption("--producers")]
    [Description("Number of producers")]
    [DefaultValue(1)]
    public int Producers { get; set; }

    [CommandOption("--topics")]
    [Description("Number of topics")]
    [DefaultValue(1)]
    public int Topics { get; set; }
    
    [CommandOption("--topic-stem")]
    [Description("Topic stem")]
    [DefaultValue("my-topic")]
    public string TopicStem { get; set; }

    [CommandOption("--partitions")]
    [Description("Number of partitions per topic")]
    [DefaultValue(10)]
    public int Partitions { get; set; }

    [CommandOption("--replication-factor")]
    [Description("Number of replicas")]
    [DefaultValue(2)]
    public int ReplicationFactor { get; set; }

    [CommandOption("--min-isr")]
    [Description("Minimum in-sync replicas for created topics.")]
    [DefaultValue(1)]
    public int MinISR { get; set; }

    [CommandOption("--messages-per-second")]
    [Description("Number of messages per second")]
    [DefaultValue((long)1000)]
    public long MessagesPerSecond { get; set; }

    [CommandOption("--recreate-topics")]
    [Description("Recreate topics?")]
    [DefaultValue(true)]
    public bool RecreateTopics { get; set; }

    [CommandOption("--recreate-topics-delay")]
    [Description("Recreate topics delay in ms")]
    [DefaultValue((long)1000)]
    public long RecreateTopicsDelayMs { get; set; }

    [CommandOption("--recreate-topics-batch-size")]
    [Description("Recreate topics batch size")]
    [DefaultValue(500)]
    public int RecreateTopicsBatchSize { get; set; }

    [CommandOption("--burst-messages-per-second")]
    [Description("Number of messages per second (burst)")]
    [DefaultValue((long)0)]
    public long BurstMessagesPerSecond { get; set; }

    [CommandOption("--burst-cycle")]
    [Description("Burst cycle in ms")]
    [DefaultValue((long)0)]
    public long BurstCycle { get; set; }

    [CommandOption("--burst-duration")]
    [Description("Burst duration in ms")]
    [DefaultValue((long)0)]
    public long BurstDuration { get; set; }

    [CommandOption("--reporting-cycle")]
    [Description("Reporting cycle in ms")]
    [DefaultValue((long)10000)]
    public long ReportingCycle { get; set; }

    [CommandOption("--statistics-path")]
    [Description("Where to rite statistics json")]
    [DefaultValue("statistics.txt")]
    public string StatisticsPath { get; set; }

    [CommandOption("--extra-payload-bytes")]
    [Description("Additional payload in bytes")]
    [DefaultValue((long)0)]
    public long ExtraPayloadBytes { get; set; }

    [CommandOption("--avoid-allocations")]
    [Description("Avoid unnecessary memory allocations")]
    [DefaultValue(true)]
    public bool AvoidAllocations { get; set; }

    [CommandOption("--exit-after")]
    [Description("Exits after provided amount of seconds")]
    [DefaultValue((long)0)]
    public long ExitAfter { get; set; }

    [CommandOption("--enable-sequence-validation")]
    [Description("Indicates that the consumer will not validate whether the messages are in sequence.")]
    [DefaultValue(true)]
    public bool EnableSequenceValidation { get; set; }

    [CommandOption("--set-oauth-token-callback")]
    [Description("Sets the OIDCtoken refresh callback")]
    [DefaultValue(false)]
    public bool SetOAuthTokenCallback { get; set; }
}