using System.ComponentModel;
using Spectre.Console.Cli;

namespace KafkaTool;

public class ProducerSettings : KafkaSettings
{
    [CommandOption("--topics")]
    [Description("Number of topics")]
    [DefaultValue(1)]
    public int Topics { get; set; }
    
    [CommandOption("--partitions")]
    [Description("Number of partitions per topic")]
    [DefaultValue(10)]
    public int Partitions { get; set; }
    
    [CommandOption("--replication-factor")]
    [Description("Number of replicas")]
    [DefaultValue(2)]
    public int ReplicationFactor { get; set; }
    
    [CommandOption("--producers")]
    [Description("Number of producers")]
    [DefaultValue(1)]
    public int Producers { get; set; }
    
    [CommandOption("--messages-per-second")]
    [Description("Number of messages per second")]
    [DefaultValue(1000)]
    public long MessagesPerSecond { get; set; }
}