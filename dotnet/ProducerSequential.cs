using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
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
            .AddInMemoryCollection(settings.ConfigDictionary)
            .Build();

        _adminClient = new AdminClientBuilder(configuration.AsEnumerable())
            .Build();

        var topicsCreated = false;
        for (int i = 0; i < settings.Topics; i++)
        {
            string topic = Utils.GetTopicName(i);

            if (!Utils.TopicExists(_adminClient, topic))
            {
                topicsCreated = true;
                await Utils.CreateTopicAsync(_adminClient, topic, numPartitions: settings.Partitions,
                    settings.ReplicationFactor,
                    settings.MinISR);
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                while (true)
                {
                    if (!Utils.TopicExists(_adminClient, topic))
                    {
                        Log.Log(LogLevel.Information, $"Waiting for {topic} to be created");
                        await Task.Delay(TimeSpan.FromMilliseconds(100));
                        continue;
                    }

                    break;
                }
            }

            await Utils.WaitForCLusterReadyAsync(_adminClient);
            await Task.Delay(TimeSpan.FromMilliseconds(1));
        }

        if (topicsCreated)
        {
            var millisecondsPerTopic = 31;
            var delay = TimeSpan.FromMilliseconds(millisecondsPerTopic * settings.Topics * settings.Partitions);
            await Utils.WaitForCLusterReadyAsync(_adminClient);
            Log.Log(LogLevel.Information, $"Waiting for {(int)delay.TotalSeconds}s");
            await Task.Delay(delay);
        }

        long numConsumed = 0;
        long numOutOfSequence = 0;
        long numDuplicated = 0;
        var producerSemaphore = new SemaphoreSlim(0);
        var consumerTask = Task.Run(async () =>
        {
            var config = new ConsumerConfig
            {
                GroupId = Guid.NewGuid().ToString(),
                EnableAutoOffsetStore = false,
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Latest,
                PartitionAssignmentStrategy = PartitionAssignmentStrategy.Range,
            };

            IConfiguration consumerConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(config)
                .AddInMemoryCollection(settings.ConfigDictionary)
                .Build();

            using (var consumer = new ConsumerBuilder<long, long>(
                           consumerConfiguration.AsEnumerable())
                       .SetErrorHandler((_, e) => Log.Log(LogLevel.Error,
                           $"Consumer error: reason={e.Reason}, IsLocal={e.IsLocalError}, IsBroker={e.IsBrokerError}, IsFatal={e.IsFatal}, IsCode={e.Code}"))
                       .SetLogHandler((_, m) => Log.Log(LogLevel.Information,
                           $"Consumer log: message={m.Message}, name={m.Name}, facility={m.Facility}, level={m.Level}"))
                       .SetPartitionsAssignedHandler((_, l) => Log.Log(LogLevel.Information,
                           $"Consumer log: PartitionsAssignedHandler: count={l.Count}"))
                       .SetPartitionsRevokedHandler((_, l) => Log.Log(LogLevel.Information,
                           $"Consumer log: PartitionsRevokedHandler: count={l.Count}"))
                       .SetPartitionsLostHandler((_, l) => Log.Log(LogLevel.Information,
                           $"Consumer log: PartitionsLostHandler: count={l.Count}"))
                       .Build())
            {
                var topics = Enumerable.Range(0, settings.Topics)
                    .Select(Utils.GetTopicName)
                    .ToArray();

                var topicPartitions = new List<TopicPartitionOffset>();
                foreach (var topic in topics)
                {
                    foreach (var partition in Enumerable.Range(0, settings.Partitions))
                    {
                        topicPartitions.Add(
                            new TopicPartitionOffset(new TopicPartition(topic, new Partition(partition)), Offset.End));
                    }
                }

                consumer.Assign(topicPartitions);
                // consumer.Subscribe(topics);

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

                    var key = (consumeResult.Topic, consumeResult.Message.Key);
                    if (valueDictionary.TryGetValue(key, out var previousResult))
                    {
                        if (consumeResult.Message.Value != previousResult.Message.Value + 1)
                        {
                            Log.Log(LogLevel.Error,
                                $"Unexpected message value, topic/k [p]={consumeResult.Topic}/{consumeResult.Message.Key} {consumeResult.Partition}, Offset={previousResult.Offset}/{consumeResult.Offset}, " +
                                $"LeaderEpoch={previousResult.LeaderEpoch}/{consumeResult.LeaderEpoch},  previous value={previousResult.Message.Value}, messageValue={consumeResult.Message.Value}, numConsumed={numConsumed} !");

                            if (consumeResult.Message.Value < previousResult.Message.Value + 1)
                                Interlocked.Increment(ref numDuplicated);

                            if (consumeResult.Message.Value > previousResult.Message.Value + 1)
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
        });

        long numProduced = 0;

        // Only one producer to avoid messages reordering
        var producerTask = Task.Run(async () =>
        {
            await producerSemaphore.WaitAsync();

            var logger = LoggerFactory
                .Create(builder => builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                }))
                .CreateLogger($"Producer");

            logger.Log(LogLevel.Information, "Starting producer task:");

            Exception e = null;
            var producerConfig = new ProducerConfig(settings.ConfigDictionary);

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

            var sw = Stopwatch.StartNew();
            var m = 0;
            for (var currentValue = 0L;; currentValue++)
            for (var topicIndex = 0; topicIndex < settings.Topics; topicIndex++)
            {
                var topicName = Utils.GetTopicName(topicIndex);
                for (var k = 0; k < settings.Partitions * 7; k++)
                {
                    if (e != null)
                    {
                        throw e;
                    }

                    if (m <= 0)
                    {
                        var elapsed = sw.Elapsed;
                        if (elapsed < TimeSpan.FromMilliseconds(100))
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100) - elapsed);
                        }

                        sw = Stopwatch.StartNew();
                        m = (int)settings.MessagesPerSecond / 10;
                    }

                    m -= 1;

                    var msg = new Message<long, long> { Key = k, Value = currentValue };
                    producer.Produce(topicName, msg,
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
                        });

                    if (Interlocked.Increment(ref numProduced) % 100000 == 0)
                    {
                        producer.Flush();
                    }
                }
            }
        });

        var reporterTask = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            var prevProduced = 0L;
            var prevConsumed = 0L;
            for (;;)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                var totalProduced = Interlocked.Read(ref numProduced);
                var totalConsumed = Interlocked.Read(ref numConsumed);
                var outOfSequence = Interlocked.Read(ref numOutOfSequence);
                var duplicated = Interlocked.Read(ref numDuplicated);
                var newlyProduced = totalProduced - prevProduced;
                var newlyConsumed = totalConsumed - prevConsumed;
                prevProduced = totalProduced;
                prevConsumed = totalConsumed;

                Log.Log(LogLevel.Information,
                    $"Elapsed: {(int)sw.Elapsed.TotalSeconds}s, {totalProduced} (+{newlyProduced}) messages produced, {totalConsumed} (+{newlyConsumed}) messages consumed, {duplicated} duplicated, {outOfSequence} out of sequence.");
            }
        });

        var task = await Task.WhenAny([consumerTask, reporterTask, producerTask]);

        await task;
        return 0;
    }
}