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
        .CreateLogger("Log");

    public override async Task<int> ExecuteAsync(CommandContext context, ProducerSequentialSettings settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddIniFile(settings.IniFile)
            .Build();

        _adminClient = new AdminClientBuilder(configuration.AsEnumerable())
            .Build();
        var queue = new ConcurrentQueue<(string topic, long k, long v)>();

        var topicsCreated = false;
        for (int i = 0; i < settings.Topics; i++)
        {
            string topic = $"topic{i}";

            if (!TopicExists(topic))
            {
                topicsCreated = true;
                await CreateTopicAsync(topic, numPartitions: settings.Partitions, settings.ReplicationFactor,
                    settings.MinISR);
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                while (true)
                {
                    if (!TopicExists(topic))
                    {
                        Log.Log(LogLevel.Information, $"Waiting for {topic} to be created");
                        await Task.Delay(TimeSpan.FromMilliseconds(100));
                        continue;
                    }

                    break;
                }
            }

            await WaitForCLusterReadyAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(1));
        }

        if (topicsCreated)
        {
            var millisecondsPerTopic = 7;
            var delay = TimeSpan.FromMilliseconds(millisecondsPerTopic * settings.Topics * settings.Partitions);
            await WaitForCLusterReadyAsync();
            Log.Log(LogLevel.Information, $"Waiting for {(int)delay.TotalSeconds}s");
            await Task.Delay(delay);
        }

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
                Dictionary<long, long> valueDictionary = new();

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

        var messagesToReceive = new ConcurrentDictionary<(string Topic, long Key, long Value), DateTime>();
        long numConsumed = 0;
        long numOutOfSequence = 0;
        var producerSemaphore = new SemaphoreSlim(0);
        var consumerTasks = Enumerable.Range(0, 1)
            .Select(i => Task.Run(async () =>
            {
                var config = new ConsumerConfig
                {
                    GroupId = Guid.NewGuid().ToString(),
                    EnableAutoOffsetStore = true,
                    EnableAutoCommit = true,
                    AutoOffsetReset = AutoOffsetReset.Latest,
                    // A good introduction to the CooperativeSticky assignor and incremental rebalancing:
                    // https://www.confluent.io/blog/cooperative-rebalancing-in-kafka-streams-consumer-ksqldb/
                    PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
                };

                IConfiguration consumerConfiguration = new ConfigurationBuilder()
                    .AddInMemoryCollection(config)
                    .AddInMemoryCollection(settings.ConfigDictionary)
                    .AddIniFile(settings.IniFile)
                    .Build();

                using (var consumer = new ConsumerBuilder<long, long>(
                               consumerConfiguration.AsEnumerable())
                           .SetErrorHandler((_, e) => Console.WriteLine($"Consumer error: {e.Reason}"))
                           .Build())
                {
                    var topics = Enumerable.Range(0, settings.Topics)
                        .Select(x => $"topic{x}")
                        .ToArray();

                    consumer.Subscribe(topics);
                    
                    // Make sure consumer is really subscribed
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    
                    // Finally we can let the producer produce some message
                    producerSemaphore.Release(1);
                    Dictionary<(string Topic, long Key), ConsumeResult<long, long>> valueDictionary = new();

                    while (true)
                    {
                        var consumeResult = consumer.Consume();

                        if (consumeResult.IsPartitionEOF)
                        {
                            throw new Exception(
                                $"Reached end of topic {consumeResult.Topic}, partition {consumeResult.Partition}, offset {consumeResult.Offset}.");
                        }

                        messagesToReceive.Remove(
                            (consumeResult.Topic, consumeResult.Message.Key, consumeResult.Message.Value), out var _);
                        var key = (consumeResult.Topic, consumeResult.Message.Key);
                        if (valueDictionary.TryGetValue(key, out var previousResult))
                        {
                            if (consumeResult.Message.Value != previousResult.Message.Value + 1)
                            {
                                Log.Log(LogLevel.Error,
                                    $"Unexpected message value, topic/k [p]={consumeResult.Topic}/{consumeResult.Message.Key} {consumeResult.Partition}, Offset={previousResult.Offset}/{consumeResult.Offset}, " +
                                    $"LeaderEpoch={previousResult.LeaderEpoch}/{consumeResult.LeaderEpoch},  previous value={previousResult.Message.Value}, messageValue={consumeResult.Message.Value}, numConsumed={numConsumed} !");

                                Interlocked.Increment(ref numOutOfSequence);
                            }

                            valueDictionary[key] = consumeResult;
                        }
                        else
                        {
                            valueDictionary[key] = consumeResult;
                        }

                        Interlocked.Increment(ref numConsumed);
                    }
                }
            }));

        long numProduced = 0;
        var sw = Stopwatch.StartNew();

        // Only one producer to avoid messages reordering
        var producersTask = Enumerable.Range(0, 1)
            .Select(i => Task.Run(async () =>
            {
                await producerSemaphore.WaitAsync();

                var logger = LoggerFactory
                    .Create(builder => builder.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss ";
                    }))
                    .CreateLogger($"Producer{i}");

                logger.Log(LogLevel.Information, $"Starting producer task: {i}");

                Exception e = null;
                var producerConfig = new ProducerConfig(settings.ConfigDictionary)
                {
                    Acks = settings.Acks,
                    MessageTimeoutMs = 60000,
                    RequestTimeoutMs = 120000,
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

                        var msg = new Message<long, long> { Key = kvp.k, Value = kvp.v };
                        messagesToReceive.AddOrUpdate((kvp.topic, kvp.k, kvp.v), DateTime.UtcNow,
                            (_, _) => DateTime.Now);
                        producer.Produce(kvp.topic, msg,
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
            var prevConsumed = 0l;
            for (;;)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                var totalProduced = Interlocked.Read(ref numProduced);
                var totalConsumed = Interlocked.Read(ref numConsumed);
                var totalDuplicated = Interlocked.Read(ref numOutOfSequence);
                var newlyProduced = totalProduced - prevProduced;
                var newlyConsumed = totalConsumed - prevConsumed;
                prevProduced = totalProduced;
                prevConsumed = totalConsumed;
                string oldestMessageString = string.Empty;
                var oldestMessages = messagesToReceive.ToArray();
                if (oldestMessages.Any())
                {
                    var oldestMessage = oldestMessages.MinBy(x => x.Value);
                    var age = (DateTime.UtcNow - oldestMessage.Value);
                    if (age > TimeSpan.FromSeconds(10))
                    {
                        oldestMessageString =
                            $"Oldest topic:{oldestMessage.Key.Topic}, k:{oldestMessage.Key.Key}, v:{oldestMessage.Key.Value}, age:{(int)age.TotalSeconds}s";
                    }
                }

                Log.Log(LogLevel.Information,
                    $"Elapsed: {(int)sw.Elapsed.TotalSeconds}s, {totalProduced} (+{newlyProduced}) messages produced, {totalConsumed} (+{newlyConsumed}) messages consumed, {totalDuplicated} duplicated. {oldestMessageString}");
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
    public async Task CreateTopicAsync(string topic, int numPartitions, int replicationFactor, int minIsr)
    {
        Log.Log(LogLevel.Information, ($"Creating a topic: {topic}"));

        await _adminClient.CreateTopicsAsync(new[]
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

    public async Task DeleteTopicAsync(string topic)
    {
        Log.Log(LogLevel.Information, ($"Removing a topic: {topic}"));
        await _adminClient.DeleteTopicsAsync(new[] { topic }, new DeleteTopicsOptions
        {
            OperationTimeout = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(30),
        });
    }

    public async Task WaitForCLusterReadyAsync()
    {
        while (true)
        {
            var meta = _adminClient.GetMetadata(TimeSpan.FromSeconds(60));
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