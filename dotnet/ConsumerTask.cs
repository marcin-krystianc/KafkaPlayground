using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace KafkaTool;

public static class ConsumerTask 
{
    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Log");
    
    public static Task GetTask(ConsumerSettings settings, ProducerConsumerData data)
    {
        return Task.Run(async () =>
        {
            var consumedAnyRecords = false;
            var errorLogged = false;

            var logger = LoggerFactory
                .Create(builder => builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                }))
                .CreateLogger($"Consumer:");

            logger.Log(LogLevel.Information, "Starting consumer task:");
            
            var config = new ConsumerConfig
            {
                GroupId = Guid.NewGuid().ToString(),
                EnableAutoOffsetStore = false,
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Error,
            };

            IConfiguration consumerConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(config)
                .AddInMemoryCollection(settings.ConfigDictionary)
                .Build();

            create_consumer:
            using (var consumer = new ConsumerBuilder<long, long>(
                           consumerConfiguration.AsEnumerable())
                       .SetErrorHandler((_, e) =>
                       {
                           if (consumedAnyRecords || !errorLogged)
                           {
                               logger.Log(LogLevel.Error,
                                   $"Consumer error: reason={e.Reason}, IsLocal={e.IsLocalError}, IsBroker={e.IsBrokerError}, IsFatal={e.IsFatal}, IsCode={e.Code}");
                           }

                           errorLogged = true;
                       })
                       .SetLogHandler((_, m) => logger.Log(LogLevel.Information,
                           $"Consumer log: message={m.Message}, name={m.Name}, facility={m.Facility}, level={m.Level}"))
                       .SetPartitionsAssignedHandler((_, l) => logger.Log(LogLevel.Information,
                           $"Consumer log: PartitionsAssignedHandler: count={l.Count}"))
                       .SetPartitionsRevokedHandler((_, l) => logger.Log(LogLevel.Information,
                           $"Consumer log: PartitionsRevokedHandler: count={l.Count}"))
                       .SetPartitionsLostHandler((_, l) => logger.Log(LogLevel.Information,
                           $"Consumer log: PartitionsLostHandler: count={l.Count}"))
                       .Build())
            {
                var topics = Enumerable.Range(0, settings.Topics)
                    .Select(x => Utils.GetTopicName(settings.TopicStem, x))
                    .ToArray();

                var topicPartitions = new List<TopicPartitionOffset>();
                foreach (var topic in topics)
                {
                    foreach (var partition in Enumerable.Range(0, settings.Partitions))
                    {
                        topicPartitions.Add(
                            new TopicPartitionOffset(new TopicPartition(topic, new Partition(partition)), Offset.Beginning));
                    }
                }

                try
                {
                    consumer.Assign(topicPartitions);
                }
                catch (Exception e)
                {
                    logger.Log(LogLevel.Error,"consumer.Assign:" + e);
                    throw;
                }

                Dictionary<(string Topic, long Key), ConsumeResult<long, long>> valueDictionary = new();

                while (true)
                {
                    try
                    {
                        var consumeResult = consumer.Consume();
                        consumedAnyRecords = true;

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
                                logger.Log(LogLevel.Error,
                                    $"Unexpected message value, topic/k [p]={consumeResult.Topic}/{consumeResult.Message.Key} {consumeResult.Partition}, Offset={previousResult.Offset}/{consumeResult.Offset}, " +
                                    $"LeaderEpoch={previousResult.LeaderEpoch}/{consumeResult.LeaderEpoch},  previous value={previousResult.Message.Value}, messageValue={consumeResult.Message.Value}!");

                                if (consumeResult.Message.Value < previousResult.Message.Value + 1)
                                    data.IncrementDuplicated();

                                if (consumeResult.Message.Value > previousResult.Message.Value + 1)
                                    data.IncrementOutOfOrder();
                            }

                            valueDictionary[key] = consumeResult;
                        }
                        else
                        {
                            valueDictionary[key] = consumeResult;
                        }

                        data.IncrementConsumed();
                    }
                    catch (ConsumeException e)
                    {
                        logger.Log(LogLevel.Error,"Consumer.Consume:" + e);
                        if (e.Error.Code == ErrorCode.UnknownTopicOrPart && !consumedAnyRecords)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(1000));
                            logger.Log(LogLevel.Warning,"Recreating consumer.");
                            goto create_consumer;
                        }
                    }
                }
            }
        });
    }
}
