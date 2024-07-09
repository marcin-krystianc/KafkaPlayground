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

import org.apache.kafka.clients.producer.Callback;
import org.apache.kafka.clients.producer.KafkaProducer;
import org.apache.kafka.clients.producer.ProducerConfig;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.apache.kafka.clients.producer.RecordMetadata;
import org.apache.kafka.common.KafkaException;
import org.apache.kafka.common.errors.AuthorizationException;
import org.apache.kafka.common.errors.FencedInstanceIdException;
import org.apache.kafka.common.errors.OutOfOrderSequenceException;
import org.apache.kafka.common.errors.ProducerFencedException;
import org.apache.kafka.common.errors.RetriableException;
import org.apache.kafka.common.errors.SerializationException;
import org.apache.kafka.common.errors.UnsupportedVersionException;
import org.apache.kafka.common.serialization.IntegerSerializer;
import org.apache.kafka.common.serialization.StringSerializer;

import java.util.Map;
import java.util.Properties;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * A simple producer thread supporting two send modes:
 * - Async mode (default): records are sent without waiting for the response.
 * - Sync mode: each send operation blocks waiting for the response.
 */
public class Producer extends Thread {
    private final String[] topics;
    private final KafkaProperties kafkaProperties;
    private volatile boolean closed;
    private final AtomicInteger sentRecords = new AtomicInteger(0);

    public Producer(KafkaProperties kafkaProperties,
                    String[] topics
    ) {
        super("producer");
        this.kafkaProperties = kafkaProperties;
        this.topics = topics;
    }

    public long GetSentRecords() {
        return this.sentRecords.get();
    }

    @Override
    public void run() {
        int messagesToSend = 0;
        long startTime = System.currentTimeMillis();

        // the producer instance is thread safe
        try (KafkaProducer<Integer, Integer> producer = createKafkaProducer(kafkaProperties.getConfigs())) {

            var numberOfKeys = kafkaProperties.getNumberOfPartitions() * 7;
            for (var value = 0; ; value++)
                for (var key = 0; key < numberOfKeys; key++)
                    for (var topic : this.topics) {
                        if (closed)
                            return;

                        var currentTime = System.currentTimeMillis();
                        if (messagesToSend == 0) {
                            var elapsedTime = currentTime - startTime;
                            if (elapsedTime < 100) {
                                Thread.sleep(100 - elapsedTime); // Sleep for 1/10 second
                            }

                            startTime = System.currentTimeMillis();
                            messagesToSend = kafkaProperties.getMessagesPerSecond() / 10;
                        }
                        messagesToSend--;

                        asyncSend(producer, topic, key, value);
                        sentRecords.incrementAndGet();
                    }
        } catch (Throwable e) {
            Utils.printErr("Unhandled exception");
            e.printStackTrace();
        } finally {
            shutdown();
        }
    }

    public void shutdown() {
        if (!closed) {
            closed = true;
        }
    }

    public KafkaProducer<Integer, Integer> createKafkaProducer(Map<String, String> configs) {
        Properties props = new Properties();
        // client id is not required, but it's good to track the source of requests beyond just ip/port
        // by allowing a logical application name to be included in server-side request logging
        props.put(ProducerConfig.CLIENT_ID_CONFIG, "client-" + UUID.randomUUID());
        // key and value are just byte arrays, so we need to set appropriate serializers
        props.put(ProducerConfig.KEY_SERIALIZER_CLASS_CONFIG, IntegerSerializer.class);
        props.put(ProducerConfig.VALUE_SERIALIZER_CLASS_CONFIG, IntegerSerializer.class);
        props.put(ProducerConfig.MAX_BLOCK_MS_CONFIG, 180000);

        for (var entry : configs.entrySet()) {
            props.put(entry.getKey(), entry.getValue());
        }

        return new KafkaProducer<>(props);
    }

    private void asyncSend(KafkaProducer<Integer, Integer> producer, String topic, int key, int value) {
        // send the record asynchronously, setting a callback to be notified of the result
        // note that, even if you set a small batch.size with linger.ms=0, the send operation
        // will still be blocked when buffer.memory is full or metadata are not available
        producer.send(new ProducerRecord<>(topic, key, value), new ProducerCallback(key, value));
    }

    class ProducerCallback implements Callback {
        private final int key;
        private final int value;

        public ProducerCallback(int key, Integer value) {
            this.key = key;
            this.value = value;
        }

        /**
         * A callback method the user can implement to provide asynchronous handling of request completion. This method will
         * be called when the record sent to the server has been acknowledged. When exception is not null in the callback,
         * metadata will contain the special -1 value for all fields except for topicPartition, which will be valid.
         *
         * @param metadata  The metadata for the record that was sent (i.e. the partition and offset). An empty metadata
         *                  with -1 value for all fields except for topicPartition will be returned if an error occurred.
         * @param exception The exception thrown during processing of this record. Null if no error occurred.
         */
        public void onCompletion(RecordMetadata metadata, Exception exception) {
            if (exception != null) {
                Utils.printErr(exception.getMessage());
                if (!(exception instanceof RetriableException)) {
                    // we can't recover from these exceptions
                    shutdown();
                }
            }
        }
    }
}
