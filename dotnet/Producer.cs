using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MoreLinq.Extensions;
using Spectre.Console.Cli;

namespace KafkaTool;

public class Producer : AsyncCommand<ProducerConsumerSettings>
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
        await Utils.RecreateTopics(settings);
        var data = new ProducerConsumerData();
        var producerTasks = Enumerable.Range(0, settings.Producers)
            .Select(producerIndex => ProducerTask.GetTask(settings, data, producerIndex));

        var reporterTask = ReporterTask.GetTask(data);
        var task = await Task.WhenAny(producerTasks.Concat([reporterTask]));
        await task;
        return 0;
    }
}
