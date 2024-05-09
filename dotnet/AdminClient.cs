using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace KafkaTool;

public class AdminClient : AsyncCommand<AdminSettings>
{
    private static readonly ILogger Log = LoggerFactory
        .Create(builder => builder.AddSimpleConsole(configure => configure.SingleLine = true))
        .CreateLogger("Admin1");

    static string ToString(int[] array) => $"[{string.Join(", ", array)}]";

    public override async Task<int> ExecuteAsync(CommandContext context, AdminSettings settings)
    {
        await Task.Delay(1);
        Log.Log(LogLevel.Information, $"librdkafka Version: {Library.VersionString} ({Library.Version:X})");
        Log.Log(LogLevel.Information, $"Debug Contexts: {string.Join(", ", Library.DebugContexts)}");

        var config = new AdminClientConfig { };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .AddIniFile(settings.IniFile)
            .Build();

        using (var adminClient = new AdminClientBuilder(configuration.AsEnumerable()).Build())
        {
            ListGroups(adminClient);
            await ListConsumerGroupsAsync(adminClient);
            PrintMetadata(adminClient);
            // await ListOffsetsAsync(adminClient);
            await DescribeClusterAsync(adminClient);
        }

        return 0;
    }

    static void ListGroups(IAdminClient adminClient)
    {
        {
            // Warning: The API for this functionality is subject to change.
            var groups = adminClient.ListGroups(TimeSpan.FromSeconds(10));
            Log.Log(LogLevel.Information, $"Consumer Groups:");
            foreach (var g in groups)
            {
                Log.Log(LogLevel.Information,
                    $"  Group: {g.Group} {g.Error} {g.State}, Protocol: {g.ProtocolType} {g.Protocol}");
                // Log.Log(LogLevel.Information, $"  Broker: {g.Broker.BrokerId} {g.Broker.Host}:{g.Broker.Port}");
                Log.Log(LogLevel.Information, $"  Members:");
                foreach (var m in g.Members)
                {
                    Log.Log(LogLevel.Information, $"    {m.MemberId} {m.ClientId} {m.ClientHost}");
                    Log.Log(LogLevel.Information, $"    Metadata: {m.MemberMetadata.Length} bytes");
                    Log.Log(LogLevel.Information, $"    Assignment: {m.MemberAssignment.Length} bytes");
                }
            }
        }
    }

    static void PrintMetadata(IAdminClient adminClient)
    {
        var meta = adminClient.GetMetadata(TimeSpan.FromSeconds(20));
        // Log.Log(LogLevel.Information, $"{meta.OriginatingBrokerId} {meta.OriginatingBrokerName}");
        meta.Brokers.ForEach(broker =>
            Log.Log(LogLevel.Information, $"Broker: {broker.BrokerId} {broker.Host}:{broker.Port}"));

        meta.Topics.ForEach(topic =>
        {
            Log.Log(LogLevel.Information, $"Topic: {topic.Topic} {topic.Error}");
            topic.Partitions.ForEach(partition =>
            {
                Log.Log(LogLevel.Information,
                    $"  Partition: {partition.PartitionId}, Replicas: {ToString(partition.Replicas)}, InSyncReplicas: {ToString(partition.InSyncReplicas)}");
            });
        });
    }
    
    static void PrintListOffsetsResultInfos(List<ListOffsetsResultInfo> ListOffsetsResultInfos)
    {
        foreach (var listOffsetsResultInfo in ListOffsetsResultInfos)
        {
            Log.Log(LogLevel.Information, "  ListOffsetsResultInfo:");
            Log.Log(LogLevel.Information,
                $"    TopicPartitionOffsetError: {listOffsetsResultInfo.TopicPartitionOffsetError}");
            Log.Log(LogLevel.Information, $"    Timestamp: {listOffsetsResultInfo.Timestamp}");
        }
    }

    static async Task ListConsumerGroupsAsync(IAdminClient adminClient)
    {
        try
        {
            var result = await adminClient.ListConsumerGroupsAsync(new ListConsumerGroupsOptions()
            {
            });
            Log.Log(LogLevel.Information, result.ToString());
        }
        catch (KafkaException e)
        {
            Log.Log(LogLevel.Information, "An error occurred listing consumer groups." +
                                          $" Code: {e.Error.Code}" +
                                          $", Reason: {e.Error.Reason}");
            Environment.ExitCode = 1;
            return;
        }
    }


    static async Task DescribeConsumerGroupsAsync(string bootstrapServers, string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            Log.Log(LogLevel.Information,
                "usage: .. <bootstrapServers> describe-consumer-groups <username> <password> <include_authorized_operations> <group1> [<group2 ... <groupN>]");
            Environment.ExitCode = 1;
            return;
        }

        var username = commandArgs[0];
        var password = commandArgs[1];
        var includeAuthorizedOperations = (commandArgs[2] == "1");
        var groupNames = commandArgs.Skip(3).ToList();

        if (string.IsNullOrWhiteSpace(username))
        {
            username = null;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            password = null;
        }

        var timeout = TimeSpan.FromSeconds(30);
        var config = new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
        };
        if (username != null && password != null)
        {
            config = new AdminClientConfig
            {
                BootstrapServers = bootstrapServers,
                SecurityProtocol = SecurityProtocol.SaslPlaintext,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = username,
                SaslPassword = password,
            };
        }

        using (var adminClient = new AdminClientBuilder(config).Build())
        {
            try
            {
                var descResult = await adminClient.DescribeConsumerGroupsAsync(groupNames,
                    new DescribeConsumerGroupsOptions()
                        { RequestTimeout = timeout, IncludeAuthorizedOperations = includeAuthorizedOperations });
                foreach (var group in descResult.ConsumerGroupDescriptions)
                {
                    Log.Log(LogLevel.Information, $"\n  Group: {group.GroupId} {group.Error}");
                    Log.Log(LogLevel.Information, $"  Broker: {group.Coordinator}");
                    Log.Log(LogLevel.Information, $"  IsSimpleConsumerGroup: {group.IsSimpleConsumerGroup}");
                    Log.Log(LogLevel.Information, $"  PartitionAssignor: {group.PartitionAssignor}");
                    Log.Log(LogLevel.Information, $"  State: {group.State}");
                    Log.Log(LogLevel.Information, $"  Members:");
                    foreach (var m in group.Members)
                    {
                        Log.Log(LogLevel.Information, $"    {m.ClientId} {m.ConsumerId} {m.Host}");
                        Log.Log(LogLevel.Information, $"    Assignment:");
                        var topicPartitions = "";
                        if (m.Assignment.TopicPartitions != null)
                        {
                            topicPartitions = String.Join(", ",
                                m.Assignment.TopicPartitions.Select(tp => tp.ToString()));
                        }

                        Log.Log(LogLevel.Information, $"      TopicPartitions: [{topicPartitions}]");
                    }

                    if (includeAuthorizedOperations)
                    {
                        string operations = string.Join(" ", group.AuthorizedOperations);
                        Log.Log(LogLevel.Information, $"  Authorized operations: {operations}");
                    }
                }
            }
            catch (KafkaException e)
            {
                Log.Log(LogLevel.Information, $"An error occurred describing consumer groups: {e}");
                Environment.ExitCode = 1;
            }
        }
    }

    static async Task IncrementalAlterConfigsAsync(string bootstrapServers, string[] commandArgs)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var configResourceList = new Dictionary<ConfigResource, List<ConfigEntry>>();
        try
        {
            if (commandArgs.Length > 0)
            {
                timeout = TimeSpan.FromSeconds(Int32.Parse(commandArgs[0]));
            }

            if (((commandArgs.Length - 1) % 3) != 0)
            {
                throw new ArgumentException("invalid arguments length");
            }

            for (int i = 1; i < commandArgs.Length; i += 3)
            {
                var resourceType = Enum.Parse<ResourceType>(commandArgs[i]);
                var resourceName = commandArgs[i + 1];
                var configs = commandArgs[i + 2];
                var configList = new List<ConfigEntry>();
                foreach (var config in configs.Split(";"))
                {
                    var nameOpValue = config.Split("=");
                    if (nameOpValue.Length != 2)
                    {
                        throw new ArgumentException($"invalid alteration name \"{config}\"");
                    }

                    var name = nameOpValue[0];
                    var opValue = nameOpValue[1].Split(":");
                    if (opValue.Length != 2)
                    {
                        throw new ArgumentException($"invalid alteration value \"{nameOpValue[1]}\"");
                    }

                    var op = Enum.Parse<AlterConfigOpType>(opValue[0]);
                    var value = opValue[1];
                    configList.Add(new ConfigEntry
                    {
                        Name = name,
                        Value = value,
                        IncrementalOperation = op
                    });
                }

                var resource = new ConfigResource
                {
                    Name = resourceName,
                    Type = resourceType
                };
                configResourceList[resource] = configList;
            }
        }
        catch (Exception e) when (
            e is ArgumentException ||
            e is FormatException
        )
        {
            Log.Log(LogLevel.Information, $"error: {e.Message}");
            Log.Log(LogLevel.Information,
                "usage: .. <bootstrapServers> incremental-alter-configs [<timeout_seconds> <resource-type1> <resource-name1> <config-name1=op-type1:config-value1;config-name1=op-type1:config-value1> ...]");
            Environment.ExitCode = 1;
            return;
        }

        using (var adminClient =
               new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build())
        {
            try
            {
                var alterResultList = await adminClient.IncrementalAlterConfigsAsync(configResourceList,
                    new IncrementalAlterConfigsOptions() { RequestTimeout = timeout });
                foreach (var alterResult in alterResultList)
                {
                    Log.Log(LogLevel.Information, $"Resource {alterResult.ConfigResource} altered correctly");
                }
            }
            catch (IncrementalAlterConfigsException e)
            {
                foreach (var alterResult in e.Results)
                {
                    Log.Log(LogLevel.Information,
                        $"Resource {alterResult.ConfigResource} had error: {alterResult.Error}");
                }

                Environment.ExitCode = 1;
            }
            catch (Exception e)
            {
                Log.Log(LogLevel.Information, $"An error occurred altering configs incrementally: {e.Message}");
                Environment.ExitCode = 1;
            }
        }
    }

    static async Task ListOffsetsAsync(IAdminClient adminClient)
    {
        var timeout = TimeSpan.FromSeconds(30);
        ListOffsetsOptions options = new ListOffsetsOptions()
            { RequestTimeout = timeout, IsolationLevel = IsolationLevel.ReadUncommitted };

        try
        {
            var listOffsetsResult = await adminClient.ListOffsetsAsync(
                new[]
                {
                    new TopicPartitionOffsetSpec
                    {
                        TopicPartition = new TopicPartition("topic1", 0), OffsetSpec = OffsetSpec.Latest()
                    }
                }, options);
            Log.Log(LogLevel.Information, "ListOffsetsResult:");
            PrintListOffsetsResultInfos(listOffsetsResult.ResultInfos);
        }
        catch (ListOffsetsException e)
        {
            Log.Log(LogLevel.Information, "ListOffsetsReport:");
            Log.Log(LogLevel.Information, $"  Error: {e.Error}");
            PrintListOffsetsResultInfos(e.Result.ResultInfos);
        }
        catch (KafkaException e)
        {
            Log.Log(LogLevel.Information, $"An error occurred listing offsets: {e}");
            Environment.ExitCode = 1;
        }
    }

    static void PrintTopicDescriptions(List<TopicDescription> topicDescriptions, bool includeAuthorizedOperations)
    {
        foreach (var topic in topicDescriptions)
        {
            Log.Log(LogLevel.Information, $"\n  Topic: {topic.Name} {topic.Error}");
            Log.Log(LogLevel.Information, $"  Topic Id: {topic.TopicId}");
            Log.Log(LogLevel.Information, $"  Partitions:");
            foreach (var partition in topic.Partitions)
            {
                Log.Log(LogLevel.Information,
                    $"    Partition ID: {partition.Partition} with leader: {partition.Leader}");
                if (!partition.ISR.Any())
                {
                    Log.Log(LogLevel.Information, "      There is no In-Sync-Replica broker for the partition");
                }
                else
                {
                    string isrs = string.Join("; ", partition.ISR);
                    Log.Log(LogLevel.Information, $"      The In-Sync-Replica brokers are: {isrs}");
                }

                if (!partition.Replicas.Any())
                {
                    Log.Log(LogLevel.Information, "      There is no Replica broker for the partition");
                }
                else
                {
                    string replicas = string.Join("; ", partition.Replicas);
                    Log.Log(LogLevel.Information, $"      The Replica brokers are: {replicas}");
                }
            }

            Log.Log(LogLevel.Information, $"  Is internal: {topic.IsInternal}");
            if (includeAuthorizedOperations)
            {
                string operations = string.Join(" ", topic.AuthorizedOperations);
                Log.Log(LogLevel.Information, $"  Authorized operations: {operations}");
            }
        }
    }

    static async Task DescribeTopicsAsync(string bootstrapServers, string[] commandArgs)
    {
        if (commandArgs.Length < 3)
        {
            Log.Log(LogLevel.Information,
                "usage: .. <bootstrapServers> describe-topics <username> <password> <include_authorized_operations> <topic1> [<topic2 ... <topicN>]");
            Environment.ExitCode = 1;
            return;
        }

        var username = commandArgs[0];
        var password = commandArgs[1];
        var includeAuthorizedOperations = (commandArgs[2] == "1");
        if (string.IsNullOrWhiteSpace(username))
        {
            username = null;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            password = null;
        }

        var topicNames = commandArgs.Skip(3).ToList();

        var timeout = TimeSpan.FromSeconds(30);
        var config = new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
        };
        if (username != null && password != null)
        {
            config = new AdminClientConfig
            {
                BootstrapServers = bootstrapServers,
                SecurityProtocol = SecurityProtocol.SaslPlaintext,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = username,
                SaslPassword = password,
            };
        }

        using (var adminClient = new AdminClientBuilder(config).Build())
        {
            try
            {
                var descResult = await adminClient.DescribeTopicsAsync(
                    TopicCollection.OfTopicNames(topicNames),
                    new DescribeTopicsOptions()
                        { RequestTimeout = timeout, IncludeAuthorizedOperations = includeAuthorizedOperations });
                PrintTopicDescriptions(descResult.TopicDescriptions, includeAuthorizedOperations);
            }
            catch (DescribeTopicsException e)
            {
                // At least one TopicDescription will have an error.
                PrintTopicDescriptions(e.Results.TopicDescriptions, includeAuthorizedOperations);
            }
            catch (KafkaException e)
            {
                Log.Log(LogLevel.Information, $"An error occurred describing topics: {e}");
                Environment.ExitCode = 1;
            }
        }
    }

    static async Task DescribeClusterAsync(IAdminClient adminClient)
    {
        try
        {
            var descResult = await adminClient.DescribeClusterAsync(new DescribeClusterOptions()
                { RequestTimeout = TimeSpan.FromSeconds(30), IncludeAuthorizedOperations = true });

            Log.Log(LogLevel.Information,
                $"  Cluster Id: {descResult.ClusterId}\n  Controller: {descResult.Controller}");
            Log.Log(LogLevel.Information, "  Nodes:");
            foreach (var node in descResult.Nodes)
            {
                Log.Log(LogLevel.Information, $"    {node}");
            }

            string operations = string.Join(" ", descResult.AuthorizedOperations);
            Log.Log(LogLevel.Information, $"  Authorized operations: {operations}");
        }
        catch (KafkaException e)
        {
            Log.Log(LogLevel.Information, $"An error occurred describing cluster: {e}");
            Environment.ExitCode = 1;
        }
    }
}