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

    // Option objects as class members
    private final Option config;
    private final Option topics;
    private final Option partitions;
    private final Option replicationFactor;
    private final Option minIsr;
    private final Option messagesPerSecond;
    private final Option producers;
    private final Option recreateTopicsDelay;
    private final Option recreateTopicsBatchSize;
    private final Option topicStem;
    private final Option recreateTopics;
    private final Option burstMessagesPerSecond;
    private final Option burstCycle;
    private final Option burstDuration;
    private final Option reportingCycle;
    private final Option extraPayloadBytes;
    private final Option avoidAllocations;
    private final Option exitAfter;
    private final Option enableSequenceValidation;

    public int getNumberOfTopics() {
        return Integer.parseInt(commandLine.getOptionValue(topics));
    }

    public int getNumberOfPartitions() {
        return Integer.parseInt(commandLine.getOptionValue(partitions));
    }

    public int getReplicationFactor() {
        return Integer.parseInt(commandLine.getOptionValue(replicationFactor));
    }

    public int getMinIsr() {
        return Integer.parseInt(commandLine.getOptionValue(minIsr));
    }

    public int getMessagesPerSecond() {
        return Integer.parseInt(commandLine.getOptionValue(messagesPerSecond));
    }

    public int getNumberOfProducers() {
        if (!commandLine.hasOption(producers)) {
            return 1;
        }
        return Integer.parseInt(commandLine.getOptionValue(producers));
    }

    public int getRecreateTopicsDelay() {
        if (!commandLine.hasOption(recreateTopicsDelay)) {
            return 1;
        }
        return Integer.parseInt(commandLine.getOptionValue(recreateTopicsDelay));
    }

    public int getRecreateTopicsBatchSize() {
        if (!commandLine.hasOption(recreateTopicsBatchSize)) {
            return 500;
        }
        return Integer.parseInt(
                commandLine.getOptionValue(recreateTopicsBatchSize)
        );
    }

    public String getTopicStem() {
        if (!commandLine.hasOption(topicStem)) {
            return "my-topic";
        }
        return commandLine.getOptionValue(topicStem);
    }

    public boolean getRecreateTopics() {
        if (!commandLine.hasOption(recreateTopics)) {
            return true;
        }
        return Boolean.parseBoolean(commandLine.getOptionValue(recreateTopics));
    }

    public int getBurstMessagesPerSecond() {
        if (!commandLine.hasOption(burstMessagesPerSecond)) {
            return 0;
        }
        return Integer.parseInt(
                commandLine.getOptionValue(burstMessagesPerSecond)
        );
    }

    public int getBurstCycle() {
        if (!commandLine.hasOption(burstCycle)) {
            return 0;
        }
        return Integer.parseInt(commandLine.getOptionValue(burstCycle));
    }

    public int getBurstDuration() {
        if (!commandLine.hasOption(burstDuration.getLongOpt())) {
            return 0;
        }
        return Integer.parseInt(commandLine.getOptionValue(burstDuration));
    }

    public int getReportingCycle() {
        if (!commandLine.hasOption(reportingCycle)) {
            return 10000;
        }
        return Integer.parseInt(commandLine.getOptionValue(reportingCycle));
    }

    public int getExtraPayloadBytes() {
        if (!commandLine.hasOption(extraPayloadBytes)) {
            return 0;
        }
        return Integer.parseInt(commandLine.getOptionValue(extraPayloadBytes));
    }

    public boolean getAvoidAllocations() {
        if (!commandLine.hasOption(avoidAllocations)) {
            return true;
        }
        return Boolean.parseBoolean(commandLine.getOptionValue(avoidAllocations));
    }

    public int getExitAfter() {
        if (!commandLine.hasOption(exitAfter.getLongOpt())) {
            return 0;
        }
        return Integer.parseInt(commandLine.getOptionValue(exitAfter));
    }

    public boolean getEnableSequenceValidation() {
        if (!commandLine.hasOption(enableSequenceValidation)) {
            return true;
        }
        return Boolean.parseBoolean(commandLine.getOptionValue(enableSequenceValidation));
    }

    public String[] getArgs() {
        return commandLine.getArgs();
    }

    public Map<String, String> getConfigs() {
        var strings = commandLine.getOptionValues(config.getLongOpt());
        Map<String, String> map = new HashMap<>();

        if (strings != null) {
            for (var s : strings) {
                String[] parts = s.split("=", 2); // Split each string into key and value
                if (parts.length != 2) {
                    throw new IllegalArgumentException("Invalid config string:" + s);
                }
                map.put(parts[0], parts[1]);
            }
        }
        return map;
    }

    KafkaProperties(String[] args) throws ParseException {
        // Initialize all option objects
        config = new Option("c", "config", true, "Additional configuration");
        config.setRequired(false);
        config.setArgs(Option.UNLIMITED_VALUES);

        topics = new Option(null, "topics", true, "Number of topics");
        topics.setRequired(true);

        partitions = new Option(null, "partitions", true, "Number of partitions");
        partitions.setRequired(true);

        replicationFactor = new Option(null, "replication-factor", true, "Number of replicas");
        replicationFactor.setRequired(true);

        minIsr = new Option(null, "min-isr", true, "Minimum in-sync replicas");
        minIsr.setRequired(true);

        messagesPerSecond = new Option(null, "messages-per-second", true, "Messages per second");
        messagesPerSecond.setRequired(true);

        producers = new Option(null, "producers", true, "Number of producers (default 1)");
        producers.setRequired(false);

        recreateTopicsDelay = new Option(null, "recreate-topics-delay", true,                "Delay after creating a batch of topics (default 1ms)");
        recreateTopicsDelay.setRequired(false);

        recreateTopicsBatchSize = new Option(null, "recreate-topics-batch-size", true,                "Size of the batch of topics to create (default 500)");
        recreateTopicsBatchSize.setRequired(false);

        topicStem = new Option(null, "topic-stem", true, "Topic stem");
        topicStem.setRequired(false);

        recreateTopics = new Option(null, "recreate-topics", true, "Recreate topics?");
        recreateTopics.setRequired(false);

        burstMessagesPerSecond = new Option(null, "burst-messages-per-second", true,                "Number of messages per second (burst)");
        burstMessagesPerSecond.setRequired(false);

        burstCycle = new Option(null, "burst-cycle", true, "Burst cycle in ms");
        burstCycle.setRequired(false);

        burstDuration = new Option(null, "burst-duration", true, "Burst duration in ms");
        burstDuration.setRequired(false);

        reportingCycle = new Option(null, "reporting-cycle", true, "Reporting cycle in ms");
        reportingCycle.setRequired(false);

        extraPayloadBytes = new Option(null, "extra-payload-bytes", true,
                "Additional payload in bytes");
        extraPayloadBytes.setRequired(false);

        avoidAllocations = new Option(null, "avoid-allocations", true,
                "Avoid unnecessary memory allocations");
        avoidAllocations.setRequired(false);

        exitAfter = new Option(null, "exit-after", true,"Exits after provided amount of seconds");
        exitAfter.setRequired(false);

        enableSequenceValidation = new Option(null, "enable-sequence-validation", true,"Indicates that the consumer will not validate whether the messages are in sequence");
        enableSequenceValidation.setRequired(false);
        
        // Create options object and add all options
        var options = new Options();
        options.addOption(config);
        options.addOption(topics);
        options.addOption(partitions);
        options.addOption(replicationFactor);
        options.addOption(minIsr);
        options.addOption(messagesPerSecond);
        options.addOption(producers);
        options.addOption(recreateTopicsDelay);
        options.addOption(recreateTopicsBatchSize);
        options.addOption(topicStem);
        options.addOption(recreateTopics);
        options.addOption(burstMessagesPerSecond);
        options.addOption(burstCycle);
        options.addOption(burstDuration);
        options.addOption(reportingCycle);
        options.addOption(extraPayloadBytes);
        options.addOption(avoidAllocations);
        options.addOption(exitAfter);
        options.addOption(enableSequenceValidation);

        CommandLineParser parser = new DefaultParser();
        commandLine = parser.parse(options, args);
    }
}
