using System.ComponentModel;
using Confluent.Kafka;
using Spectre.Console.Cli;

namespace KafkaTool;

public class ConsumerSettings : KafkaSettings
{
    [CommandOption("--topics")]
    [Description("Number of topics")]
    [DefaultValue(1)]
    public int Topics { get; set; }
    
    [CommandOption("--topic")]
    [Description("Topic stem")]
    [DefaultValue("topic")]
    public string TopicStem { get; set; }
    
    [CommandOption("--partitions")]
    [Description("Number of partitions per topic")]
    [DefaultValue(10)]
    public int Partitions { get; set; }
}