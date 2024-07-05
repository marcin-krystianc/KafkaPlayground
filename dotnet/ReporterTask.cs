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
    
    public static Task GetTask(ProducerConsumerData data)
    {
        return Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            var prevProduced = 0L;
            var prevConsumed = 0L;
            for (;;)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                var totalProduced = data.GetProduced();
                var totalConsumed = data.GetConsumed();
                var outOfSequence = data.GetOutOfOrder();
                var duplicated =  data.GetDuplicated();
                var newlyProduced = totalProduced - prevProduced;
                var newlyConsumed = totalConsumed - prevConsumed;
                prevProduced = totalProduced;
                prevConsumed = totalConsumed;

                Log.Log(LogLevel.Information,
                    $"Elapsed: {(int)sw.Elapsed.TotalSeconds}s, {totalProduced} (+{newlyProduced}) messages produced, {totalConsumed} (+{newlyConsumed}) messages consumed, {duplicated} duplicated, {outOfSequence} out of sequence.");
            }
        });
    }
}