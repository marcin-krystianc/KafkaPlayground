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
        if (settings.RecreateTopics)
        {
            await Utils.RecreateTopics(settings);
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