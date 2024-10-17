using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace KafkaTool;

public class Consumer : AsyncCommand<ProducerConsumerSettings>
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
        var data = new ProducerConsumerData();
        var consumerTask = ConsumerTask.GetTask(settings, data);
        var reporterTask = ReporterTask.GetTask(settings, data);
        var task = await Task.WhenAny([consumerTask, reporterTask]);
        await task;
        return 0;
    }
}
