using System.ComponentModel;
using Spectre.Console.Cli;

namespace KafkaTool;

public class Producer1Settings : KafkaSettings
{
    [CommandOption("--tasks")]
    [Description("Number of tasks")]
    [DefaultValue(1)]
    public int TaskCount { get; set; }
    
    [CommandOption("--partition")]
    [Description("Id of partition to query")]
    [DefaultValue(0)]
    public int PartitionNumber { get; set; }
    
    [CommandOption("--duration")]
    [Description("Duration in seconds")]
    public int? Duration { get; set; }
    
    [CommandOption("--records")]
    [Description("Records or requests to read, then stop.")]
    public long? Records { get; set; }
}