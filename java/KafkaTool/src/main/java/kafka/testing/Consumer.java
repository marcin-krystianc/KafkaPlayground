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

import org.apache.kafka.clients.consumer.ConsumerConfig;
import org.apache.kafka.clients.consumer.ConsumerRebalanceListener;
import org.apache.kafka.clients.consumer.ConsumerRecord;
import org.apache.kafka.clients.consumer.ConsumerRecords;
import org.apache.kafka.clients.consumer.KafkaConsumer;
import org.apache.kafka.clients.consumer.NoOffsetForPartitionException;
import org.apache.kafka.clients.consumer.OffsetOutOfRangeException;
import org.apache.kafka.common.KafkaException;
import org.apache.kafka.common.TopicPartition;
import org.apache.kafka.common.errors.AuthorizationException;
import org.apache.kafka.common.errors.RecordDeserializationException;
import org.apache.kafka.common.errors.UnsupportedVersionException;
import org.apache.kafka.common.serialization.IntegerDeserializer;
import org.apache.kafka.common.serialization.LongDeserializer;

import java.time.Duration;
import java.util.*;

/**
 * A simple consumer thread that subscribes to a topic, fetches new records and prints them.
 * The thread does not stop until all records are completed or an exception is raised.
 */
public class Consumer extends Thread implements ConsumerRebalanceListener {
    private final String[] topics;
    private final KafkaProperties kafkaProperties;
    private volatile boolean closed;
    private final KafkaData kafkaData;

    public Consumer(KafkaProperties kafkaProperties,
                    KafkaData kafkaData,
                    String[] topics) {
        super("consumer");
        this.kafkaProperties = kafkaProperties; 
        this.kafkaData = kafkaData;
        this.topics = topics;
    }

    @Override
    public void run() {

        // the consumer instance is NOT thread safe
        try (KafkaConsumer<Integer, Long> consumer = createKafkaConsumer(kafkaProperties.getConfigs())) {

            Map<String, Map<Integer, ConsumerRecord<Integer, Long>>> consumeResults = new HashMap<>();
            // subscribes to a list of topics to get dynamically assigned partitions
            // this class implements the rebalance listener that we pass here to be notified of such events
            consumer.subscribe(Arrays.asList(topics), this);

            while (!closed) {
                try {
                 
                    // if required, poll updates partition assignment and invokes the configured rebalance listener
                    // then tries to fetch records sequentially using the last committed offset or auto.offset.reset policy
                    // returns immediately if there are records or times out returning an empty record set
                    // the next poll must be called within session.timeout.ms to avoid group rebalance
                    ConsumerRecords<Integer, Long> records = consumer.poll(Duration.ofSeconds(1));
                    for (ConsumerRecord<Integer, Long> record : records) {

                        double latency = (double)(System.currentTimeMillis() - record.timestamp());
                        kafkaData.digestConsumerLatency(latency);
                        kafkaData.incrementConsumed();
   
                        if (!consumeResults.containsKey(record.topic())) {
                            consumeResults.put(record.topic(), new HashMap<>());
                        }

                        var dictionary = consumeResults.get(record.topic());
                        var previousResult = dictionary.getOrDefault(record.key(), null);
                        if (previousResult != null) {
                            if (record.value() != previousResult.value() + 1) {
                                Utils.printErr("Unexpected message value topic %s/%s [%d], Offset=%d/%d, LeaderEpoch=%d/%d Value=%d/%d %n"
                                        , record.topic()
                                        , record.key().toString(), record.partition()
                                        , previousResult.offset(), record.offset()
                                        , previousResult.leaderEpoch().orElse(-1), record.leaderEpoch().orElse(-1)
                                        , previousResult.value(), record.value()
                                );

                                if (record.value() < previousResult.value() + 1) {
                                    kafkaData.incrementDuplicated();
                                }

                                if (record.value() > previousResult.value() + 1) {
                                    kafkaData.incrementOutOfOrder();
                                }                                        
                            }
                        }

                        dictionary.put(record.key(), record);
                    }
                } catch (AuthorizationException | UnsupportedVersionException
                         | RecordDeserializationException e) {
                    // we can't recover from these exceptions
                    Utils.printErr(e.getMessage());
                    shutdown();
                } catch (OffsetOutOfRangeException | NoOffsetForPartitionException e) {
                    // invalid or no offset found without auto.reset.policy
                    Utils.printOut("Invalid or no offset found, using latest");
                    consumer.seekToEnd(e.partitions());
                } catch (KafkaException e) {
                    // log the exception and try to continue
                    Utils.printErr(e.getMessage());
                }
            }
        } catch (Throwable e) {
            Utils.printErr("Unhandled exception");
            e.printStackTrace();
        }
        
        shutdown();
    }

    public void shutdown() {
        if (!closed) {
            closed = true;
        }
    }

    public KafkaConsumer<Integer, Long> createKafkaConsumer(Map<String, String> configs) {
        Properties props = new Properties();

        // client id is not required, but it's good to track the source of requests beyond just ip/port
        // by allowing a logical application name to be included in server-side request logging
        props.put(ConsumerConfig.CLIENT_ID_CONFIG, "client-" + UUID.randomUUID());

        // consumer group id is required when we use subscribe(topics) for group management
        props.put(ConsumerConfig.GROUP_ID_CONFIG, "group-" + UUID.randomUUID());
        
        // key and value are just byte arrays, so we need to set appropriate deserializers
        props.put(ConsumerConfig.KEY_DESERIALIZER_CLASS_CONFIG, IntegerDeserializer.class);
        props.put(ConsumerConfig.VALUE_DESERIALIZER_CLASS_CONFIG, LongDeserializer.class);

        // sets the reset offset policy in case of invalid or no offset
        props.put(ConsumerConfig.AUTO_OFFSET_RESET_CONFIG, "earliest");
        for (var entry : configs.entrySet())
        {
            props.put(entry.getKey(), entry.getValue());
        }

        return new KafkaConsumer<>(props);
    }

    @Override
    public void onPartitionsRevoked(Collection<TopicPartition> partitions) {
        Utils.printOut("Revoked partitions: %d", partitions.size());
        // this can be used to commit pending offsets when using manual commit and EOS is disabled
    }

    @Override
    public void onPartitionsAssigned(Collection<TopicPartition> partitions) {
        Utils.printOut("Assigned partitions: %d", partitions.size());
        // this can be used to read the offsets from an external store or some other initialization
    }

    @Override
    public void onPartitionsLost(Collection<TopicPartition> partitions) {
        Utils.printOut("Lost partitions: %d", partitions.size());
        // this is called when partitions are reassigned before we had a chance to revoke them gracefully
        // we can't commit pending offsets because these partitions are probably owned by other consumers already
        // nevertheless, we may need to do some other cleanup
    }
}
