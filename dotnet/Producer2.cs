using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Spectre.Console.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

public sealed class Producer2 : AsyncCommand<Producer1Settings>
{
    public const int DefaultReplicationFactor = 3;
    public const int DefaultPartitions = 1000;

    private IAdminClient _adminClient;

    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(configure => configure.SingleLine = true))
        .CreateLogger("Producer1");

    public override async Task<int> ExecuteAsync(CommandContext context, Producer1Settings settings)
    {
        await Task.Yield();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddIniFile(settings.IniFile)
            .Build();

        _adminClient = new AdminClientBuilder(configuration.AsEnumerable())
            .Build();

        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(async () =>
            {
                string topic = $"topic{i}";

                if (!TopicExists(topic))
                {
                    CreateTopic(topic, numPartitions: DefaultPartitions);
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

                var producer = new ProducerBuilder<long, long>(
                        configuration.AsEnumerable())
                    .SetLogHandler(
                        (a, b) => Log.LogInformation($"kafka-log  Facility:{b.Facility}, Message{b.Message}"))
                    .Build();

                Exception e = null;
                var numProduced = 0;
                const int numMessages = 1 << 28;
                var queue = new Queue<(long k, long v)>();

                for (int i = 0; i < numMessages; ++i)
                {
                    queue.Enqueue((k: i % 1000, v: i));

                    await Task.Delay(TimeSpan.FromMilliseconds(1));

                    if (e != null)
                    {
                        throw e;
                    }

                    while (queue.Count != 0)
                    {
                        var (k, v) = queue.Dequeue();

                        try
                        {
                            producer.Produce(topic, new Message<long, long> { Key = k, Value = v },
                                (deliveryReport) =>
                                {
                                    if (deliveryReport.Error.Code != ErrorCode.NoError)
                                    {
                                        queue.Enqueue((deliveryReport.Message.Key, deliveryReport.Message.Value));

                                        Log.Log(LogLevel.Information,
                                            $"DeliveryReport.Error, Code = {deliveryReport.Error.Code}, Reason = {deliveryReport.Error.Reason}" +
                                            $", IsFatal = {deliveryReport.Error.IsFatal}, IsError = {deliveryReport.Error.IsError}" +
                                            $", IsLocalError = {deliveryReport.Error.IsLocalError}, IsBrokerError = {deliveryReport.Error.IsBrokerError}");

                                        producer = new ProducerBuilder<long, long>(
                                                configuration.AsEnumerable())
                                            .SetLogHandler((a, b) =>
                                                Log.LogInformation(
                                                    $"kafka-log  Facility:{b.Facility}, Message{b.Message}"))
                                            .Build();

                                        /*
                                        if (e == null)
                                            e = new Exception(
                                                $"DeliveryReport.Error, Code = {deliveryReport.Error.Code}, Reason = {deliveryReport.Error.Reason}" +
                                                $", IsFatal = {deliveryReport.Error.IsFatal}, IsError = {deliveryReport.Error.IsError}" +
                                                $", IsLocalError = {deliveryReport.Error.IsLocalError}, IsBrokerError = {deliveryReport.Error.IsBrokerError}");
                                                */
                                    }
                                    else
                                    {
                                        numProduced += 1;
                                        if (numProduced % 100 == 0)
                                        {
                                            Log.Log(LogLevel.Information,
                                                $"{numProduced} messages were produced to topic: {topic}");
                                        }
                                    }
                                });
                        }
                        catch (Exception exception)
                        {
                            Log.Log(LogLevel.Information, $"Producer exception:{exception}, topic:{topic}");
                            queue.Enqueue((k, v));
                        }

                        if (i % 100 == 0)
                        {
                            var result = producer.Flush(TimeSpan.FromSeconds(10));
                            Log.Log(LogLevel.Information,
                                $"Topic: {topic}, remaining {result} messages in the queue");
                        }
                    }
                }

                producer.Flush(TimeSpan.FromSeconds(10));
                Log.Log(LogLevel.Information, $"{numProduced} messages were produced to topic: {topic}");
            }));

        var task = await Task.WhenAny(tasks);
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