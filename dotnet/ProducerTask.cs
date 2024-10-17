using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KafkaTool;


public static class ProducerTask
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
                .CreateLogger($"Producer{producerIndex}:");

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
                .SetErrorHandler((_, e) => logger.Log(LogLevel.Error,
                    $"Producer error: reason={e.Reason}, IsLocal={e.IsLocalError}, IsBroker={e.IsBrokerError}, IsFatal={e.IsFatal}, IsCode={e.Code}"))
                .Build();

            var oneMillisecond = TimeSpan.FromMilliseconds(1);
            var swTimestamp = Stopwatch.GetTimestamp();
            double messagesToSend = 0;
            double messagesRate = (double)settings.MessagesPerSecond / 1000;
            var burstCycleSw = Stopwatch.StartNew();
            var resetMessagesToSend = false;
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
            for (var topicIndex = 0; topicIndex < topicsPerProducer; topicIndex++)
            {
                for (var partition = 0; partition < settings.Partitions; partition++)
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
                            if (burstCycleSw.ElapsedMilliseconds > settings.BurstCycle)
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

                    var msg = new Message<long, long> { Key = partition, Value = currentValue, Timestamp = new Timestamp(DateTime.UtcNow)};
                    
                    bool produced = false;
                    do
                    {
                        try
                        {
                            producer.Produce(topicPartition, msg,
                                (deliveryReport) =>
                                {
                                    if (deliveryReport.Error.Code != ErrorCode.NoError)
                                    {
                                        using var adminClient = Utils.GetAdminClient(settings.ConfigDictionary);
                                        var topicMetadata = adminClient.GetMetadata(deliveryReport.Topic,
                                            TimeSpan.FromSeconds(30));
                                        var partitionsCount = topicMetadata.Topics.Single().Partitions.Count;

                                        if (e == null)
                                        {
                                            e = new Exception(
                                                $"DeliveryReport.Error, Code = {deliveryReport.Error.Code}, Reason = {deliveryReport.Error.Reason}" +
                                                $", IsFatal = {deliveryReport.Error.IsFatal}, IsError = {deliveryReport.Error.IsError}" +
                                                $", IsLocalError = {deliveryReport.Error.IsLocalError}, IsBrokerError = {deliveryReport.Error.IsBrokerError}" +
                                                $", topic = {deliveryReport.Topic}, partition = {deliveryReport.Partition.Value}, partitionsCount = {partitionsCount}");
                                        }
                                    }
                                    else
                                    {
                                        data.IncrementProduced();
                                        var latency = DateTime.UtcNow - deliveryReport.Message.Timestamp.UtcDateTime;
                                        data.DigestProducerLatency(latency.TotalSeconds);
                                    }
                                });
                            produced = true;
                        }
                        catch (ProduceException<long, long> exception)
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