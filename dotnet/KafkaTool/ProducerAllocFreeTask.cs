using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KafkaTool;
#if EXPERIMENTAL_ALLOC_FREE

public static class ProducerAllocFreeTask
{
    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Log");

    public static Task GetTask(ProducerConsumerSettings settings, ProducerConsumerData data, int producerIndex)
    {
        return Task.Run(async () =>
        {
            var cancellationTokenSource = new CancellationTokenSource();
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.ConfigDictionary)
                .Build();

            var logger = LoggerFactory
                .Create(builder => builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                }))
                .CreateLogger($"ProducerAllcFree{producerIndex}:");

            logger.Log(LogLevel.Information, "Starting producer task:");

            Exception e = null;
            var producerConfig = new ProducerConfig
            {
                ClientId = $"rdkafka-producer-{producerIndex}",
                EnableDeliveryReports = true,
            };

            Experimental.AllocFreeDeliveryHandler allocFreeDeliveryHandler = (in Experimental.MessageReader mr) =>
            {
                if (mr.ErrorCode != ErrorCode.NoError)
                {
                    if (e == null)
                    {
                        e = new Exception($"DeliveryReport.Error, Code = {mr.ErrorCode}");
                    }
                }
                else
                {
                    var latency = DateTime.UtcNow - mr.Timestamp.UtcDateTime;
                    data.DigestProducerLatency(latency.TotalMilliseconds);
                    data.IncrementProduced();
                }
            };

            var producerBuilder = new ProducerBuilder<Null, Null>(
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
                .SetErrorHandler((_, e) => logger.Log(LogLevel.Error,
                    $"Producer error: reason={e.Reason}, IsLocal={e.IsLocalError}, IsBroker={e.IsBrokerError}, IsFatal={e.IsFatal}, IsCode={e.Code}"))
                .SetAllocFreeDeliveryHandler(allocFreeDeliveryHandler)
                .SetStatisticsHandler((_, json) =>
                {
                    string formattedJson = System.Text.Json.Nodes.JsonNode.Parse(json).ToString();
                    File.WriteAllText(settings.StatisticsPath, formattedJson);
                });

            if (settings.SetOAuthTokenCallback)
            {
                producerBuilder.SetOAuthBearerTokenRefreshHandler( (client, cfg) =>
                    OAuthHelper.OAuthTokenRefreshHandler(client, cfg, logger, settings));
            }
            var emptyHeaders = new Headers();
            var producer = producerBuilder.Build();
            var keyBytes = new byte[sizeof(int)];
            var payloadBytes = new byte[sizeof(long) + settings.ExtraPayloadBytes];
            new Random().NextBytes(payloadBytes);

            var oneMillisecond = TimeSpan.FromMilliseconds(1);
            var swTimestamp = Stopwatch.GetTimestamp();
            double messagesToSend = 0;
            double messagesRate = (double)settings.MessagesPerSecond / 1000;
            var burstCycleSw = Stopwatch.StartNew();
            var resetMessagesToSend = true;
            if (settings.Topics % settings.Producers != 0)
            {
                throw new Exception($"Cannot evenly schedule {settings.Topics} on a {settings.Producers} producers!");
            }

            var topicsPerProducer = settings.Topics / settings.Producers;
            var topicPartitions = new List<TopicPartition>(
                from topic in Enumerable.Range(0, settings.Topics)
                from partition in Enumerable.Range(0, settings.Partitions)
                select new TopicPartition(Utils.GetTopicName(settings.TopicStem, topic), new Partition(partition)));

            for (var currentValue = 0L;; currentValue++)
            for (var partition = 0; partition < settings.Partitions; partition++)
            {
                for (var topicIndex = 0; topicIndex < topicsPerProducer; topicIndex++)
                {
                    var topicPartition = topicPartitions[(topicIndex + producerIndex * topicsPerProducer) * settings.Partitions + partition];
                    if (e != null)
                    {
                        throw e;
                    }

                    do
                    {
                        if (settings.BurstCycle > 0)
                        {
                            if (burstCycleSw.ElapsedMilliseconds >= settings.BurstCycle)
                            {
                                burstCycleSw = Stopwatch.StartNew();
                                resetMessagesToSend = true;
                            }

                            if (burstCycleSw.ElapsedMilliseconds < settings.BurstDuration)
                            {
                                messagesRate = (double)settings.BurstMessagesPerSecond / 1000;
                            }
                            else
                            {
                                if (resetMessagesToSend)
                                {
                                    messagesToSend = 0;
                                    resetMessagesToSend = false;
                                }

                                messagesRate = (double)settings.MessagesPerSecond / 1000;
                            }
                        }

                        var currentTimestamp = Stopwatch.GetTimestamp();
                        var elapsed = Stopwatch.GetElapsedTime(swTimestamp, currentTimestamp);
                        if (elapsed > oneMillisecond)
                        {
                            swTimestamp = currentTimestamp;
                            messagesToSend += messagesRate * elapsed.TotalMilliseconds;
                        }

                        if (messagesToSend < 1)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(1));
                        }
                    } while (messagesToSend < 1);

                    messagesToSend -= 1.0;

                    payloadBytes[7] = (byte)(currentValue >> 0);
                    payloadBytes[6] = (byte)(currentValue >> 8);
                    payloadBytes[5] = (byte)(currentValue >> 16);
                    payloadBytes[4] = (byte)(currentValue >> 24);
                    payloadBytes[3] = (byte)(currentValue >> 32);
                    payloadBytes[2] = (byte)(currentValue >> 40);
                    payloadBytes[1] = (byte)(currentValue >> 48);
                    payloadBytes[0] = (byte)(currentValue >> 56);

                    keyBytes[3] = (byte)(partition >> 0);
                    keyBytes[2] = (byte)(partition >> 8);
                    keyBytes[1] = (byte)(partition >> 16);
                    keyBytes[0] = (byte)(partition >> 24);
                    
                    var timestamp = new Timestamp(DateTime.UtcNow);

                    bool produced = false;
                    do
                    {
                        try
                        {
                            producer.Produce(topicPartition, payloadBytes.AsSpan(), keyBytes.AsSpan()
                                , timestamp, emptyHeaders);

                            produced = true;
                        }
                        catch (ProduceException<Null, Null> exception)
                        {
                            if (exception.Error.Code != ErrorCode.Local_QueueFull)
                            {
                                logger.LogInformation($"Handling exception");
                                throw;
                            }

                            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationTokenSource.Token);
                        }
                    } while (!produced);
                }
            }
        });
    }
}

#endif
