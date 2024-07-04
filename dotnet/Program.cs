using System;
using System.Reflection;
using System.Threading.Tasks;
using Confluent.Kafka;
using Spectre.Console.Cli;

namespace KafkaTool
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            var libAssembly = Assembly.Load("confluent.kafka");
            Console.WriteLine($"Using assembly:{libAssembly.FullName}m location:{libAssembly.Location}");
            Console.WriteLine( $"librdkafka Version: {Library.VersionString} ({Library.Version:X})");
            Console.WriteLine( $"Debug Contexts: {string.Join(", ", Library.DebugContexts)}");

            var app = new CommandApp();
            
            app.Configure(c =>
            {
                c.UseStrictParsing();
                c.AddCommand<ProducerSequential>("producer-consumer");
                c.AddCommand<Consumer>("consumer");
            });

            await app.RunAsync(args);
        }
    }
}