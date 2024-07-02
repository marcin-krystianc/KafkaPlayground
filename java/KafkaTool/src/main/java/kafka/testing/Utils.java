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

    public static void printHelp(String message, Object... args) {
        System.out.println(format(message, args));
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
    
    public static void recreateTopics(String bootstrapServers, int numPartitions, short replicationFactor, String... topicNames) {
        Properties props = new Properties();
        props.put(AdminClientConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
        props.put(AdminClientConfig.CLIENT_ID_CONFIG, "client-" + UUID.randomUUID());
        Map<String, String> configs = new HashMap<>() {{
            put("min.insync.replicas", String.valueOf(replicationFactor - 1));
        }};        

        try (Admin admin = Admin.create(props)) {
            // delete topics if present
            try {
                admin.deleteTopics(Arrays.asList(topicNames)).all().get();
            } catch (ExecutionException e) {
                if (!(e.getCause() instanceof UnknownTopicOrPartitionException)) {
                    throw e;
                }
                printErr("Topics deletion error: %s", e.getCause());
            }
            printOut("Deleted topics: %s", Arrays.toString(topicNames));
            // create topics in a retry loop
            while (true) {
                List<NewTopic> newTopics = Arrays.stream(topicNames)
                    .map(name -> new NewTopic(name, numPartitions, replicationFactor).configs(configs))
                    .collect(Collectors.toList());
                try {
                    admin.createTopics(newTopics).all().get();
                    printOut("Created topics: %s", Arrays.toString(topicNames));
                    break;
                } catch (ExecutionException e) {
                    if (!(e.getCause() instanceof TopicExistsException)) {
                        throw e;
                    }
                    printOut("Waiting for topics metadata cleanup");
                    TimeUnit.MILLISECONDS.sleep(1_000);
                }
            }
        } catch (Throwable e) {
            throw new RuntimeException("Topics creation error", e);
        }
    }
}
