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
import org.apache.kafka.common.errors.RetriableException;
import org.apache.kafka.common.serialization.ByteArraySerializer;
import org.apache.kafka.common.serialization.IntegerSerializer;

import java.time.Instant;
import java.util.Map;
import java.util.Properties;
import java.util.Random;
import java.util.UUID;

/**
 * A simple producer thread supporting two send modes:
 * - Async mode (default): records are sent without waiting for the response.
 * - Sync mode: each send operation blocks waiting for the response.
 */
public class Producer extends Thread {
    private final String[] topics;
    private final KafkaProperties kafkaProperties;
    private final KafkaData kafkaData;
    private volatile boolean closed;
    private final byte[] dataBytes;
    
    public Producer(KafkaProperties kafkaProperties,
                    KafkaData kafkaData,
                    String[] topics
    ) {
        super("producer");
        this.kafkaProperties = kafkaProperties;
        this.kafkaData = kafkaData;
        this.topics = topics;
        this.dataBytes = new byte[Long.BYTES + kafkaProperties.getExtraPayloadBytes()];
        new Random().nextBytes(this.dataBytes);
    }

    @Override
    public void run() {
        final long burstCycle = kafkaProperties.getBurstCycle();
        final long burstDuration = kafkaProperties.getBurstDuration();
        final long burstMessagesPerSecond = kafkaProperties.getBurstMessagesPerSecond();
        final long messagesPerSecond = kafkaProperties.getMessagesPerSecond();

        double messagesToSend = 0;
        long startTime = System.currentTimeMillis();
        long burstTime = System.currentTimeMillis();
        boolean resetMessagesToSend = true;
        double messagesRate = (double)messagesPerSecond / 1000;

        // the producer instance is thread safe
        try (KafkaProducer<Integer, byte[]> producer = createKafkaProducer(kafkaProperties.getConfigs())) {

            var numberOfPartitions = kafkaProperties.getNumberOfPartitions();
            for (long value = 0; ; value++)
                for (int key = 0; key < numberOfPartitions; key++)
                    for (var topic : this.topics) {
                        if (closed)
                            return;

                        do
                        {
                            var currentTime = System.currentTimeMillis();
                            if (burstCycle > 0)
                            {
                                if (currentTime - burstTime >= burstCycle)
                                {
                                    burstTime = currentTime;
                                    resetMessagesToSend = true;
                                }

                                if (currentTime - burstTime < burstDuration)
                                {
                                    messagesRate = (double)burstMessagesPerSecond / 1000;
                                }
                                else
                                {
                                    if (resetMessagesToSend)
                                    {
                                        messagesToSend = 0;
                                        resetMessagesToSend = false;
                                    }

                                    messagesRate = (double)messagesPerSecond / 1000;
                                }
                            }

                            var elapsed = currentTime - startTime;
                            if (elapsed >= 1)
                            {
                                startTime = currentTime;
                                messagesToSend += messagesRate * elapsed;
                            }

                            if (messagesToSend < 1)
                            {
                                Thread.sleep(1); 
                            }
                        } while (messagesToSend < 1);

                        messagesToSend -= 1.0;

                        asyncSend(producer, topic, key, value);
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

    public KafkaProducer<Integer, byte[]> createKafkaProducer(Map<String, String> configs) {
        Properties props = new Properties();
        // client id is not required, but it's good to track the source of requests beyond just ip/port
        // by allowing a logical application name to be included in server-side request logging
        props.put(ProducerConfig.CLIENT_ID_CONFIG, "client-" + UUID.randomUUID());
        // key and value are just byte arrays, so we need to set appropriate serializers
        props.put(ProducerConfig.KEY_SERIALIZER_CLASS_CONFIG, IntegerSerializer.class);
        props.put(ProducerConfig.VALUE_SERIALIZER_CLASS_CONFIG, ByteArraySerializer.class);
        props.put(ProducerConfig.MAX_BLOCK_MS_CONFIG, 180000);

        for (var entry : configs.entrySet()) {
            props.put(entry.getKey(), entry.getValue());
        }

        return new KafkaProducer<>(props);
    }

    private void asyncSend(KafkaProducer<Integer, byte[]> producer, String topic, int key, long value) {
        // send the record asynchronously, setting a callback to be notified of the result
        // note that, even if you set a small batch.size with linger.ms=0, the send operation
        // will still be blocked when buffer.memory is full or metadata are not available
        int partition = (int)key;
        this.dataBytes[7] = (byte)(value >> 0);
        this.dataBytes[6] = (byte)(value >> 8);
        this.dataBytes[5] = (byte)(value >> 16);
        this.dataBytes[4] = (byte)(value >> 24);
        this.dataBytes[3] = (byte)(value >> 32);
        this.dataBytes[2] = (byte)(value >> 40);
        this.dataBytes[1] = (byte)(value >> 48);
        this.dataBytes[0] = (byte)(value >> 56);
        
        producer.send(new ProducerRecord<Integer, byte[]>(topic, partition, Instant.now().toEpochMilli(), key, this.dataBytes), new ProducerCallback(this.kafkaData));
    }

    class ProducerCallback implements Callback {
        private final KafkaData kafkaData;
        public ProducerCallback(KafkaData kafkaData) {
            this.kafkaData = kafkaData;
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
            else {
                double latency = (double)(Instant.now().toEpochMilli() - metadata.timestamp());
                kafkaData.incrementProduced();
                kafkaData.digestProducerLatency(latency);
            }
        }
    }
}
