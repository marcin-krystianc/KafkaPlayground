// https://github.com/marcin-krystianc/confluent-kafka-dotnet/commits/dev-20250609-alloc_free/
#if CONSUME_WITH_CALLBACK

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

public static class ConsumerWthCallbacksTask
{
    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Log");
    
    static long DeserializeInt64(ReadOnlySpan<byte> data)
    {
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
    
    static long DeserializeInt32(ReadOnlySpan<byte> data)
    {
        // network byte order -> big endian -> most significant byte in the smallest address.
        long result = ((long)(data[0])) << 24 |
                      ((long)(data[1])) << 16 |
                      ((long)(data[2])) << 8 |
                      (data[3]);
        return result;
    }

    public static Task GetTask(ProducerConsumerSettings settings, ProducerConsumerData data)
    {
        return Task.Run(async () =>
        {;
            var errorLogged = false;

            var logger = LoggerFactory
                .Create(builder => builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                }))
                .CreateLogger($"Consumer:");

            logger.Log(LogLevel.Information, "Starting consumer with callbacks task:");

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
                    $"Consumer log: PartitionsLostHandler: count={l.Count}"));
            
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

                Dictionary<(string Topic, int Key), long> valueDictionary = new();
                
                ConsumeCallback callback = (in MessageReader mr) =>
                {
                    data.IncrementConsumed();
                    if (mr.IsPartitionEOF)
                    {
                        throw new Exception($"Reached end of topic {mr.Topic}, partition {mr.Partition}, offset {mr.Offset}.");
                    }
                    
                    if (settings.EnableSequenceValidation)
                    {
                        var latency = DateTime.UtcNow - mr.Timestamp.UtcDateTime;
                        data.DigestConsumerLatency(latency.TotalMilliseconds);

                        var key = (int)DeserializeInt32(mr.KeySpan);
                        var dictKey = (mr.Topic, key);
                        var msgValue = DeserializeInt64(mr.ValueSpan);
                        // var key = (mr.Topic,  Encoding.UTF8.GetString());
                        var offset = mr.Offset;
                        offsetDictionary.AddOrUpdate(new TopicPartition(mr.Topic, mr.Partition), offset, (_, _) => offset);

                        if (valueDictionary.TryGetValue(dictKey, out var previousConsumeResult))
                        {
                            if (msgValue != previousConsumeResult + 1)
                            {
                                logger.Log(LogLevel.Error,
                                    $"Unexpected message value, topic/k [p]={mr.Topic}/{key} {mr.Partition}, Offset=?/{offset}, " +
                                    $"LeaderEpoch=?/{mr.LeaderEpoch},  previous value={previousConsumeResult}, messageValue={msgValue}!");

                                if (msgValue < msgValue + 1)
                                    data.IncrementDuplicated();

                                if (msgValue > msgValue + 1)
                                    data.IncrementOutOfOrder();
                            }
                        }

                        valueDictionary[dictKey] = msgValue;
                    }
                };
                
                while (true)
                {
                    try
                    {
                        consumer.ConsumeWithCallback(1, callback);

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

#endif