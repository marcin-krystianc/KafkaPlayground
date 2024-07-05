using System;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console.Cli;
using Microsoft.Extensions.Logging;

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
            for (var i = 0; i < settings.Topics; i++)
            {
                string topic = Utils.GetTopicName(settings.TopicStem, i);

                if (Utils.TopicExists(adminClient, topic))
                {
                    await Utils.DeleteTopicAsync(adminClient, topic);
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }

                await Utils.CreateTopicAsync(adminClient, topic, numPartitions: settings.Partitions,
                    settings.ReplicationFactor,
                    settings.MinISR);
                await Task.Delay(TimeSpan.FromMilliseconds(100));
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