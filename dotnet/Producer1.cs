using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Spectre.Console.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace KafkaTool;

public sealed class Producer1 : AsyncCommand<Producer1Settings>
{
    public const int DefaultReplicationFactor = 2;
    public const int DefaultPartitions = 10;
    
    private IAdminClient _adminClient;
    
    private static readonly ILogger Log = LoggerFactory.Create(builder => builder.AddSimpleConsole(configure => configure.SingleLine = true))
        .CreateLogger("Producer1");

    public override async Task<int> ExecuteAsync(CommandContext context, Producer1Settings settings)
    {
        await Task.Yield();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddIniFile(settings.IniFile)
            .Build();

        _adminClient = new AdminClientBuilder(configuration.AsEnumerable())
            .Build();
        
        const string topic = "topic1";

        if (!TopicExists(topic))
            CreateTopic(topic, numPartitions:DefaultPartitions);
        
        using (var producer = new ProducerBuilder<long, long>(
                   configuration.AsEnumerable())
                   .SetLogHandler((a, b) => Log.LogInformation(b.ToString()))
                   .Build())
        {
            Exception e = null;
            var numProduced = 0;
            const int numMessages = 1 << 28;
            for (int i = 0; i < numMessages; ++i)
            {
                //Thread.Sleep(1);
                if (e != null) throw e;
                
                producer.Produce(topic, new Message<long, long> { Key = i % 1000, Value = i },
                    (deliveryReport) =>
                    {
                        if (deliveryReport.Error.Code != ErrorCode.NoError)
                        {
                            if (e == null) e = new Exception(
                                    $"DeliveryReport.Error, Code = {deliveryReport.Error.Code}, Reason = {deliveryReport.Error.Reason}" +
                                    $", IsFatal = {deliveryReport.Error.IsFatal}, IsError = {deliveryReport.Error.IsError}" +
                                    $", IsLocalError = {deliveryReport.Error.IsLocalError}, IsBrokerError = {deliveryReport.Error.IsBrokerError}");
                        }
                        else {
                            numProduced += 1;
                            if (numProduced % 100 == 0)
                            {
                                
                                Log.Log(LogLevel.Information,$"{numProduced} messages were produced to topic: {topic}");
                            }
                        }
                    });

                if (i % 100 == 0)
                { 
                    var result = producer.Flush(TimeSpan.FromSeconds(10));
                    Log.Log(LogLevel.Information,$"Remaining {result} messages  in the queue");
                }
            }

            producer.Flush(TimeSpan.FromSeconds(10));
            Log.Log(LogLevel.Information,$"{numProduced} messages were produced to topic: {topic}");
        }

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
        Log.Log(LogLevel.Information,($"Creating topic: {topic}"));

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