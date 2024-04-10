using System;
using System.Threading.Tasks;
using Confluent.Kafka;
using Spectre.Console.Cli;
using Microsoft.Extensions.Configuration;

namespace KafkaTool;

public sealed class Producer1 : AsyncCommand<Producer1Settings>
{

    public override async Task<int> ExecuteAsync(CommandContext context, Producer1Settings settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddIniFile(settings.IniFile)
            .Build();

        await Task.Yield();
        
        const string topic = "topic1";

        using (var producer = new ProducerBuilder<long, long>(
                   configuration.AsEnumerable()).Build())
        {
            var numProduced = 0;
            const int numMessages = 1 << 28;
            for (int i = 0; i < numMessages; ++i)
            {
                producer.Produce(topic, new Message<long, long> { Key = i % 1000, Value = i },
                    (deliveryReport) =>
                    {
                        if (deliveryReport.Error.Code != ErrorCode.NoError)
                        {
                            throw new Exception($"DeliveryReport.Error.Code = {deliveryReport.Error.Code}");
                        }
                        else {
                            numProduced += 1;
                        }
                    });
                
                if (i % 100000 == 0) producer.Flush(TimeSpan.FromSeconds(10));
            }

            producer.Flush(TimeSpan.FromSeconds(10));
            Console.WriteLine($"{numProduced} messages were produced to topic {topic}");
        }

        return 0;
    }
}