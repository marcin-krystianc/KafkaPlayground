﻿using System.ComponentModel;
using Confluent.Kafka;
using Spectre.Console.Cli;

namespace KafkaTool;

public class ProducerSequentialSettings : KafkaSettings
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

    [CommandOption("--min-isr")]
    [Description("Minimum in-sync replicas for created topics.")]
    [DefaultValue(1)]
    public int MinISR { get; set; }

    [CommandOption("--messages-per-second")]
    [Description("Number of messages per second")]
    [DefaultValue(1000)]
    public long MessagesPerSecond { get; set; }

    [CommandOption("--acks")]
    [Description("Acks for produced message: All, None, Leader")]
    [DefaultValue("Leader")]
    public Acks Acks { get; set; }
}