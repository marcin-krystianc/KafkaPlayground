using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KafkaTool;

public static class ReporterTask
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
        return Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            var prevProduced = 0L;
            var prevConsumed = 0L;
            for (;;)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(settings.ReportingCycle));
                var totalProduced = data.GetProduced();
                var totalConsumed = data.GetConsumed();
                var outOfSequence = data.GetOutOfOrder();
                var duplicated =  data.GetDuplicated();
                var consumerLatency =  data.GetConsumerLatency();
                var producerLatency =  data.GetProducerLatency();
                var newlyProduced = totalProduced - prevProduced;
                var newlyConsumed = totalConsumed - prevConsumed;
                prevProduced = totalProduced;
                prevConsumed = totalConsumed;

                Log.Log(LogLevel.Information,
                    $"Elapsed: {(int)sw.Elapsed.TotalSeconds}s, {totalProduced} (+{newlyProduced}, p95={producerLatency.Quantile(0.95):0.000}s) messages produced, {totalConsumed} (+{newlyConsumed}, p95={consumerLatency.Quantile(0.95):0.000}s) messages consumed, {duplicated} duplicated, {outOfSequence} out of sequence.");
            }
        });
    }
}