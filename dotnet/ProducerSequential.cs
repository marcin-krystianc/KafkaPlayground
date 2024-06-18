using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Spectre.Console.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

public sealed class ProducerSequential : AsyncCommand<ProducerSequentialSettings>
{
    private IAdminClient _adminClient;

    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Producer");

    public override async Task<int> ExecuteAsync(CommandContext context, ProducerSequentialSettings settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddIniFile(settings.IniFile)
            .Build();

        _adminClient = new AdminClientBuilder(configuration.AsEnumerable())
            .Build();

        var queue = new ConcurrentQueue<(string topic, long k, long v)>();

        for (int i = 0; i < settings.Topics; i++)
        {
            string topic = $"topic{i}";
            if (!TopicExists(topic))
            {
                await CreateTopicAsync(topic, numPartitions: settings.Partitions, settings.ReplicationFactor);
                await Task.Delay(TimeSpan.FromMilliseconds(1));
            }
        }

        await WaitForCLusterReadyAsync(settings.Topics, settings.Partitions, settings.ReplicationFactor);

        long msgToEnqueue = 0;
        var msgControllerTask = Task.Run(async () =>
        {
            for (;;)
            {
                Interlocked.Exchange(ref msgToEnqueue, settings.MessagesPerSecond);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        });
      
        var msgCreationTasks = Enumerable.Range(0, settings.Topics)
            .Select(i => Task.Run(async () =>
            {
                string topic = $"topic{i}";
                var rnd = new Random();
                Dictionary<long, long> valueDictionary = new ();

                for (;;)
                {
                    if (Interlocked.Decrement(ref msgToEnqueue) < 0 || queue.Count > 1_0000_000)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1));
                        continue;
                    }

                    var m = rnd.NextInt64();
                    long key = m % (settings.Partitions * 7);
                    var nextValue = valueDictionary.GetValueOrDefault(key);
                    queue.Enqueue((topic: topic, k: key, v: nextValue));
                    valueDictionary[key] = nextValue + 1;
                }
            }));

        var consumerTasks = Enumerable.Range(0, settings.Topics)
            .Select(i => Task.Run( () =>
            {
                string topic = $"topic{i}";

                using (var consumer = new ConsumerBuilder<long, long>(
                               configuration.AsEnumerable())
                           .SetErrorHandler((_, e) => Console.WriteLine($"Error: {e.Reason}"))
                           .SetStatisticsHandler((_, json) => Console.WriteLine($"Statistics: <<json>>"))
                           .SetPartitionsAssignedHandler((c, partitions) =>
                           {
                               // Since a cooperative assignor (CooperativeSticky) has been configured, the
                               // partition assignment is incremental (adds partitions to any existing assignment).
                               Console.WriteLine(
                                   "Partitions incrementally assigned: [" +
                                   string.Join(',', partitions.Select(p => p.Partition.Value)) +
                                   "], all: [" +
                                   string.Join(',', c.Assignment.Concat(partitions).Select(p => p.Partition.Value)) +
                                   "]");

                               // Possibly manually specify start offsets by returning a list of topic/partition/offsets
                               // to assign to, e.g.:
                               // return partitions.Select(tp => new TopicPartitionOffset(tp, externalOffsets[tp]));
                           })
                           .SetPartitionsRevokedHandler((c, partitions) =>
                           {
                               // Since a cooperative assignor (CooperativeSticky) has been configured, the revoked
                               // assignment is incremental (may remove only some partitions of the current assignment).
                               var remaining = c.Assignment.Where(atp =>
                                   partitions.Where(rtp => rtp.TopicPartition == atp).Count() == 0);
                               Console.WriteLine(
                                   "Partitions incrementally revoked: [" +
                                   string.Join(',', partitions.Select(p => p.Partition.Value)) +
                                   "], remaining: [" +
                                   string.Join(',', remaining.Select(p => p.Partition.Value)) +
                                   "]");
                           })
                           .SetPartitionsLostHandler((c, partitions) =>
                           {
                               // The lost partitions handler is called when the consumer detects that it has lost ownership
                               // of its assignment (fallen out of the group).
                               Console.WriteLine($"Partitions were lost: [{string.Join(", ", partitions)}]");
                           })
                           .Build())
                {
                    Dictionary<long, long> valueDictionary = new();
                    consumer.Subscribe(topic);

                    while (true)
                    {
                        var consumeResult = consumer.Consume();

                        if (consumeResult.IsPartitionEOF)
                        {
                            throw new Exception($"Reached end of topic {consumeResult.Topic}, partition {consumeResult.Partition}, offset {consumeResult.Offset}.");
                        }

                        long expectedValue = valueDictionary.GetValueOrDefault(consumeResult.Message.Key);
                        if (consumeResult.Message.Value != expectedValue)
                        {
                            throw new Exception("TODO");
                        }
                    }
                }
            }));

        long numProduced = 0;
        var sw = Stopwatch.StartNew();

        // Only one producer to avoid messages reordering
        var producersTask = Enumerable.Range(0, 1)
            .Select(i => Task.Run(async () =>
            {
                var logger = LoggerFactory
                    .Create(builder => builder.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss ";
                    }))
                    .CreateLogger($"Producer{i}");

                logger.Log(LogLevel.Information, $"Starting producer task: {i}");

                Exception e = null;
                var producerConfig = new ProducerConfig
                {
                    Acks = Acks.Leader,
                };

                var producer = new ProducerBuilder<long, long>(
                        producerConfig.AsEnumerable().Concat(configuration.AsEnumerable()))
                    .SetLogHandler(
                        (a, b) =>
                        {
                            if (!b.Message.Contains(": Disconnected (after ",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                logger.LogInformation($"kafka-log Facility:{b.Facility}, Message{b.Message}");
                            }
                        })
                    .Build();

                var m = 0;
                for (;;)
                {
                    if (e != null)
                    {
                        logger.Log(LogLevel.Information, $"Exception: {e.Message}");
                        throw e;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(1));
                    while (queue.TryDequeue(out var kvp))
                    {
                        m++;
                        
                        if (m % 100000 == 0)
                        {
                            producer.Flush();
                        }

                        producer.Produce(kvp.topic, new Message<long, long> { Key = kvp.k, Value = kvp.v },
                            (deliveryReport) =>
                            {
                                if (deliveryReport.Error.Code != ErrorCode.NoError)
                                {
                                    var topicMetadata = _adminClient.GetMetadata(deliveryReport.Topic,
                                        TimeSpan.FromSeconds(30));
                                    var partitionsCount = topicMetadata.Topics.Single().Partitions.Count;

                                    producer = new ProducerBuilder<long, long>(
                                            producerConfig.AsEnumerable().Concat(configuration.AsEnumerable()))
                                        .SetLogHandler(
                                            (a, b) => logger.LogInformation(
                                                $"kafka-log Facility:{b.Facility}, Message{b.Message}"))
                                        .Build();

                                    if (e == null)
                                        e = new Exception(
                                            $"DeliveryReport.Error, Code = {deliveryReport.Error.Code}, Reason = {deliveryReport.Error.Reason}" +
                                            $", IsFatal = {deliveryReport.Error.IsFatal}, IsError = {deliveryReport.Error.IsError}" +
                                            $", IsLocalError = {deliveryReport.Error.IsLocalError}, IsBrokerError = {deliveryReport.Error.IsBrokerError}" +
                                            $", topic = {deliveryReport.Topic}, partition = {deliveryReport.Partition.Value}, partitionsCount = {partitionsCount}");
                                }
                                else
                                {
                                    Interlocked.Increment(ref numProduced);
                                }
                            });
                    }
                }
            }));

        var reporterTask = Task.Run(async () =>
        {
            var prevProduced = 0l;
            for (;;)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                var totalProduced = Interlocked.Read(ref numProduced);
                var newlyProduced = totalProduced - prevProduced;
                prevProduced = totalProduced;
                Log.Log(LogLevel.Information, $"Elapsed: {(int)sw.Elapsed.TotalSeconds}s, {totalProduced} (+{newlyProduced}) messages produced");
            }
        });

        var task = await Task.WhenAny(msgCreationTasks.Concat(producersTask)
            .Concat(consumerTasks)
            .Concat(new[] { msgControllerTask, reporterTask }));

        await task;
        return 0;
    }

    /// <summary>
    /// Returns true iff the topic <paramref name="topic"/> exists. 
    /// </summary>
    public bool TopicExists(string topic)
    {
        var topicMetadata = _adminClient.GetMetadata(topic, TimeSpan.FromSeconds(30));
        //Metadata is returned (with a topic) even if it doesn't exist on the broker, workaround based is to check
        //if it has partitions. https://github.com/confluentinc/confluent-kafka-go/issues/672
        return topicMetadata.Topics.Single().Partitions.Count > 0;
    }

    /// <summary>
    /// Creates a topic with the name <paramref name="topic"/> and <paramref name="numPartitions"/> partitions using
    /// all default settings, this should mirror the behaviour of publishing to a topic with 'allow.auto.create.topics'
    /// set to true.
    /// </summary>
    public async Task CreateTopicAsync(string topic, int numPartitions, int replicationFactor)
    {
        Log.Log(LogLevel.Information, ($"Creating topic: {topic}"));

        await _adminClient.CreateTopicsAsync(new[]
                {
                    new TopicSpecification
                    {
                        Name = topic,
                        NumPartitions = numPartitions,
                        ReplicationFactor = (short)replicationFactor,
                    }
                },
                new CreateTopicsOptions
                {
                    OperationTimeout = TimeSpan.FromSeconds(30)
                });
    }

    public async Task WaitForCLusterReadyAsync(int numTopics, int numPartitions, int numReplicas)
    {
        while (true)
        {
            var meta = _adminClient.GetMetadata(TimeSpan.FromSeconds(60));
            var isrCount = meta.Topics.SelectMany(x => x.Partitions).SelectMany(x => x.InSyncReplicas).Count();
            var expectedIsrCount = numTopics * numPartitions * numReplicas;
            if (isrCount != expectedIsrCount)
            {
                Log.Log(LogLevel.Information, $"isrCount = {isrCount}, expectedIsrCount = {expectedIsrCount}.");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            break;
        }
    }
}