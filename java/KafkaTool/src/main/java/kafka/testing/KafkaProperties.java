/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
package kafka.testing;

import org.apache.commons.cli.*;

import java.util.HashMap;
import java.util.Map;

public class KafkaProperties {
    public CommandLine commandLine;
    public int getNumberOfTopics()
    {
        return Integer.parseInt(commandLine.getOptionValue("topics"));
    }
    
    public int getNumberOfPartitions()
    {
        return Integer.parseInt(commandLine.getOptionValue("partitions"));
    }
    
    public int getReplicationFactor()
    {
        return Integer.parseInt(commandLine.getOptionValue("replication-factor"));
    }
    
    public int getMinIsr()
    {
        return Integer.parseInt(commandLine.getOptionValue("min-isr"));
    }
    
    public int getMessagesPerSecond()
    {
        return Integer.parseInt(commandLine.getOptionValue("messages-per-second"));
    }

    public int getNumberOfProducers()
    {
        if (!commandLine.hasOption("producers"))
            return 1;
        
        return Integer.parseInt(commandLine.getOptionValue("producers"));
    }

    public int getRecreateTopicsDelay()
    {
        if (!commandLine.hasOption("recreate-topics-delay"))
            return 1;
        
        return Integer.parseInt(commandLine.getOptionValue("recreate-topics-delay"));
    }

    public int getRecreateTopicsBatchSize()
    {
        if (!commandLine.hasOption("recreate-topics-batch-size"))
            return 500;
        
        return Integer.parseInt(commandLine.getOptionValue("recreate-topics-batch-size"));
    }
    
    public String[] getArgs()
    {
        return commandLine.getArgs();
    }
    
    public Map<String, String> getConfigs()
    {
        var strings = commandLine.getOptionValues("config");
        Map<String, String> map = new HashMap<>();

        for (var s : strings) {
            String[] parts = s.split("=", 2); // Split each string into key and value
            if (parts.length != 2) {
                throw new IllegalArgumentException("Invalid config string:" + s);
            }

            map.put(parts[0], parts[1]);
        }
        
        return map;           
    }    

    KafkaProperties(String[] args) throws ParseException
    {
        var options = new Options();

        var config = new Option("c", "config", true, "Additional configuration");
        config.setRequired(false);
        config.setArgs(Option.UNLIMITED_VALUES);

        var topics = new Option(null, "topics", true, "Number of topics");
        topics.setRequired(true);

        var partitions = new Option(null, "partitions", true, "Number of partitions");
        partitions.setRequired(true);
        
        var replicationFactor = new Option(null, "replication-factor", true, "Number of replicas");
        replicationFactor.setRequired(true);
        
        var minIsr = new Option(null, "min-isr", true, "Minimum in-sync replicas");
        minIsr.setRequired(true);

        var messagesPerSecond = new Option(null, "messages-per-second", true, "Messages per second");
        messagesPerSecond.setRequired(true);
        
        var producers = new Option(null, "producers", true, "Number of producers (default 1)");
        producers.setRequired(false);
       
        var recreateTopicsDelay = new Option(null, "recreate-topics-delay", true, "Delay after crating a batch of topics (default 1ms)");
        recreateTopicsDelay.setRequired(false);
          
        var recreateTopicsBatch = new Option(null, "recreate-topics-batch-size", true, "Size of the batch of topics to create (default 500)");
        recreateTopicsBatch.setRequired(false);
       
        options.addOption(config);
        options.addOption(topics);
        options.addOption(partitions);
        options.addOption(replicationFactor);
        options.addOption(minIsr);
        options.addOption(messagesPerSecond);
        options.addOption(producers);
        options.addOption(recreateTopicsDelay);
        options.addOption(recreateTopicsBatch);
        
        CommandLineParser parser = new DefaultParser();
        commandLine = parser.parse(options, args);
    }
}
