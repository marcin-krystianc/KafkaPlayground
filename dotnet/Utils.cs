using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;

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
    
    public static string GetTopicName(string topicStem, int i)
    {
        return $"{topicStem}-{i}";
    }
    
    public static bool TopicExists(IAdminClient adminClient, string topic)
    {
        var topicMetadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(30));
        return topicMetadata.Topics.Single().Partitions.Count > 0;
    }
    
    public static IReadOnlyList<string> GetExistingTopics(IAdminClient adminClient)
    {
        var topicMetadata = adminClient.GetMetadata(TimeSpan.FromSeconds(30));
        return topicMetadata.Topics
            .Where(x => x.Partitions.Count > 0)
            .Select(x => x.Topic)
            .ToList();
    }
    
    public static async Task CreateTopicAsync(IAdminClient adminClient, IReadOnlyCollection<string> topics, int numPartitions, int replicationFactor, int minIsr)
    {
        Log.Log(LogLevel.Information, ($"Creating {topics.Count} topics"));

        await adminClient.CreateTopicsAsync(
            topics.Select(x =>  new TopicSpecification
            {
                Configs = new Dictionary<string, string> { { "min.insync.replicas", $"{minIsr}" } },
                Name = x,
                NumPartitions = numPartitions,
                ReplicationFactor = (short)replicationFactor,
            }).ToArray(),
            new CreateTopicsOptions
            {
                OperationTimeout = TimeSpan.FromSeconds(30),
                RequestTimeout = TimeSpan.FromSeconds(30),
            });
    }

    public static async Task DeleteTopicAsync(IAdminClient adminClient, IReadOnlyCollection<string> topics)
    {
        Log.Log(LogLevel.Information, ($"Removing {topics.Count} topics"));
        await adminClient.DeleteTopicsAsync(topics, new DeleteTopicsOptions
        {
            OperationTimeout = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(30),
        });
    }

    public static async Task WaitForClusterReadyAsync(IAdminClient adminClient)
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

    public static IAdminClient GetAdminClient(IEnumerable<KeyValuePair<string, string>> configuration)
    {
        return new AdminClientBuilder(configuration.AsEnumerable())
            .SetErrorHandler((_, e) => Log.Log(LogLevel.Error,
                $"Admin error: reason={e.Reason}, IsLocal={e.IsLocalError}, IsBroker={e.IsBrokerError}, IsFatal={e.IsFatal}, IsCode={e.Code}"))
            .SetLogHandler((_, m) => Log.Log(LogLevel.Information,
                $"Admin log: message={m.Message}, name={m.Name}, facility={m.Facility}, level={m.Level}"))
            .Build();
    }

    public static async Task RecreateTopics(ProducerConsumerSettings settings)
    {
        using var adminClient = Utils.GetAdminClient(settings.ConfigDictionary);

        var existingTopics = Utils.GetExistingTopics(adminClient);
            
        foreach (var batch in Enumerable.Range(0, settings.Topics)
                     .Select(x => Utils.GetTopicName(settings.TopicStem, x))
                     .Intersect(existingTopics)
                     .Batch(settings.RecreateTopicsBatchSize))
        {
            await DeleteTopicAsync(adminClient, batch.ToArray());
            await Task.Delay(TimeSpan.FromMilliseconds(settings.RecreateTopicsDelayMs));
        }

        foreach (var batch in Enumerable.Range(0, settings.Topics)
                     .Batch(settings.RecreateTopicsBatchSize))
        {
            var topics = batch
                .Select(x => Utils.GetTopicName(settings.TopicStem, x))
                .ToArray();

            await CreateTopicAsync(adminClient, topics, numPartitions: settings.Partitions,
                settings.ReplicationFactor,
                settings.MinISR);

            await Task.Delay(TimeSpan.FromMilliseconds(settings.RecreateTopicsDelayMs));
        }
    }
}