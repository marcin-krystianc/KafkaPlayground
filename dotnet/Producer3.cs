using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Spectre.Console.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

public sealed class Producer3 : AsyncCommand<Producer3Settings>
{
    public const int DefaultReplicationFactor = 3;
    public const int DefaultPartitions = 500;

    private IAdminClient _adminClient;

    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(configure => configure.SingleLine = true))
        .CreateLogger("Producer1");

    public override async Task<int> ExecuteAsync(CommandContext context, Producer3Settings settings)
    {
        await Task.Yield();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddIniFile(settings.IniFile)
            .Build();

        _adminClient = new AdminClientBuilder(configuration.AsEnumerable())
            .Build();
        
        var queue = new ConcurrentQueue<(string topic, long k, long v)>();

        var tasks = Enumerable.Range(0, 30)
            .Select(i => Task.Run(async () =>
            {
                string topic = $"topic{i}";

                if (!TopicExists(topic))
                {
                    CreateTopic(topic, numPartitions: DefaultPartitions);
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

                const int numMessages = 1 << 28;
                for (int m = 0; m < numMessages; ++m)
                {
                    while (queue.Count > 1000)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1));
                    }

                    queue.Enqueue((topic: topic, k: m % 1000, v: m));
                    await Task.Delay(TimeSpan.FromMilliseconds(1));
                }
            }));

        var producerTask = Task.Run(async () =>
        {
            long numProduced = 0;
            Exception e = null;
            var producerConfig = new ProducerConfig 
            {
                Acks = Acks.None,
            };

            var producer = new ProducerBuilder<long, long>(
                    producerConfig.AsEnumerable().Concat(configuration.AsEnumerable()))
                .SetLogHandler(
                    (a, b) => Log.LogInformation($"kafka-log Facility:{b.Facility}, Message{b.Message}"))
                .Build();

            var m = 0;
            for (;;)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1));
                while (queue.TryDequeue(out var kvp))
                {
                    m++;
                    if (e != null)
                    {
                        throw e;
                    }

                    if (m % 1000 == 0)
                    {
                        producer.Flush();
                    }

                    producer.Produce(kvp.topic, new Message<long, long> { Key = kvp.k, Value = kvp.v },
                        (deliveryReport) =>
                        {
                            if (deliveryReport.Error.Code != ErrorCode.NoError)
                            {
                                if (e == null)
                                    e = new Exception(
                                        $"DeliveryReport.Error, Code = {deliveryReport.Error.Code}, Reason = {deliveryReport.Error.Reason}" +
                                        $", IsFatal = {deliveryReport.Error.IsFatal}, IsError = {deliveryReport.Error.IsError}" +
                                        $", IsLocalError = {deliveryReport.Error.IsLocalError}, IsBrokerError = {deliveryReport.Error.IsBrokerError}");
                            }
                            else
                            {
                                numProduced += 1;
                                if (numProduced % 100 == 0)
                                {
                                    Log.Log(LogLevel.Information,
                                        $"{numProduced} messages were produced");
                                }
                            }
                        });
                    
                }
            }
        });
        
        var task = await Task.WhenAny(tasks.Concat(new []{producerTask}));
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
    public void CreateTopic(string topic, int numPartitions)
    {
        Log.Log(LogLevel.Information, ($"Creating topic: {topic}"));

        _adminClient.CreateTopicsAsync(new[]
                {
                    new TopicSpecification
                    {
                        Name = topic,
                        NumPartitions = numPartitions,
                        ReplicationFactor = DefaultReplicationFactor,
                    }
                },
                new CreateTopicsOptions
                {
                    OperationTimeout = TimeSpan.FromSeconds(30)
                })
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }
}