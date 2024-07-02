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
public class KafkaProperties {
    public CommandLine commandLine;
    public int getNumberOfTopics()
    {
        return Integer.parseInt(commandLine.getOptionValue("topics"));
    }

    public int getReplicationFactor()
    {
        return Integer.parseInt(commandLine.getOptionValue("replication-factor"));
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
        
        options.addOption(config);
        options.addOption(topics);
        options.addOption(partitions);
        options.addOption(replicationFactor);
        options.addOption(minIsr);
        options.addOption(messagesPerSecond);
        
        CommandLineParser parser = new DefaultParser();
        commandLine = parser.parse(options, args);
    }
}
