using System;
using System.Reflection;
using System.Threading.Tasks;
using Spectre.Console.Cli;

namespace KafkaTool
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            var libAssembly = Assembly.Load("confluent.kafka");
            Console.WriteLine($"Using assembly:{libAssembly.FullName}m location:{libAssembly.Location}");
            var app = new CommandApp();
            
            app.Configure(c =>
            {
                c.UseStrictParsing();
                c.AddCommand<Producer>("producer");
                c.AddCommand<Consumer1>("consumer1");
            });

            await app.RunAsync(args);
        }
    }
}