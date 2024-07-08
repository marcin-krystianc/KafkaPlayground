using System;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console.Cli;
using Microsoft.Extensions.Logging;
using MoreLinq;

namespace KafkaTool;

public sealed class ProducerConsumer : AsyncCommand<ProducerConsumerSettings>
{
    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }))
        .CreateLogger("Log");

    public override async Task<int> ExecuteAsync(CommandContext context, ProducerConsumerSettings settings)
    {
        {
            using var adminClient = Utils.GetAdminClient(settings.ConfigDictionary);
            foreach (var batch in Enumerable.Range(0, settings.Topics)
                         .Select(x => Utils.GetTopicName(settings.TopicStem, x))
                             .Where(x => Utils.TopicExists(adminClient, x))
                             .Batch(100))
            {
                await Utils.DeleteTopicAsync(adminClient, batch.ToArray());
                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            foreach (var batch in Enumerable.Range(0, settings.Topics).Batch(100))
            {
                var topics = batch
                    .Select(x => Utils.GetTopicName(settings.TopicStem, x))
                    .ToArray();

                await Utils.CreateTopicAsync(adminClient, topics, numPartitions: settings.Partitions,
                    settings.ReplicationFactor,
                    settings.MinISR);

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }

        var data = new ProducerConsumerData();
        var producerTasks = Enumerable.Range(0, settings.Producers)
            .Select(producerIndex => ProducerTask.GetTask(settings, data, producerIndex));

        var consumerTask = ConsumerTask.GetTask(settings, data);
        var reporterTask = ReporterTask.GetTask(data);
        var task = await Task.WhenAny(producerTasks.Concat([consumerTask, reporterTask]));
        await task;
        return 0;
    }
}