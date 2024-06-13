using System;
using System.Collections.Concurrent;
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

public sealed class Producer : AsyncCommand<ProducerSettings>
{
    private IAdminClient _adminClient;

    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Producer");

    public override async Task<int> ExecuteAsync(CommandContext context, ProducerSettings settings)
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
                long m = 0;

                for (;;)
                {
                    if (Interlocked.Decrement(ref msgToEnqueue) < 0 || queue.Count > 1_0000_000)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1));
                        continue;
                    }

                    m++;
                    queue.Enqueue((topic: topic, k: m % (settings.Partitions * 10), v: m));
                }
            }));

        long numProduced = 0;
        var sw = Stopwatch.StartNew();

        var producersTask = Enumerable.Range(0, settings.Producers)
            .Select(i => Task.Run(() =>
            {
                Log.Log(LogLevel.Information, $"Starting producer task: {i}");
                
                Exception e = null;
                var producerConfig = new ProducerConfig
                {
                    Acks = Acks.Leader,
                };

                var producer = new ProducerBuilder<long, long>(
                        producerConfig.AsEnumerable().Concat(configuration.AsEnumerable()))
                    .SetLogHandler(
                        (a, b) => Log.LogInformation($"kafka-log Facility:{b.Facility}, Message{b.Message}"))
                    .Build();

                var m = 0;
                for (;;)
                {
                    if (e != null)
                    {
                        Log.Log(LogLevel.Information, $"Exception: {e.Message}");
                        throw e;
                    }
                    
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
                                            (a, b) => Log.LogInformation(
                                                $"kafka-log Facility:{b.Facility}, Message{b.Message}"))
                                        .Build();

                                    if (e == null)
                                        e = new Exception(
                                            $"DeliveryReport.Error, Code = {deliveryReport.Error.Code}, Reason = {deliveryReport.Error.Reason}" +
                                            $", IsFatal = {deliveryReport.Error.IsFatal}, IsError = {deliveryReport.Error.IsError}" +
                                            $", IsLocalError = {deliveryReport.Error.IsLocalError}, IsBrokerError = {deliveryReport.Error.IsBrokerError}" +
                                            $", topic = {deliveryReport.Topic}, partitionsCount = {partitionsCount}");
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
}