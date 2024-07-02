﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

public static class Utils
{
    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Log");
    
    public static string GetTopicName(int i)
    {
        return $"topic-{i}";
    }
    
    public static bool TopicExists(IAdminClient adminClient, string topic)
    {
        var topicMetadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(30));
        return topicMetadata.Topics.Single().Partitions.Count > 0;
    }

    public static async Task CreateTopicAsync(IAdminClient adminClient, string topic, int numPartitions, int replicationFactor, int minIsr)
    {
        Log.Log(LogLevel.Information, ($"Creating a topic: {topic}"));

        await adminClient.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Configs = new Dictionary<string, string> { { "min.insync.replicas", $"{minIsr}" } },
                    Name = topic,
                    NumPartitions = numPartitions,
                    ReplicationFactor = (short)replicationFactor,
                }
            },
            new CreateTopicsOptions
            {
                OperationTimeout = TimeSpan.FromSeconds(30),
                RequestTimeout = TimeSpan.FromSeconds(30),
            });
    }

    public static async Task DeleteTopicAsync(IAdminClient adminClient, string topic)
    {
        Log.Log(LogLevel.Information, ($"Removing a topic: {topic}"));
        await adminClient.DeleteTopicsAsync(new[] { topic }, new DeleteTopicsOptions
        {
            OperationTimeout = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(30),
        });
    }

    public static async Task WaitForCLusterReadyAsync(IAdminClient adminClient)
    {
        while (true)
        {
            var meta = adminClient.GetMetadata(TimeSpan.FromSeconds(60));
            var isrCount = meta.Topics
                .SelectMany(x => x.Partitions)
                .SelectMany(x => x.InSyncReplicas)
                .Count();

            var replicas = meta.Topics
                .SelectMany(x => x.Partitions)
                .SelectMany(x => x.Replicas)
                .Count();

            if (isrCount != replicas)
            {
                Log.Log(LogLevel.Information, $"isrCount = {isrCount}, replicas  = {replicas}.");
                await Task.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            break;
        }
    }
}