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

    [CommandOption("--recreate-topics-delay")]
    [Description("Recreate topics delay in ms")]
    [DefaultValue((long)1000)]
    public long RecreateTopicsDelayMs { get; set; }

    [CommandOption("--recreate-topics-batch-size")]
    [Description("Recreate topics batch size")]
    [DefaultValue(500)]
    public int RecreateTopicsBatchSize { get; set; }

    [CommandOption("--recreate-topics")]
    [Description("Recreate topics")]
    [DefaultValue(true)]
    public bool RecreateTopics { get; set; }
}