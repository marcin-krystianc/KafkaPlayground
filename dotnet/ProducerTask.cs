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
            var flushCounter = 0;
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
                .Build();

            var sw = Stopwatch.StartNew();
            var m = 0;

            if (settings.Topics % settings.Producers != 0)
            {
                throw new Exception($"Cannot evenly schedule {settings.Topics} on a {settings.Producers} producers!");
            }

            var topicsPerProducer = settings.Topics / settings.Producers;
            for (var currentValue = 0L;; currentValue++)
            for (var topicIndex = 0; topicIndex < topicsPerProducer; topicIndex++)
            {
                var topicName = Utils.GetTopicName(settings.TopicStem, topicIndex + producerIndex * topicsPerProducer);
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
                                using var adminClient = Utils.GetAdminClient(settings.ConfigDictionary);
                                var topicMetadata = adminClient.GetMetadata(deliveryReport.Topic,
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

                    data.IncrementProduced();
                    if (++flushCounter % 1000000 == 0)
                    {
                        producer.Flush();
                    }
                }
            }
        });
    }
}