﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

class MyInt64Deserializer : IDeserializer<long>
{
    public long Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        if (isNull)
        {
            throw new ArgumentNullException($"Null data encountered deserializing Int64 value.");
        }

        // network byte order -> big endian -> most significant byte in the smallest address.
        long result = ((long)data[0]) << 56 |
                      ((long)(data[1])) << 48 |
                      ((long)(data[2])) << 40 |
                      ((long)(data[3])) << 32 |
                      ((long)(data[4])) << 24 |
                      ((long)(data[5])) << 16 |
                      ((long)(data[6])) << 8 |
                      (data[7]);
        return result;
    }
}

public static class ConsumerTask
{
    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Log");

    public static Task GetTask(ProducerConsumerSettings settings, ProducerConsumerData data)
    {
        return Task.Run(() =>
        {;
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
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                ClientId = "rdkafka-consumer-1",
                ConsumeResultFields = settings.EnableSequenceValidation ? "topic,timestamp" : "none",
            };

            IConfiguration consumerConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(config)
                .AddInMemoryCollection(settings.ConfigDictionary)
                .Build();

            var offsetDictionary = new ConcurrentDictionary<TopicPartition, Offset>();

            var consumerBuilder = new ConsumerBuilder<int, long>(
                    consumerConfiguration.AsEnumerable())
                .SetErrorHandler((_, e) =>
                {
                    if (!errorLogged)
                    {
                        logger.Log(LogLevel.Error,
                            $"Consumer error: reason={e.Reason}, IsLocal={e.IsLocalError}, IsBroker={e.IsBrokerError}, IsFatal={e.IsFatal}, IsCode={e.Code}");
                    }

                    errorLogged = true;
                })
                .SetLogHandler((_, m) => logger.Log(LogLevel.Information,
                    $"Consumer log: message={m.Message}, name={m.Name}, facility={m.Facility}, level={m.Level}"))
                .SetPartitionsAssignedHandler((c, topicPartitions) =>
                {
                    logger.Log(LogLevel.Information,
                        $"Consumer log: PartitionsAssignedHandler: count={topicPartitions.Count}");


                    // Example: Set all partitions to start from the beginning
                    var offsets = new List<TopicPartitionOffset>();
                    foreach (var topicPartition in topicPartitions)
                    {
                        if (offsetDictionary.TryGetValue(topicPartition, out var offset))
                        {
                            offsets.Add(new TopicPartitionOffset(topicPartition, offset + 1));
                        }
                        else
                        {
                            offsets.Add(new TopicPartitionOffset(topicPartition, Offset.Unset));
                        }
                    }

                    // Assign the partitions with the specified offsets
                    return offsets;

                })
                .SetPartitionsRevokedHandler((_, l) => logger.Log(LogLevel.Information,
                    $"Consumer log: PartitionsRevokedHandler: count={l.Count}"))
                .SetPartitionsLostHandler((_, l) => logger.Log(LogLevel.Information,
                    $"Consumer log: PartitionsLostHandler: count={l.Count}"))
                .SetValueDeserializer(new MyInt64Deserializer());
            
            if (settings.SetOAuthTokenCallback)
            {
                consumerBuilder.SetOAuthBearerTokenRefreshHandler( (client, cfg) =>
                    OAuthHelper.OAuthTokenRefreshHandler(client, cfg, logger, settings));
            }

            using (var consumer = consumerBuilder.Build())
            {
                var topics = Enumerable.Range(0, settings.Topics)
                    .Select(x => Utils.GetTopicName(settings.TopicStem, x))
                    .ToArray();

                consumer.Subscribe(topics);

                Dictionary<(string Topic, int Key), ConsumeResult<int, long>> valueDictionary = new();

                while (true)
                {
                    try
                    {
                        var consumeResult = consumer.Consume();
                        data.IncrementConsumed();

                        if (consumeResult.IsPartitionEOF)
                        {
                            throw new Exception(
                                $"Reached end of topic {consumeResult.Topic}, partition {consumeResult.Partition}, offset {consumeResult.Offset}.");
                        }

                        if (settings.EnableSequenceValidation)
                        {
                            var latency = DateTime.UtcNow - consumeResult.Message.Timestamp.UtcDateTime;
                            data.DigestConsumerLatency(latency.TotalMilliseconds);

                            var key = (consumeResult.Topic, consumeResult.Message.Key);
                            offsetDictionary.AddOrUpdate(consumeResult.TopicPartition, consumeResult.Offset, (_, _) => consumeResult.Offset);

                            if (valueDictionary.TryGetValue(key, out var previousConsumeResult))
                            {
                                if (consumeResult.Message.Value != previousConsumeResult.Message.Value + 1)
                                {
                                    logger.Log(LogLevel.Error,
                                        $"Unexpected message value, topic/k [p]={consumeResult.Topic}/{consumeResult.Message.Key} {consumeResult.Partition}, Offset={previousConsumeResult.Offset}/{consumeResult.Offset}, " +
                                        $"LeaderEpoch={previousConsumeResult.LeaderEpoch}/{consumeResult.LeaderEpoch},  previous value={previousConsumeResult.Message.Value}, messageValue={consumeResult.Message.Value}!");

                                    if (consumeResult.Message.Value < previousConsumeResult.Message.Value + 1)
                                        data.IncrementDuplicated();

                                    if (consumeResult.Message.Value > previousConsumeResult.Message.Value + 1)
                                        data.IncrementOutOfOrder();
                                }

                                valueDictionary[key] = consumeResult;
                            }
                            else
                            {
                                valueDictionary[key] = consumeResult;
                            }
                        }
                    }
                    catch (ConsumeException e)
                    {
                        logger.Log(LogLevel.Error, "Consumer.Consume:" + e);
                    }
                }
            }
        });
    }
}