using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
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
            Log.Log(LogLevel.Information, $"settings: {JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true } )}");

            using var adminClient = Utils.GetAdminClient(settings);
            
            var sw = Stopwatch.StartNew();
            var prevProduced = 0L;
            var prevConsumed = 0L;
            var delay = true;
            for (;;)
            {
                if (delay) await Task.Delay(TimeSpan.FromMilliseconds(settings.ReportingCycle));
                delay = false;
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
                try
                {
                    var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(1));

                    var numberOfTopics = metadata.Topics.Count;
                    var isrCount = metadata.Topics
                        .SelectMany(x => x.Partitions)
                        .SelectMany(x => x.InSyncReplicas)
                        .Count();

                    var replicaCount = metadata.Topics
                        .SelectMany(x => x.Partitions)
                        .SelectMany(x => x.Replicas)
                        .Count();
                    
                    Log.Log(LogLevel.Information,
                        $"Elapsed: {(int)sw.Elapsed.TotalSeconds}s, Produced: {totalProduced} (+{newlyProduced}, p95={producerLatency.Quantile(0.95):0.}ms), Consumed: {totalConsumed} (+{newlyConsumed}, p95={consumerLatency.Quantile(0.95):0.}ms)" + 
                        $" Dup/Seq={duplicated}/{outOfSequence}, Topics={numberOfTopics}, Replicas/ISR={replicaCount}/{isrCount}.");

                    if (settings.ExitAfter > 0 && sw.Elapsed.TotalSeconds > settings.ExitAfter)
                        return;
                    delay = true;
                }
                catch (Exception e)
                {
                    Log.LogError($"Exception: {e}");
                }
            }
        });
    }
}