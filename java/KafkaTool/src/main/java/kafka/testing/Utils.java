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

import org.apache.kafka.clients.admin.Admin;
import org.apache.kafka.clients.admin.AdminClientConfig;
import org.apache.kafka.clients.admin.NewTopic;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.apache.kafka.clients.producer.RecordMetadata;
import org.apache.kafka.common.errors.TopicExistsException;
import org.apache.kafka.common.errors.UnknownTopicOrPartitionException;

import java.text.SimpleDateFormat;
import java.util.*;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.TimeUnit;
import java.util.stream.Collectors;

import static java.lang.String.format;

public class Utils {
    private Utils() {
    }

    public static void printOut(String message, Object... args) {
        Date time = new java.util.Date(System.currentTimeMillis());
        System.out.print(new SimpleDateFormat("[HH:mm:ss] ").format(time));
        System.out.printf("%s - %s%n", Thread.currentThread().getName(), format(message, args));
    }

    public static void printErr(String message, Object... args) {
        Date time = new java.util.Date(System.currentTimeMillis());
        System.err.print(new SimpleDateFormat("[HH:mm:ss] ").format(time));
        System.err.printf("%s - %s%n", Thread.currentThread().getName(), format(message, args));
    }

    public static void recreateTopics(Map<String, String> configs, int numPartitions, int replicationFactor, int minIsr, int delay, int batchSize, String... topicNames) {
        Properties props = new Properties();
        props.put(AdminClientConfig.CLIENT_ID_CONFIG, "client-" + UUID.randomUUID());
        for (var entry : configs.entrySet())
        {
            props.put(entry.getKey(), entry.getValue());
        }
        
        Map<String, String> topicConfigs = new HashMap<>() {{
            put("min.insync.replicas", String.valueOf(minIsr));
        }};

        try (Admin admin = Admin.create(props)) {
            // delete topics if present
            try {
                admin.deleteTopics(Arrays.asList(topicNames)).all().get();
                Thread.sleep(delay);
            } catch (ExecutionException e) {
                if (!(e.getCause() instanceof UnknownTopicOrPartitionException)) {
                    throw e;
                }
                printErr("Topics deletion error: %s", e.getCause());
            }
            printOut("Deleted topics");
            Thread.sleep(1);
            List<NewTopic> newTopics = Arrays.stream(topicNames)
                .map(name -> new NewTopic(name, numPartitions, (short)replicationFactor).configs(topicConfigs))
                .collect(Collectors.toList());

            // Iterate over the list in batches
            for (int i = 0; i < newTopics.size(); i += batchSize) {
                int end = Math.min(i + batchSize, newTopics.size());
                printOut("Creating %d topics", end - i);
                admin.createTopics(newTopics.subList(i, end)).all().get();
                Thread.sleep(delay);
            }
        } catch (Throwable e) {
            throw new RuntimeException("Topics creation error", e);
        }
    }

    public static String GetTopicName(String topicStem, int i)
    {
        return topicStem + "-" + String.format("%04d", i);
    }

}
